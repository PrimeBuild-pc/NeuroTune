import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { systemFindings } from '../skills/neurotune/scripts/system.mjs';

const cli = fileURLToPath(new URL('../skills/neurotune/scripts/cli.mjs', import.meta.url));
const NOW = Date.parse('2026-11-03T12:00:00Z');
const dev = (id, name, cls, parent, extra = {}) => ({ id, name, class: cls, status: 'OK', present: true, problem: 0, parent, service: null, inf: null, driverVersion: null, driverProvider: 'Microsoft', driverDate: null, ...extra });

// Synthetic scan in the Collect-System.ps1 shape.
export function scan() {
  return {
    schemaVersion: 1, kind: 'system-scan', scannerVersion: '1', generatedAtUtc: '2026-11-03T10:00:00Z', isAdmin: true, unavailable: {},
    hardware: { os: { caption: 'Windows 11 Pro', build: '26200' }, cpus: [{ name: 'Synthetic CPU', cores: 8, logical: 16 }], gpus: [{ name: 'Synthetic GPU', driverVersion: '1.0' }],
      memory: [{ slot: 'A1', gb: 16, ratedMHz: 4800, configuredMHz: 4800, part: 'X' }], memorySlots: 4, board: { product: 'Board' }, bios: { version: 'F1', date: '2022-01-01' }, disks: [] },
    devices: [
      dev('PCI\\VEN_1\\CTRL', 'USB Controller A', 'USB', null),
      dev('USB\\ROOT_HUB30\\A', 'Root hub', 'USB', 'PCI\\VEN_1\\CTRL', { service: 'USBHUB3' }),
      dev('USB\\VID_1&PID_1\\HUB', 'Front hub', 'USB', 'USB\\ROOT_HUB30\\A', { service: 'USBHUB3' }),
      dev('USB\\VID_2&PID_2\\MOUSE', 'Gaming mouse', 'USB', 'USB\\VID_1&PID_1\\HUB'),
      dev('HID\\VID_2&PID_2\\M', 'HID-compliant mouse', 'Mouse', 'USB\\VID_2&PID_2\\MOUSE'),
      dev('USB\\VID_3&PID_3\\CAM', 'Webcam', 'Camera', 'USB\\ROOT_HUB30\\A'),
      dev('PCI\\VEN_9\\NIC', 'Ethernet', 'Net', null, { inf: 'oem5.inf', driverProvider: 'Vendor', driverDate: '2019-05-01', driverVersion: '1.0' }),
      dev('ACPI\\BROKEN', 'Broken device', 'System', null, { status: 'Error', problem: 10 }),
      dev('ACPI\\OFF', 'Disabled reader', 'SmartCardReader', null, { status: 'Error', problem: 22 }),
      { ...dev('USB\\OLD', 'Old stick', 'DiskDrive', null), present: false }
    ],
    driverStore: [
      { inf: 'oem1.inf', original: 'gpu.inf', provider: 'Vendor', class: 'Display', version: '30.0.1', date: '2025-01-01' },
      { inf: 'oem2.inf', original: 'gpu.inf', provider: 'Vendor', class: 'Display', version: '31.0.5', date: '2026-01-01' },
      { inf: 'oem5.inf', original: 'nic.inf', provider: 'Vendor', class: 'Net', version: '1.0', date: '2019-05-01' }
    ],
    interrupts: [
      { id: 'PCI\\VEN_9\\NIC', name: 'Ethernet', class: 'Net', msiCapable: true, msiEnabled: false, messageLimit: null, priority: null, policy: null, affinityMask: null },
      { id: 'PCI\\GPU', name: 'Synthetic GPU', class: 'Display', msiCapable: true, msiEnabled: true, messageLimit: 1, priority: 3, policy: 4, affinityMask: 'BAAAAAAAAAA=' }
    ],
    services: [
      { name: 'VendorUpdater', display: 'Vendor Updater', startMode: 'Auto', state: 'Running', delayed: false, path: 'C:\\Program Files\\Vendor\\up.exe', windowsPath: false },
      { name: 'WinDefend', display: 'Microsoft Defender Antivirus Service', startMode: 'Auto', state: 'Running', delayed: false, path: '"C:\\ProgramData\\Microsoft\\Windows Defender\\Platform\\4.18\\MsMpEng.exe"', windowsPath: false },
      { name: 'Spooler', display: 'Print Spooler', startMode: 'Auto', state: 'Running', delayed: false, path: 'C:\\Windows\\System32\\spoolsv.exe', windowsPath: true },
      { name: 'SysMain', display: 'SysMain', startMode: 'Disabled', state: 'Stopped', delayed: false, path: 'C:\\Windows\\system32\\svchost.exe -k LocalSystemNetworkRestricted', windowsPath: true }
    ],
    scheduledTasks: [{ path: '\\', name: 'VendorTelemetry', state: 'Ready', author: 'Vendor', microsoft: false },
      { path: '\\', name: 'Sync Task-S-1-5-21-1111111111-2222222222-3333333333-1001', state: 'Ready', author: 'GAMING-PC\\realname', microsoft: false }, { path: '\\Microsoft\\Windows\\Defrag\\', name: 'ScheduledDefrag', state: 'Ready', author: 'Microsoft', microsoft: true }],
    startup: [{ name: 'Discord', command: 'x', location: 'HKU\\S-1-5-21-1111111111-2222222222-3333333333-1001\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run', enabled: true }, { name: 'Old', command: 'y', location: 'Startup', enabled: false }],
    apps: { desktop: [{ name: 'Fortnite', publisher: 'Epic Games' }, { name: 'Vendor Suite', publisher: 'Vendor' }], store: [{ name: 'Clipchamp.Clipchamp', nonRemovable: false }, { name: 'Microsoft.VCLibs.140.00', nonRemovable: false }] },
    updates: { lastInstalled: '2026-07-01', lastSecurity: '2026-07-01', recent: [], serviceStartMode: 'Disabled', pausedUntil: '2026-12-01T00:00:00Z', noAutoUpdatePolicy: null, deferFeatureDays: null },
    privacy: { telemetryPolicy: null, telemetrySetting: 3, advertisingId: 1, tailoredExperiences: 0, backgroundAppsGlobal: null, backgroundAppsPolicy: null, suggestionsStart: 0, suggestionsSettings: 0, silentAppInstalls: 1 },
    gaming: { gameMode: 1, hags: 2 },
    riskyTweaks: { bcdTimerOverrides: ['useplatformclock'], spectreMitigationOverride: null, svcHostSplitThresholdKB: 3670016, threadDpcEnable: null, globalTimerResolutionRequests: null,
      mouseDataQueueSize: 20, keyboardDataQueueSize: 100, csrssCpuPriority: null, mpoDisabled: null, disablePagingExecutive: 0, largeSystemCache: 0, errorReportingDisabled: null, tcpAutoTuning: 'Normal' },
    network: [{ name: 'Ethernet', description: 'Vendor NIC', status: 'Up', linkSpeed: '1 Gbps', driverVersion: '1.0',
      advanced: [{ name: 'Energy Efficient Ethernet', value: 'Enabled', keyword: '*EEE' }, { name: 'Jumbo Packet', value: 'Disabled', keyword: '*JumboPacket' }] }],
    audio: [{ name: 'Speakers', status: 'OK', present: true, direction: 'output' }, { name: 'Monitor audio', status: 'OK', present: true, direction: 'output' },
      { name: 'Mic', status: 'OK', present: true, direction: 'input' }, { name: 'Old headset', status: 'Unknown', present: true, direction: 'output' }],
    games: [{ store: 'epic', name: 'Fortnite' }]
  };
}

