import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { gameConfigReferences } from '../skills/neurotune/scripts/game-configs.mjs';

const ids = ['cod-mw4', 'cod-bo7', 'battlefield-6', 'arc-raiders', 'delta-force', 'fortnite',
  'rainbow-six-siege-x', 'cs2', 'overwatch-2', 'valorant', 'apex-legends', 'escape-from-tarkov', 'pubg'];
const cli = fileURLToPath(new URL('../skills/neurotune/scripts/cli.mjs', import.meta.url));
function run(args, cwd) {
  const result = spawnSync(process.execPath, [cli, ...args], { encoding: 'utf8', timeout: 10000, cwd });
  assert.equal(result.error, undefined);
  return result;
}

test('all thirteen games have bounded discovery metadata, never preset values or evidence claims', () => {
  const result = gameConfigReferences('all');
  assert.deepEqual(result.games.map(game => game.id), ids);
  assert.equal(result.kind, 'game-config-reference');
  assert.equal(result.systemWrites, false);
  assert.equal(result.filesRead, false);
  assert.equal(result.presetsProvided, false);
  assert.equal(JSON.stringify(result).length < 32 * 1024, true);
  for (const game of result.games) {
    assert.deepEqual(Object.keys(game).sort(), ['id', 'title', 'editPolicy', 'locations', 'keyHints', 'caveats', 'sources'].sort());
    assert.ok(game.locations.length > 0 && game.locations.length <= 3);
    assert.ok(game.caveats.length > 0 && game.sources.length > 0);
    assert.equal(new Set(game.keyHints).size, game.keyHints.length);
    assert.ok(game.keyHints.every(key => typeof key === 'string' && key.length <= 128 && !key.includes('=')));
    for (const location of game.locations) {
      assert.deepEqual(Object.keys(location).sort(), ['directory', 'files', 'format']);
      assert.ok(location.directory.length > 0 && location.files.length > 0 && location.format.length > 0);
    }
    for (const source of game.sources) assert.equal(new URL(source).protocol, 'https:');
    assert.deepEqual(gameConfigReferences(game.id).games, [game]);
  }
});

test('returned metadata cannot mutate future lookups and invalid IDs fail without reflection', () => {
  const first = gameConfigReferences('cod-bo7');
  const original = structuredClone(first);
  first.games[0].locations[0].files.push('MUTATED');
  first.games[0].keyHints.push('MUTATED');
  first.games[0].caveats.length = 0;
  assert.deepEqual(gameConfigReferences('cod-bo7'), original);
  for (const value of [undefined, null, '', '__proto__', '../../PRIVATE_SENTINEL', '$(PRIVATE_SENTINEL)', 'COD-BO7']) {
    assert.throws(() => gameConfigReferences(value), error => error.code === 'invalid_input' && !error.message.includes('PRIVATE_SENTINEL'));
  }
});

test('policy and format exceptions stay explicit instead of advertising generic write support', () => {
  const lookup = id => gameConfigReferences(id).games[0];
  assert.equal(lookup('delta-force').editPolicy, 'in-game-only');
  assert.deepEqual(lookup('delta-force').keyHints, []);
  assert.match(lookup('delta-force').sources[0], /store\.steampowered\.com\/news/u);
  assert.match(lookup('delta-force').caveats.join(' '), /permanent bans/u);
  assert.equal(lookup('cod-mw4').editPolicy, 'verify-build-first');
  assert.match(lookup('cod-mw4').caveats.join(' '), /no first-party/u);
  assert.match(lookup('battlefield-6').caveats.join(' '), /outside the existing native extension allowlist/u);
  assert.match(lookup('cs2').caveats.join(' '), /unsupported/u);
  assert.equal(lookup('escape-from-tarkov').locations[0].format, 'json-in-ini');
  assert.match(lookup('arc-raiders').caveats.join(' '), /binary/u);
});

test('CLI lookup works without local game files, preserves files and rejects extra/write options', async t => {
  const directory = await mkdtemp(path.join(tmpdir(), 'neurotune-game-configs-'));
  t.after(() => rm(directory, { recursive: true, force: true }));
  const file = path.join(directory, 'GameUserSettings.ini');
  await writeFile(file, 'PRIVATE_SENTINEL=unchanged\r\n');
  for (const id of ['all', ...ids]) {
    const result = run(['game-configs', '--game', id], directory);
    assert.equal(result.status, 0, result.stdout);
    assert.equal(result.stderr, '');
    assert.deepEqual(JSON.parse(result.stdout), { ok: true, data: gameConfigReferences(id) });
    assert.equal(result.stdout.includes('PRIVATE_SENTINEL'), false);
  }
  for (const args of [[], ['--game', 'PRIVATE_SENTINEL'], ['--game', 'all', '--game', 'cs2'],
    ['--game', 'cs2', '--out', 'PRIVATE_SENTINEL'], ['--game', 'cs2', '--apply'], ['--game', 'cs2', file]]) {
    const result = run(['game-configs', ...args], directory);
    assert.equal(result.status, 1);
    assert.equal(JSON.parse(result.stdout).ok, false);
    assert.equal(result.stdout.includes('PRIVATE_SENTINEL'), false);
  }
  assert.deepEqual(await readdir(directory), ['GameUserSettings.ini']);
  assert.equal(await readFile(file, 'utf8'), 'PRIVATE_SENTINEL=unchanged\r\n');
  assert.ok(JSON.parse(run(['help'], directory).stdout).data.usage.some(usage => usage.includes('game-configs')));
});
