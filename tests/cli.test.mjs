import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtemp, writeFile, readFile, rm, mkdir, symlink } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { MAX_REPORT_BYTES, readText, writeJson, sha256 } from '../skills/neurotune/scripts/io.mjs';
import { csv, rawInventory } from './fixtures.mjs';
import { normalizeInventory } from '../skills/neurotune/scripts/evidence.mjs';

const root = fileURLToPath(new URL('../', import.meta.url));
const cli = path.join(root, 'skills/neurotune/scripts/cli.mjs');
function run(args, executable = cli, cwd = root) {
  const result = spawnSync(process.execPath, [executable, ...args], { encoding: 'utf8', cwd, timeout: 30000 });
  assert.equal(result.error, undefined);
  return result;
}
async function temporary(t) {
  const dir = await mkdtemp(path.join(tmpdir(), 'neurotune-test-'));
  t.after(() => rm(dir, { recursive: true, force: true }));
  return dir;
}
test('help and doctor use JSON without touching Windows or a provider', () => {
  for (const command of ['help', 'doctor']) {
    const result = run([command]);
    assert.equal(result.status, 0); assert.equal(result.stderr, '');
    assert.equal(JSON.parse(result.stdout).ok, true);
  }
  assert.equal(JSON.parse(run(['doctor']).stdout).data.systemWrites, false);
});
test('unknown commands, duplicate options and untrusted arguments return safe errors', () => {
  for (const args of [['apply'], ['doctor', '--extra', 'PRIVATE_SENTINEL'], ['evidence', '--input', 'a', '--input', 'b'], ['frames']]) {
    const result = run(args);
    assert.equal(result.status, 1);
    assert.equal(JSON.parse(result.stdout).ok, false);
    assert.equal(result.stdout.includes('PRIVATE_SENTINEL'), false);
  }
});
test('paths with spaces and shell metacharacters remain data; existing output is preserved', async t => {
  const dir = await temporary(t), input = path.join(dir, 'capture & data.csv'), out = path.join(dir, 'report.json');
  await writeFile(input, csv());
  const args = ['frames', '--input', input, '--process', 'game.exe', '--conditions', 'Same scene; not a shell command', '--out', out];
  const result = run(args);
  assert.equal(result.status, 0, result.stdout);
  const before = await readFile(out, 'utf8');
  const executable = path.join(dir, 'report.cmd');
  const rejected = run([...args.slice(0, -1), executable]);
  assert.equal(rejected.status, 1);
  assert.equal(JSON.parse(rejected.stdout).error.code, 'invalid_input');
  await assert.rejects(readFile(executable), { code: 'ENOENT' });
  assert.equal(run(args).status, 1);
  assert.equal(await readFile(out, 'utf8'), before);
  const facts = run(['evidence', '--input', out]);
  assert.equal(JSON.parse(facts.stdout).data.facts.length, 8);
  const comparison = run(['compare', '--baseline', out, '--candidate', out]);
  assert.deepEqual(JSON.parse(comparison.stdout).data.reasons, ['duplicate_source']);
});
test('the CSV command rejects CapFrameX JSON rather than guessing its format', async t => {
  const dir = await temporary(t), input = path.join(dir, 'capture.json'), out = path.join(dir, 'report.json');
  await writeFile(input, JSON.stringify({ ProcessName: 'game.exe', PRIVATE_SENTINEL: [8, 9, 10] }));
  const result = run(['frames', '--input', input, '--process', 'game.exe', '--conditions', 'Synthetic scene', '--out', out]);
  assert.equal(result.status, 1);
  assert.equal(JSON.parse(result.stdout).error.code, 'invalid_input');
  assert.equal(result.stdout.includes('PRIVATE_SENTINEL'), false);
  await assert.rejects(readFile(out), { code: 'ENOENT' });
});