test('system findings flag what to review and keep defaults quiet', () => {
  const raw = scan();
  const f = systemFindings(raw, Buffer.from(JSON.stringify(raw)), NOW);
  assert.equal(f.kind, 'system-findings');
  assert.match(f.scanSha256, /^[0-9a-f]{64}$/u);
  assert.deepEqual(f.hardware.memory.notes.includes('single_memory_module_possible_single_channel'), true);
  assert.deepEqual(f.devices.withProblems.items.map(d => d.name), ['Broken device']);
  assert.deepEqual(f.devices.disabled.items.map(d => d.name), ['Disabled reader']);
  assert.equal(f.devices.notPresentByClass.DiskDrive, 1);
  assert.deepEqual(f.drivers.oldUnusedPackages.items.map(p => p.inf), ['oem1.inf']);
  assert.deepEqual(f.drivers.oldThirdPartyDrivers.items.map(d => d.name), ['Ethernet']);
  assert.deepEqual(f.interrupts.msiCapableButOff.items.map(i => i.name), ['Ethernet']);
  assert.deepEqual(f.interrupts.affinityOverrides.items.map(i => i.name), ['Synthetic GPU']);
  const mouse = f.inputChains.find(c => c.kind === 'Mouse');
  assert.equal(mouse.device, 'Gaming mouse'); assert.equal(mouse.hubsBetween, 2); assert.equal(mouse.controller, 'USB Controller A');
  assert.deepEqual(mouse.sharesControllerWith, ['Webcam']);
  assert.deepEqual(f.services.thirdPartyActive.items.map(s => s.name), ['VendorUpdater']);
  assert.deepEqual(f.services.windowsDisabled.items, ['SysMain']);
  // Defender runs from ProgramData: it must never sit in the third-party cleanup list.
  assert.deepEqual(f.services.microsoftActive.items, ['WinDefend']);
  assert.deepEqual(f.scheduledTasks.enabledThirdParty.items.map(t => t.author), ['Vendor', 'local-account']);
  // The summary goes to a remote model: no account SID, account name or computer name.
  assert.doesNotMatch(JSON.stringify(f), /1111111111|realname|GAMING-PC/u);
  assert.match(f.startup.enabled.items[0].location, /S-1-5-21-<user>/u);
  assert.deepEqual(f.startup.enabled.items.map(s => s.name), ['Discord']);
  assert.deepEqual(f.apps.store.items, ['Clipchamp.Clipchamp']);
  assert.deepEqual(f.updates.flags.sort(), ['no_security_update_in_45_days', 'updates_paused', 'windows_update_service_disabled']);
  assert.ok(f.privacy.flags.includes('advertising_id_on') && f.privacy.flags.includes('silent_app_installs_on') && f.privacy.flags.includes('background_apps_allowed'));
  assert.deepEqual(f.riskyTweaks.map(r => r.id).sort(), ['bcd_timer_overrides', 'mouse_queue_changed']);
  assert.deepEqual(f.network[0].reviewSettings, [{ name: 'Energy Efficient Ethernet', value: 'Enabled' }]);
  assert.deepEqual(f.audio.outputsActive, ['Speakers', 'Monitor audio']); assert.equal(f.audio.inactive, 1);
  assert.deepEqual(f.games.supportedConfigCatalogue, ['fortnite']);
  assert.ok(JSON.stringify(f).length < JSON.stringify(raw).length * 2);
});

