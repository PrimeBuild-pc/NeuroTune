export type ProviderKind = 'openRouter' | 'openAI' | 'anthropic' | 'deepSeek' | 'custom' | 'local' | 'chatGpt';
export type ApiProtocol = 'openAiCompatible' | 'anthropic';
export type ThemePreference = 'system' | 'light' | 'dark';

export interface ProviderSettings {
  provider: ProviderKind;
  providerName: string;
  baseUrl: string;
  protocol: ApiProtocol;
  model: string;
  requiresApiKey: boolean;
  chatGptAccountId?: string;
  investigationMaxTurns?: number;
  investigationMaxMinutes?: number;
}

export interface SupportingAttachment { id: string; name: string; kind: 'report' | 'image'; contentType: 'text/plain' | 'image/png'; content: string; sha256: string; }
export interface SupportingAttachmentInfo { id: string; name: string; kind: 'report' | 'image'; contentType: string; sha256: string; bytes: number; }

export interface ChatGptAccountInfo { id: string; label: string; connected: boolean; planEnabled: boolean; }

export interface SystemProfile {
  schemaVersion: number;
  collectedAt: string;
  operatingSystem: string;
  cpu: string;
  gpus: string[];
  memory: string;
  disks: string[];
  activePowerPlan: string;
  windowsSettings: Record<string, string>;
  privacySecurity?: Record<string, string>;
  gamingSettings: Record<string, string>;
  networkAdapters: string[];
  networkSettings: Record<string, string>;
  hardwareCapabilities: Record<string, string>;
  firmwareAndMemory: Record<string, string>;
  componentIdentities: Record<string, string>;
  factoryBaselines: Record<string, string>;
  telemetryCapabilities: TelemetryCapability[];
  bootConfiguration: Record<string, string>;
  performanceRegistry: Record<string, string>;
  policyConflicts: string[];
  installedSoftware: string[];
  relevantDrivers: string[];
  deviceIssues: string[];
  softwareSignals: string[];
  scanPhases: ScanPhase[];
  topProcesses: string[];
  startupItems: string[];
  automaticServices: string[];
}

export interface TelemetryCapability {
  name: string;
  status: 'supported' | 'unavailable' | 'blockedByHvci' | 'driverNotApproved';
  detail: string;
}

export interface DefenderReport {
  status: 'observed' | 'unavailable'; readAtUtc: string; scanState: 'idle' | 'busy' | 'unknown';
  protection?: { antivirusEnabled?: boolean; realTimeProtectionEnabled?: boolean; tamperProtected?: boolean; runningMode: string; signatureAgeDays?: number; signatureUpdatedAtUtc?: string; quickScanEndedAtUtc?: string; fullScanEndedAtUtc?: string };
  preferences?: { cloudReporting?: number; sampleSubmission?: number; cloudBlockLevel?: number; excludedPathCount?: number; excludedProcessCount?: number; excludedExtensionCount?: number };
  threats: Array<{ threatId?: string; name: string; severity?: number; isActive?: boolean; didThreatExecute?: boolean }>;
  truncated: boolean; limitation: string;
}
export interface DefenderScanOperation { id: string; scanType: 'quick' | 'full'; requestedAtUtc: string; state: 'Running' | 'CommandReturned' | 'InterruptedOrFailed' | 'ReviewedAfterInterruption'; detail: string; }
export interface DefenderScanResult { operation: DefenderScanOperation; report: DefenderReport; }

export interface PerformanceSnapshot {
  cpuLoadPercent?: number;
  usedMemoryGb?: number;
  totalMemoryGb?: number;
  processCount: number;
  latencyMs?: number;
  activePowerPlan: string;
}

export interface Availability {
  canApply: boolean;
  alreadyApplied: boolean;
  status: string;
  currentValue: string;
}

export interface OptimizationAction {
  id: string;
  name: string;
  description: string;
  category: string;
  risk: 'low' | 'medium' | 'high';
  requiresRestart: boolean;
  availability: Availability;
}

export interface CustomPowerPlanFile {
  name: string;
  path: string;
  sizeBytes: number;
  sha256: string;
}

export type PlanRecommendationKind = 'executableAction' | 'manualGuidance' | 'scriptArtifact' | 'externalResource' | 'updateNotice';
export type RiskProfile = 'safe' | 'balanced' | 'aggressive';

export interface SourceReference {
  title: string;
  url: string;
  grade: string;
}

export interface Recommendation {
  id: string;
  kind: PlanRecommendationKind;
  title: string;
  actionId: string;
  resourceId: string;
  updateId: string;
  evidenceIds: string[];
  reason: string;
  risk: 'low' | 'medium' | 'high';
  expectedImpact: string;
  uncertainty?: string;
  reversibility?: string;
  tradeoffs: string[];
  prerequisites: string[];
  requiresRestart: boolean;
  sourceReferences: SourceReference[];
  scriptLanguage: string;
  script: string;
  reviewWarnings: string[];
}

