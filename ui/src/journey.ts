import type { OptimizationRun } from './types';

// Where the user is in the measured cycle, derived only from the persisted run state.
export const measured = ['Diagnose', 'Measure before', 'Choose changes', 'Apply', 'Measure after', 'Keep or undo'];
export const audit = ['Diagnose', 'Review findings'];
const stepOf: Partial<Record<OptimizationRun['state'], number>> = {
  draft: 0, scanned: 0, hypothesizing: 0, proposalReady: 1, baselinePending: 1, baselineReady: 2, approved: 2,
  applying: 3, restartPending: 3, candidatePending: 4, evaluating: 4, decisionPending: 5, rollingBack: 5, completed: 6,
};

export function journeyStep(run?: OptimizationRun) {
  if (!run || run.state === 'failed') return 0;
  if (run.mode === 'auditOnly') return run.state === 'completed' ? audit.length : run.diagnosis ? 1 : 0;
  return stepOf[run.state] ?? 0;
}

