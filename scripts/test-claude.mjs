import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtemp, rm, realpath } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { parseArgs } from 'node:util';
import { fileURLToPath } from 'node:url';

const { values } = parseArgs({ options: { cli: { type: 'string' } }, strict: true });
assert.ok(values.cli && path.isAbsolute(values.cli), 'Usage: npm run test:claude -- --cli <absolute path to installed Claude executable or cli.js>');
const cli = await realpath(values.cli);
const root = fileURLToPath(new URL('../', import.meta.url));
const directory = await mkdtemp(path.join(tmpdir(), 'neurotune-claude-'));
try {
  const env = Object.fromEntries(Object.entries(process.env).filter(([name]) => ['PATH', 'SYSTEMROOT', 'WINDIR', 'COMSPEC', 'PATHEXT', 'TEMP', 'TMP', 'LANG'].includes(name.toUpperCase())));
  Object.assign(env, { HOME: directory, USERPROFILE: directory, APPDATA: path.join(directory, 'roaming'),
    LOCALAPPDATA: path.join(directory, 'local'), CLAUDE_CONFIG_DIR: path.join(directory, 'config'),
    CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC: '1', DISABLE_AUTOUPDATER: '1', NO_COLOR: '1' });
  function run(args) {
    const isScript = /\.(?:mjs|cjs|js)$/iu.test(cli);
    const result = spawnSync(isScript ? process.execPath : cli, isScript ? [cli, ...args] : args,
      { cwd: directory, env, encoding: 'utf8', timeout: 45000, maxBuffer: 2 * 1024 * 1024 });
    assert.equal(result.error, undefined, 'Claude startup timed out or failed.');
    assert.equal(result.status, 0, 'Claude command failed. Check installed CLI compatibility.');
    return result.stdout;
  }
  const validation = JSON.parse(run(['plugin', 'validate', root, '--strict', '--json']));
  assert.equal(validation.success, true);
  assert.deepEqual(validation.manifest.errors, []);
  assert.deepEqual(validation.manifest.warnings, []);
  const details = run(['--plugin-dir', root, 'plugin', 'details', 'neurotune']);
  assert.match(details, /Skills\s+\(1\)\s+neurotune/u);
  for (const component of ['Agents', 'Hooks', 'MCP servers', 'LSP servers']) {
    assert.match(details, new RegExp(`${component}\\s+\\(0\\)`));
  }
  console.log('PASS: Claude strict manifest validation and actual plugin inventory (one skill, no agents/hooks/servers). No prompt or eval was submitted.');
} finally { await rm(directory, { recursive: true, force: true }); }
