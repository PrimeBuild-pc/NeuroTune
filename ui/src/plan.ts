import { t } from './i18n';
import type { MeasurementSession, OptimizationAction, OptimizationPriority, Recommendation, RiskProfile } from './types';

const analysisPresets: Array<{ id: OptimizationPriority; label: string; detail: string; legacy?: boolean }> = [
  { id: 'fps', label: 'Competitive FPS', detail: 'Highest, most consistent frame rate and lowest latency in your game: GPU, CPU, interrupts, background apps, network and game config.' },
  { id: 'streaming', label: 'Streaming & recording', detail: 'Smooth game and encoder, no dropped frames, stable upload and clean audio.' },
  { id: 'balanced', label: 'Overall performance', detail: 'Investigate workload bottlenecks, smoothness and sustained performance, weighing trade-offs.' },
  { id: 'systemLatency', label: 'System latency', detail: 'Investigate responsiveness, waits and stalls. Scheduling traces do not measure end-to-end input latency.' },
  { id: 'networkLatency', label: 'Network optimization', detail: 'Investigate latency, jitter, loss, disconnections or throughput, distinguishing the PC, local network and external path.' },
  { id: 'stability', label: 'System stability', detail: 'Investigate crashes, freezes, resets and recurring errors, distinguishing symptoms, hypotheses and causes to verify.' },
  { id: 'privacySecurity', label: 'Windows privacy & security', detail: 'Investigate data collection, protections and detections. The preset starts no tweak, scanner or removal.' },
  { id: 'compromiseReview', label: 'Vulnerability & compromise review', detail: 'Look for exposure and compromise indicators as hypotheses, never as a verdict. The preset starts no scanner, removal or process kill.' },
  { id: 'hardwareLayout', label: 'Ports & PCIe lanes', detail: 'CPU versus chipset lanes, GPU and NVMe lane sharing, and the best USB port for each device. Advice only.' },
  { id: 'efficiency', label: 'Efficiency (legacy)', detail: 'Keep the saved run’s energy focus; it is not converted to another objective.', legacy: true },
];

// One-click starting points. They only fill the objective, risk preset and notes for the AI; approval, baselines and
// rollback are unchanged, and every field stays editable.
export const scenarioProfiles: Array<{ id: string; label: string; detail: string; priority: OptimizationPriority; riskProfile: RiskProfile; notes: string }> = [
  { id: 'competitive', label: 'Competitive gaming', detail: 'Lowest, most consistent latency. Balanced risk.', priority: 'fps', riskProfile: 'balanced',
    notes: 'Competitive gaming: lowest and most consistent latency and frame times. Keep anti-cheat, Windows security, stability and image clarity intact.' },
  { id: 'streaming', label: 'Streaming & recording', detail: 'Smooth game and encoder, no dropped frames. Low-risk changes only.', priority: 'streaming', riskProfile: 'safe',
    notes: 'Streaming or recording while gaming: smooth frame times for the game and the encoder, no dropped frames, stable upload. Keep audio devices, capture tools, network stability and security intact.' },
  { id: 'quiet', label: 'Quiet & efficient', detail: 'Less power, heat, fan noise and background work. Low-risk changes only.', priority: 'balanced', riskProfile: 'safe',
    notes: 'Quiet and efficient: reduce power draw, heat, fan noise and background activity while keeping everyday responsiveness acceptable. Keep security, updates and stability intact.' },
  { id: 'privacy', label: 'Privacy & security check', detail: 'Data collection, Windows protections and Defender detections. Low-risk changes only.', priority: 'privacySecurity', riskProfile: 'safe',
    notes: 'Privacy and security check: reduce optional data collection and confirm that Defender, Firewall, SmartScreen, updates and core isolation are working. Keep every security feature on and the apps I use working.' },
  { id: 'compromise', label: 'Compromise check', detail: 'Exposure and signs of compromise, as hypotheses, never a verdict. Advice only.', priority: 'compromiseReview', riskProfile: 'safe',
    notes: 'Compromise check: look for exposed services, unusual persistence and signs of tampering, and tell me what to verify by hand. Do not delete files, stop processes or turn off protections.' },
  { id: 'ports', label: 'Ports & PCIe lanes', detail: 'Where mouse, audio, GPU and NVMe are connected. Advice only.', priority: 'hardwareLayout', riskProfile: 'safe',
    notes: 'Ports and PCIe lanes: show which devices use CPU or chipset lanes, whether the GPU or an NVMe drive loses lanes, and which port each device should use. Advice only; I decide what to move. Devices that matter:' },
];

export function analysisPresetsFor(priority: OptimizationPriority) {
  return analysisPresets.filter(item => !item.legacy || item.id === priority).map(item => ({ ...item, label: t(item.label), detail: t(item.detail) }));
}

// Typed AI proposals are validated by the agent but are not in the static catalog, so they are listed next to it.
export function withParametricActions(actions: OptimizationAction[], recommendations: Recommendation[]): OptimizationAction[] {
  const proposed = recommendations.filter(item => item.kind === 'executableAction' && item.actionKind && !actions.some(action => action.id === item.actionId));
  return [...actions, ...proposed.filter((item, index) => proposed.findIndex(other => other.actionId === item.actionId) === index).map(item => ({
    id: item.actionId, name: item.actionSummary || item.title, description: item.actionKind!, category: 'AI proposal', risk: item.risk,
    requiresRestart: item.requiresRestart, availability: { canApply: true, alreadyApplied: false, status: 'Ready', currentValue: '' },
  }))];
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
