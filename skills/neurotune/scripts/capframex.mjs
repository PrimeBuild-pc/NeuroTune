import { frameMetrics, processKey } from './frames.mjs';
import { InputError, MAX_BYTES, requireInput, seal, sha256, text } from './io.mjs';

export function analyzeCapFrameX(input, { process, conditions }) {
  requireInput(typeof input === 'string' && input.length > 0 && Buffer.byteLength(input) <= MAX_BYTES, 'CapFrameX JSON must contain at most 10 MiB.');
  requireInput(text(conditions, 1000), 'Describe the repeatable workload conditions (1–1000 characters).');
  let session;
  try { session = JSON.parse(input.replace(/^\uFEFF/u, '')); }
  catch { throw new InputError('Input must be valid CapFrameX JSON.'); }
  requireInput(Array.isArray(session?.Runs) && session.Runs.length === 1, 'Select a single-run CapFrameX session; runs are never concatenated.');
  const target = processKey(process);
  requireInput(target.length > 0 && processKey(session.Info?.ProcessName) === target, 'Selected process does not match the CapFrameX session.');
  const capture = session.Runs[0]?.CaptureData;
  const frames = capture?.MsBetweenPresents;
  const modes = capture?.PresentMode;
  const presentModes = [];
  if (modes !== undefined && modes !== null) {
    requireInput(Array.isArray(frames) && Array.isArray(modes) && modes.length === frames.length && modes.every(mode => Number.isInteger(mode) && mode >= 0 && mode <= 9), 'CapFrameX presentation modes must align with frames and use supported enum values.');
    presentModes.push(...[...new Set(modes)].map(mode => `capframex:${mode}`));
  }
  return seal({
    schemaVersion: 1, kind: 'frames', sourceHash: sha256(input), provenance: 'imported-unverified',
    process: target, conditions, frameColumn: 'msbetweenpresents', presentModes: presentModes.sort(),
    // This session format does not preserve per-frame ProcessID or SwapChainAddress.
    streamIdentity: { processIdPresent: false, swapChainPresent: false }, excludedFrames: 0,
    metrics: frameMetrics(frames)
  });
}
