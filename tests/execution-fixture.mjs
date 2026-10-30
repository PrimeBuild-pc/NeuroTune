import { rawInventory } from './fixtures.mjs';
import { normalizeInventory } from '../skills/neurotune/scripts/evidence.mjs';
import { buildAssessment } from '../skills/neurotune/scripts/assessment.mjs';

export function executionFixture() {
  const report = normalizeInventory(rawInventory);
  const intake = { schemaVersion: 1, goal: 'stutter', workload: 'Synthetic approval integration; no settings will change', symptoms: 'Synthetic pauses', answers: [], hypotheses: [{
    id: 'capture', claim: 'Synthetic test only, not a tuning recommendation', evidenceIds: [`${report.id}:processors`], uncertainty: 'No real workload was measured',
    nextMeasurement: 'Collect comparable synthetic trials', risk: 'unknown', rollback: 'No change has occurred'
  }] };
  const assessment = buildAssessment(intake, [report]);
  const parameter = { stepId: 'synthetic', keyPath: 'SOFTWARE\\NeuroTune.Skill.Tests\\Approval', valueName: 'SyntheticValue', desiredDword: 7 };
  const proposal = { schemaVersion: 1, assessmentId: assessment.id, steps: [{ id: parameter.stepId, title: 'Synthetic parameter binding test', category: 'registry', hypothesisId: 'capture', evidenceIds: [`${report.id}:processors`],
    target: `HKLM [Registry64]\\${parameter.keyPath}\\${parameter.valueName}`, expectedState: 'Native collector must capture the actual state', proposedChange: 'Set DWORD to 7', risk: 'unknown', requiresRestart: false, dependsOn: [],
    backup: { method: 'Protected native originals required before any actual write', verified: false }, recovery: { mode: 'automatic', description: 'Not implemented; do not execute' }, verification: { method: 'Readback plus comparable measurement', successCriteria: 'No change is authorized by this test' } }] };
  return { schemaVersion: 1, kind: 'execution-bundle', assessment, reports: [report], proposal, selection: ['all'], parameters: [parameter] };
}
