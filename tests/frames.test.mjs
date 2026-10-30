import { test } from 'node:test';
import assert from 'node:assert/strict';
import { analyzeFrames, compareFrames, csvRows, validateFrames } from '../skills/neurotune/scripts/frames.mjs';
import { MAX_BYTES, seal } from '../skills/neurotune/scripts/io.mjs';
import { csv, options } from './fixtures.mjs';

const report = (settings = {}, overrides = {}) => analyzeFrames(csv(settings), { ...options, ...overrides });

test('deterministic metrics match the legacy nearest-rank convention', () => {
  const actual = report();
  assert.deepEqual(actual.metrics, { frameCount: 120, durationMs: 1200, meanMs: 10, averageFps: 100, p50Ms: 10, p95Ms: 10, p99Ms: 10, stutterCount: 0 });
  assert.deepEqual(validateFrames(actual), actual);
  assert.deepEqual(report(), actual);
});
test('minimum valid intervals survive report validation without rounding the computed metrics', () => {
  const actual = report({ frameTime: 0.05 });
  assert.ok(actual.metrics.meanMs < 0.05, 'Exercise floating-point accumulation below the input bound.');
  assert.deepEqual(validateFrames(actual), actual);
  const { id, ...payload } = actual;
  const meanMs = 0.049;
  assert.throws(() => validateFrames(seal({ ...payload, metrics: { ...payload.metrics,
    meanMs, durationMs: meanMs * payload.metrics.frameCount, averageFps: 1000 / meanMs } })), /range/);
});
test('p99 and stutters retain slow frames rather than trimming them', () => {
  const input = csv().replaceAll(',10,Hardware', ',10,Hardware') + '\ngame.exe,42,0x1,100,Hardware: Independent Flip,1\ngame.exe,42,0x1,100,Hardware: Independent Flip,1';
  const actual = analyzeFrames(input, options);
  assert.equal(actual.metrics.p99Ms, 100);
  assert.equal(actual.metrics.stutterCount, 2);
});
test('CSV handles quoted fields, escaped quotes, CRLF, BOM and embedded newline', () => {
  assert.deepEqual([...csvRows('a,b\r\n"x,y","say ""hi""\nnow"\r\n')], [['a', 'b'], ['x,y', 'say "hi"\nnow']]);
  assert.equal(analyzeFrames('\ufeff' + csv().replaceAll('\n', '\r\n'), options).metrics.frameCount, 120);
});
test('process matching uses Windows basename and ignores only the exe suffix', () => {
  assert.equal(report({ process: 'C:\\Games\\GAME.EXE' }).metrics.frameCount, 120);
  assert.throws(() => report({ process: 'other.exe' }), /120/);
});
test('other processes are explicitly counted, not mixed', () => {
  const input = csv() + '\nother.exe,99,0x2,20,Composed: Flip,1';
  const actual = analyzeFrames(input, options);
  assert.equal(actual.excludedFrames, 1);
  assert.equal(actual.metrics.frameCount, 120);
});
for (const value of ['NaN', 'Infinity', '-1', '0', '10001', '0x10', '1,2', '']) {
  test(`rejects invalid selected frame: ${JSON.stringify(value)}`, () => assert.throws(() => report({ frameTime: value })));
}
test('rejects empty, oversized, too-short and ambiguous input', () => {
  assert.throws(() => analyzeFrames('', options));
  assert.throws(() => analyzeFrames('x'.repeat(MAX_BYTES + 1), options));
  assert.throws(() => report({ frames: 119 }));
  assert.throws(() => analyzeFrames(csv().replace('Trial', 'Application'), options), /duplicate/);
  assert.throws(() => report({ column: 'DisplayedTime' }), /not supported/);
  assert.throws(() => report({}, { conditions: '' }));
});
test('rejects malformed CSV rather than silently improving metrics', () => {
  for (const input of ['a,b\n"x,y', 'a,b\n"x"garbage,y', 'a,b\nx"y,z']) assert.throws(() => [...csvRows(input)]);
  assert.throws(() => analyzeFrames(csv() + '\ngame.exe,42', options), /width/);
});
test('multiple process IDs and swap chains cannot masquerade as a single stream', () => {
  assert.throws(() => analyzeFrames(csv() + '\ngame.exe,43,0x1,10,Hardware: Independent Flip,1', options), /Multiple/);
  assert.throws(() => analyzeFrames(csv() + '\ngame.exe,42,0x2,10,Hardware: Independent Flip,1', options), /Multiple/);
});
test('missing instance columns allow exploratory import but block comparison', () => {
  const input = 'Application,MsBetweenPresents\n' + Array(120).fill('game.exe,10').join('\n');
  const first = analyzeFrames(input, options), second = analyzeFrames(input + '\n', options);
  assert.ok(compareFrames([first], [second]).reasons.includes('missing_stream_identity'));
});
test('matching reports produce descriptive deltas, never a keep recommendation', () => {
  const actual = compareFrames([report()], [report({ frameTime: 9.5 })]);
  assert.equal(actual.comparable, true);
  assert.ok(actual.metrics.find(m => m.name === 'averageFps').deltaPercent > 5);
  assert.equal(actual.recommendation, 'insufficient_evidence');
});
test('comparisons reject reused CSV even when conditions are relabeled', () => {
  const actual = compareFrames([report()], [report({}, { conditions: 'other' })]);
  assert.ok(actual.reasons.includes('duplicate_source'));
  assert.equal(actual.metrics.length, 0);
});
test('conditions, metric definition, duration, modes and process must match', () => {
  const cases = [
    [report({ trial: 2 }, { conditions: 'different' }), 'conditions_mismatch'],
    [report({ column: 'CPUFrameTime' }), 'frameColumn_mismatch'],
    [report({ frameTime: 20 }), 'duration_mismatch'],
    [report({ process: 'other.exe' }, { process: 'other.exe' }), 'process_mismatch'],
    [analyzeFrames(csv({ trial: 2 }).replaceAll('Hardware: Independent Flip', 'Composed: Flip'), options), 'presentation_modes_mismatch']
  ];
  for (const [candidate, reason] of cases) assert.ok(compareFrames([report()], [candidate]).reasons.includes(reason));
});
test('three distinct runs each side remain unverified despite repetition', () => {
  const actual = compareFrames([1, 2, 3].map(trial => report({ trial })), [4, 5, 6].map(trial => report({ trial, frameTime: 9.5 })));
  assert.equal(actual.repetition, 'repeated');
  assert.equal(actual.recommendation, 'insufficient_evidence');
});
test('re-sealed source hashes must remain strings, not coercible arrays', () => {
  const { id, ...payload } = report();
  assert.throws(() => validateFrames(seal({ ...payload, sourceHash: [payload.sourceHash] })), /source hash/);
});
test('generated reports stay within the same frame and mode bounds as the reader', () => {
  const modes = 'Application,MsBetweenPresents,PresentMode\n' + Array.from({ length: 120 }, (_, i) => `game.exe,10,Mode${i}`).join('\n');
  assert.throws(() => analyzeFrames(modes, options), /modes/);
  const tooMany = 'Application,MsBetweenPresents\n' + 'a,1\n'.repeat(1000001);
  assert.throws(() => analyzeFrames(tooMany, { ...options, process: 'a' }), /1000000/);
});
test('report seals detect accidents but schema validation also rejects re-sealed malformed reports', () => {
  const tampered = report(); tampered.metrics.averageFps = 1000;
  assert.throws(() => validateFrames(tampered), /integrity/);
  const { id, ...payload } = tampered;
  assert.throws(() => validateFrames(seal(payload)), /Inconsistent/);
  assert.throws(() => validateFrames(seal({ ...payload, extra: 'unexpected' })), /unexpected/);
});
