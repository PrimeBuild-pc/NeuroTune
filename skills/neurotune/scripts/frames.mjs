import path from 'node:path';
import { exactKeys, MAX_BYTES, requireInput, seal, sha256, text, verifySeal } from './io.mjs';

// Strict CSV: quoted commas/newlines are data; malformed rows never disappear silently.
export function* csvRows(input) {
  let row = [], field = '', quoted = false, closed = false;
  for (let i = 0; i < input.length; i++) {
    const char = input[i];
    if (quoted) {
      if (char === '"') {
        if (input[i + 1] === '"') { field += '"'; i++; }
        else { quoted = false; closed = true; }
      } else field += char;
    } else if (char === ',' || char === '\r' || char === '\n') {
      row.push(field.trim()); field = ''; closed = false;
      if (char !== ',') {
        if (char === '\r' && input[i + 1] === '\n') i++;
        yield row; row = [];
      }
    } else if (char === '"') {
      requireInput(field.length === 0 && !closed, 'Malformed CSV quoting.');
      quoted = true;
    } else {
      requireInput(!closed, 'Unexpected text after a quoted CSV field.');
      field += char;
    }
  }
  requireInput(!quoted, 'Unterminated CSV quote.');
  if (row.length || field.length || closed) { row.push(field.trim()); yield row; }
}
export function processKey(value) {
  requireInput(text(value), 'A process name is required.');
  return path.win32.basename(value).replace(/\.exe$/iu, '').toLowerCase();
}
const percentile = (sorted, p) => sorted[Math.ceil(sorted.length * p) - 1];
const metricKeys = ['frameCount', 'durationMs', 'meanMs', 'averageFps', 'p50Ms', 'p95Ms', 'p99Ms', 'stutterCount'];

export function frameMetrics(frames) {
  requireInput(Array.isArray(frames) && frames.length >= 120 && frames.length <= 1000000, 'Select 120–1000000 valid frames.');
  requireInput(frames.every(value => typeof value === 'number' && Number.isFinite(value) && value >= 0.05 && value <= 10000), 'Selected frame times must be within 0.05–10000 ms.');
  frames.sort((a, b) => a - b);
  const durationMs = frames.reduce((sum, value) => sum + value, 0);
  const meanMs = durationMs / frames.length, p50Ms = percentile(frames, 0.5);
  return { frameCount: frames.length, durationMs, meanMs, averageFps: 1000 / meanMs,
    p50Ms, p95Ms: percentile(frames, 0.95), p99Ms: percentile(frames, 0.99),
    stutterCount: frames.filter(value => value > Math.max(50, p50Ms * 2)).length };
}

export function analyzeFrames(csv, { process, conditions }) {
  requireInput(typeof csv === 'string' && csv.length > 0 && Buffer.byteLength(csv) <= MAX_BYTES, 'CSV must contain at most 10 MiB.');
  requireInput(text(conditions, 1000), 'Describe the repeatable workload conditions (1–1000 characters).');
  const target = processKey(process);
  requireInput(target.length > 0, 'Invalid process name.');
  const rows = csvRows(csv.replace(/^\uFEFF/u, ''));
  const headers = rows.next().value?.map(name => name.toLowerCase());
  requireInput(headers?.length > 1 && new Set(headers).size === headers.length && headers.every(Boolean), 'Missing or duplicate CSV headers.');
  const processColumns = ['processname', 'application'].filter(name => headers.includes(name));
  requireInput(processColumns.length === 1, 'CSV must have exactly one ProcessName or Application column.');
  const processColumn = headers.indexOf(processColumns[0]);
  // DisplayedTime is not interchangeable with CPU present cadence.
  const frameColumn = ['msbetweenpresents', 'cpuframetime'].find(name => headers.includes(name));
  requireInput(frameColumn !== undefined, 'Expected MsBetweenPresents or CPUFrameTime; DisplayedTime is not supported.');
  const frameIndex = headers.indexOf(frameColumn);
  const pidIndex = headers.indexOf('processid'), swapIndex = headers.indexOf('swapchainaddress');
  const modeIndex = headers.indexOf('presentmode');
  const frames = [], processes = new Set(), swapChains = new Set(), modes = new Set();
  let excludedFrames = 0;
  for (const fields of rows) {
    if (fields.length === 1 && fields[0] === '') continue;
    requireInput(fields.length === headers.length, 'CSV row width differs from its header.');
    if (processKey(fields[processColumn]) !== target) { excludedFrames++; continue; }
    const raw = fields[frameIndex];
    requireInput(/^(?:\d+(?:\.\d*)?|\.\d+)(?:e[+-]?\d+)?$/iu.test(raw), 'Selected frames contain an invalid number.');
    frames.push(Number(raw));
    requireInput(frames.length <= 1000000, 'At most 1000000 selected frames are supported.');
    if (pidIndex >= 0) {
      requireInput(/^\d+$/u.test(fields[pidIndex]), 'Selected frames have a missing or invalid ProcessID.');
      processes.add(fields[pidIndex]);
    }
    if (swapIndex >= 0) {
      requireInput(text(fields[swapIndex], 100), 'Selected frames have a missing swap chain.');
      swapChains.add(fields[swapIndex]);
    }
    if (modeIndex >= 0) {
      requireInput(text(fields[modeIndex], 100), 'Selected frames have a missing presentation mode.');
      modes.add(fields[modeIndex]);
      requireInput(modes.size <= 100, 'At most 100 presentation modes are supported.');
    }
  }
  requireInput(processes.size <= 1 && swapChains.size <= 1, 'Multiple process instances or swap chains: filter the CSV to one capture stream first.');
  return seal({
    schemaVersion: 1, kind: 'frames', sourceHash: sha256(csv), provenance: 'imported-unverified',
    process: target, conditions, frameColumn, presentModes: [...modes].sort(),
    streamIdentity: { processIdPresent: pidIndex >= 0, swapChainPresent: swapIndex >= 0 },
    excludedFrames,
    metrics: frameMetrics(frames)
  });
}

