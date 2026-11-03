#!/usr/bin/env node
import { parseArgs } from 'node:util';
import { collectInventory } from './collect.mjs';
import { evidence } from './evidence.mjs';
import { analyzeFrames, compareFrames } from './frames.mjs';
import { analyzeCapFrameX } from './capframex.mjs';
import { normalizeAppsServices } from './apps-services.mjs';
import { gameConfigReferences } from './game-configs.mjs';
import { InputError, MAX_REPORT_BYTES, readJson, readText, requireInput, writeJson } from './io.mjs';
import { validatePlan } from './plan.mjs';
import { buildAssessment, goalOptions, toolCapabilities } from './assessment.mjs';
import { reviewProposal } from './proposal.mjs';

const commands = {
  doctor: {},
  goals: {},
  tools: {},
  'game-configs': { game: {} },
  assess: { input: {}, evidence: { multiple: true }, out: {} },
  'proposal-check': { input: {}, assessment: {}, evidence: { multiple: true }, select: { multiple: true } },
  collect: { out: {} },
  'apps-services': { input: {}, out: {} },
  evidence: { input: {} },
  frames: { input: {}, process: {}, conditions: {}, out: {} },
  'frames-capframex': { input: {}, process: {}, conditions: {}, out: {} },
  compare: { baseline: { multiple: true }, candidate: { multiple: true } },
  'plan-check': { input: {}, evidence: { multiple: true } }
};
const help = {
  name: 'neurotune', version: '0.2.0-beta.1', schemaVersion: 1,
  usage: [
    'neurotune doctor',
    'neurotune goals',
    'neurotune tools',
    'neurotune game-configs --game <all or game-id>',
    'neurotune assess --input <intake.json> --evidence <report.json> [--evidence ...] --out <new assessment.json>',
    'neurotune proposal-check --input <proposal.json> --assessment <assessment.json> --evidence <report.json> [--evidence ...] --select <all or step-id> [--select ...]',
    'neurotune collect --out <new inventory.json>',
    'neurotune apps-services --input <native-snapshot.json> --out <new apps-services.json>',
    'neurotune evidence --input <inventory.json or frames.json or apps-services.json>',
    'neurotune frames --input <PresentMon.csv> --process <game.exe> --conditions <description> --out <new frames.json>',
    'neurotune frames-capframex --input <single-run CapFrameX.json> --process <game.exe> --conditions <description> --out <new frames.json>',
    'neurotune compare --baseline <before.json> [--baseline ...] --candidate <after.json> [--candidate ...]',
    'neurotune plan-check --input <plan.json> --evidence <report.json> [--evidence ...]'
  ],
  policy: 'This CLI only reads, analyzes and validates; it never changes the system. Applying user-confirmed steps happens through the harness tools, following references/apply.md. No provider calls or arbitrary commands. Report outputs require .json filenames without alternate data streams and never overwrite existing files.',
  privacy: 'Except local-write receipts and doctor/help/goals/tools/game-configs, results contain user evidence in your harness context. Obtain sharing consent before reading intake, evidence, assessment or proposal files.'
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
        providerCalls: false, administratorRequired: false, guidedAssessment: true, proposalReview: true, toolAcquisition: false,
        limitations: ['Doctor checks runtime/platform only, not CIM availability or PowerShell policy.', 'This skill does not sandbox the host harness.'] };
    } else if (command === 'goals') {
      data = goalOptions();
    } else if (command === 'tools') {
      data = toolCapabilities();
    } else if (command === 'game-configs') {
      data = gameConfigReferences(values.game);
    } else if (command === 'assess' || command === 'proposal-check') {
      requireInput(values.evidence.length <= 20, 'At most 20 evidence reports.');
      const reports = await Promise.all(values.evidence.map(file => readJson(file, MAX_REPORT_BYTES)));
      const input = await readJson(values.input, 128 * 1024);
      if (command === 'assess') {
        const assessment = buildAssessment(input, reports);
        await writeJson(values.out, assessment);
        data = { schemaVersion: 1, kind: 'assessment-receipt', saved: true, reportId: assessment.id };
      } else {
        data = reviewProposal(input, await readJson(values.assessment, MAX_REPORT_BYTES), reports, values.select);
      }
    } else if (command === 'collect') {
      data = await collectInventory(values.out);
    } else if (command === 'apps-services') {
      const report = normalizeAppsServices(await readJson(values.input));
      await writeJson(values.out, report);
      data = { schemaVersion: 1, kind: 'apps-services-receipt', saved: true, reportId: report.id };
    } else if (command === 'evidence') {
      data = evidence(await readJson(values.input, MAX_REPORT_BYTES));
    } else if (command === 'frames' || command === 'frames-capframex') {
      const analyze = command === 'frames' ? analyzeFrames : analyzeCapFrameX;
      const report = analyze(await readText(values.input), values);
      await writeJson(values.out, report);
      data = { schemaVersion: 1, kind: 'frame-receipt', saved: true, reportId: report.id, metrics: report.metrics,
        provenance: report.provenance, streamIdentity: report.streamIdentity, excludedFrames: report.excludedFrames };
    } else if (command === 'compare') {
      requireInput(values.baseline.length <= 20 && values.candidate.length <= 20, 'At most 20 reports per side.');
      data = compareFrames(await Promise.all(values.baseline.map(file => readJson(file, MAX_REPORT_BYTES))), await Promise.all(values.candidate.map(file => readJson(file, MAX_REPORT_BYTES))));
    } else if (command === 'plan-check') {
      requireInput(values.evidence.length <= 20, 'At most 20 evidence reports.');
      data = validatePlan(await readJson(values.input, 128 * 1024), await Promise.all(values.evidence.map(file => readJson(file, MAX_REPORT_BYTES))));
    }
  }
  console.log(JSON.stringify({ ok: true, data }));
} catch (error) {
  const code = error instanceof InputError ? error.code : ({ EEXIST: 'output_exists', ENOENT: 'file_not_found', EACCES: 'access_denied', EPERM: 'access_denied' }[error.code] ?? 'operation_failed');
  const message = error instanceof InputError ? error.message : 'Local operation failed. Check file paths and permissions; existing files are never overwritten.';
  console.log(JSON.stringify({ ok: false, error: { code, message } }));
  process.exitCode = 1;
}
