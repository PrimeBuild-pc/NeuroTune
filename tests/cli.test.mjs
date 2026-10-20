import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtemp, writeFile, readFile, rm, mkdir, symlink } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { readText, writeJson } from '../skills/neurotune/scripts/io.mjs';
import { csv } from './fixtures.mjs';

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
  assert.equal(run(args).status, 1);
  assert.equal(await readFile(out, 'utf8'), before);
  const facts = run(['evidence', '--input', out]);
  assert.equal(JSON.parse(facts.stdout).data.facts.length, 8);
  const comparison = run(['compare', '--baseline', out, '--candidate', out]);
  assert.deepEqual(JSON.parse(comparison.stdout).data.reasons, ['duplicate_source']);
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
test('exclusive writes refuse existing files and linked output ancestors', async t => {
  const dir = await temporary(t), file = path.join(dir, 'existing');
  await writeFile(file, 'keep');
  await assert.rejects(writeJson(file, { changed: true }), { code: 'EEXIST' });
  const real = path.join(dir, 'real'), link = path.join(dir, 'link');
  await mkdir(real);
  await symlink(real, link, process.platform === 'win32' ? 'junction' : 'dir');
  await assert.rejects(writeJson(path.join(link, 'out.json'), {}), /links|junctions/);
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
