import { createHash } from 'node:crypto';
import { open, lstat } from 'node:fs/promises';
import { constants } from 'node:fs';
import path from 'node:path';

export const MAX_BYTES = 10 * 1024 * 1024;
// Covers bounded inventory rows even when each text character needs six JSON bytes.
export const MAX_REPORT_BYTES = 512 * 1024;
export class InputError extends Error {
  constructor(message, code = 'invalid_input') { super(message); this.code = code; }
}
export function requireInput(condition, message) {
  if (!condition) throw new InputError(message);
}
export function text(value, max = 256) {
  return typeof value === 'string' && value.trim().length > 0 && value.length <= max && !/[\x00-\x1f\x7f]/u.test(value);
}
export function exactKeys(value, keys) {
  requireInput(value !== null && typeof value === 'object' && !Array.isArray(value), 'Expected an object.');
  requireInput(Object.keys(value).length === keys.length && keys.every(key => Object.hasOwn(value, key)), 'Missing or unexpected fields.');
}
export function sha256(value) { return createHash('sha256').update(value).digest('hex'); }
function canonical(value) {
  if (Array.isArray(value)) return value.map(canonical);
  if (value !== null && typeof value === 'object') {
    return Object.fromEntries(Object.keys(value).sort().map(key => [key, canonical(value[key])]));
  }
  return value;
}
export function seal(payload) { return { ...payload, id: sha256(JSON.stringify(canonical(payload))) }; }
export function verifySeal(report) {
  requireInput(report !== null && typeof report === 'object' && !Array.isArray(report), 'Invalid report.');
  const { id, ...payload } = report;
  requireInput(typeof id === 'string' && id === seal(payload).id, 'Report integrity check failed. Recreate the report.');
  requireInput(report.schemaVersion === 1, 'Unsupported report schema.');
}

export async function readText(file, maxBytes = MAX_BYTES) {
  const bytes = await readBytes(file, maxBytes);
  // Preserve the BOM for source hashes; parsers decide how to handle it.
  try { return new TextDecoder('utf-8', { fatal: true, ignoreBOM: true }).decode(bytes); }
  catch { throw new InputError('Input must be UTF-8 text.'); }
}
export async function readBytes(file, maxBytes = MAX_BYTES) {
  // On POSIX, opening a FIFO must not block before the regular-file check.
  const handle = await open(file, constants.O_RDONLY | (constants.O_NONBLOCK ?? 0));
  try {
    const stat = await handle.stat();
    requireInput(stat.isFile() && stat.size <= maxBytes, 'Input must be a regular file within the size limit.');
    // Bound the read too: a concurrently growing file must not bypass the stat check.
    const buffer = Buffer.alloc(maxBytes + 1);
    let length = 0;
    while (length < buffer.length) {
      const { bytesRead } = await handle.read(buffer, length, buffer.length - length, null);
      if (!bytesRead) break;
      length += bytesRead;
    }
    requireInput(length <= maxBytes, 'Input exceeds the size limit.');
    return buffer.subarray(0, length);
  } finally { await handle.close(); }
}
export async function readJson(file, maxBytes = MAX_BYTES) {
  const value = await readText(file, maxBytes);
  try { return JSON.parse(value.replace(/^\uFEFF/u, '')); }
  catch { throw new InputError('Input must be valid JSON.'); }
}
export async function rejectLinkedAncestors(file) {
  let current = path.resolve(file);
  while (true) {
    try {
      const stat = await lstat(current);
      requireInput(!stat.isSymbolicLink(), 'Symbolic links and junctions are not accepted for output paths.');
    } catch (error) { if (error.code !== 'ENOENT') throw error; }
    const parent = path.dirname(current);
    if (parent === current) return;
    current = parent;
  }
}
export function reportOutputPath(file) {
  const target = path.resolve(file);
  requireInput(path.extname(target).toLowerCase() === '.json' && !target.slice(path.parse(target).root.length).includes(':'), 'Report output must be a .json file without alternate data streams.');
  return target;
}
export async function writeJson(file, value) {
  const target = reportOutputPath(file);
  await rejectLinkedAncestors(target);
  const handle = await open(target, 'wx', 0o600);
  try {
    await handle.writeFile(`${JSON.stringify(value, null, 2)}\n`, 'utf8');
    await handle.sync();
  } finally { await handle.close(); }
}
