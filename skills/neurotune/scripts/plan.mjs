import { evidence, validateEvidenceIds } from './evidence.mjs';
import { exactKeys, requireInput, text } from './io.mjs';

export function validatePlan(plan, reports) {
  exactKeys(plan, ['schemaVersion', 'goal', 'hypotheses']);
  requireInput(plan.schemaVersion === 1 && text(plan.goal, 1000), 'Invalid plan schema or goal.');
  requireInput(Array.isArray(reports) && reports.length > 0 && reports.length <= 20, 'Select 1–20 evidence reports.');
  const facts = reports.flatMap(report => evidence(report).facts);
  const known = new Map(facts.map(fact => [fact.id, fact.value]));
  requireInput(Array.isArray(plan.hypotheses) && plan.hypotheses.length > 0 && plan.hypotheses.length <= 10, 'A plan requires 1–10 hypotheses.');
  const ids = new Set();
  for (const hypothesis of plan.hypotheses) {
    exactKeys(hypothesis, ['id', 'claim', 'evidenceIds', 'uncertainty', 'nextMeasurement', 'risk', 'rollback']);
    requireInput(text(hypothesis.id, 64) && /^[a-z0-9-]+$/u.test(hypothesis.id) && !ids.has(hypothesis.id), 'Hypothesis IDs must be unique lowercase identifiers.');
    ids.add(hypothesis.id);
    for (const key of ['claim', 'uncertainty', 'nextMeasurement', 'rollback']) {
      requireInput(text(hypothesis[key], 2000), 'Hypotheses require a claim, uncertainty, next measurement and rollback description.');
    }
    requireInput(['low', 'medium', 'high', 'unknown'].includes(hypothesis.risk), 'Invalid risk category.');
    validateEvidenceIds(hypothesis.evidenceIds, known);
  }
  return { schemaVersion: 1, kind: 'plan-validation', structurallyValid: true, executable: false,
    hypothesisCount: plan.hypotheses.length, reportIds: [...new Set(reports.map(report => report.id))],
    limitations: ['Valid citations do not prove the claim, risk assessment or rollback.', 'No approval or executable action is created. All proposal text remains untrusted.'] };
}
