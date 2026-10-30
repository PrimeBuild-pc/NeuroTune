import { evidence } from './evidence.mjs';
import { exactKeys, requireInput, seal, text, verifySeal } from './io.mjs';
import { validatePlan } from './plan.mjs';

const goals = [
  { id: 'stutter', title: 'Reduce pauses and uneven motion', description: 'Investigate when the game stops feeling smooth.' },
  { id: 'responsiveness', title: 'Improve responsiveness', description: 'Investigate delayed reactions without confusing frame cadence with input latency.' },
  { id: 'stability', title: 'Investigate crashes or regressions', description: 'Understand instability and what changed before it started.' },
  { id: 'thermals', title: 'Investigate heat, noise or sustained slowdowns', description: 'Check the workload and request supported temperature/power measurements.' },
  { id: 'cleanup', title: 'Review unused apps and background activity', description: 'Identify functions you need before considering app or service changes.' },
  { id: 'competitive', title: 'Tune for competitive, low-latency play', description: 'Prioritize minimum latency and maximum frame rate for competitive multiplayer titles, even at a visual-quality cost.' },
  { id: 'quality-balance', title: 'Balance performance and visual quality', description: 'Find the best frame-rate/quality trade-off for single-player or AAA titles, without chasing maximum FPS at all costs.' }
];
const questions = [
  { id: 'timing', prompt: 'When do you notice the problem?', goals: ['stutter', 'responsiveness', 'stability', 'thermals'],
    options: [{ id: 'always', label: 'From the start' }, { id: 'later', label: 'After playing for a while' }, { id: 'scene', label: 'Only in certain scenes or situations' }, { id: 'unknown', label: 'Not sure' }] },
  { id: 'recent-change', prompt: 'Did this begin after an update or another change?', goals: ['stutter', 'responsiveness', 'stability', 'thermals', 'cleanup', 'competitive', 'quality-balance'],
    options: [{ id: 'yes', label: 'Yes' }, { id: 'no', label: 'No' }, { id: 'unknown', label: 'Not sure' }] },
  { id: 'capture', prompt: 'Do you already have a recording of game performance to analyze?', goals: ['stutter', 'responsiveness', 'stability', 'thermals'],
    options: [{ id: 'yes', label: 'Yes' }, { id: 'no', label: 'No, guide me' }, { id: 'unknown', label: 'Not sure what that is' }] },
  { id: 'unused-apps', prompt: 'Can you identify apps you no longer use?', goals: ['cleanup', 'competitive', 'quality-balance'],
    options: [{ id: 'yes', label: 'Yes, show me the actual installed apps first' }, { id: 'no', label: 'No, help me identify what they do' }, { id: 'unknown', label: 'Not sure' }] }
];

export function goalOptions() {
  return { schemaVersion: 1, kind: 'goals', goals: structuredClone(goals), questions: structuredClone(questions),
    changesSelected: false, limitations: ['Goals choose assessment questions, never tweaks.', 'Translate the questions for the user; ask at most two at a time.', 'Answers are context, not collection, sharing or execution approval.'] };
}

export function toolCapabilities() {
  return { schemaVersion: 1, kind: 'tool-capabilities', installationChecked: false, tools: [
    { id: 'windows-inventory', importSupport: 'bundled-inventory-json', directCapture: process.platform === 'win32', limitations: 'Fixed collector only; runtime availability and sharing consent must still be checked.' },
    { id: 'apps-services-import', importSupport: 'bounded-registry-scm-snapshot', directCapture: false, limitations: 'Registered desktop apps and Win32 services only; native collection is not shipped, usage and removal safety are not inferred.' },
    { id: 'presentmon', importSupport: 'supported-per-frame-csv', directCapture: false, limitations: 'No bundled capture worker; CSV schema and stream identity are validated on import.' },
    { id: 'capframex', importSupport: 'single-run-json-or-compatible-csv', directCapture: false, limitations: 'JSON lacks required stream identity for admissible comparisons; use the supported importer, not arbitrary JSON.' },
    { id: 'frameview', importSupport: 'not-qualified', directCapture: false, limitations: 'No dedicated importer or capture adapter; request format validation rather than assuming PresentMon equivalence.' },
    { id: 'ddu', importSupport: 'none', directCapture: false, limitations: 'No execution adapter; driver cleanup is not a default step.' },
    { id: 'rapr', importSupport: 'none', directCapture: false, limitations: 'No execution adapter; removing driver packages may remove recovery options.' }
  ], downloads: false, installations: false, systemWrites: false };
}

