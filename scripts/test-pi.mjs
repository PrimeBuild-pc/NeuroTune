import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtemp, mkdir, writeFile, rm, realpath } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { parseArgs } from 'node:util';
import { fileURLToPath } from 'node:url';

const { values } = parseArgs({ options: { cli: { type: 'string' } }, strict: true });
assert.ok(values.cli, 'Usage: npm run test:pi -- --cli <absolute path to the installed Pi dist/cli.js>');
assert.ok(path.isAbsolute(values.cli), 'Use the installed Pi entry point, not a shell command.');
const cli = await realpath(values.cli);
const root = fileURLToPath(new URL('../', import.meta.url));
const directory = await mkdtemp(path.join(tmpdir(), 'neurotune-pi-'));
try {
  // Withhold credentials, user configuration, extensions and project context from the smoke process.
  const env = Object.fromEntries(Object.entries(process.env).filter(([name]) => ['PATH', 'SYSTEMROOT', 'WINDIR', 'COMSPEC', 'PATHEXT', 'TEMP', 'TMP', 'LANG'].includes(name.toUpperCase())));
  Object.assign(env, { HOME: directory, USERPROFILE: directory, APPDATA: path.join(directory, 'roaming'),
    LOCALAPPDATA: path.join(directory, 'local'), PI_CODING_AGENT_DIR: path.join(directory, 'agent'),
    PI_OFFLINE: '1', PI_TELEMETRY: '0', PI_SKIP_VERSION_CHECK: '1', NO_COLOR: '1' });
  await mkdir(env.PI_CODING_AGENT_DIR);
  const args = [cli, '--mode', 'rpc', '--no-session', '--offline', '--no-extensions', '--no-mcp',
    '--no-tools', '--no-context-files', '--no-prompt-templates', '--no-themes', '--no-approve'];
  const input = [{ id: 'commands', type: 'get_commands' }, { id: 'usage', type: 'get_session_stats' }]
    .map(value => JSON.stringify(value)).join('\n') + '\n';
  for (const mode of ['explicit-skill', 'local-package']) {
    await writeFile(path.join(env.PI_CODING_AGENT_DIR, 'settings.json'), JSON.stringify({
      enableInstallTelemetry: false, enableAnalytics: false,
      ...(mode === 'local-package' ? { packages: [root] } : {})
    }));
    const extra = mode === 'explicit-skill' ? ['--no-skills', '--skill', path.join(root, 'skills/neurotune')] : [];
    const result = spawnSync(process.execPath, [...args, ...extra], { cwd: directory, env, input, encoding: 'utf8', timeout: 45000, maxBuffer: 2 * 1024 * 1024 });
    assert.equal(result.error, undefined, 'Pi startup timed out or failed.');
    assert.equal(result.status, 0, 'Pi exited unsuccessfully. Check installed CLI compatibility.');
    const records = result.stdout.split('\n').filter(line => line.trim()).map(line => JSON.parse(line));
    const commands = records.find(record => record.id === 'commands');
    assert.equal(commands?.success, true, 'Missing successful get_commands response.');
    assert.equal(commands.data.commands.length, 1, 'Unrelated resources leaked into the isolated smoke session.');
    const matches = commands.data.commands.filter(command => command.name === 'skill:neurotune');
    assert.equal(matches.length, 1, 'Exactly one NeuroTune skill must load.');
    assert.equal(matches[0].source, 'skill');
    assert.equal(await realpath(matches[0].sourceInfo.path), await realpath(path.join(root, 'skills/neurotune/SKILL.md')));
    if (mode === 'local-package') assert.equal(matches[0].sourceInfo.origin, 'package');
    const usage = records.find(record => record.id === 'usage');
    assert.equal(usage?.success, true, 'Missing successful session statistics.');
    assert.equal(usage.data.tokens.total, 0);
    assert.equal(usage.data.toolCalls, 0);
    assert.equal(usage.data.assistantMessages, 0);
    console.log(`PASS: Pi ${mode} discovery; no inference or tool calls.`);
  }
} finally { await rm(directory, { recursive: true, force: true }); }