test('the explicit CapFrameX command creates bounded evidence without exposing session metadata', async t => {
  const dir = await temporary(t), input = path.join(dir, 'capture.json'), out = path.join(dir, 'report.json');
  await writeFile(input, '\ufeff' + JSON.stringify({ Info: { ProcessName: 'game.exe', DeviceName: 'PRIVATE_SENTINEL' },
    Runs: [{ CaptureData: { MsBetweenPresents: Array(120).fill(10) } }] }));
  const args = ['frames-capframex', '--input', input, '--process', 'game.exe', '--conditions', 'Synthetic scene', '--out', out];
  const result = run(args);
  assert.equal(result.status, 0, result.stdout);
  assert.equal(JSON.parse(result.stdout).data.metrics.averageFps, 100);
  assert.deepEqual(JSON.parse(result.stdout).data.streamIdentity, { processIdPresent: false, swapChainPresent: false });
  assert.equal(result.stdout.includes('PRIVATE_SENTINEL'), false);
  const original = await readFile(out, 'utf8');
  assert.equal(JSON.parse(original).sourceHash, sha256(await readFile(input)));
  assert.equal(original.includes('PRIVATE_SENTINEL'), false);
  assert.equal(run(args).status, 1);
  assert.equal(await readFile(out, 'utf8'), original);
});

test('large valid Unicode inventory remains usable by evidence and plan-check', async t => {
  const dir = await temporary(t), input = path.join(dir, 'inventory.json'), proposal = path.join(dir, 'plan.json');
  const raw = structuredClone(rawInventory);
  const name = '\ud800'.repeat(256); // Lone UTF-16 surrogates take six bytes each when JSON-escaped.
  raw.processors = Array.from({ length: 64 }, () => ({ name, physicalCores: 1, logicalProcessors: 1 }));
  raw.gpus = Array.from({ length: 64 }, () => ({ name, vendor: name, driverVersion: name }));
  const report = normalizeInventory(raw);
  await writeJson(input, report);
  const size = (await readFile(input)).length;
  assert.ok(size > 128 * 1024 && size < MAX_REPORT_BYTES);
  const facts = run(['evidence', '--input', input]);
  assert.equal(facts.status, 0, facts.stdout);
  assert.equal(JSON.parse(facts.stdout).data.reportId, report.id);
  await writeJson(proposal, { schemaVersion: 1, goal: 'Synthetic investigation', hypotheses: [{
    id: 'capture', claim: 'Inventory needs workload evidence.', evidenceIds: [`${report.id}:processors`],
    uncertainty: 'No workload measured.', nextMeasurement: 'Capture a repeatable scene.', risk: 'unknown', rollback: 'No changes.'
  }] });
  const result = run(['plan-check', '--input', proposal, '--evidence', input]);
  assert.equal(result.status, 0, result.stdout);
  assert.equal(JSON.parse(result.stdout).data.executable, false);
});

test('report readers retain a 512 KiB limit while plans keep their 128 KiB budget', async t => {
  const dir = await temporary(t), input = path.join(dir, 'oversized.json'), valid = path.join(dir, 'inventory.json');
  await writeJson(valid, normalizeInventory(rawInventory));
  await writeFile(input, ' '.repeat(512 * 1024 + 1));
  for (const args of [
    ['evidence', '--input', input],
    ['compare', '--baseline', input, '--candidate', valid],
    ['plan-check', '--input', valid, '--evidence', input]
  ]) {
    const result = run(args);
    assert.equal(result.status, 1);
    assert.match(JSON.parse(result.stdout).error.message, /size limit/);
  }
  await writeFile(input, ' '.repeat(128 * 1024 + 1));
  const result = run(['plan-check', '--input', input, '--evidence', valid]);
  assert.equal(result.status, 1);
  assert.match(JSON.parse(result.stdout).error.message, /size limit/);
});

test('both capture commands produce readable reports at the minimum interval', async t => {
  const dir = await temporary(t);
  for (const [command, content] of [
    ['frames', csv({ frameTime: 0.05 })],
    ['frames-capframex', JSON.stringify({ Info: { ProcessName: 'game.exe' }, Runs: [{ CaptureData: { MsBetweenPresents: Array(120).fill(0.05) } }] })]
  ]) {
    const input = path.join(dir, `${command}.txt`), out = path.join(dir, `${command}.json`);
    await writeFile(input, content);
    const result = run([command, '--input', input, '--process', 'game.exe', '--conditions', 'Synthetic scene', '--out', out]);
    assert.equal(result.status, 0, result.stdout);
    const facts = run(['evidence', '--input', out]);
    assert.equal(facts.status, 0, facts.stdout);
    assert.equal(JSON.parse(facts.stdout).data.facts.find(fact => fact.id.endsWith(':p50Ms')).value, 0.05);
  }
});

