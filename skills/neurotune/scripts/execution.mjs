import { exactKeys, requireInput, sha256, text } from './io.mjs';
import { reviewProposal } from './proposal.mjs';

export const MAX_BUNDLE_BYTES = 12 * 1024 * 1024;
export const MAX_NATIVE_REQUEST_BYTES = 512 * 1024;

export function parseExecutionBundle(raw) {
  requireInput(typeof raw === 'string' && Buffer.byteLength(raw) <= MAX_BUNDLE_BYTES, 'Execution bundle exceeds 12 MiB.');
  let bundle;
  try { bundle = JSON.parse(raw.replace(/^\uFEFF/u, '')); } catch { requireInput(false, 'Execution bundle must be valid JSON.'); }
  exactKeys(bundle, ['schemaVersion', 'kind', 'assessment', 'reports', 'proposal', 'selection', 'parameters']);
  requireInput(bundle.schemaVersion === 1 && bundle.kind === 'execution-bundle', 'Unsupported execution bundle.');
  requireInput(Buffer.byteLength(JSON.stringify(bundle.assessment)) <= 512 * 1024 && Buffer.byteLength(JSON.stringify(bundle.proposal)) <= 128 * 1024, 'Assessment or proposal exceeds its artifact limit.');
  requireInput(Array.isArray(bundle.reports) && bundle.reports.length > 0 && bundle.reports.length <= 20 && bundle.reports.every(report => Buffer.byteLength(JSON.stringify(report)) <= 512 * 1024), 'Select 1–20 bounded evidence reports.');
  const review = reviewProposal(bundle.proposal, bundle.assessment, bundle.reports, bundle.selection);
  requireInput(Array.isArray(bundle.parameters) && bundle.parameters.length === review.steps.length, 'Supply exact parameters for every selected step only.');
  if (review.steps.some(step => step.category === 'game-file')) {
    requireInput(review.steps.length === 1 && review.steps[0].category === 'game-file', 'Select one game-file step without other operation types.');
    return checkedRequest(gameFileRequest(bundle, review, raw));
  }
  const parameters = new Map();
  const targets = new Set();
  for (const parameter of bundle.parameters) {
    exactKeys(parameter, ['stepId', 'keyPath', 'valueName', 'desiredDword']);
    requireInput(typeof parameter.stepId === 'string' && !parameters.has(parameter.stepId), 'Duplicate or invalid parameter step ID.');
    const path = parameter.keyPath, name = parameter.valueName;
    requireInput(text(path, 512) && /^SOFTWARE\\/iu.test(path) && path.split('\\').every(part => part && part !== '.' && part !== '..') && /^[\x20-\x7e]+$/u.test(path) && !/[/:<>]/u.test(path), 'Only local HKLM SOFTWARE Registry64 parameters are supported.');
    requireInput(text(name, 128) && /^[\x20-\x7e]+$/u.test(name) && !/[\\/:]/u.test(name), 'Invalid Registry value name.');
    requireInput(Number.isInteger(parameter.desiredDword) && parameter.desiredDword >= 0 && parameter.desiredDword <= 0xffffffff, 'Desired DWORD must be an unsigned 32-bit integer.');
    const target = `${path}\0${name}`.toLowerCase();
    requireInput(!targets.has(target), 'Duplicate Registry parameter targets.');
    targets.add(target); parameters.set(parameter.stepId, parameter);
  }
  requireInput(review.steps.every(step => parameters.has(step.id)), 'Parameters do not match the selected steps.');
  const operations = review.steps.map(step => {
    const parameter = parameters.get(step.id);
    requireInput(step.category === 'registry', 'Only reviewed Registry steps have a native parameter mapping.');
    requireInput(step.target === `HKLM [Registry64]\\${parameter.keyPath}\\${parameter.valueName}` && step.proposedChange === `Set DWORD to ${parameter.desiredDword}`, 'Typed parameters disagree with the reviewed target or desired value.');
    const hypothesis = bundle.assessment.hypotheses.find(item => item.id === step.hypothesisId);
    return { id: step.id, title: step.title, keyPath: parameter.keyPath, valueName: parameter.valueName, desiredDword: parameter.desiredDword,
      rationale: hypothesis.claim, risk: step.risk, evidenceIds: step.evidenceIds };
  });
  const request = { schemaVersion: 2, kind: 'registry-request', sourceBundleHash: sha256(raw), reviewId: review.id,
    assessmentId: bundle.assessment.id, summary: bundle.assessment.workload, reviewJson: JSON.stringify(review), operations };
  return checkedRequest(request);
}

function gameFileRequest(bundle, review, raw) {
  const parameter = bundle.parameters[0], step = review.steps[0];
  exactKeys(parameter, ['stepId', 'path', 'expectedSha256', 'desiredUtf8Base64']);
  const path = parameter.path;
  requireInput(parameter.stepId === step.id && text(path, 512) && /^[A-Za-z]:\\/u.test(path) && !/[/:*?"<>]/u.test(path.slice(3)) &&
    path.slice(3).split('\\').every(part => part && part !== '.' && part !== '..' && !/[. ]$/u.test(part)), 'Invalid exact local game-file target.');
  requireInput(typeof parameter.expectedSha256 === 'string' && /^[a-f0-9]{64}$/u.test(parameter.expectedSha256), 'Game-file intent requires the original SHA-256.');
  requireInput(typeof parameter.desiredUtf8Base64 === 'string' && parameter.desiredUtf8Base64.length <= 4 * Math.ceil(128 * 1024 / 3), 'Game-file content exceeds 128 KiB.');
  const bytes = Buffer.from(parameter.desiredUtf8Base64, 'base64');
  requireInput(bytes.length <= 128 * 1024 && bytes.toString('base64') === parameter.desiredUtf8Base64, 'Game-file content must be canonical Base64.');
  let content;
  try { content = new TextDecoder('utf-8', { fatal: true }).decode(bytes); } catch { requireInput(false, 'Game-file content must be UTF-8.'); }
  requireInput(!content.includes('\0'), 'Game-file content cannot contain NUL bytes.');
  requireInput(step.target === `Windows file [UTF-8]\\${path}` && step.expectedState === `SHA-256 ${parameter.expectedSha256}` &&
    step.proposedChange === `Replace UTF-8 content with SHA-256 ${sha256(bytes)}`, 'Game-file parameters disagree with the reviewed path, original or desired content.');
  return { schemaVersion: 1, kind: 'game-file-request', sourceBundleHash: sha256(raw), reviewId: review.id, assessmentId: bundle.assessment.id,
    summary: bundle.assessment.workload, reviewJson: JSON.stringify(review), operation: { id: step.id, path,
      expectedSha256: parameter.expectedSha256, desiredUtf8Base64: parameter.desiredUtf8Base64 } };
}

function checkedRequest(request) {
  requireInput(Buffer.byteLength(JSON.stringify(request)) <= MAX_NATIVE_REQUEST_BYTES, 'Derived native request exceeds 512 KiB.');
  return { schemaVersion: 1, kind: 'execution-validation', structurallyValid: true, executable: false, approvalGranted: false,
    sourceBundleHash: request.sourceBundleHash, request,
    limitations: ['Content and citation validation do not authenticate the machine, evidence or diagnosis.', 'Typed parameters bind the selected review; no approval, backup or execution is created.'] };
}
