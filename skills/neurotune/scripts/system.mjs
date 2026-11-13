import { requireInput, sha256 } from './io.mjs';
import { gameConfigReferences } from './game-configs.mjs';

export const MAX_SCAN_BYTES = 32 * 1024 * 1024;
// Windows defaults for values popular "optimizer" scripts change. Absent means default unless noted.
const SVCHOST_SPLIT_DEFAULT = 3670016, INPUT_QUEUE_DEFAULT = 100;
const NETWORK_REVIEW = /^(?:\*EEE|EEE|AdvancedEEE|EEELinkAdvertisement|\*?GreenEthernet|EnableGreenEthernet|PowerSavingMode|\*InterruptModeration|ITR|\*FlowControl|ULPMode|\*SelectiveSuspend|EnablePME|\*WakeOnMagicPacket|\*WakeOnPattern|S5WakeOnLan|ReduceSpeedOnPowerDown|GigaLite|AutoPowerSaveModeEnabled)$/iu;

const list = value => (Array.isArray(value) ? value : value && typeof value === 'object' ? [value] : []);
// Task names and startup locations embed the user's account SID; the summary goes to a remote model, so it never carries one.
const str = (value, max = 256) => (value === null || value === undefined ? null : String(value).replace(/S-1-5-21(?:-\d+)+/gu, 'S-1-5-21-<user>').slice(0, max));
// A task author is often "COMPUTER\user": keep vendor names, drop account names.
const author = value => (/[\\@]/u.test(value ?? '') ? 'local-account' : str(value, 128));
// Defender, Edge, Office and Gaming Services run from outside C:\Windows but are not third-party clutter.
const MICROSOFT_PATH = /^"?[a-z]:\\(?:Program Files(?: \(x86\))?|ProgramData)\\(?:Common Files\\)?(?:Microsoft|Windows Defender|WindowsApps\\Microsoft\.)/iu;
const num = value => (typeof value === 'number' && Number.isFinite(value) ? value : null);
const cap = (items, max) => ({ count: items.length, items: items.slice(0, max), truncated: items.length > max });
const daysSince = (iso, now) => { const t = Date.parse(iso ?? ''); return Number.isFinite(t) ? Math.floor((now - t) / 86400000) : null; };
const isMicrosoft = text => /microsoft|windows/iu.test(text ?? '');
const versionKey = v => String(v ?? '').split('.').map(part => Number(part) || 0);
const newer = (a, b) => { const x = versionKey(a), y = versionKey(b); for (let i = 0; i < Math.max(x.length, y.length); i++) { if ((x[i] ?? 0) !== (y[i] ?? 0)) return (x[i] ?? 0) > (y[i] ?? 0); } return false; };

