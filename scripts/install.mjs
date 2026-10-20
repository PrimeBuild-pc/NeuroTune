#!/usr/bin/env node
import { cp, mkdir, readdir, rm } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseArgs } from 'node:util';
import { rejectLinkedAncestors } from '../skills/neurotune/scripts/io.mjs';

try {
  const { values, tokens } = parseArgs({ options: { 'skills-dir': { type: 'string' } }, tokens: true });
  if (!values['skills-dir']?.trim() || tokens.length !== 1) throw new Error('Usage: node scripts/install.mjs --skills-dir <harness skills directory>');
  const parent = path.resolve(values['skills-dir']);
  const target = path.join(parent, 'neurotune');
  const source = fileURLToPath(new URL('../skills/neurotune', import.meta.url));
  if (target === source || parent.startsWith(`${source}${path.sep}`)) throw new Error('Install outside the source skill directory.');
  await rejectLinkedAncestors(target);
  await mkdir(parent, { recursive: true });
  await mkdir(target); // Exclusive reservation: never overwrite an installed skill.
  try {
    for (const name of await readdir(source)) {
      await cp(path.join(source, name), path.join(target, name), { recursive: true, force: false, errorOnExist: true });
    }
  } catch (error) { await rm(target, { recursive: true, force: true }); throw error; }
  console.log(JSON.stringify({ ok: true, installed: target, next: 'Reload your harness. Read SKILL.md before use.' }));
} catch (error) {
  console.error(error.code === 'EEXIST' ? 'Skill already exists. Review and move the old directory before upgrading; no files were replaced.' : (error.code ? 'Installation failed. Check directory permissions; no existing skill was replaced.' : error.message));
  process.exitCode = 1;
}
