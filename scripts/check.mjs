import assert from 'node:assert/strict';
import { readdir, readFile, access } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { createHash } from 'node:crypto';

const root = fileURLToPath(new URL('../', import.meta.url));
async function files(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  return (await Promise.all(entries.filter(entry => !['.git', 'graphify-out', 'node_modules', 'artifacts'].includes(entry.name)).map(entry => {
    const file = path.join(directory, entry.name);
    return entry.isDirectory() ? files(file) : [file];
  }))).flat();
}
const all = await files(root);
for (const file of all.filter(file => file.endsWith('.mjs'))) {
  const result = spawnSync(process.execPath, ['--check', file], { encoding: 'utf8' });
  assert.equal(result.status, 0, result.stderr);
}
for (const file of all.filter(file => file.endsWith('.md'))) {
  const contents = await readFile(file, 'utf8');
  for (const match of contents.matchAll(/\]\(([^)]+)\)/gu)) {
    if (/^(?:https?:|#)/u.test(match[1])) continue;
    await access(path.resolve(path.dirname(file), match[1].split('#')[0]));
  }
}
const skill = await readFile(path.join(root, 'skills/neurotune/SKILL.md'), 'utf8');
assert.match(skill, /^---\r?\nname: neurotune\r?\ndescription: .+\r?\n/u);
assert.ok(skill.match(/^description: (.+)$/mu)[1].length <= 1024);
const pkg = JSON.parse(await readFile(path.join(root, 'package.json'), 'utf8'));
assert.ok(!pkg.dependencies && !pkg.devDependencies, 'This milestone intentionally has no npm dependencies.');
assert.ok(skill.includes(`version: ${pkg.version}`));
const collector = await readFile(path.join(root, 'skills/neurotune/scripts/Collect-Hardware.ps1'));
assert.equal(createHash('sha256').update(collector).digest('hex'), 'e96f937e563348a9c5c20d05412647c8b9eecaa56d6ce574245379084367dd40', 'Re-review the collector and update provenance before changing its pinned bytes.');
console.log('PASS: JavaScript syntax, Markdown links, skill metadata, dependency policy and collector provenance.');