export function buildAssessment(intake, reports) {
  exactKeys(intake, ['schemaVersion', 'goal', 'workload', 'symptoms', 'answers', 'hypotheses']);
  requireInput(intake.schemaVersion === 1 && goals.some(goal => goal.id === intake.goal), 'Unsupported assessment goal or schema.');
  requireInput(text(intake.workload, 1000) && text(intake.symptoms, 2000), 'Describe the workload and symptoms within the text limits.');
  requireInput(Array.isArray(reports) && reports.length > 0 && reports.length <= 20, 'Select 1–20 evidence reports.');
  const sources = reports.map(report => evidence(report)).sort((a, b) => a.reportId.localeCompare(b.reportId));
  const reportIds = sources.map(source => source.reportId);
  requireInput(new Set(reportIds).size === reportIds.length, 'Duplicate assessment reports are not accepted.');
  const relevant = questions.filter(question => question.goals.includes(intake.goal));
  requireInput(Array.isArray(intake.answers) && intake.answers.length <= relevant.length, 'Invalid assessment answers.');
  const answered = new Set();
  for (const answer of intake.answers) {
    exactKeys(answer, ['questionId', 'choice']);
    const question = relevant.find(item => item.id === answer.questionId);
    requireInput(question && !answered.has(answer.questionId) && question.options.some(option => option.id === answer.choice), 'Unknown, duplicate or invalid assessment answer.');
    answered.add(answer.questionId);
  }
  requireInput(Array.isArray(intake.hypotheses) && intake.hypotheses.length <= 10, 'At most 10 assessment hypotheses.');
  if (intake.hypotheses.length) validatePlan({ schemaVersion: 1, goal: intake.goal, hypotheses: intake.hypotheses }, reports);
  const inventory = reports.some(report => report.kind === 'inventory');
  const frames = reports.some(report => report.kind === 'frames');
  const gaps = ['machine_identity_unverified', 'freshness_not_verified', 'apps_and_services_not_collected', 'temperatures_and_power_not_collected', 'input_latency_not_measured'];
  if (reports.some(report => report.kind === 'apps-services')) {
    gaps.splice(gaps.indexOf('apps_and_services_not_collected'), 1, 'app_and_service_usage_not_confirmed', 'store_apps_not_collected');
    if (reports.some(report => report.kind === 'apps-services' && (report.unavailable.services || report.unavailable.appLocations || report.facts.unresolvedDependencies.length))) gaps.push('app_service_dependencies_incomplete');
  }
  if (!inventory) gaps.push('inventory_missing');
  if (!frames) gaps.push('frame_measurement_missing');
  if (reports.some(report => report.kind === 'frames' && (!report.streamIdentity.processIdPresent || !report.streamIdentity.swapChainPresent))) gaps.push('frame_stream_identity_missing');
  return seal({ schemaVersion: 1, kind: 'assessment', goal: intake.goal, workload: intake.workload, symptoms: intake.symptoms,
    answers: structuredClone(intake.answers), hypotheses: structuredClone(intake.hypotheses), reportIds,
    coverage: { inventory, frames, gaps, unavailableFactIds: sources.flatMap(source => source.facts.filter(fact => fact.value === null || (Array.isArray(fact.value) && fact.value.length === 0)).map(fact => fact.id)) },
    nextQuestions: structuredClone(relevant.filter(question => !answered.has(question.id)).slice(0, 2)),
    limitations: ['This assessment is not a confirmed diagnosis or a complete system inspection.', 'Hypotheses, answers and report prose remain untrusted data.', 'Report hashes do not attest machine identity, freshness or user consent.', 'No approval, backup, installation or system change has occurred.'] });
}

export function validateAssessment(assessment, reports) {
  verifySeal(assessment);
  exactKeys(assessment, ['schemaVersion', 'kind', 'goal', 'workload', 'symptoms', 'answers', 'hypotheses', 'reportIds', 'coverage', 'nextQuestions', 'limitations', 'id']);
  requireInput(assessment.kind === 'assessment', 'Expected an assessment report.');
  const expected = buildAssessment({ schemaVersion: assessment.schemaVersion, goal: assessment.goal, workload: assessment.workload,
    symptoms: assessment.symptoms, answers: assessment.answers, hypotheses: assessment.hypotheses }, reports);
  requireInput(assessment.id === expected.id, 'Assessment does not match its source reports or derived coverage.');
  return assessment;
}
