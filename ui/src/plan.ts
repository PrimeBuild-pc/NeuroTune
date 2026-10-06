import { t } from './i18n';
import type { MeasurementSession, OptimizationAction, OptimizationPriority, Recommendation, RiskProfile } from './types';

const analysisPresets: Array<{ id: OptimizationPriority; label: string; detail: string; legacy?: boolean }> = [
  { id: 'balanced', label: 'Overall performance', detail: 'Investigate workload bottlenecks, smoothness and sustained performance, weighing trade-offs.' },
  { id: 'systemLatency', label: 'System latency', detail: 'Investigate responsiveness, waits and stalls. Scheduling traces do not measure end-to-end input latency.' },
  { id: 'networkLatency', label: 'Network optimization', detail: 'Investigate latency, jitter, loss, disconnections or throughput, distinguishing the PC, local network and external path.' },
  { id: 'stability', label: 'System stability', detail: 'Investigate crashes, freezes, resets and recurring errors, distinguishing symptoms, hypotheses and causes to verify.' },
  { id: 'privacySecurity', label: 'Windows privacy & security', detail: 'Investigate data collection, protections and detections. The preset starts no tweak, scanner or removal.' },
  { id: 'fps', label: 'Frame rate (legacy)', detail: 'Keep the saved run’s FPS focus; it is not converted to another objective.', legacy: true },
  { id: 'efficiency', label: 'Efficiency (legacy)', detail: 'Keep the saved run’s energy focus; it is not converted to another objective.', legacy: true },
];

export function analysisPresetsFor(priority: OptimizationPriority) {
  return analysisPresets.filter(item => !item.legacy || item.id === priority).map(item => ({ ...item, label: t(item.label), detail: t(item.detail) }));
}

export function selectActionIdsForProfile(
  actions: OptimizationAction[],
  recommendations: Recommendation[],
  profile: RiskProfile | 'none',
): string[] {
  if (profile === 'none') return [];
  const recommended = new Set(
    recommendations
      .filter(item => item.kind === 'executableAction')
      .map(item => item.actionId),
  );

  return actions
    .filter(action => action.availability.canApply && recommended.has(action.id))
    .filter(action => profile === 'aggressive'
      || (profile === 'balanced' && action.risk !== 'high')
      || (profile === 'safe' && action.risk === 'low'))
    .map(action => action.id);
}

export function planKindLabel(kind: Recommendation['kind']): string {
  return t(({
    executableAction: 'NeuroTune action',
    manualGuidance: 'Manual guidance',
    scriptArtifact: 'Unverified script',
    externalResource: 'Verified resource',
    updateNotice: 'Official update notice',
  } satisfies Record<Recommendation['kind'], string>)[kind]);
}

export function preparatoryBaselines(sessions: MeasurementSession[], selectedIds: Set<string>): MeasurementSession[] {
  const valid = sessions.filter(item => selectedIds.has(item.id) && !item.systemWide && item.label === 'baseline'
    && item.state === 'completed' && item.report?.quality.isValid && item.hardwareFingerprint && item.configurationFingerprint)
    .sort((a, b) => b.createdAtUtc.localeCompare(a.createdAtUtc));
  const first = valid[0];
  return first ? valid.filter(item => item.processName.toLowerCase() === first.processName.toLowerCase()
    && item.hardwareFingerprint === first.hardwareFingerprint && item.configurationFingerprint === first.configurationFingerprint
    && item.durationSeconds === first.durationSeconds && item.report?.schemaVersion === first.report?.schemaVersion).slice(0, 20) : [];
}

export function scriptArtifactFilename(id: string): string {
  return `${id.replace(/[^a-z0-9_-]/gi, '_') || 'neurotune-script'}.txt`;
}
