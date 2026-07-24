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
  performanceRegistry: Record<string, string>;
  policyConflicts: string[];
  topProcesses: string[];
  startupItems: string[];
  automaticServices: string[];
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

export interface Recommendation {
  actionId: string;
  evidenceId: string;
  reason: string;
}

export type OptimizationPriority = 'balanced' | 'fps' | 'systemLatency' | 'networkLatency' | 'efficiency';

export interface TuningGoals {
  priority: OptimizationPriority;
  games: string[];
  notes: string;
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
  consentQuestion: string;
}

export interface ScanResult {
  profile: SystemProfile;
  sanitizedProfile: string;
  snapshot: PerformanceSnapshot;
  actions: OptimizationAction[];
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
