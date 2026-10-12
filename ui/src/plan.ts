import { t } from './i18n';
import type { MeasurementSession, OptimizationAction, OptimizationPriority, Recommendation, RiskProfile } from './types';

const analysisPresets: Array<{ id: OptimizationPriority; label: string; detail: string; legacy?: boolean }> = [
  { id: 'balanced', label: 'Overall performance', detail: 'Investigate workload bottlenecks, smoothness and sustained performance, weighing trade-offs.' },
  { id: 'systemLatency', label: 'System latency', detail: 'Investigate responsiveness, waits and stalls. Scheduling traces do not measure end-to-end input latency.' },
  { id: 'networkLatency', label: 'Network optimization', detail: 'Investigate latency, jitter, loss, disconnections or throughput, distinguishing the PC, local network and external path.' },
  { id: 'stability', label: 'System stability', detail: 'Investigate crashes, freezes, resets and recurring errors, distinguishing symptoms, hypotheses and causes to verify.' },
  { id: 'privacySecurity', label: 'Windows privacy & security', detail: 'Investigate data collection, protections and detections. The preset starts no tweak, scanner or removal.' },
  { id: 'compromiseReview', label: 'Vulnerability & compromise review', detail: 'Look for exposure and compromise indicators as hypotheses, never as a verdict. The preset starts no scanner, removal or process kill.' },
  { id: 'fps', label: 'Frame rate (legacy)', detail: 'Keep the saved run’s FPS focus; it is not converted to another objective.', legacy: true },
  { id: 'efficiency', label: 'Efficiency (legacy)', detail: 'Keep the saved run’s energy focus; it is not converted to another objective.', legacy: true },
];

// One-click starting points. They only fill the objective, risk preset and notes for the AI; approval, baselines and
// rollback are unchanged, and every field stays editable.
export const scenarioProfiles: Array<{ id: string; label: string; detail: string; priority: OptimizationPriority; riskProfile: RiskProfile; notes: string }> = [
  { id: 'competitive', label: 'Competitive gaming', detail: 'Lowest, most consistent latency. Balanced risk.', priority: 'systemLatency', riskProfile: 'balanced',
    notes: 'Competitive gaming: lowest and most consistent latency and frame times. Keep anti-cheat, Windows security, stability and image clarity intact.' },
  { id: 'streaming', label: 'Streaming & recording', detail: 'Smooth game and encoder, no dropped frames. Low-risk changes only.', priority: 'balanced', riskProfile: 'safe',
    notes: 'Streaming or recording while gaming: smooth frame times for the game and the encoder, no dropped frames, stable upload. Keep audio devices, capture tools, network stability and security intact.' },
  { id: 'quiet', label: 'Quiet & efficient', detail: 'Less power, heat, fan noise and background work. Low-risk changes only.', priority: 'balanced', riskProfile: 'safe',
    notes: 'Quiet and efficient: reduce power draw, heat, fan noise and background activity while keeping everyday responsiveness acceptable. Keep security, updates and stability intact.' },
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
