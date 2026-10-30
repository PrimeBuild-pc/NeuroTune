import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtemp, rm, readFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../', import.meta.url));
const npm = process.env.npm_execpath;
assert.ok(npm, 'Run through npm run test:package so the installed npm CLI is used.');
const directory = await mkdtemp(path.join(tmpdir(), 'neurotune-package-'));
function run(file, args, cwd = directory) {
  const result = spawnSync(process.execPath, [file, ...args], { cwd, encoding: 'utf8', timeout: 60000 });
  assert.equal(result.status, 0, result.stderr || result.stdout || String(result.error));
  return result.stdout;
}
try {
  const [metadata] = JSON.parse(run(npm, ['pack', '--ignore-scripts', '--json', '--pack-destination', directory], root));
  assert.ok(metadata.files.some(file => file.path === 'skills/neurotune/SKILL.md'));
  assert.ok(metadata.files.some(file => file.path === '.claude-plugin/plugin.json'));
  assert.ok(metadata.files.some(file => file.path === 'skills/neurotune/agents/openai.yaml'));
  for (const file of ['scripts/assessment.mjs', 'scripts/proposal.mjs', 'references/assessment.md', 'scripts/game-configs.mjs', 'references/game-configs.md']) {
    assert.ok(metadata.files.some(entry => entry.path === `skills/neurotune/${file}`));
  }
  assert.ok(metadata.files.every(file => !/^(?:tests|evals|native|graphify-out|artifacts|node_modules)\//u.test(file.path)));
  const prefix = path.join(directory, 'offline-install');
  run(npm, ['install', '--prefix', prefix, '--offline', '--ignore-scripts', '--no-audit', '--no-fund', path.join(directory, metadata.filename)]);
  const installed = path.join(prefix, 'node_modules/neurotune-skill');
  assert.deepEqual(JSON.parse(await readFile(path.join(installed, '.claude-plugin/plugin.json'), 'utf8')),
    JSON.parse(await readFile(path.join(root, '.claude-plugin/plugin.json'), 'utf8')));
  const doctor = JSON.parse(run(path.join(installed, 'skills/neurotune/scripts/cli.mjs'), ['doctor']));
  assert.equal(doctor.data.systemWrites, false);
  const result = JSON.parse(run(path.join(installed, 'scripts/install.mjs'), ['--skills-dir', path.join(directory, 'standalone')]));
  assert.ok((await readFile(path.join(result.installed, 'SKILL.md'), 'utf8')).includes('name: neurotune'));
  assert.equal(JSON.parse(run(path.join(result.installed, 'scripts/cli.mjs'), ['doctor'])).ok, true);
  for (const command of ['goals', 'tools']) {
    assert.equal(JSON.parse(run(path.join(result.installed, 'scripts/cli.mjs'), [command])).ok, true);
  }
  for (const base of [path.join(installed, 'skills/neurotune'), result.installed]) {
    const catalogue = JSON.parse(run(path.join(base, 'scripts/cli.mjs'), ['game-configs', '--game', 'all']));
    assert.equal(catalogue.data.games.length, 13);
    assert.equal(catalogue.data.systemWrites, false);
    assert.ok((await readFile(path.join(base, catalogue.data.reference), 'utf8')).includes('inventory → assessment → proposal'));
  }
  assert.equal(await readFile(path.join(result.installed, 'agents/openai.yaml'), 'utf8'),
    await readFile(path.join(root, 'skills/neurotune/agents/openai.yaml'), 'utf8'));
  console.log(`PASS: packed ${metadata.files.length} files, installed archive offline, executed CLI and copied standalone skill from an unrelated directory.`);
} finally { await rm(directory, { recursive: true, force: true }); }