test('JSON and file errors never echo file contents', async t => {
  const dir = await temporary(t), input = path.join(dir, 'private.json');
  await writeFile(input, '{PRIVATE_SENTINEL');
  const result = run(['evidence', '--input', input]);
  assert.equal(result.status, 1); assert.equal(result.stdout.includes('PRIVATE_SENTINEL'), false);
});
test('bounded reads reject oversized, non-UTF8 and non-regular input', async t => {
  const dir = await temporary(t), input = path.join(dir, 'input');
  await writeFile(input, '123456');
  await assert.rejects(readText(input, 5), /size limit/);
  await writeFile(input, Buffer.from([0xc3, 0x28]));
  await assert.rejects(readText(input), /UTF-8/);
  await assert.rejects(readText(dir));
});
test('capture hashes preserve the original UTF-8 BOM while JSON reports remain readable', async t => {
  const dir = await temporary(t), input = path.join(dir, 'capture.csv'), out = path.join(dir, 'report.json');
  await writeFile(input, '\ufeff' + csv());
  assert.ok((await readText(input)).startsWith('\ufeff'));
  const result = run(['frames', '--input', input, '--process', 'game.exe', '--conditions', 'Synthetic scene', '--out', out]);
  assert.equal(result.status, 0, result.stdout);
  const contents = await readFile(out, 'utf8');
  assert.equal(JSON.parse(contents).sourceHash, sha256(await readFile(input)));
  await writeFile(out, '\ufeff' + contents);
  assert.equal(run(['evidence', '--input', out]).status, 0);
});

test('POSIX named-pipe input is rejected without waiting for a writer', { skip: process.platform === 'win32' }, async t => {
  const dir = await temporary(t), input = path.join(dir, 'input.fifo');
  const created = spawnSync('mkfifo', [input], { encoding: 'utf8', timeout: 5000 });
  assert.equal(created.status, 0, created.stderr);
  const result = spawnSync(process.execPath, [cli, 'evidence', '--input', input], { encoding: 'utf8', timeout: 5000 });
  assert.equal(result.error, undefined, 'A non-regular input must fail, not block during open.');
  assert.equal(result.status, 1);
  assert.equal(JSON.parse(result.stdout).error.code, 'invalid_input');
});

test('exclusive writes refuse existing files and linked output ancestors', async t => {
  const dir = await temporary(t), file = path.join(dir, 'existing.json');
  await writeFile(file, 'keep');
  await assert.rejects(writeJson(file, { changed: true }), { code: 'EEXIST' });
  const real = path.join(dir, 'real'), link = path.join(dir, 'link');
  await mkdir(real);
  await symlink(real, link, process.platform === 'win32' ? 'junction' : 'dir');
  await assert.rejects(writeJson(path.join(link, 'out.json'), {}), /links|junctions/);
});
test('report writers reject executable filenames and alternate data streams before creation', async t => {
  const dir = await temporary(t);
  for (const name of ['report.cmd', 'report.ps1', 'report.json.cmd', 'report.cmd:payload.json', 'stream:alias/report.json']) {
    const file = path.join(dir, name);
    await assert.rejects(writeJson(file, { value: 'Only report data.' }), /\.json/);
    await assert.rejects(readFile(file), { code: 'ENOENT' });
  }
});

test('installer creates a self-contained skill, works from unrelated cwd, and refuses overwrite', async t => {
  const dir = await temporary(t), installer = path.join(root, 'scripts/install.mjs');
  const args = ['--skills-dir', path.join(dir, 'skills space')];
  let result = run(args, installer, dir);
  assert.equal(result.status, 0, result.stderr);
  const installed = JSON.parse(result.stdout).installed;
  result = run(['doctor'], path.join(installed, 'scripts/cli.mjs'), dir);
  assert.equal(result.status, 0, result.stdout);
  assert.equal(JSON.parse(result.stdout).data.systemWrites, false);
  assert.equal(run(args, installer, dir).status, 1);
  assert.ok((await readFile(path.join(installed, 'SKILL.md'), 'utf8')).includes('name: neurotune'));
});
