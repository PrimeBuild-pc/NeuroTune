export type ProviderKind = 'openRouter' | 'openAI' | 'anthropic' | 'deepSeek' | 'custom' | 'local';
export type ApiProtocol = 'openAiCompatible' | 'anthropic';
export type ThemePreference = 'system' | 'light' | 'dark';

export interface ProviderSettings {
  provider: ProviderKind;
  providerName: string;
  baseUrl: string;
  protocol: ApiProtocol;
  model: string;
  requiresApiKey: boolean;
}

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
  tradeoffs: string[];
  prerequisites: string[];
  requiresRestart: boolean;
  sourceReferences: SourceReference[];
  scriptLanguage: string;
  script: string;
  reviewWarnings: string[];
}

export type OptimizationPriority = 'balanced' | 'fps' | 'systemLatency' | 'networkLatency' | 'efficiency';

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

export interface Diagnosis {
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
  id: string;
  createdAt: string;
  status: string;
  actions: ActionRecord[];
  before?: PerformanceSnapshot;
  after?: PerformanceSnapshot;
  error?: string;
}
