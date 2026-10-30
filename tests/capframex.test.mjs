import { test } from 'node:test';
import assert from 'node:assert/strict';
import { analyzeCapFrameX } from '../skills/neurotune/scripts/capframex.mjs';
import { analyzeFrames, compareFrames, validateFrames } from '../skills/neurotune/scripts/frames.mjs';
import { MAX_BYTES, sha256 } from '../skills/neurotune/scripts/io.mjs';

function capture(change = {}) {
  return JSON.stringify({ Info: { ProcessName: 'game.exe', DeviceName: 'PRIVATE_SENTINEL' },
    Runs: [{ CaptureData: { MsBetweenPresents: Array(120).fill(10), PresentMode: Array(120).fill(3), ...change } }] });
}
const options = { process: 'GAME.EXE', conditions: 'Synthetic fixed scene' };

test('a single CapFrameX run yields standard frame evidence without inventing stream identity', () => {
  const input = capture(), report = analyzeCapFrameX(input, options);
  validateFrames(report);
  assert.equal(report.metrics.averageFps, 100);
  assert.equal(report.metrics.frameCount, 120);
  assert.equal(report.process, 'game');
  assert.equal(report.sourceHash, sha256(input));
  assert.equal(JSON.stringify(report).includes('PRIVATE_SENTINEL'), false);
  assert.deepEqual(report.streamIdentity, { processIdPresent: false, swapChainPresent: false });
  assert.deepEqual(report.presentModes, ['capframex:3']);
  assert.ok(compareFrames([report], [report]).reasons.includes('missing_stream_identity'));
});

test('CapFrameX and CSV share numerical conventions, while raw source hashes stay distinct', () => {
  const values = [...Array(118).fill(10), 100, 100];
  const session = capture({ MsBetweenPresents: values });
  const csv = 'Application,MsBetweenPresents\n' + values.map(value => `game.exe,${value}`).join('\n');
  const jsonReport = analyzeCapFrameX('\ufeff' + session, options);
  assert.deepEqual(jsonReport.metrics, analyzeFrames(csv, options).metrics);
  assert.equal(jsonReport.metrics.stutterCount, 2);
  assert.equal(jsonReport.metrics.p99Ms, 100);
  assert.equal(jsonReport.sourceHash, sha256('\ufeff' + session));
});

test('CapFrameX rejects invalid frame values and misaligned or unknown modes without coercion', () => {
  for (const value of [null, '10', 0, -1, 10001, {}, []]) {
    assert.throws(() => analyzeCapFrameX(capture({ MsBetweenPresents: [value, ...Array(119).fill(10)] }), options), /frame times/);
  }
  for (const modes of [[], Array(120).fill('3'), Array(120).fill(10), Array(120).fill(-1), '3']) {
    assert.throws(() => analyzeCapFrameX(capture({ PresentMode: modes }), options), /presentation modes/);
  }
  assert.throws(() => analyzeCapFrameX(capture().replace('[10,', '[1e309,'), options), /frame times/);
  assert.deepEqual(analyzeCapFrameX(capture({ PresentMode: null }), options).presentModes, []);
});

test('CapFrameX rejects malformed, oversized, mismatched and multi-run input rather than merging it', () => {
  for (const input of ['', '{PRIVATE_SENTINEL', 'null', '[]', '{}', 'x'.repeat(MAX_BYTES + 1)]) {
    assert.throws(() => analyzeCapFrameX(input, options));
  }
  assert.throws(() => analyzeCapFrameX(capture(), { ...options, process: 'other.exe' }), /process/);
  assert.throws(() => analyzeCapFrameX(capture(), { ...options, conditions: '' }), /conditions/);
  const session = JSON.parse(capture());
  session.Runs.push(session.Runs[0]);
  assert.throws(() => analyzeCapFrameX(JSON.stringify(session), options), /single-run/);
  session.Runs = [{}];
  assert.throws(() => analyzeCapFrameX(JSON.stringify(session), options), /frames/);
  assert.throws(() => analyzeCapFrameX(capture({ MsBetweenPresents: Array(119).fill(10), PresentMode: null }), options), /120/);
  assert.throws(() => analyzeCapFrameX(capture({ MsBetweenPresents: Array(1000001).fill(1), PresentMode: null }), options), /1000000/);
});