export type OptimizationPriority = 'balanced' | 'fps' | 'systemLatency' | 'networkLatency' | 'efficiency' | 'stability' | 'privacySecurity';
export type InvestigationMode = 'measuredOptimization' | 'auditOnly';

export interface TuningGoals {
  priority: OptimizationPriority;
  riskProfile: RiskProfile;
  games: string[];
  gameContext: GameContext;
  performanceInput: UserPerformanceInput;
  notes: string;
}

export interface GameContext {
  game: string;
  version: string;
  launcher: string;
  graphicsApi: string;
  width?: number;
  height?: number;
  refreshRateHz?: number;
  displayMode: string;
  vrr: string;
  vSync: string;
  frameCap?: number;
  symptoms: string[];
  preserve: string;
}

export interface UserPerformanceInput {
  userProvided: true;
  averageFps?: number;
  onePercentLowFps?: number;
  averageFrameTimeMs?: number;
  inputLatencyMs?: number;
  networkLatencyMs?: number;
  packetLossPercent?: number;
  notes: string;
}

export interface ScanPhase {
  name: string;
  durationMilliseconds: number;
  factsCollected: number;
}

export interface ConflictPattern {
  id: string;
  title: string;
  kind: 'confirmed' | 'conditional' | 'suspiciousOverride' | 'missingEvidence';
  evidenceIds: string[];
  evidence: Record<string, string>;
  objectives: OptimizationPriority[];
  explanation: string;
  whyCounterproductive: string;
  confidence: string;
  suggestedActionIds: string[];
}

export interface DiagnosisFinding {
  title: string;
  evidenceId: string;
  currentValue: string;
  assessment: string;
}

export interface SystemOneAdvisory { phase: string; status: string; domain: string | null; score: number | null; seconds: number; detail: string; evidenceIds: string[]; }

export interface AuditCheckAssessment {
  checkId: string; area: string; label: string;
  status: 'notChecked' | 'reviewed' | 'partial' | 'unavailable' | 'consentDenied';
  evidenceIds: string[]; assessment: string;
}

export interface Diagnosis {
  auditCoverage?: AuditCheckAssessment[];
  auditCoverageComplete?: boolean;
  systemOneAdvisories?: SystemOneAdvisory[];
  summary: string;
  findings: DiagnosisFinding[];
  recommendations: Recommendation[];
  conflicts: ConflictPattern[];
  consentQuestion: string;
}

export interface EvidencePayloadReport {
  factCount: number;
  utf8Bytes: number;
  singlePassLimitBytes: number;
  fitsSinglePass: boolean;
  privacyClasses: Record<string, number>;
}

export interface ScanResult {
  profile: SystemProfile;
  sanitizedProfile: string;
  payloadReport: EvidencePayloadReport;
  updateNotices: UpdateNoticeDefinition[];
  snapshot: PerformanceSnapshot;
  actions: OptimizationAction[];
}

export type OptimizationRunState = 'draft' | 'scanned' | 'hypothesizing' | 'baselinePending' |
  'baselineReady' | 'proposalReady' | 'approved' | 'applying' | 'restartPending' |
  'candidatePending' | 'evaluating' | 'decisionPending' | 'rollingBack' | 'recoveryRequired' |
  'completed' | 'failed';

export interface OptimizationRun {
  id: string;
  mode?: InvestigationMode; // Legacy schema-1 runs are measured optimizations.
  state: OptimizationRunState;
  requiresRecovery: boolean;
  goals: TuningGoals;
  evidenceFacts: Record<string, string>;
  usedLocalFallback?: boolean;
  error?: string | null;
  diagnosis?: Diagnosis;
  baselineSessionIds: string[];
  candidateSessionIds: string[];
  diagnosticSessionIds?: string[];
  supportingAttachments?: SupportingAttachmentInfo[];
  approvedActionIds: string[];
  operationId?: string;
  comparison?: MeasurementComparison;
}

export interface UpdateNoticeDefinition {
  id: string;
  kind: 'gpuDriver' | 'chipsetDriver' | 'bios';
  vendor: string;
  model: string;
  installedVersion: string;
  latestVersion: string;
  officialUrl: string;
  status: 'updateAvailable' | 'current' | 'comparisonUnavailable';
  reason: string;
}

export interface ActionRecord {
  actionId: string;
  attempted: boolean;
  applied: boolean;
  rolledBack: boolean;
  error?: string;
}

export interface OperationManifest {
  systemOneAdvisories?: SystemOneAdvisory[];
  id: string;
  optimizationRunId?: string;
  createdAt: string;
  status: string;
  actions: ActionRecord[];
  before?: PerformanceSnapshot;
  after?: PerformanceSnapshot;
  error?: string;
}

