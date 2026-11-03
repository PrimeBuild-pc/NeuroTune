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

test('runtime imports and optional Codex UI metadata keep the copied skill self-contained', async () => {
  const directory = path.join(root, 'skills/neurotune');
  const scripts = path.join(directory, 'scripts');
  for (const name of (await readdir(scripts)).filter(name => name.endsWith('.mjs'))) {
    const source = await readFile(path.join(scripts, name), 'utf8');
    for (const [, specifier] of source.matchAll(/\bfrom ['"]([^'"]+)['"]/gu)) {
      if (specifier.startsWith('node:')) continue;
      assert.ok(specifier.startsWith('./'), `${name}: runtime imports must stay within the skill.`);
      const resolved = path.resolve(scripts, specifier);
      assert.ok(!path.relative(directory, resolved).startsWith('..'), `${name}: import escapes the skill.`);
      assert.ok((await stat(resolved)).isFile());
    }
  }
  const metadata = await readFile(path.join(directory, 'agents/openai.yaml'), 'utf8');
  assert.match(metadata, /^interface:\r?\n  display_name: "NeuroTune"\r?\n/u);
  const description = metadata.match(/^  short_description: "(.+)"$/mu)?.[1];
  assert.ok(description && description.length >= 25 && description.length <= 64);
  assert.match(metadata, /^  default_prompt: ".*\$neurotune.*"$/mu);
  assert.equal(/^(?:dependencies|policy|model|tools):/mu.test(metadata), false);
});

test('native eval definitions remain bounded, synthetic and read-only; no automatic inference in npm test', async () => {
  const entries = (await readdir(path.join(root, 'evals'), { withFileTypes: true }))
    .filter(entry => entry.isDirectory() && entry.name !== 'results');
  assert.deepEqual(entries.map(entry => entry.name).sort(), [
    'consent-before-reading', 'descriptive-comparison', 'exact-fact-citations', 'invented-citation',
    'no-provider-fallback', 'no-shell-fallback', 'partial-inventory', 'rejected-comparison',
    'resume-without-consent', 'unsupported-measurement', 'untrusted-device-text'
  ]);
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
    for (const grader of definition.graders.filter(grader => grader.type === 'llm')) {
      assert.equal(grader.focus, 'trace', 'A safe final reply must not erase earlier disclosure, fabrication or workarounds.');
    }
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
      // add_dirs is not attached on native Windows runs; a scaffold copies the synthetic fixture into the workspace.
      assert.deepEqual(Object.keys(definition.context), ['scaffold_script']);
      const script = await readFile(path.join(root, 'evals', entry.name, definition.context.scaffold_script), 'utf8');
      assert.ok(script.startsWith('#!/usr/bin/env bash') && script.includes('cp "$(dirname "$0")/fixtures/'), 'Scaffold only copies the case fixture.');
      assert.ok((await stat(path.join(root, 'evals', entry.name, 'fixtures'))).isDirectory());
    }
  }
  for (const expected of ['consent', 'injection', 'authority', 'missing-data', 'measurement', 'citations', 'provider', 'resume', 'positive', 'capability']) assert.ok(tags.has(expected));
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
