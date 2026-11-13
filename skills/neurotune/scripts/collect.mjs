import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { mkdtemp, rm, access } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { InputError, readJson, rejectLinkedAncestors, reportOutputPath, requireInput, writeJson } from './io.mjs';
import { normalizeInventory } from './evidence.mjs';
import { analyzeKernelTrace } from './latency.mjs';
import { MAX_SCAN_BYTES } from './system.mjs';

// Bundled scripts only, by fixed name. "-ExecutionPolicy Bypass" applies to this child process alone and leaves
// the machine policy unchanged; on a default Windows client the policy is Restricted and would block -File.
export async function runBundledScript(name, args, timeout) {
  requireInput(/^[A-Za-z-]+\.ps1$/u.test(name), 'Unknown bundled script.');
  const executable = path.join(process.env.SystemRoot ?? 'C:\\Windows', 'System32', 'WindowsPowerShell', 'v1.0', 'powershell.exe');
  const script = fileURLToPath(new URL(`./${name}`, import.meta.url));
  return promisify(execFile)(executable, ['-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', script, ...args],
    { windowsHide: true, timeout, maxBuffer: 4 * 1024 * 1024, encoding: 'utf8' });
}

async function newOutput(output) {
  const target = reportOutputPath(output);
  await rejectLinkedAncestors(target);
  try { await access(target); throw new InputError('Output already exists; choose a new file.', 'output_exists'); }
  catch (error) { if (error.code !== 'ENOENT') throw error; }
  return target;
}

/** Read-only full scan; the raw file stays local and only a receipt is returned. */
export async function scanSystem(output) {
  requireInput(process.platform === 'win32', 'The system scan requires Windows.');
  const target = await newOutput(output);
  const directory = await mkdtemp(path.join(tmpdir(), 'neurotune-'));
  try {
    const rawPath = path.join(directory, 'scan.json');
    try { await runBundledScript('Collect-System.ps1', ['-OutputPath', rawPath], 300000); }
    catch { throw new InputError('System scan failed or timed out. Rerun it; do not disable protections to make it pass.', 'scan_failed'); }
    const raw = await readJson(rawPath, MAX_SCAN_BYTES);
    requireInput(raw?.kind === 'system-scan', 'The scan produced no usable output.');
    await writeJson(target, raw);
    return { schemaVersion: 1, kind: 'scan-receipt', saved: true, isAdmin: raw.isAdmin === true, unavailableSections: Object.keys(raw.unavailable ?? {}),
      sharedFacts: false, next: 'system-findings puts device, app, service and setting names into the model context: run it only under the sharing consent the user already gave, and ask if they gave none.' };
  } finally { await rm(directory, { recursive: true, force: true }); }
}

/** Records a built-in kernel trace (administrator), reduces it to a sealed latency report and deletes the raw trace. */
export async function measureLatency(output, seconds) {
  requireInput(process.platform === 'win32', 'Kernel latency measurement requires Windows.');
  requireInput(Number.isInteger(seconds) && seconds >= 5 && seconds <= 300, 'Choose 5–300 seconds.');
  const target = await newOutput(output);
  const directory = await mkdtemp(path.join(tmpdir(), 'neurotune-'));
  try {
    let stdout;
    try { ({ stdout } = await runBundledScript('Measure-Latency.ps1', ['-WorkDirectory', directory, '-Seconds', String(seconds)], (seconds + 240) * 1000)); }
    catch (error) {
      const reason = String(error.stderr ?? '');
      if (/NEEDS_ADMIN/u.test(reason)) throw new InputError('Needs administrator rights: rerun this command from an elevated prompt (normal Windows UAC). Nothing was recorded.', 'needs_admin');
      if (/KERNEL_LOGGER_BUSY/u.test(reason)) throw new InputError('The "NT Kernel Logger" trace session is already running and was not stopped. Close the tool that owns it (LatencyMon, WPR, xperf) and retry. If none is open, an interrupted earlier measure-latency may have left it: with the user\'s agreement, stop it from an elevated prompt with: logman stop "NT Kernel Logger" -ets', 'kernel_logger_busy');
      throw new InputError('Kernel trace failed or timed out.', 'trace_failed');
    }
    const result = JSON.parse(stdout.trim().split(/\r?\n/u).pop());
    const report = await analyzeKernelTrace(path.join(directory, 'kernel.csv'), { eventsLost: Number.isSafeInteger(result.eventsLost) ? result.eventsLost : null });
    await writeJson(target, report);
    return { schemaVersion: 1, kind: 'latency-receipt', saved: true, reportId: report.id, sharedFacts: false,
      next: 'Run evidence on the report (after sharing consent) for per-driver DPC/ISR facts.' };
  } finally { await rm(directory, { recursive: true, force: true }); }
}

export async function collectInventory(output) {
  requireInput(process.platform === 'win32', 'Live inventory requires Windows. Offline analysis works on other platforms.');
  const target = reportOutputPath(output);
  await rejectLinkedAncestors(target);
  try { await access(target); throw new InputError('Output already exists; choose a new file.', 'output_exists'); }
  catch (error) { if (error.code !== 'ENOENT') throw error; }
  const directory = await mkdtemp(path.join(tmpdir(), 'neurotune-'));
  try {
    const rawPath = path.join(directory, 'inventory.json');
    try { await runBundledScript('Collect-Hardware.ps1', ['-OutputPath', rawPath], 90000); }
    catch {
      throw new InputError('Windows collector failed or timed out. Check CIM availability locally; do not disable protections or elevate automatically.', 'collector_failed');
    }
    const report = normalizeInventory(await readJson(rawPath));
    requireInput(report.facts.windowsBuild !== null && report.facts.processors.length > 0, 'Essential Windows inventory is unavailable.');
    await writeJson(target, report);
    return { schemaVersion: 1, kind: 'collection-receipt', saved: true, reportId: report.id,
      warningCount: report.warningCount, sharedFacts: false,
      next: 'Review the file locally. Obtain permission before running evidence or reading it into a model context.' };
  } finally { await rm(directory, { recursive: true, force: true }); }
}