export type MeasurementLabel = 'baseline' | 'candidate';
export type MeasurementState = 'prepared' | 'recording' | 'captured' | 'analyzing' | 'completed' | 'cancelled' | 'failed';

export interface MeasurementWorkload {
  processId: number;
  name: string;
  startTimeUtc: string;
  description: string;
}

export interface DistributionMetrics {
  count: number;
  eventsPerSecond: number;
  totalMicroseconds: number;
  p50Microseconds: number;
  p95Microseconds: number;
  p99Microseconds: number;
  maxMicroseconds: number;
}

export interface TraceReport {
  schemaVersion?: number;
  sessionId: string;
  generatedAtUtc: string;
  targetExecutable: string;
  quality: { durationMilliseconds: number; etlBytes: number; eventsLost: number; missingProviders: string[]; targetPresencePercent: number; isValid: boolean };
  interrupts: Array<{ kind: string; module: string; logicalProcessor: number; distribution: DistributionMetrics }>;
  processors: Array<{ logicalProcessor: number; interruptSharePercent: number; targetRunningMilliseconds: number; readyOverlapMicroseconds: number; scheduledBusyMilliseconds?: number | null; scheduledIdleMilliseconds?: number | null; unobservedMilliseconds?: number | null; dpc?: DistributionMetrics; isr?: DistributionMetrics }>;
  hardFaults?: Array<{ processKey: string; processName: string; resolution: DistributionMetrics }>;
  longestSpikes?: Array<{ kind: string; source: string; logicalProcessor: number; startMilliseconds: number; durationMicroseconds: number }>;
  limitations?: string[];
  threads: Array<{ threadKey: string; runningMilliseconds: number; readyTime: DistributionMetrics; migrations: number; residencyMilliseconds: Record<string, number> }>;
  frameTimes?: {
    source: string; sampleCount: number; capturedDurationMilliseconds: number; averageFps: number;
    onePercentLowFps: number; p50Milliseconds: number; p95Milliseconds: number; p99Milliseconds: number;
    stutterCount: number; presentModes: string[];
  };
  observations: Array<{ title: string; category: string; evidenceIds: string[]; observedMetric: string; explanation: string; verifiableHypothesis: string; confidence: string }>;
}

export interface MeasurementSession {
  hardwareFingerprint?: string;
  configurationFingerprint?: string;
  systemWide?: boolean;
  hardFaultsEnabled?: boolean;
  id: string;
  optimizationRunId?: string;
  processId: number;
  processName: string;
  processStartTimeUtc: string;
  label: MeasurementLabel;
  durationSeconds: number;
  keepRawTrace: boolean;
  state: MeasurementState;
  createdAtUtc: string;
  recordingStartedAtUtc?: string;
  capturedAtUtc?: string;
  thermalCelsius?: number;
  cpuPerformancePercent?: number;
  report?: TraceReport;
  error?: string;
}

export interface MeasurementComparison {
  systemOneAdvisories?: SystemOneAdvisory[];
  id: string;
  level: 'exploratory' | 'repeated';
  baselineSessionIds: string[];
  candidateSessionIds: string[];
  metrics: Array<{ evidenceId: string; baselineMedian: number; candidateMedian: number; deltaPercent: number; outcome: 'improvement' | 'regression' | 'inconclusive' }>;
  rejectionReasons: string[];
  recommendation: 'insufficientEvidence' | 'keep' | 'rollback';
  recommendationReason: string;
}

export interface MachineTopology {
  processors: Array<{ processorGroup: number; logicalProcessor: number; physicalCore: number; smtIndex: number; efficiencyClass: number; cacheCluster: number }>;
  gpus: Array<{ deviceKey: string; name: string; vendor: string; driverVersion: string; deviceInstanceId: string; affinityRegistryPath: string; physicalHost: boolean }>;
}

export interface GpuCandidateSet {
  hardwareFingerprint: string;
  baselineSessionIds: string[];
  candidates: Array<{
    candidateId: string;
    action: 'gpuIrqAffinitySingleCore';
    deviceKey: string;
    deviceName: string;
    processorGroup: number;
    logicalProcessor: number;
    physicalCore: number;
    smtIndex: number;
    efficiencyClass: number;
    cacheCluster: number;
    assignmentSetOverrideHex: string;
    devicePolicy: number;
    interruptSharePercent: number;
    targetRunningMilliseconds: number;
    readyOverlapMicroseconds: number;
    evidenceIds: string[];
    applyEnabled: false;
    gateReason: string;
  }>;
}

export interface GpuAffinityPolicySnapshot {
  deviceKey: string;
  deviceName: string;
  state: 'windowsDefault' | 'configured' | 'unsupported';
  assignmentSetOverride: { exists: boolean; kind: string; hexValue: string; byteLength: number };
  devicePolicy: { exists: boolean; kind: string; hexValue: string; byteLength: number };
  restorable: boolean;
  applyEnabled: false;
  gateReason: string;
}
