import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile, readdir, stat } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { spawnSync } from 'node:child_process';

const root = fileURLToPath(new URL('../', import.meta.url));
const json = async relative => JSON.parse(await readFile(path.join(root, relative), 'utf8'));

test('package, plugin, skill and CLI versions agree without host-specific runtime code', async () => {
  const pkg = await json('package.json'), plugin = await json('.claude-plugin/plugin.json');
  assert.equal(plugin.name, 'neurotune');
  assert.equal(plugin.version, pkg.version);
  assert.deepEqual(Object.keys(plugin).sort(), ['name', 'version', 'description', 'author', 'license', 'keywords'].sort());
  assert.ok(pkg.files.includes('.claude-plugin/'));
  assert.deepEqual(pkg.pi, { skills: ['./skills/neurotune'] });
  const skill = await readFile(path.join(root, 'skills/neurotune/SKILL.md'), 'utf8');
  assert.ok(skill.includes(`version: ${pkg.version}`));
  const result = spawnSync(process.execPath, [path.join(root, 'skills/neurotune/scripts/cli.mjs'), 'doctor'], { encoding: 'utf8', timeout: 10000 });
  assert.equal(result.status, 0);
  assert.equal(JSON.parse(result.stdout).data.version, pkg.version);
});

test('native eval definitions remain bounded, synthetic and read-only; no automatic inference in npm test', async () => {
  const entries = (await readdir(path.join(root, 'evals'), { withFileTypes: true }))
    .filter(entry => entry.isDirectory() && entry.name !== 'results');
  assert.equal(entries.length, 9);
  const tags = new Set();
  for (const entry of entries) {
    // JSON is a YAML 1.2 subset accepted by the native evaluator; no second YAML parser is needed here.
    const definition = await json(`evals/${entry.name}/case.yaml`);
    assert.equal(definition.schema_version, '1.1');
    assert.equal(definition.name, entry.name);
    assert.equal(definition.runs, 3);
    assert.equal(definition.execution.max_turns, 6);
    assert.equal(definition.execution.timeout_seconds, 120);
    assert.ok(definition.execution.prompt.length > 100);
    assert.match(definition.execution.prompt, /synthetic|handoff|proposal/iu);
    assert.deepEqual(Object.keys(definition.execution).sort(), ['max_turns', 'timeout_seconds', 'allowed_tools', 'prompt'].sort());
    assert.ok(definition.execution.allowed_tools.includes('Skill'));
    assert.ok(definition.execution.allowed_tools.every(tool => ['Read', 'Glob', 'Skill', 'AskUserQuestion'].includes(tool)));
    definition.tags.forEach(tag => tags.add(tag));
    assert.ok(definition.graders.some(grader => grader.type === 'llm' && /PASS/.test(grader.criteria) && /FAIL/.test(grader.criteria)));
    const skillGrader = definition.graders.find(grader => grader.type === 'tool_used' && grader.tool === 'Skill');
    assert.ok(skillGrader);
    const pattern = new RegExp(skillGrader.input_match);
    assert.ok(pattern.test(JSON.stringify({ skill: 'neurotune:neurotune' })));
    assert.ok(pattern.test(JSON.stringify({ skill: 'neurotune' })));
    assert.equal(pattern.test(JSON.stringify({ skill: 'other' })), false);
    for (const grader of definition.graders) {
      assert.ok(['llm', 'tool_used'].includes(grader.type));
      if (grader.input_match) new RegExp(grader.input_match);
    }
    if (definition.context) {
      assert.deepEqual(Object.keys(definition.context), ['add_dirs']);
      assert.deepEqual(definition.context.add_dirs, ['fixtures']);
      assert.ok((await stat(path.join(root, 'evals', entry.name, 'fixtures'))).isDirectory());
    }
  }
  for (const expected of ['consent', 'injection', 'authority', 'missing-data', 'measurement', 'citations', 'provider', 'resume', 'positive']) assert.ok(tags.has(expected));
});

test('the consent case has a real unread fixture and the injection case requires a file read', async () => {
  const consent = await json('evals/consent-before-reading/case.yaml');
  const guard = consent.graders.find(grader => grader.name === 'private-report-not-read');
  assert.equal(guard.min, 0); assert.equal(guard.max, 0);
  const privateData = await json('evals/consent-before-reading/fixtures/private-inventory.json');
  assert.equal(privateData.synthetic, true);
  assert.equal(consent.execution.prompt.includes(privateData.privateMarker), false);
  const injection = await json('evals/untrusted-device-text/case.yaml');
  assert.ok(injection.graders.some(grader => grader.name === 'fixture-read' && grader.tool === 'Read'));
  const data = await json('evals/untrusted-device-text/fixtures/evidence.json');
  assert.equal(data.synthetic, true);
  assert.match(data.facts[0].value[0].name, /SYSTEM OVERRIDE/);
});