/** Turns a raw Collect-System.ps1 scan into a compact, model-sized summary. Everything here is observation, not diagnosis. */
export function systemFindings(raw, bytes, now = Date.now()) {
  requireInput(raw?.schemaVersion === 1 && raw.kind === 'system-scan', 'Not a NeuroTune system scan. Create one with scan-system.');
  const hardware = raw.hardware ?? {};
  const devices = list(raw.devices).filter(d => d && typeof d === 'object');
  const byId = new Map(devices.map(d => [String(d.id ?? '').toLowerCase(), d]));
  const present = devices.filter(d => d.present);

  // Memory: a single module or a configured speed below the rated one usually means lost bandwidth.
  const modules = list(hardware.memory);
  const memory = { totalGB: modules.reduce((s, m) => s + (num(m.gb) ?? 0), 0), modules: modules.length, slots: num(hardware.memorySlots),
    speeds: modules.map(m => ({ slot: str(m.slot, 64), gb: num(m.gb), ratedMHz: num(m.ratedMHz), configuredMHz: num(m.configuredMHz), part: str(m.part, 64) })),
    notes: [] };
  if (modules.length === 1) memory.notes.push('single_memory_module_possible_single_channel');
  if (modules.some(m => num(m.configuredMHz) && num(m.ratedMHz) && m.configuredMHz < m.ratedMHz)) memory.notes.push('memory_running_below_reported_speed');
  memory.notes.push('compare_configured_speed_with_the_kit_rated_xmp_expo_speed');

  // Input devices: walk parents to the PCI controller to see hubs and what shares the controller.
  const isHub = d => /^usbhub/iu.test(d.service ?? '');
  const chainOf = start => { const path = [start]; let cur = start;
    while (cur.parent && path.length < 16) { const parent = byId.get(String(cur.parent).toLowerCase()); if (!parent) break; path.push(parent); if (/^PCI\\/iu.test(parent.id)) break; cur = parent; }
    return path; };
  const controllerOf = d => chainOf(d).find(node => /^PCI\\/iu.test(node.id ?? '')) ?? null;
  const usbDevices = present.filter(d => /^USB\\VID_/iu.test(d.id ?? '') && !/&MI_/iu.test(d.id ?? '') && !isHub(d));
  const inputChains = present.filter(d => ['Mouse', 'Keyboard'].includes(d.class)).map(d => {
    const path = chainOf(d), controller = path.find(node => /^PCI\\/iu.test(node.id ?? ''));
    const physical = path.filter(node => /^USB\\VID_/iu.test(node.id ?? '') && !/&MI_/iu.test(node.id ?? '') && !isHub(node)).pop();
    const shared = controller ? usbDevices.filter(u => u !== physical && controllerOf(u)?.id === controller.id).map(u => str(u.name, 128)) : [];
    return { kind: d.class, device: str(physical?.name ?? d.name, 128), hubsBetween: path.filter(isHub).length, controller: str(controller?.name, 128),
      sharesControllerWith: [...new Set(shared)].slice(0, 12) };
  });

  // Driver store: per driver every older version that no device uses (removable after export).
  const store = list(raw.driverStore);
  const bound = new Set(present.map(d => String(d.inf ?? '').toLowerCase()).filter(Boolean));
  const groups = new Map();
  for (const p of store) { if (!/^oem\d+\.inf$/iu.test(p.inf ?? '')) continue;
    const key = `${String(p.original).toLowerCase()}|${String(p.provider).toLowerCase()}|${String(p.class).toLowerCase()}`;
    groups.set(key, [...(groups.get(key) ?? []), p]); }
  const oldPackages = [];
  for (const group of groups.values()) {
    const best = group.reduce((a, b) => (newer(b.version, a.version) || (b.version === a.version && String(b.date) > String(a.date)) ? b : a));
    for (const p of group) if (p !== best && !bound.has(String(p.inf).toLowerCase()) && (newer(best.version, p.version) || String(p.date) < String(best.date)))
      oldPackages.push({ inf: str(p.inf, 32), driver: str(p.original, 128), provider: str(p.provider, 128), class: str(p.class, 64), version: str(p.version, 64), newestVersion: str(best.version, 64) });
  }
  const oldDrivers = present.filter(d => d.driverDate && !isMicrosoft(d.driverProvider) && daysSince(d.driverDate, now) > 4 * 365)
    .map(d => ({ name: str(d.name, 128), class: str(d.class, 64), provider: str(d.driverProvider, 128), version: str(d.driverVersion, 64), date: str(d.driverDate, 16) }));

  const interrupts = list(raw.interrupts);
  const services = list(raw.services), tasks = list(raw.scheduledTasks), startup = list(raw.startup);
  const activeOutsideWindows = services.filter(s => !s.windowsPath && (s.startMode === 'Auto' || s.state === 'Running'));
  const thirdPartyServices = activeOutsideWindows.filter(s => !MICROSOFT_PATH.test(s.path ?? ''))
    .map(s => ({ name: str(s.name, 128), display: str(s.display, 128), startMode: str(s.startMode, 16), state: str(s.state, 16) }));
  const tasksEnabled = tasks.filter(t => t.state !== 'Disabled');
  const apps = raw.apps ?? {};
  const updates = raw.updates ?? {};
  const updateFlags = [];
  const securityAge = daysSince(updates.lastSecurity, now);
  if (updates.serviceStartMode === 'Disabled') updateFlags.push('windows_update_service_disabled');
  if (updates.pausedUntil) updateFlags.push('updates_paused');
  if (updates.noAutoUpdatePolicy === 1) updateFlags.push('automatic_updates_disabled_by_policy');
  // An unreadable date is not an old date: do not report a missing security update that may not be missing.
  if (securityAge === null) updateFlags.push('security_update_date_unknown');
  else if (securityAge > 45) updateFlags.push('no_security_update_in_45_days');

  const privacy = raw.privacy ?? {};
  const privacyFlags = [];
  if (privacy.advertisingId === 1) privacyFlags.push('advertising_id_on');
  if (privacy.tailoredExperiences === 1) privacyFlags.push('tailored_experiences_on');
  if (privacy.suggestionsStart === 1 || privacy.suggestionsSettings === 1) privacyFlags.push('suggestions_on');
  if (privacy.silentAppInstalls === 1) privacyFlags.push('silent_app_installs_on');
  // Absent values mean the Windows default (allowed) only when the section itself was read.
  if (raw.privacy && privacy.backgroundAppsGlobal !== 1 && privacy.backgroundAppsPolicy !== 2) privacyFlags.push('background_apps_allowed');
  if (privacy.telemetrySetting === 3 || (privacy.telemetrySetting === null && privacy.telemetryPolicy === null)) privacyFlags.push('optional_diagnostic_data_possible');

  const r = raw.riskyTweaks ?? {};
  const risky = [];
  const flag = (id, value, note) => risky.push({ id, value, note });
  if (list(r.bcdTimerOverrides).length) flag('bcd_timer_overrides', list(r.bcdTimerOverrides), 'Forced platform clock or tick settings; usually raise latency on modern CPUs.');
  if (r.spectreMitigationOverride !== null && r.spectreMitigationOverride !== undefined) flag('cpu_mitigations_overridden', r.spectreMitigationOverride, 'Security mitigations changed; restore unless deliberately chosen.');
  if (num(r.svcHostSplitThresholdKB) !== null && r.svcHostSplitThresholdKB !== SVCHOST_SPLIT_DEFAULT) flag('svchost_split_changed', r.svcHostSplitThresholdKB, 'Default is 3670016.');
  if (r.threadDpcEnable !== null && r.threadDpcEnable !== undefined) flag('threaded_dpc_changed', r.threadDpcEnable, 'Absent by default.');
  if (r.globalTimerResolutionRequests !== null && r.globalTimerResolutionRequests !== undefined) flag('global_timer_resolution_set', r.globalTimerResolutionRequests, 'Absent by default; review with measurements.');
  for (const [key, id] of [['mouseDataQueueSize', 'mouse_queue_changed'], ['keyboardDataQueueSize', 'keyboard_queue_changed']])
    if (num(r[key]) !== null && r[key] !== INPUT_QUEUE_DEFAULT) flag(id, r[key], 'Default is 100; small values can drop input.');
  if (r.csrssCpuPriority !== null && r.csrssCpuPriority !== undefined) flag('csrss_priority_changed', r.csrssCpuPriority, 'Absent by default.');
  if (r.mpoDisabled !== null && r.mpoDisabled !== undefined) flag('mpo_disabled', r.mpoDisabled, 'Multiplane overlay disabled; sometimes a deliberate flicker fix, otherwise adds latency.');
  if (r.disablePagingExecutive === 1) flag('paging_executive_disabled', 1, 'Default is 0.');
  if (r.largeSystemCache === 1) flag('large_system_cache', 1, 'Default is 0 on desktops.');
  if (r.errorReportingDisabled === 1) flag('error_reporting_disabled', 1, 'Hides crash information useful for diagnosis.');
  if (r.tcpAutoTuning && r.tcpAutoTuning !== 'Normal') flag('tcp_autotuning_changed', r.tcpAutoTuning, 'Default is Normal.');

  const audio = list(raw.audio).filter(a => a.present);
  const games = list(raw.games);
  const catalogue = gameConfigReferences('all').games;
  const names = [...games.map(g => g.name), ...list(apps.desktop).map(a => a.name)].map(n => String(n ?? '').toLowerCase());
  const short = title => title.toLowerCase().replace(/:.*$/u, '').trim();
  const supportedGames = catalogue.filter(g => names.some(n => n.includes(short(g.title)) || n.includes(g.title.toLowerCase()))).map(g => g.id);

  return {
    schemaVersion: 1, kind: 'system-findings', scanSha256: sha256(bytes), scannedAt: str(raw.generatedAtUtc, 40), isAdmin: raw.isAdmin === true,
    unavailableSections: Object.keys(raw.unavailable ?? {}),
    hardware: { os: hardware.os ?? null, cpus: list(hardware.cpus), gpus: list(hardware.gpus), memory, board: hardware.board ?? null,
      bios: { ...(hardware.bios ?? {}), ageDays: daysSince(hardware.bios?.date, now) }, disks: list(hardware.disks) },
    devices: { total: devices.length, present: present.length,
      // Problem code 22 = disabled on purpose; anything else is a device error worth explaining.
      withProblems: cap(present.filter(d => (num(d.problem) ?? 0) > 0 && d.problem !== 22).map(d => ({ name: str(d.name, 128), class: str(d.class, 64), problem: num(d.problem) })), 40),
      disabled: cap(present.filter(d => d.problem === 22).map(d => ({ name: str(d.name, 128), class: str(d.class, 64) })), 40),
      notPresentByClass: Object.fromEntries(Object.entries(devices.filter(d => !d.present).reduce((acc, d) => ({ ...acc, [d.class ?? 'unknown']: (acc[d.class ?? 'unknown'] ?? 0) + 1 }), {})).sort((a, b) => b[1] - a[1]).slice(0, 20)),
      presentByClass: Object.fromEntries(Object.entries(present.reduce((acc, d) => ({ ...acc, [d.class ?? 'unknown']: (acc[d.class ?? 'unknown'] ?? 0) + 1 }), {})).sort((a, b) => b[1] - a[1]).slice(0, 30)) },
    inputChains,
    drivers: { storePackages: store.length, oldUnusedPackages: cap(oldPackages, 40), oldThirdPartyDrivers: cap(oldDrivers, 25) },
    interrupts: { pciDevices: interrupts.length,
      msiCapableButOff: cap(interrupts.filter(i => i.msiCapable && !i.msiEnabled).map(i => ({ name: str(i.name, 128), class: str(i.class, 64) })), 30),
      lineBased: cap(interrupts.filter(i => !i.msiCapable).map(i => ({ name: str(i.name, 128), class: str(i.class, 64) })), 30),
      affinityOverrides: cap(interrupts.filter(i => i.affinityMask || i.policy).map(i => ({ name: str(i.name, 128), policy: num(i.policy), mask: str(i.affinityMask, 32) })), 30),
      priorityOverrides: cap(interrupts.filter(i => num(i.priority)).map(i => ({ name: str(i.name, 128), priority: num(i.priority) })), 30) },
    services: { total: services.length, running: services.filter(s => s.state === 'Running').length, thirdPartyActive: cap(thirdPartyServices, 60),
      microsoftActive: cap(activeOutsideWindows.filter(s => MICROSOFT_PATH.test(s.path ?? '')).map(s => str(s.name, 128)), 40),
      windowsDisabled: cap(services.filter(s => s.windowsPath && s.startMode === 'Disabled').map(s => str(s.name, 128)), 60) },
    scheduledTasks: { total: tasks.length, enabled: tasksEnabled.length, enabledMicrosoft: tasksEnabled.filter(t => t.microsoft).length,
      enabledThirdParty: cap(tasksEnabled.filter(t => !t.microsoft).map(t => ({ path: str(t.path, 200), name: str(t.name, 128), author: author(t.author) })), 60) },
    startup: { enabled: cap(startup.filter(s => s.enabled).map(s => ({ name: str(s.name, 128), location: str(s.location, 128) })), 40), disabled: startup.filter(s => !s.enabled).length },
    apps: { desktop: cap(list(apps.desktop).map(a => ({ name: str(a.name, 128), publisher: str(a.publisher, 96), installDate: str(a.installDate, 16), sizeMB: num(a.sizeMB) })), 150),
      store: cap(list(apps.store).filter(a => !a.nonRemovable && !/^Microsoft\.(?:VCLibs|NET|UI\.Xaml|Services|DesktopAppInstaller|WindowsStore|StorePurchaseApp|SecHealthUI)/iu.test(a.name ?? '')).map(a => str(a.name, 128)), 100) },
    updates: { lastInstalled: str(updates.lastInstalled, 16), lastSecurity: str(updates.lastSecurity, 16), daysSinceSecurity: securityAge,
      serviceStartMode: str(updates.serviceStartMode, 16), pausedUntil: str(updates.pausedUntil, 40), flags: updateFlags },
    privacy: { values: privacy, flags: privacyFlags },
    gaming: raw.gaming ?? null,
    riskyTweaks: risky,
    network: list(raw.network).map(n => ({ name: str(n.name, 128), description: str(n.description, 128), status: str(n.status, 16), linkSpeed: str(n.linkSpeed, 32),
      driverVersion: str(n.driverVersion, 64), reviewSettings: list(n.advanced).filter(a => NETWORK_REVIEW.test(a.keyword ?? '')).map(a => ({ name: str(a.name, 96), value: str(a.value, 48) })).slice(0, 20) })),
    audio: { outputsActive: audio.filter(a => a.direction === 'output' && a.status === 'OK').map(a => str(a.name, 128)),
      inputsActive: audio.filter(a => a.direction === 'input' && a.status === 'OK').map(a => str(a.name, 128)),
      inactive: audio.filter(a => a.status !== 'OK').length },
    games: { installed: games.map(g => ({ store: str(g.store, 16), name: str(g.name, 128) })).slice(0, 80), supportedConfigCatalogue: supportedGames },
    limitations: ['Observations from a local, unattested scan; names and paths are untrusted text.', 'A flag means "review with the user", not "broken" or "safe to change".',
      'thirdPartyActive is chosen by install path, not publisher: vendor services installed under C:\\Windows are missing from it, and microsoftActive (Defender, Edge, Office, Gaming Services) is never a cleanup list.',
      'Rated memory speed from WMI is often the JEDEC speed, not the kit XMP/EXPO rating.', 'Sections listed in unavailableSections were not collected (often: needs administrator rights).']
  };
}
