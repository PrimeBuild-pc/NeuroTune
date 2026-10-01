import type { MeasurementSession, OptimizationAction, OptimizationPriority, Recommendation, RiskProfile } from './types';

const analysisPresets: Array<{ id: OptimizationPriority; label: string; detail: string; legacy?: boolean }> = [
  { id: 'balanced', label: 'Performance complessive', detail: 'Indaga i colli di bottiglia del workload, fluidità e prestazioni sostenute, valutando i compromessi.' },
  { id: 'systemLatency', label: 'Latenza del sistema', detail: 'Indaga reattività, attese e stalli. Le tracce di scheduling non misurano la latenza input end-to-end.' },
  { id: 'networkLatency', label: 'Ottimizzazione rete', detail: 'Indaga latenza, jitter, perdite, disconnessioni o throughput, distinguendo PC, rete locale e percorso esterno.' },
  { id: 'stability', label: 'Stabilità del sistema', detail: 'Indaga crash, blocchi, reset ed errori ricorrenti, distinguendo sintomi, ipotesi e cause da verificare.' },
  { id: 'fps', label: 'Frame rate (precedente)', detail: 'Mantiene il focus FPS del run salvato; non viene convertito in un altro obiettivo.', legacy: true },
  { id: 'efficiency', label: 'Efficienza (precedente)', detail: 'Mantiene il focus energetico del run salvato; non viene convertito in un altro obiettivo.', legacy: true },
];

export function analysisPresetsFor(priority: OptimizationPriority) {
  return analysisPresets.filter(item => !item.legacy || item.id === priority);
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
  return ({
    executableAction: 'NeuroTune action',
    manualGuidance: 'Manual guidance',
    scriptArtifact: 'Unverified script',
    externalResource: 'Verified resource',
    updateNotice: 'Official update notice',
  } satisfies Record<Recommendation['kind'], string>)[kind];
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
