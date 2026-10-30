import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { mkdtemp, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { parseExecutionBundle, MAX_BUNDLE_BYTES } from '../skills/neurotune/scripts/execution.mjs';
import { sha256 } from '../skills/neurotune/scripts/io.mjs';
import { executionFixture } from './execution-fixture.mjs';

const verify = fileURLToPath(new URL('../skills/neurotune/scripts/verify-execution.mjs', import.meta.url));
function child(input, extra = []) {
  return spawnSync(process.execPath, ['--no-addons', '--disable-proto=throw', verify, ...extra], { input, encoding: 'utf8', timeout: 10000, maxBuffer: 2 * 1024 * 1024 });
}
function gameFileFixture() {
  const bundle = executionFixture(), step = bundle.proposal.steps[0];
  const filePath = 'C:\\Users\\NeuroTuneTest\\Saved Games\\Synthetic\\settings.ini';
  const original = sha256('setting=3\r\n'), desired = Buffer.from('setting=17\r\n');
  step.category = 'game-file'; step.target = `Windows file [UTF-8]\\${filePath}`;
  step.expectedState = `SHA-256 ${original}`; step.proposedChange = `Replace UTF-8 content with SHA-256 ${sha256(desired)}`;
  bundle.parameters = [{ stepId: step.id, path: filePath, expectedSha256: original, desiredUtf8Base64: desired.toString('base64') }];
  return bundle;
}
test('game-file intent binds exact reviewed path, original hash and UTF-8 bytes without authority', () => {
  const bundle = gameFileFixture(), raw = JSON.stringify(bundle), result = parseExecutionBundle(raw);
  assert.equal(result.executable, false); assert.equal(result.approvalGranted, false);
  assert.equal(result.request.kind, 'game-file-request'); assert.equal(result.request.schemaVersion, 1);
  assert.equal(result.sourceBundleHash, sha256(raw));
  assert.equal(result.request.operation.path, bundle.parameters[0].path);
  assert.equal(result.request.operation.expectedSha256, bundle.parameters[0].expectedSha256);
  assert.equal(result.request.operation.desiredUtf8Base64, bundle.parameters[0].desiredUtf8Base64);
});
test('game-file parameter substitutions cannot change reviewed path, before or after content', () => {
  for (const change of [value => { value.parameters[0].path = 'C:\\Users\\Other\\settings.ini'; },
    value => { value.parameters[0].expectedSha256 = 'f'.repeat(64); },
    value => { value.parameters[0].desiredUtf8Base64 = Buffer.from('different').toString('base64'); },
    value => { value.parameters[0].stepId = 'other'; }, value => { value.parameters[0].approved = true; }]) {
    const bundle = gameFileFixture(); change(bundle); assert.throws(() => parseExecutionBundle(JSON.stringify(bundle)));
  }
});
test('game-file parameter mapping rejects unsafe path shapes, invalid UTF-8 and oversized content', () => {
  for (const target of ['relative.ini', '\\\\server\\share\\settings.ini', 'C:\\test\\..\\settings.ini', 'C:\\test\\settings.ini:ads']) {
    const bundle = gameFileFixture(); bundle.parameters[0].path = target; bundle.proposal.steps[0].target = `Windows file [UTF-8]\\${target}`;
    assert.throws(() => parseExecutionBundle(JSON.stringify(bundle)));
  }
  for (const bytes of [Buffer.from([255]), Buffer.from('with\0nul'), Buffer.alloc(128 * 1024 + 1)]) {
    const bundle = gameFileFixture(); bundle.parameters[0].desiredUtf8Base64 = bytes.toString('base64');
    bundle.proposal.steps[0].proposedChange = `Replace UTF-8 content with SHA-256 ${sha256(bytes)}`;
    assert.throws(() => parseExecutionBundle(JSON.stringify(bundle)));
  }
  const bundle = gameFileFixture(); bundle.parameters[0].desiredUtf8Base64 += ' ';
  assert.throws(() => parseExecutionBundle(JSON.stringify(bundle)), /canonical/);
});
test('the first game-file adapter rejects mixed and multi-file selected batches', () => {
  const bundle = gameFileFixture(), second = structuredClone(bundle.proposal.steps[0]); second.id = 'second';
  bundle.proposal.steps.push(second); bundle.parameters.push({ ...bundle.parameters[0], stepId: 'second' });
  assert.throws(() => parseExecutionBundle(JSON.stringify(bundle)), /one game-file/);
  bundle.proposal.steps[1].category = 'registry';
  assert.throws(() => parseExecutionBundle(JSON.stringify(bundle)), /one game-file/);
});
test('the private bounded verifier returns inert game-file intent, not an execution token', () => {
  const result = child(JSON.stringify(gameFileFixture())); assert.equal(result.status, 0, result.stderr);
  const response = JSON.parse(result.stdout); assert.equal(response.ok, true);
  assert.equal(JSON.parse(response.requestJson).kind, 'game-file-request');
});
test('execution intent binds validated review, typed parameters and exact bundle bytes without authority', () => {
  const bundle = executionFixture(), raw = JSON.stringify(bundle), result = parseExecutionBundle(raw);
  assert.equal(result.executable, false); assert.equal(result.approvalGranted, false);
  assert.equal(result.request.schemaVersion, 2);
  assert.equal(result.request.sourceBundleHash, sha256(raw));
  const review = JSON.parse(result.request.reviewJson);
  assert.equal(result.request.reviewId, review.id);
  assert.equal(result.request.assessmentId, bundle.assessment.id);
  assert.deepEqual(result.request.operations[0].evidenceIds, bundle.proposal.steps[0].evidenceIds);
  assert.equal(result.request.operations[0].desiredDword, 7);
  assert.notEqual(parseExecutionBundle(' ' + raw).sourceBundleHash, result.sourceBundleHash);
  assert.equal(parseExecutionBundle('\ufeff' + raw).request.reviewId, result.request.reviewId);
});
test('a parameter cannot silently change reviewed target, type, value or selected scope', () => {
  for (const modify of [
    value => { value.parameters[0].keyPath = 'SOFTWARE\\Different'; },
    value => { value.parameters[0].valueName = 'Different'; },
    value => { value.parameters[0].desiredDword = 8; },
    value => { value.proposal.steps[0].category = 'powershell'; },
    value => { value.parameters[0].stepId = 'unselected'; },
    value => { value.parameters.push(structuredClone(value.parameters[0])); },
    value => { value.parameters[0].approved = true; },
    value => { value.approved = true; }
  ]) { const bundle = executionFixture(); modify(bundle); assert.throws(() => parseExecutionBundle(JSON.stringify(bundle))); }
});
test('execution bundles revalidate evidence and assessment, not a model-asserted review receipt', () => {
  for (const modify of [
    value => { value.reports[0].facts.processors[0].name = 'changed'; },
    value => { value.proposal.steps[0].evidenceIds = ['invented:processors']; },
    value => { value.assessment.coverage.frames = true; },
    value => { value.selection = ['missing']; },
    value => { value.proposal.steps[0].backup.verified = true; },
    value => { value.proposal.steps[0].dependsOn = ['missing']; }
  ]) { const bundle = executionFixture(); modify(bundle); assert.throws(() => parseExecutionBundle(JSON.stringify(bundle))); }
});
test('typed Registry intent rejects unsupported paths, coercion, ADS and duplicate targets', () => {
  for (const desired of [-1, 0x100000000, 1.5, '7', null]) {
    const bundle = executionFixture(); bundle.parameters[0].desiredDword = desired;
    assert.throws(() => parseExecutionBundle(JSON.stringify(bundle)));
  }
  for (const path of ['SYSTEM\\Control', 'SOFTWARE\\..\\Escape', 'SOFTWARE\\Colon:ads', 'SOFTWARE\\\\Empty']) {
    const bundle = executionFixture(); bundle.parameters[0].keyPath = path;
    assert.throws(() => parseExecutionBundle(JSON.stringify(bundle)));
  }
  const bundle = executionFixture();
  const second = structuredClone(bundle.proposal.steps[0]); second.id = 'second';
  bundle.proposal.steps.push(second); bundle.parameters.push({ ...bundle.parameters[0], stepId: 'second' });
  assert.throws(() => parseExecutionBundle(JSON.stringify(bundle)), /Duplicate Registry/);
});
test('dependency ordering is retained and omitted prerequisites are never added silently', () => {
  const bundle = executionFixture(); const first = bundle.proposal.steps[0];
  const dependent = { ...structuredClone(first), id: 'dependent', dependsOn: [first.id], target: 'HKLM [Registry64]\\SOFTWARE\\NeuroTune.Skill.Tests\\Approval\\Second' };
  bundle.proposal.steps.unshift(dependent);
  bundle.parameters.unshift({ ...bundle.parameters[0], stepId: 'dependent', valueName: 'Second' });
  const result = parseExecutionBundle(JSON.stringify(bundle));
  assert.deepEqual(result.request.operations.map(operation => operation.id), ['synthetic', 'dependent']);
  bundle.selection = ['dependent']; bundle.parameters = [bundle.parameters[0]];
  assert.throws(() => parseExecutionBundle(JSON.stringify(bundle)), /dependencies/);
});
test('bundle and embedded artifact size limits remain enforced', () => {
  assert.throws(() => parseExecutionBundle(' '.repeat(MAX_BUNDLE_BYTES + 1)), /12 MiB/);
  assert.throws(() => parseExecutionBundle('{invalid'), /valid JSON/);
  const bundle = executionFixture(); bundle.proposal.padding = 'x'.repeat(128 * 1024);
  assert.throws(() => parseExecutionBundle(JSON.stringify(bundle)), /artifact limit/);
});
test('execution-check CLI exposes only inert validation, never confirmation flags', async () => {
  const directory = await mkdtemp(path.join(tmpdir(), 'neurotune-execution-'));
  try {
    const input = path.join(directory, 'bundle.json'); await writeFile(input, JSON.stringify(executionFixture()));
    const cli = fileURLToPath(new URL('../skills/neurotune/scripts/cli.mjs', import.meta.url));
    const good = spawnSync(process.execPath, [cli, 'execution-check', '--input', input], { encoding: 'utf8', timeout: 10000 });
    assert.equal(good.status, 0, good.stderr);
    const response = JSON.parse(good.stdout); assert.equal(response.ok, true); assert.equal(response.data.approvalGranted, false);
    const bad = spawnSync(process.execPath, [cli, 'execution-check', '--input', input, '--approved'], { encoding: 'utf8', timeout: 10000 });
    assert.equal(bad.status, 1); assert.equal(JSON.parse(bad.stdout).ok, false);
  } finally { await rm(directory, { recursive: true, force: true }); }
});
test('fixed stdio verifier accepts no flags, performs no writes and redacts invalid inputs', () => {
  const raw = JSON.stringify(executionFixture());
  const good = child(raw); assert.equal(good.status, 0, good.stderr);
  const response = JSON.parse(good.stdout);
  assert.equal(response.ok, true);
  assert.equal(JSON.parse(response.requestJson).sourceBundleHash, sha256(raw));
  for (const [input, extra] of [['{"PRIVATE_SENTINEL":true}', []], [raw, ['--approved']], [Buffer.from([0xff]), []]]) {
    const result = child(input, extra); assert.equal(result.status, 1);
    assert.deepEqual(JSON.parse(result.stdout), { ok: false, error: 'execution_bundle_rejected' });
    assert.equal(result.stdout.includes('PRIVATE_SENTINEL'), false);
  }
});
