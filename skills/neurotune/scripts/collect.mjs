import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { mkdtemp, rm, access } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { InputError, readJson, rejectLinkedAncestors, requireInput, writeJson } from './io.mjs';
import { normalizeInventory } from './evidence.mjs';

export async function collectInventory(output) {
  requireInput(process.platform === 'win32', 'Live inventory requires Windows. Offline analysis works on other platforms.');
  await rejectLinkedAncestors(output);
  try { await access(output); throw new InputError('Output already exists; choose a new file.', 'output_exists'); }
  catch (error) { if (error.code !== 'ENOENT') throw error; }
  const directory = await mkdtemp(path.join(tmpdir(), 'neurotune-'));
  try {
    const rawPath = path.join(directory, 'inventory.json');
    const executable = path.join(process.env.SystemRoot ?? 'C:\\Windows', 'System32', 'WindowsPowerShell', 'v1.0', 'powershell.exe');
    const script = fileURLToPath(new URL('./Collect-Hardware.ps1', import.meta.url));
    try {
      await promisify(execFile)(executable, ['-NoLogo', '-NoProfile', '-NonInteractive', '-File', script, '-OutputPath', rawPath],
        { windowsHide: true, timeout: 90000, maxBuffer: 1024 * 1024, encoding: 'utf8' });
    } catch {
      throw new InputError('Windows collector failed or timed out. Check PowerShell policy and CIM availability locally; do not disable protections or elevate automatically.', 'collector_failed');
    }
    const report = normalizeInventory(await readJson(rawPath));
    requireInput(report.facts.windowsBuild !== null && report.facts.processors.length > 0, 'Essential Windows inventory is unavailable.');
    await writeJson(output, report);
    return { schemaVersion: 1, kind: 'collection-receipt', saved: true, reportId: report.id,
      warningCount: report.warningCount, sharedFacts: false,
      next: 'Review the file locally. Obtain permission before running evidence or reading it into a model context.' };
  } finally { await rm(directory, { recursive: true, force: true }); }
}
