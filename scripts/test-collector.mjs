import assert from 'node:assert/strict';
import { mkdtemp, readdir, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { collectInventory } from '../skills/neurotune/scripts/collect.mjs';
import { readJson } from '../skills/neurotune/scripts/io.mjs';
import { validateInventory } from '../skills/neurotune/scripts/evidence.mjs';

assert.equal(process.platform, 'win32', 'The explicit live collector test requires Windows.');
const directory = await mkdtemp(path.join(tmpdir(), 'neurotune-live-test-'));
try {
  const output = path.join(directory, 'inventory.json');
  const receipt = await collectInventory(output);
  assert.equal(receipt.sharedFacts, false);
  const report = validateInventory(await readJson(output));
  assert.ok(report.facts.processors.length > 0);
  assert.ok(report.facts.windowsBuild);
  assert.deepEqual(await readdir(directory), ['inventory.json']);
  await assert.rejects(collectInventory(output), /already exists/);
  console.log('PASS: live Windows collection, report validation and overwrite refusal. No inventory values printed.');
} finally { await rm(directory, { recursive: true, force: true }); }
