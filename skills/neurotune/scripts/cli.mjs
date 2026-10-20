#!/usr/bin/env node
import { parseArgs } from 'node:util';
import { collectInventory } from './collect.mjs';
import { evidence } from './evidence.mjs';
import { analyzeFrames, compareFrames } from './frames.mjs';
import { InputError, readJson, readText, requireInput, writeJson } from './io.mjs';
import { validatePlan } from './plan.mjs';

const commands = {
  doctor: {},
  collect: { out: {} },
  evidence: { input: {} },
  frames: { input: {}, process: {}, conditions: {}, out: {} },
  compare: { baseline: { multiple: true }, candidate: { multiple: true } },
  'plan-check': { input: {}, evidence: { multiple: true } }
};
const help = {
  name: 'neurotune', version: '0.1.0-alpha.1', schemaVersion: 1,
  usage: [
    'neurotune doctor',
    'neurotune collect --out <new inventory.json>',
    'neurotune evidence --input <inventory.json or frames.json>',
    'neurotune frames --input <PresentMon.csv> --process <game.exe> --conditions <description> --out <new frames.json>',
    'neurotune compare --baseline <before.json> [--baseline ...] --candidate <after.json> [--candidate ...]',
    'neurotune plan-check --input <plan.json> --evidence <report.json> [--evidence ...]'
  ],
  policy: 'Read-only investigation. No provider calls, arbitrary commands, Apply or rollback. Output files never overwrite existing files.',
  privacy: 'Except collect receipts and doctor/help, results enter your harness context. Obtain sharing consent first.'
};

try {
  const [command = 'help', ...args] = process.argv.slice(2);
  let data;
  if (command === 'help' || command === '--help') {
    requireInput(args.length === 0, 'Help takes no arguments.');
    data = help;
  } else {
    requireInput(Object.hasOwn(commands, command), 'Unknown command. Run help for available commands.');
    const options = Object.fromEntries(Object.entries(commands[command]).map(([name, value]) => [name, { type: 'string', ...value }]));
    let parsed;
    try { parsed = parseArgs({ args, options, strict: true, allowPositionals: false, tokens: true }); }
    catch { throw new InputError('Unknown option, positional argument or missing option value. Run help.'); }
    const { values } = parsed;
    // parseArgs normally accepts repeated scalar options; reject ambiguity instead.
    const seen = new Set();
    for (const token of parsed.tokens.filter(token => token.kind === 'option')) {
      requireInput(options[token.name].multiple || !seen.has(token.name), 'Duplicate scalar option.');
      seen.add(token.name);
    }
    for (const name of Object.keys(options)) {
      const value = values[name];
      requireInput(typeof value === 'string' ? value.trim().length > 0 : Array.isArray(value) && value.length > 0 && value.every(item => item.trim().length > 0), 'A required option is missing or empty. Run help.');
    }
    if (command === 'doctor') {
      data = { version: help.version, schemaVersion: 1, nodeSupported: Number(process.versions.node.split('.')[0]) >= 22,
        liveInventory: process.platform === 'win32', offlineAnalysis: true, systemWrites: false,
        providerCalls: false, administratorRequired: false,
        limitations: ['Doctor checks runtime/platform only, not CIM availability or PowerShell policy.', 'This skill does not sandbox the host harness.'] };
    } else if (command === 'collect') {
      data = await collectInventory(values.out);
    } else if (command === 'evidence') {
      data = evidence(await readJson(values.input, 128 * 1024));
    } else if (command === 'frames') {
      const report = analyzeFrames(await readText(values.input), values);
      await writeJson(values.out, report);
      data = { schemaVersion: 1, kind: 'frame-receipt', saved: true, reportId: report.id, metrics: report.metrics,
        provenance: report.provenance, excludedFrames: report.excludedFrames };
    } else if (command === 'compare') {
      requireInput(values.baseline.length <= 20 && values.candidate.length <= 20, 'At most 20 reports per side.');
      data = compareFrames(await Promise.all(values.baseline.map(file => readJson(file, 128 * 1024))), await Promise.all(values.candidate.map(file => readJson(file, 128 * 1024))));
    } else if (command === 'plan-check') {
      requireInput(values.evidence.length <= 20, 'At most 20 evidence reports.');
      data = validatePlan(await readJson(values.input, 128 * 1024), await Promise.all(values.evidence.map(file => readJson(file, 128 * 1024))));
    }
  }
  console.log(JSON.stringify({ ok: true, data }));
} catch (error) {
  const code = error instanceof InputError ? error.code : ({ EEXIST: 'output_exists', ENOENT: 'file_not_found', EACCES: 'access_denied', EPERM: 'access_denied' }[error.code] ?? 'operation_failed');
  const message = error instanceof InputError ? error.message : 'Local operation failed. Check file paths and permissions; existing files are never overwritten.';
  console.log(JSON.stringify({ ok: false, error: { code, message } }));
  process.exitCode = 1;
}