export function validateFrames(report) {
  verifySeal(report);
  exactKeys(report, ['schemaVersion', 'kind', 'sourceHash', 'provenance', 'process', 'conditions', 'frameColumn', 'presentModes', 'streamIdentity', 'excludedFrames', 'metrics', 'id']);
  requireInput(report.kind === 'frames' && report.provenance === 'imported-unverified', 'Expected an imported frame report.');
  requireInput(typeof report.sourceHash === 'string' && /^[a-f0-9]{64}$/u.test(report.sourceHash), 'Invalid source hash.');
  requireInput(text(report.process) && text(report.conditions, 1000), 'Missing workload identity.');
  requireInput(['msbetweenpresents', 'cpuframetime'].includes(report.frameColumn), 'Unsupported frame metric.');
  requireInput(Array.isArray(report.presentModes) && report.presentModes.length <= 100 && report.presentModes.every(mode => text(mode, 100)), 'Invalid presentation modes.');
  exactKeys(report.streamIdentity, ['processIdPresent', 'swapChainPresent']);
  requireInput(Object.values(report.streamIdentity).every(value => typeof value === 'boolean'), 'Invalid stream identity.');
  requireInput(Number.isSafeInteger(report.excludedFrames) && report.excludedFrames >= 0, 'Invalid excluded frame count.');
  exactKeys(report.metrics, metricKeys);
  const m = report.metrics;
  requireInput(Object.values(m).every(value => typeof value === 'number' && Number.isFinite(value) && value >= 0), 'Invalid metrics.');
  requireInput(Number.isSafeInteger(m.frameCount) && m.frameCount >= 120 && m.frameCount <= 1000000, 'Invalid frame count.');
  requireInput(Number.isSafeInteger(m.stutterCount) && m.stutterCount <= m.frameCount, 'Invalid stutter count.');
  // Match the derived-metric tolerance below; valid 0.05 ms inputs can sum just below the bound.
  requireInput(m.meanMs >= 0.05 - 1e-8 && m.meanMs <= 10000 && m.p50Ms >= 0.05 && m.p50Ms <= m.p95Ms && m.p95Ms <= m.p99Ms && m.p99Ms <= 10000, 'Invalid frame-time range.');
  requireInput(Math.abs(m.durationMs / m.frameCount - m.meanMs) < 1e-8 && Math.abs(1000 / m.meanMs - m.averageFps) < 1e-8, 'Inconsistent metrics.');
  return report;
}

export function compareFrames(baseline, candidate) {
  requireInput(Array.isArray(baseline) && Array.isArray(candidate) && baseline.length > 0 && candidate.length > 0 && baseline.length <= 20 && candidate.length <= 20, 'Select 1–20 reports on each side.');
  const all = [...baseline, ...candidate].map(validateFrames);
  const reasons = [];
  if (new Set(all.map(report => report.sourceHash)).size !== all.length) reasons.push('duplicate_source');
  for (const key of ['process', 'conditions', 'frameColumn']) {
    if (new Set(all.map(report => report[key])).size !== 1) reasons.push(`${key}_mismatch`);
  }
  if (new Set(all.map(report => JSON.stringify(report.presentModes))).size !== 1) reasons.push('presentation_modes_mismatch');
  if (all.some(report => !report.streamIdentity.processIdPresent || !report.streamIdentity.swapChainPresent)) reasons.push('missing_stream_identity');
  const durations = all.map(report => report.metrics.durationMs);
  if (Math.max(...durations) / Math.min(...durations) > 1.1) reasons.push('duration_mismatch');
  const median = values => {
    const sorted = values.toSorted((a, b) => a - b), mid = Math.floor(sorted.length / 2);
    return sorted.length % 2 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
  };
  const metrics = reasons.length ? [] : ['averageFps', 'p50Ms', 'p95Ms', 'p99Ms'].map(name => {
    const before = median(baseline.map(report => report.metrics[name]));
    const after = median(candidate.map(report => report.metrics[name]));
    return { name, baseline: before, candidate: after, deltaPercent: (after - before) / before * 100 };
  });
  return {
    schemaVersion: 1, kind: 'comparison', comparable: reasons.length === 0, reasons,
    baselineIds: baseline.map(report => report.id), candidateIds: candidate.map(report => report.id),
    repetition: baseline.length >= 3 && candidate.length >= 3 ? 'repeated' : 'single-or-unbalanced',
    metrics, recommendation: 'insufficient_evidence',
    limitations: ['Imported captures and workload conditions are unverified.', 'Hardware, configuration, thermals and capture overlap are not verified.', 'Numerical differences do not establish causality or authorize Keep/Apply.']
  };
}