test('system findings reject other JSON and survive missing sections', () => {
  assert.throws(() => systemFindings({ kind: 'inventory' }, Buffer.from('{}')), /system scan/);
  const minimal = { schemaVersion: 1, kind: 'system-scan', isAdmin: false, unavailable: { driverStore: 'needs administrator rights' } };
  const f = systemFindings(minimal, Buffer.from(JSON.stringify(minimal)), NOW);
  assert.deepEqual(f.unavailableSections, ['driverStore']);
  assert.equal(f.devices.total, 0);
  // Sections that were not read give "unknown", never a finding to fix.
  assert.deepEqual(f.updates.flags, ['security_update_date_unknown']);
  assert.deepEqual(f.privacy.flags, []);
});

test('system-findings CLI reads a scan file and returns findings only', async () => {
  const dir = await mkdtemp(path.join(tmpdir(), 'neurotune-test-'));
  try {
    const file = path.join(dir, 'scan.json');
    await writeFile(file, '\uFEFF' + JSON.stringify(scan()));
    const result = spawnSync(process.execPath, [cli, 'system-findings', '--input', file], { encoding: 'utf8' });
    assert.equal(result.status, 0, result.stdout);
    assert.equal(JSON.parse(result.stdout).data.kind, 'system-findings');
    const help = JSON.parse(spawnSync(process.execPath, [cli, 'help'], { encoding: 'utf8' }).stdout).data.usage.join('\n');
    for (const command of ['scan-system', 'system-findings', 'measure-latency', 'latencymon']) assert.match(help, new RegExp(command));
  } finally { await rm(dir, { recursive: true, force: true }); }
});
