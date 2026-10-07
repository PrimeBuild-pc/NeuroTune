import { useEffect, useMemo, useRef, useState } from 'react';
import './App.css';
import { t, useLanguage, getLanguage, setLanguage, languages, formatDate, type Language } from './i18n';
import { SystemOnePanel, SystemOneNotes } from './SystemOnePanel';
import { CompleteDiagnosis, InvestigationModeControl } from './CompleteDiagnosis';
import { WindowsSecurityPanel } from './WindowsSecurityPanel';
import { DiagnosisFlow, type DiagnosisProgress } from './diagnosisFlow';
import { listen } from '@tauri-apps/api/event';
import {
  Activity, Bot, Check, CircleGauge, Cpu, Database, Download,
  HardDrive, ListChecks, LoaderCircle, LockKeyhole,
  Monitor, MonitorCog, Moon, Palette, RefreshCw, RotateCcw, ScanLine, Settings,
  ShieldCheck, Sun, Target, TerminalSquare, Timer, X,
} from 'lucide-react';
import { agent, cancelAgent, newRequestId } from './agent';
import { analysisPresetsFor, preparatoryBaselines, selectActionIdsForProfile } from './plan';
import { applyTheme, loadThemePreference, useResolvedTheme } from './theme';
import { CaptureCountdown, FirmwarePanel, LatencyDetails, LiveProcessorTimes, MeasurementFeedback } from './LatencyDetails';
import type {
  ChatGptAccountInfo, ConflictPattern, CustomPowerPlanFile, Diagnosis, OperationManifest, OptimizationAction, OptimizationRun, ProviderKind, ProviderSettings,
  GpuAffinityPolicySnapshot, GpuCandidateSet, MachineTopology, MeasurementComparison, MeasurementLabel, MeasurementSession, MeasurementWorkload,
  InvestigationMode, RiskProfile, ScanResult, SupportingAttachment, ThemePreference, TuningGoals,
} from './types';
import { Navigation, type NavigationItem, type Page } from './components/Navigation';
import { Overview } from './components/Overview';
import { Metric } from './components/Metric';
import { ProviderPage } from './components/ProviderPage';
import { ReviewPage } from './components/ReviewPage';
import { DiagnosisView } from './components/DiagnosisView';
import { EmptyState } from './components/EmptyState';
import { MeasurementHistory } from './components/MeasurementHistory';

const defaults: Record<ProviderKind, ProviderSettings> = {
  chatGpt: { provider: 'chatGpt', providerName: 'ChatGPT plan', baseUrl: 'https://api.openai.com/v1', protocol: 'openAiCompatible', model: '', requiresApiKey: true },
  openRouter: { provider: 'openRouter', providerName: 'OpenRouter', baseUrl: 'https://openrouter.ai/api/v1', protocol: 'openAiCompatible', model: 'openai/gpt-4o-mini', requiresApiKey: true },
  openAI: { provider: 'openAI', providerName: 'OpenAI', baseUrl: 'https://api.openai.com/v1', protocol: 'openAiCompatible', model: 'gpt-4o-mini', requiresApiKey: true },
  anthropic: { provider: 'anthropic', providerName: 'Anthropic', baseUrl: 'https://api.anthropic.com/v1', protocol: 'anthropic', model: 'claude-3-5-haiku-latest', requiresApiKey: true },
  deepSeek: { provider: 'deepSeek', providerName: 'DeepSeek', baseUrl: 'https://api.deepseek.com/v1', protocol: 'openAiCompatible', model: 'deepseek-chat', requiresApiKey: true },
  custom: { provider: 'custom', providerName: 'Custom provider', baseUrl: 'https://api.example.com/v1', protocol: 'openAiCompatible', model: '', requiresApiKey: true },
  local: { provider: 'local', providerName: 'Local model', baseUrl: 'http://127.0.0.1:11434/v1', protocol: 'openAiCompatible', model: '', requiresApiKey: false },
};

const navigation: NavigationItem[] = [
  { id: 'overview', label: 'Overview', icon: CircleGauge },
  { id: 'provider', label: 'AI provider', icon: Bot },
  { id: 'scan', label: 'Complete diagnosis', icon: ScanLine },
  { id: 'security', label: 'Privacy & security', icon: ShieldCheck },
  { id: 'advanced', label: 'Local evidence', icon: Database },
  { id: 'measurements', label: 'Measurements', icon: Timer },
  { id: 'review', label: 'Review changes', icon: ListChecks },
  { id: 'activity', label: 'Activity & restore', icon: Activity },
  { id: 'tools', label: 'Advanced tools', icon: MonitorCog },
  { id: 'settings', label: 'Settings', icon: Settings },
];

function App() {
  const language = useLanguage();
  const [startupStatus, setStartupStatus] = useState('Loading the interface…');
  const [page, setPage] = useState<Page>('overview');
  const [theme, setTheme] = useState<ThemePreference>(loadThemePreference);
  const resolvedTheme = useResolvedTheme(theme);
  const [diagnosisOpened, setDiagnosisOpened] = useState(false);
  const [toolsOpened, setToolsOpened] = useState(false);
  useEffect(() => {
    if (page === 'scan') setDiagnosisOpened(true);
    if (page === 'tools') setToolsOpened(true);
  }, [page]);
  const [telemetryConsent, setTelemetryConsent] = useState(
    () => localStorage.getItem('neurotune.optionalTelemetryConsent') === 'true',
  );
  const [provider, setProvider] = useState<ProviderSettings>(defaults.openRouter);
  const [apiKey, setApiKey] = useState('');
  const [hasCredential, setHasCredential] = useState(false);
  const [models, setModels] = useState<string[]>([]);
  const [modelLabels, setModelLabels] = useState<Record<string, string>>({});
  const [chatGptAccounts, setChatGptAccounts] = useState<ChatGptAccountInfo[]>([]);
  const [chatGptWelcome, setChatGptWelcome] = useState(false);
  const welcomeDialog = useRef<HTMLDialogElement>(null);
  useEffect(() => { if (chatGptWelcome && !welcomeDialog.current?.open) welcomeDialog.current?.showModal(); }, [chatGptWelcome]);
  const [scan, setScan] = useState<ScanResult>();
  const [diagnosis, setDiagnosis] = useState<Diagnosis>();
  const localDiagnosis = useRef<Diagnosis | undefined>(undefined);
  const [goals, setGoals] = useState<TuningGoals>({
    priority: 'balanced', riskProfile: 'balanced', games: [], notes: '',
    gameContext: { game: '', version: '', launcher: '', graphicsApi: '', displayMode: '', vrr: '', vSync: '', symptoms: [], preserve: '' },
    performanceInput: { userProvided: true, notes: '' },
  });
  const [mode, setMode] = useState<InvestigationMode>('measuredOptimization');
  const [actions, setActions] = useState<OptimizationAction[]>([]);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [history, setHistory] = useState<OperationManifest[]>([]);
  const [journalError, setJournalError] = useState<string>();
  const [measurementEvidenceIds, setMeasurementEvidenceIds] = useState<Set<string>>(new Set());
  const [activeRun, setActiveRun] = useState<OptimizationRun>();
  const [preparingBaseline, setPreparingBaseline] = useState(false);
  const [busy, setBusy] = useState('');
  const [busyValues, setBusyValues] = useState<Record<string, string | number>>({});
  const [securityBusy, setSecurityBusy] = useState(false);
  const [initializing, setInitializing] = useState(true);
  const [opening, setOpening] = useState(true);
  useEffect(() => {
    if (!initializing && !opening) document.querySelector<HTMLElement>('.topbar h1')?.focus();
  }, [page, initializing, opening]);
  const [recording, setRecording] = useState(true); // Quiet until the agent establishes whether a capture already exists.
  const [diagnosisProgress, setDiagnosisProgress] = useState<DiagnosisProgress>();
  const completeFlow = useRef<DiagnosisFlow | undefined>(undefined);
  const [auditPreview, setAuditPreview] = useState<ScanResult>();
  const auditReview = useRef<((approved: boolean) => void) | undefined>(undefined);
  const [scanRequestId, setScanRequestId] = useState<string>();
  const activeScan = useRef<string | undefined>(undefined);
  const diagnosisRequest = useRef<string | undefined>(undefined);
  const cancellationPending = useRef(false);
  const [notice, setNotice] = useState<{ tone: 'success' | 'danger' | 'info'; text: string; values?: Record<string, string | number>; raw?: boolean }>();

  const quietMotion = recording || diagnosisProgress?.stage === 'capture';
  useEffect(() => applyTheme(theme), [theme]);
  useEffect(() => { document.title = `NeuroTune · ${pageTitle(page)}`; }, [page, language]);
  useEffect(() => {
    const splash = document.getElementById('startup');
    splash?.setAttribute('aria-label', t('Starting NeuroTune'));
    const tagline = splash?.querySelector('.startup-tagline');
    if (tagline) tagline.textContent = t('Evidence · Intelligence · Control');
    const status = document.getElementById('startup-status');
    if (status) status.textContent = t(startupStatus);
  }, [language, startupStatus]);
  useEffect(() => {
    document.documentElement.dataset.motion = quietMotion ? 'quiet' : 'full';
  }, [quietMotion]);
  useEffect(() => {
    if (initializing) return;
    const splash = document.getElementById('startup');
    let cancelled = false;
    const finish = () => {
      if (cancelled) return;
      splash?.remove();
      delete document.documentElement.dataset.startup;
      setOpening(false);
    };
    if (!splash || quietMotion || window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
      finish();
      return;
    }
    // Wait for the real, finite logo animation, not an artificial loading timer.
    // Native CSS cancellation (quiet/reduced) rejects finished; allSettled still releases the UI.
    void Promise.allSettled(splash.getAnimations({ subtree: true }).map(animation => animation.finished)).then(async () => {
      if (cancelled) return;
      splash.classList.add('startup-ready');
      await Promise.allSettled(splash.getAnimations().map(animation => animation.finished));
      finish();
    });
    return () => { cancelled = true; };
  }, [initializing, quietMotion]);
  useEffect(() => {
    const unlisten = listen<{ requestId: string; message: string }>('agent-progress', event => {
      completeFlow.current?.note(event.payload.requestId, event.payload.message);
      if (event.payload.requestId === activeScan.current) { setBusy('Deep scan · {message}'); setBusyValues({ message: event.payload.message }); }
      if (event.payload.requestId === diagnosisRequest.current) { setBusy('Analysis · {message}'); setBusyValues({ message: event.payload.message }); }
    });
    return () => { void unlisten.then(stop => stop()); };
  }, []);
  useEffect(() => {
    Promise.all([
      agent<{ settings: ProviderSettings; hasCredential: boolean; chatGptAccounts: ChatGptAccountInfo[]; isRecording: boolean }>('get-state').then(state => {
        setRecording(state.isRecording ?? false);
        setStartupStatus(state.isRecording ? 'Existing capture: animations disabled. Restoring state…' : 'Configuration loaded. Reading capabilities and local history…');
        return state;
      }),
      agent<OptimizationAction[]>('actions'),
      loadJournals().catch(() => ({ operations: [], runs: [] })),
    ]).then(async ([state, availableActions, { operations, runs }]) => {
      setProvider(state.settings);
      setHasCredential(state.hasCredential);
      setChatGptAccounts(state.chatGptAccounts ?? []);
      setActions(availableActions);
      setHistory(operations);
      const latest = runs.find(item => !['completed', 'failed'].includes(item.state));
      let resumed = latest;
      if (latest) {
        setStartupStatus('Checking the previous session and recovery state…');
        try { resumed = await agent<OptimizationRun>('run-reconcile', { runId: latest.id }); }
        catch (error) { showError(error); }
      }
      hydrateRun(resumed);
      setInitializing(false); // Model discovery is optional network work, not an excuse to block the local interface.
      // Model discovery remains an explicit provider action; opening the UI must not contact a provider.
    }).catch(showError).finally(() => setInitializing(false));
  }, []);

  const displayedDiagnosis = diagnosis && diagnosis === localDiagnosis.current
    ? { ...diagnosis, summary: t(diagnosis.summary), consentQuestion: t(diagnosis.consentQuestion) } : diagnosis;
  const recommendations = useMemo(() => new Map(
    diagnosis?.recommendations.filter(item => item.kind === 'executableAction').map(item => [item.actionId, item.reason]) ?? [],
  ), [diagnosis]);
  const pendingRecovery = history.find(item =>
    item.actions.some(action => (action.applied || action.attempted) && !action.rolledBack) &&
    /(applying|rolling back|incomplete|in corso|applicazione)/i.test(item.status));

  const providerConfigured = Boolean(provider.model.trim() && (hasCredential || !provider.requiresApiKey));
  const runPending = Boolean(activeRun && !['completed', 'failed'].includes(activeRun.state));
  const diagnosisBlockedReason = journalError || pendingRecovery || activeRun?.requiresRecovery ? 'Review recovery before starting another diagnosis.'
    : recording ? 'A capture is active. Stop or finish it in Measurements before starting another diagnosis.'
    : runPending ? 'Finish the retained diagnosis without changes, or continue its review before starting another.'
    : busy || securityBusy ? 'Wait for the current operation to finish.'
    : !providerConfigured ? 'Select a model and configure its provider before starting diagnosis.' : undefined;
  const nextOverviewAction = journalError || pendingRecovery || activeRun?.requiresRecovery
    ? { label: 'Review recovery', onClick: () => setPage('activity') }
    : recording ? { label: 'Measurements', onClick: () => setPage('measurements') }
    : scanRequestId ? { label: 'Local evidence', onClick: () => setPage('advanced') }
    : runPending && activeRun ? { label: ['applying', 'rollingBack'].includes(activeRun.state) ? 'Activity & restore' : ['baselinePending', 'restartPending', 'candidatePending', 'evaluating', 'decisionPending'].includes(activeRun.state) ? 'Measurements' : diagnosis ? 'Review changes' : 'Complete diagnosis', onClick: () => setPage(['applying', 'rollingBack'].includes(activeRun.state) ? 'activity' : ['baselinePending', 'restartPending', 'candidatePending', 'evaluating', 'decisionPending'].includes(activeRun.state) ? 'measurements' : diagnosis ? 'review' : 'scan') }
    : undefined;
  const applyBlockedReason = activeRun?.usedLocalFallback ? 'The provider diagnosis failed. Local observations are not a completed AI audit or authorization to apply changes.'
    : activeRun?.mode === 'auditOnly' ? 'Advisory audit · Apply is unavailable.'
    : !activeRun || ['completed', 'failed'].includes(activeRun.state) ? 'This report has no active optimization run. Start a new diagnosis to propose changes.'
    : activeRun.requiresRecovery ? 'Review recovery before starting another diagnosis.'
    : ['scanned', 'hypothesizing', 'proposalReady', 'baselinePending', 'baselineReady'].includes(activeRun.state) ? 'Apply requires three matching quality-valid workload baselines in this run.'
    : 'Continue this run in Measurements or Activity & restore; action approval is closed.';

  async function loadJournals() {
    try {
      const [operations, runs] = await Promise.all([
        agent<OperationManifest[]>('history'), agent<OptimizationRun[]>('run-list'),
      ]);
      setJournalError(undefined);
      return { operations, runs };
    } catch (error) { setJournalError(String(error)); throw error; }
  }

  async function refreshJournals() {
    const result = await run('Refreshing local session history…', loadJournals);
    if (result) {
      setHistory(result.operations);
      hydrateRun(result.runs.find(item => !['completed', 'failed'].includes(item.state)));
    }
  }

  function showError(error: unknown) {
    setNotice({ tone: 'danger', text: error instanceof Error ? error.message : String(error), raw: true });
  }

  function hydrateRun(run?: OptimizationRun) {
    setActiveRun(run);
    if (!run) return;
    setGoals(run.goals);
    setMode(run.mode ?? 'measuredOptimization');
    setDiagnosis(run.diagnosis);
    setSelected(new Set(run.approvedActionIds));
    setMeasurementEvidenceIds(new Set([...run.baselineSessionIds, ...run.candidateSessionIds]));
    if (['applying', 'rollingBack', 'recoveryRequired'].includes(run.state)) setPage('activity');
    else if (run.usedLocalFallback && run.diagnosis) setPage('review');
    else if (['baselinePending', 'restartPending', 'candidatePending', 'evaluating', 'decisionPending'].includes(run.state)) setPage('measurements');
    else if (['baselineReady', 'approved', 'proposalReady'].includes(run.state)) setPage('review');
    else setPage('scan');
  }

  async function run<T>(label: string, operation: () => Promise<T>, values: Record<string, string | number> = {}): Promise<T | undefined> {
    setBusy(label);
    setBusyValues(values);
    setNotice(undefined);
    try { return await operation(); }
    catch (error) { showError(error); return undefined; }
    finally { setBusy(''); }
  }

  async function chooseProvider(kind: ProviderKind) {
    const account = kind === 'chatGpt' ? chatGptAccounts.find(item => item.connected && item.planEnabled) : undefined;
    const next = { ...defaults[kind], ...(account ? { chatGptAccountId: account.id } : {}) };
    setProvider(next); setModels([]); setModelLabels({}); setApiKey(''); setHasCredential(Boolean(account));
    if (account) {
      const saved = await run('Selecting the connected ChatGPT account…', () => agent('save-provider', { settings: next }));
      if (saved) await discoverModels(next);
    }
  }

  async function selectChatGptModel(model: string) {
    if (!models.includes(model)) return;
    const next = { ...provider, model };
    const saved = await run('Saving selected ChatGPT model…', () => agent('save-provider', { settings: next }));
    if (saved) { setProvider(next); setNotice({ tone: 'success', text: 'ChatGPT model selected and saved.' }); }
  }

  async function saveProvider() {
    const result = await run('Saving encrypted provider configuration…', () =>
      agent<{ saved: boolean; hasCredential: boolean }>('save-provider', { settings: provider, apiKey: apiKey || null }));
    if (result) {
      setHasCredential(result.hasCredential || !provider.requiresApiKey);
      setApiKey('');
      setNotice({ tone: 'success', text: 'Provider configuration saved securely with Windows DPAPI.' });
    }
    return result;
  }

  async function loadModels() {
    if (await saveProvider()) await discoverModels(provider);
  }

  async function discoverModels(settings: ProviderSettings) {
    const result = await run('Loading the connected account’s available models…', async () => {
      try { return await agent<{ models: string[]; modelLabels?: Record<string, string> }>('models'); }
      catch (error) { throw new Error(settings.provider === 'chatGpt' ? t('ChatGPT login is saved, but the model list could not be loaded. Retry loading models; no API billing fallback. {error}', { error: String(error) }) : String(error)); }
    });
    if (!result) return;
    setModels(result.models); setModelLabels(result.modelLabels ?? {});
    if (!result.models.includes(settings.model)) {
      const next = { ...settings, model: result.models[0] ?? '' };
      const saved = await run('Saving the account model selection…', () => agent('save-provider', { settings: next }));
      if (!saved) return;
      setProvider(next);
    }
    setNotice({ tone: 'success', text: '{count} models available from {provider}.', values: { count: result.models.length, provider: settings.providerName } });
  }

  async function browserSignIn(addAccount = false) {
    const chatGpt = provider.provider === 'chatGpt';
    const result = await run('Waiting for {provider} browser authorization…', () =>
      agent<{ settings: ProviderSettings; hasCredential: boolean; chatGptAccounts?: ChatGptAccountInfo[] }>(chatGpt ? 'oauth-chatgpt' : 'oauth-openrouter',
        chatGpt ? { accountId: addAccount ? null : provider.chatGptAccountId ?? null } : undefined), { provider: chatGpt ? 'ChatGPT' : 'OpenRouter' });
    if (!result) return;
    setProvider(result.settings); setHasCredential(result.hasCredential); setApiKey(''); setModels([]);
    if (result.chatGptAccounts) setChatGptAccounts(result.chatGptAccounts);
    if (!result.hasCredential) {
      setNotice({ tone: 'info', text: 'ChatGPT identity is saved, but plan usage was not granted. Continue with ChatGPT to enable it, or explicitly choose API-key billing.' });
      return;
    }
    await discoverModels(result.settings);
    if (chatGpt && !localStorage.getItem(`neurotune.chatgptWelcome.${result.settings.chatGptAccountId}`)) {
      localStorage.setItem(`neurotune.chatgptWelcome.${result.settings.chatGptAccountId}`, 'true'); setChatGptWelcome(true);
    }
  }

  async function signOutChatGpt() {
    const result = await run('Signing out of the selected ChatGPT account…', () => agent<{
      remoteRevocationConfirmed: boolean; chatGptAccounts: ChatGptAccountInfo[]; state: { hasCredential: boolean };
    }>('chatgpt-signout', { accountId: provider.chatGptAccountId }));
    if (result) {
      setChatGptAccounts(result.chatGptAccounts); setHasCredential(result.chatGptAccounts.some(account => account.id === provider.chatGptAccountId && account.connected)); setModels([]);
      setNotice({ tone: result.remoteRevocationConfirmed ? 'success' : 'info', text: result.remoteRevocationConfirmed
        ? 'ChatGPT session revoked and local tokens cleared. Its registration is retained for future sign-in.'
        : 'Local tokens cleared. Remote revocation was not confirmed; disconnect NeuroTune in ChatGPT Settings.' });
    }
  }

  async function scanSystem() {
    if (activeRun && !['completed', 'failed'].includes(activeRun.state)) {
      setNotice({ tone: 'danger', text: 'Finish or recover the active optimization run before starting a new scan.' });
      return;
    }
    if (activeScan.current) return cancelScan();
    const requestId = newRequestId();
    activeScan.current = requestId;
    setScanRequestId(requestId);
    setBusy('Deep scan · starting hardware inventory…');
    setNotice(undefined);
    try {
      const result = await agent<ScanResult>('scan', { optionalTelemetryConsent: telemetryConsent, firmwareReadConsent: localStorage.getItem('neurotune.firmwareReadConsent') === 'true', privacySecurityReadConsent: mode === 'auditOnly' || goals.priority === 'privacySecurity' }, requestId);
      setScan(result);
      setActions(result.actions);
      setDiagnosis(undefined);
      setActiveRun(undefined);
      setSelected(new Set());
      setPage('scan');
      setNotice({ tone: 'success', text: 'Local scan complete. No profile was sent to an AI provider.' });
    } catch (error) {
      if (String(error).includes('Agent request cancelled'))
        setNotice({ tone: 'info', text: 'Scan cancelled. No partial profile was kept.' });
      else showError(error);
    } finally {
      if (activeScan.current === requestId) activeScan.current = undefined;
      cancellationPending.current = false;
      setScanRequestId(current => current === requestId ? undefined : current);
      setBusy('');
    }
  }

  async function cancelScan() {
    const requestId = activeScan.current;
    if (!requestId || cancellationPending.current) return;
    cancellationPending.current = true;
    setBusy('Cancelling scan and its child processes…');
    try { await cancelAgent(requestId); }
    catch (error) { cancellationPending.current = false; showError(error); }
  }

  function cancelCompleteDiagnosis() {
    auditReview.current?.(false);
    void completeFlow.current?.cancel().catch(showError);
  }

  async function completeDiagnosis(workload: MeasurementWorkload | undefined, durationSeconds: number, attachments: SupportingAttachment[] = [], imagesConfirmed = false) {
    if (completeFlow.current || busy || securityBusy || (activeRun && !['completed', 'failed'].includes(activeRun.state))) return;
    if (!provider.model || (!hasCredential && provider.requiresApiKey)) { setPage('provider'); return; }
    const flow = new DiagnosisFlow(setDiagnosisProgress); completeFlow.current = flow;
    setNotice(undefined); setPage('scan'); setSelected(new Set());
    try {
      if (!await saveProvider()) return; // The consented UI provider/model must match what the agent will actually use.
      setNotice(undefined);
      const result = await flow.execute({ goals, mode, workload, durationSeconds, optionalTelemetryConsent: telemetryConsent, firmwareReadConsent: localStorage.getItem('neurotune.firmwareReadConsent') === 'true', attachments, imagesConfirmed,
        reviewAudit: prepared => new Promise<void>((resolve, reject) => {
          setAuditPreview(prepared);
          auditReview.current = approved => {
            setAuditPreview(undefined); auditReview.current = undefined;
            if (approved) resolve(); else reject(new Error(t("Audit cancelled before provider transmission. Local evidence was not sent.")));
          };
        }),
      });
      setScan(result.scan); setActions(result.scan.actions); setDiagnosis(result.diagnosis); setActiveRun(result.run);
      setMeasurementEvidenceIds(new Set(result.sessions.map(item => item.id))); setPreparingBaseline(false); setPage('review');
      if (result.run.usedLocalFallback) showError(result.run.error ?? result.diagnosis.summary);
    } catch (error) {
      showError(error);
      try { const runs = await agent<OptimizationRun[]>('run-list'); hydrateRun(runs.find(item => !['completed', 'failed'].includes(item.state))); }
      catch (reason) { setJournalError(String(reason)); showError(reason); }
    } finally { auditReview.current = undefined; setAuditPreview(undefined); completeFlow.current = undefined; setDiagnosisProgress(undefined); }
  }

  async function diagnose() {
    if (!scan) return scanSystem();
    if (diagnosisRequest.current) return;
    const requestId = newRequestId(); diagnosisRequest.current = requestId;
    try {
    if (!await run('Checking local recovery journals before collection…', loadJournals)) return;
    let baselineIds = [...measurementEvidenceIds];
    const runMode = activeRun && !['completed', 'failed'].includes(activeRun.state) ? activeRun.mode ?? 'measuredOptimization' : mode;
    if (runMode === 'auditOnly') {
      baselineIds = [];
      if (!Object.keys(scan.profile.privacySecurity ?? {}).length) {
        setNotice({ tone: 'info', text: 'Collect privacy/security evidence locally with Scan again, then review the prepared profile before AI audit.' });
        return;
      }
    }
    if (runMode !== 'auditOnly' && (!activeRun || ['completed', 'failed'].includes(activeRun.state))) {
      const sessions = await run('Checking selected baseline evidence before AI diagnosis…', () => agent<MeasurementSession[]>('measurement-list'));
      if (!sessions) return;
      const matched = preparatoryBaselines(sessions, measurementEvidenceIds);
      if (matched.length < 3) {
        setPreparingBaseline(true); setPage('measurements');
        setNotice({ tone: 'info', text: 'Measure first: {count}/3 selected matching workload baselines. Record and analyze three repeatable baselines, then continue to AI diagnosis. System-wide captures are diagnostic only.', values: { count: matched.length } });
        return;
      }
      baselineIds = matched.map(session => session.id); setMeasurementEvidenceIds(new Set(baselineIds)); setPreparingBaseline(false);
    }
    const conflicts = await run('Building the local objective-aware conflict graph…', () =>
      agent<ConflictPattern[]>('analyze-local', { profile: scan.profile, goals }));
    if (!conflicts) return;
    const optimizationRun = activeRun && (activeRun.state === 'scanned' || activeRun.state === 'hypothesizing')
      ? activeRun
      : await run('Opening a recoverable optimization run…', () =>
        agent<OptimizationRun>('run-create', {
          profile: scan.profile, goals, mode: runMode, measurementSessionIds: baselineIds,
        }));
    if (!optimizationRun) return;
    setActiveRun(optimizationRun);
    const result = await run('AI synthesis · checking every claim against local evidence…', () =>
      agent<Diagnosis>('diagnose', {
        profile: scan.profile, goals, measurementSessionIds: baselineIds, runId: optimizationRun.id, language: getLanguage(),
      }, requestId));
    hydrateRun(await agent<OptimizationRun>('run-get', { runId: optimizationRun.id }));
    const nextDiagnosis = result ?? {
      summary: 'The provider diagnosis failed. Local observations are not a completed AI audit or authorization to apply changes.',
      findings: [], recommendations: [], conflicts,
      consentQuestion: 'Review the unavailable investigation and retry or finish without changes?',
    };
    localDiagnosis.current = result ? undefined : nextDiagnosis;
    setDiagnosis(nextDiagnosis);
    setSelected(new Set());
    setPage('review');
    } finally { diagnosisRequest.current = undefined; }
  }

  async function dismissDiagnosis() {
    if (!activeRun || activeRun.operationId) return;
    const dismissed = await run('Closing diagnosis without changes…', () => agent<OptimizationRun>('run-dismiss', { runId: activeRun.id }));
    if (dismissed) { setActiveRun(dismissed); setSelected(new Set()); setNotice({ tone: 'info', text: 'Diagnosis closed without applying changes. The report remains reviewable.' }); }
  }

  function applyPreset(mode: RiskProfile | 'none') {
    const ids = selectActionIdsForProfile(actions, diagnosis?.recommendations ?? [], mode);
    if (mode !== 'none') setGoals(current => ({ ...current, riskProfile: mode }));
    setSelected(new Set(ids));
  }

  async function applyChanges() {
    if (!activeRun || activeRun.mode === 'auditOnly' || activeRun.state !== 'baselineReady') {
      setNotice({ tone: 'danger', text: 'A quality-valid Baseline in the active optimization run is required before apply.' });
      return;
    }
    const highRisk = actions.filter(action => selected.has(action.id) && action.risk === 'high').length;
    if (!selected.size || !window.confirm(t('Apply {count} selected changes after creating a verified restore point?', { count: selected.size }))) return;
    if (highRisk && !window.confirm(t('Separate high-risk confirmation: apply {count} HIGH RISK action(s)? Review evidence, side effects, and rollback notes before continuing.', { count: highRisk }))) return;
    const result = await run('Creating backups and applying verified changes…', () =>
      agent<OperationManifest>('apply', { actionIds: [...selected], highRiskConfirmed: highRisk > 0, runId: activeRun.id }));
    if (result) {
      setHistory(current => [result, ...current]);
      if (activeRun) setActiveRun(await agent<OptimizationRun>('run-get', { runId: activeRun.id }));
      setSelected(new Set());
      setPage('activity');
      setNotice({ tone: 'success', text: 'Operation completed. Restart Windows if a selected action requires it.' });
      const refreshed = await agent<OptimizationAction[]>('actions');
      setActions(refreshed);
    }
  }

  async function rollback(operationId: string) {
    if (!window.confirm(t("Create another restore point and restore this operation?"))) return;
    const runId = history.find(item => item.id === operationId)?.optimizationRunId;
    const result = await run('Restoring the saved system state…', () => agent<null>('rollback', { operationId, runId }));
    if (result !== undefined) {
      setHistory(await agent<OperationManifest[]>('history'));
      setActions(await agent<OptimizationAction[]>('actions'));
      if (runId) setActiveRun(await agent<OptimizationRun>('run-get', { runId }));
      setNotice({ tone: 'success', text: 'Rollback completed and verified.' });
    }
  }

  return (
    <div className="app-shell" inert={initializing || opening} aria-hidden={initializing || opening || undefined}>
      <dialog ref={welcomeDialog} className="chatgpt-welcome" aria-labelledby="chatgpt-welcome-title" onClose={() => setChatGptWelcome(false)} onCancel={() => setChatGptWelcome(false)}>
        <h2 id="chatgpt-welcome-title">{t("You’re using your ChatGPT plan")}</h2><p>{t("Eligible AI requests in NeuroTune use your ChatGPT plan or available credits. Usage limits are shared with other apps. API-key billing is separate and never selected automatically.")}</p>
        <a href="https://chatgpt.com/settings/usage" target="_blank" rel="noreferrer">{t("Manage usage in ChatGPT")}</a>
        <form method="dialog"><button className="primary">{t("Got it")}</button></form>
      </dialog>
      <a className="skip-link" href="#workspace">{t("Skip to workspace")}</a>
      <aside className="sidebar">
        <div className="brand"><img className="brand-mark" src="/logo.svg" width="40" height="40" alt=""/><div><strong>NeuroTune</strong><span>{t("Windows intelligence")}</span></div></div>
        <Navigation items={navigation.map(item => ({ ...item, label: t(item.label) }))} page={page} onPage={setPage} disabled={Boolean(diagnosisProgress) || securityBusy} quiet={quietMotion}/>
        <div className="sidebar-foot">
          <div className="security-chip"><ShieldCheck size={16}/><span>{t("Allowlisted actions")}</span></div>
          <small>v0.8.0-alpha.2</small>
        </div>
      </aside>

      <main className="workspace" id="workspace" tabIndex={-1}>
        <header className="topbar">
          <div><span className="eyebrow">{t(navigation.find(item => item.id === page)?.label ?? '')}</span><h1 tabIndex={-1}>{pageTitle(page)}</h1></div>
          <div className="topbar-actions">
            <button className="icon-button" aria-label={t('Quick Settings')} title={t('Settings')} disabled={Boolean(diagnosisProgress) || securityBusy} onClick={() => setPage('settings')}><Settings size={19}/></button>
            <button className="icon-button" aria-label={t("Toggle light and dark theme")} onClick={() => setTheme(resolvedTheme === 'dark' ? 'light' : 'dark')}>{resolvedTheme === 'dark' ? <Sun size={19}/> : <Moon size={19}/>}</button>
            <div className={providerConfigured ? 'connection online' : 'connection'} title={provider.model ? `${provider.providerName} · ${provider.model}` : t('Select a model')}><span aria-hidden="true"/><div><strong>{provider.providerName}</strong><small>{providerConfigured ? t('Configured · not connection-tested') : !provider.model ? t('Select a model') : t('Needs setup')}</small></div></div>
          </div>
        </header>

        {notice && <div className={`notice ${notice.tone}`} role="status"><span>{notice.raw ? notice.text : t(notice.text, notice.values)}</span><button aria-label={t("Dismiss message")} onClick={() => setNotice(undefined)}>×</button></div>}
        {busy && <div className="busy-bar" role="status"><LoaderCircle size={16} className="spin"/><span>{t(busy, busyValues)}</span>{scanRequestId && <button className="ghost" onClick={cancelScan}><X size={14}/>{t("Cancel scan")}</button>}</div>}
        {journalError && <div className="notice danger" role="alert"><div><strong>{t('Review recovery')}</strong><p>{journalError}</p></div><button className="secondary" disabled={Boolean(recording || busy || securityBusy || diagnosisProgress)} onClick={() => void refreshJournals()}>{t('Refresh')}</button></div>}
        {pendingRecovery && <div className="recovery-banner" role="alert"><div><RotateCcw size={18}/><span><strong>{t("An interrupted operation needs attention.")}</strong><small>{pendingRecovery.id}</small></span></div><button className="secondary" onClick={() => setPage('activity')}>{t("Review recovery")}</button></div>}

        {activeRun && !activeRun.operationId && !activeRun.requiresRecovery && !['completed', 'failed'].includes(activeRun.state) && !diagnosisProgress && <div className="notice info"><span>{t("A proposal or interrupted diagnosis is retained. No new diagnosis can replace it until you finish or decline it.")}</span><button className="secondary" disabled={Boolean(busy)} onClick={() => { void dismissDiagnosis(); }}>{t("Finish without changes")}</button></div>}
        <section className="page-content">
          {page === 'overview' && <Overview hasProvider={providerConfigured} nextAction={nextOverviewAction} disabled={Boolean(busy || securityBusy)} scan={scan} history={history} coreState={quietMotion ? 'capture' : scanRequestId ? 'scan' : diagnosis && activeRun && ['baselineReady', 'baselinePending', 'proposalReady'].includes(activeRun.state) ? 'result' : 'idle'} onProvider={() => setPage('provider')} onScan={() => setPage('scan')} onPrivacy={() => setPage('security')}/>}
          {page === 'provider' && <ProviderPage provider={provider} apiKey={apiKey} hasCredential={hasCredential} models={models} modelLabels={modelLabels} chatGptAccounts={chatGptAccounts} authBusy={Boolean(busy)} onSignOut={signOutChatGpt} onChoose={chooseProvider} onChange={value => { setProvider(value); if (value.chatGptAccountId !== provider.chatGptAccountId) { setHasCredential(false); setModels([]); } }} onKey={setApiKey} onSave={saveProvider} onLoadModels={loadModels} onBrowserSignIn={browserSignIn} onModel={selectChatGptModel}/>}
          {(page === 'scan' || diagnosisOpened) && <div hidden={page !== 'scan'}><CompleteDiagnosis onSecurity={() => setPage('security')} consentKey={`${provider.provider}:${provider.model}:${provider.baseUrl}:${provider.chatGptAccountId ?? ''}`} blockedReason={diagnosisBlockedReason} onProvider={!providerConfigured && !recording && !busy && !securityBusy && !runPending && !pendingRecovery ? () => setPage('provider') : undefined} mode={mode} onMode={setMode} goals={goals} onGoals={setGoals} onStart={(workload, duration, attachments, imagesConfirmed) => { void completeDiagnosis(workload, duration, attachments, imagesConfirmed); }} progress={diagnosisProgress} auditPreview={auditPreview} onAuditApprove={() => auditReview.current?.(true)} onCancel={cancelCompleteDiagnosis} blocked={Boolean(diagnosisBlockedReason)}><GoalContextEditor goals={goals} onGoals={setGoals}/></CompleteDiagnosis></div>}
          {page === 'security' && <div className="security-page"><WindowsSecurityPanel blocked={Boolean(recording || busy || diagnosisProgress || pendingRecovery || activeRun && activeRun.mode !== 'auditOnly' && !['completed', 'failed'].includes(activeRun.state))} onBusy={setSecurityBusy}><section className="section-card"><h2>{t('AI-led investigation')}</h2><p>{t('Review Windows privacy and security evidence, protections and detections. Opening this page does not start collection, a scan, an AI request, a download or a system change.')}</p><p className="muted-copy">{t('The privacy and security objective specializes the investigation; mode, consent, provider, risk profile and approval remain separate. Choose it below, then review the complete diagnosis controls before starting.')}</p><button className="primary" disabled={Boolean(recording || busy || securityBusy || diagnosisProgress || pendingRecovery || activeRun && !['completed', 'failed'].includes(activeRun.state))} onClick={() => { setGoals(current => ({ ...current, priority: 'privacySecurity' })); setPage('scan'); }}><ShieldCheck size={16}/>{t('Choose privacy & security diagnosis')}</button><div className="settings-lines"><div><LockKeyhole size={18}/><span><strong>{t('Credentials')}</strong><small>{t('Encrypted with Windows DPAPI for this user')}</small></span></div><div><TerminalSquare size={18}/><span><strong>{t('Model output')}</strong><small>{t('AI can propose manual guidance and review-only scripts; only registered actions have write authority')}</small></span></div><div><RotateCcw size={18}/><span><strong>{t('Recovery')}</strong><small>{t('Verified restore point, Registry exports, and action journal')}</small></span></div></div></section></WindowsSecurityPanel></div>}
          {page === 'advanced' && <ScanPage mode={mode} onMode={value => { if (!busy && (!activeRun || ['completed', 'failed'].includes(activeRun.state))) setMode(value); }} scan={scan} diagnosis={displayedDiagnosis} goals={goals} scanning={Boolean(scanRequestId)} onGoals={setGoals} onScan={scanSystem} onDiagnose={diagnose}/>}
          {page === 'measurements' && <MeasurementsPage evidenceIds={measurementEvidenceIds} onEvidenceIds={setMeasurementEvidenceIds} optimizationRun={preparingBaseline ? undefined : activeRun} onRun={setActiveRun} preparingBaseline={preparingBaseline} onBaselinePrepared={() => { void diagnose(); }} analysisGoals={goals} onRecording={setRecording}/>}
          {page === 'review' && <ReviewPage onDiagnosis={() => setPage(providerConfigured ? 'scan' : 'provider')} investigationFailed={Boolean(activeRun?.usedLocalFallback || diagnosis && diagnosis === localDiagnosis.current)} applyBlockedReason={applyBlockedReason} auditOnly={activeRun?.mode === 'auditOnly'} auditEvidence={activeRun?.evidenceFacts} diagnosis={displayedDiagnosis} supporting={activeRun?.supportingAttachments} actions={actions} recommendations={recommendations} selected={selected} riskProfile={goals.riskProfile} canApply={activeRun?.mode !== 'auditOnly' && activeRun?.state === 'baselineReady'} onToggle={id => setSelected(current => toggle(current, id))} onPreset={applyPreset} onApply={applyChanges} onDismiss={activeRun && !activeRun.operationId && !activeRun.requiresRecovery && !['completed', 'failed'].includes(activeRun.state) ? () => { void dismissDiagnosis(); } : undefined}/>}
          {page === 'activity' && !journalError && <ActivityPage history={history} onRefresh={() => void refreshJournals()} onRollback={rollback}/>}
          {page === 'settings' && <SettingsPage theme={theme} onTheme={setTheme}/>}
          {(page === 'tools' || toolsOpened) && <div hidden={page !== 'tools'}><AdvancedToolsPage telemetryConsent={telemetryConsent} onActions={setActions} onTelemetryConsent={value => {
            setTelemetryConsent(value);
            localStorage.setItem('neurotune.optionalTelemetryConsent', String(value));
          }}/></div>}
        </section>
      </main>
    </div>
  );
}

function ScanPage({ scan, diagnosis, goals, scanning, onGoals, onScan, onDiagnose, mode, onMode }: { mode: InvestigationMode; onMode: (mode: InvestigationMode) => void; scan?: ScanResult; diagnosis?: Diagnosis; goals: TuningGoals; scanning: boolean; onGoals: (value: TuningGoals) => void; onScan: () => void; onDiagnose: () => void }) {
  if (!scan) return <EmptyState icon={ScanLine} title={t("No local profile yet")} text={t("Scan Windows locally first. NeuroTune will not contact your AI provider during this step.")} action={scanning ? t("Cancel scan") : t("Scan this PC")} onAction={onScan}/>;
  const priorities = analysisPresetsFor(goals.priority);
  return <div className="stack-lg"><div className="page-actions"><div><span className="eyebrow">{t("Local scan · no AI")}</span><h2>{t("Set the target before diagnosis")}</h2></div><div className="button-row"><button className="secondary" onClick={onScan}>{scanning ? <X size={16}/> : <RefreshCw size={16}/>} {scanning ? t("Cancel scan") : t("Scan again")}</button><button className="primary" disabled={scanning || !scan.payloadReport.fitsSinglePass} onClick={onDiagnose}><Bot size={16}/>{scan.payloadReport.fitsSinglePass ? t("Run AI diagnosis") : t("Payload exceeds single-pass limit")}</button></div></div><div className="metric-grid four"><Metric icon={Monitor} label={t('Windows')} value={scan.profile.operatingSystem}/><Metric icon={Cpu} label={t("Processor")} value={scan.profile.cpu}/><Metric icon={Database} label={t("Memory")} value={scan.profile.memory}/><Metric icon={HardDrive} label={t("Registry checks")} value={t('{count} inspected', { count: Object.keys(scan.profile.performanceRegistry).length })}/></div><section className="scan-summary"><div className="scan-phases">{scan.profile.scanPhases.map(phase => <article key={phase.name}><Check size={15}/><div><strong>{phase.name}</strong><small>{t('{count} facts · {seconds} s', { count: phase.factsCollected, seconds: (phase.durationMilliseconds / 1000).toFixed(1) })}</small></div></article>)}</div><div className="inventory-counts"><span><strong>{scan.profile.installedSoftware.length}</strong>  {t("applications")}</span><span><strong>{scan.profile.relevantDrivers.length}</strong>  {t("relevant drivers")}</span><span><strong>{scan.profile.softwareSignals.length}</strong>  {t("tuning/overlay signals")}</span><span><strong>{scan.profile.deviceIssues.length}</strong>  {t("device issues")}</span></div></section>{scan.updateNotices.length > 0 && <section className="section-card update-notices"><div className="section-heading"><div><span className="eyebrow">{t("Official manual updates")}</span><h3>{t("Driver, chipset, and BIOS advisor")}</h3></div><span className="status-pill">{t("Never auto-installed")}</span></div><div className="plan-item-list">{scan.updateNotices.map(notice => <article className="plan-item updateNotice" key={notice.id}><div className="plan-item-heading"><Download size={18}/><div><span>{notice.vendor} · {statusLabel(notice.kind)}</span><strong>{notice.model}</strong></div><span className="status-pill">{statusLabel(notice.status)}</span></div><p>{notice.reason}</p><small>{t('Installed: {version}', { version: notice.installedVersion || t('unavailable') })}{notice.latestVersion && t(' · Latest verified: {version}', { version: notice.latestVersion })}</small><a href={notice.officialUrl} target="_blank" rel="noreferrer">{t('Open official {vendor} support', { vendor: notice.vendor })}</a></article>)}</div></section>}<section className="section-card telemetry-card"><div className="section-heading"><div><span className="eyebrow">{t("Optional low-level telemetry")}</span><h3>{t("Read-only support matrix")}</h3></div><span className="status-pill">{t("No driver installation")}</span></div><div className="telemetry-grid">{scan.profile.telemetryCapabilities.map(capability => <article key={capability.name}><div><strong>{capability.name}</strong><span className={`telemetry-status ${capability.status}`}>{statusLabel(capability.status)}</span></div><p>{capability.detail}</p></article>)}</div></section><section className="section-card goals-card"><div className="section-heading"><div><span className="eyebrow">{t("Optimization intent")}</span><h3>{t("What matters on this PC?")}</h3></div><Target size={22}/></div><div className="priority-options">{priorities.map(item => <button key={item.id} aria-pressed={goals.priority === item.id} className={goals.priority === item.id ? 'priority-option active' : 'priority-option'} onClick={() => onGoals({ ...goals, priority: item.id })}><strong>{item.label}</strong><small>{item.detail}</small></button>)}</div><p className="muted-copy">{t("The preset specializes the investigation prompt, not a tweak package. Mode, budget, risk profile and approval remain separate.")}</p><div className="form-grid"><InvestigationModeControl mode={mode} onMode={onMode}/></div><div className="goal-fields"><label><span>{t("Games or workloads")}</span><input maxLength={1200} value={goals.games.join(', ')} placeholder={t("Example: Valorant, Cyberpunk 2077")} onChange={event => onGoals({ ...goals, games: event.target.value.split(',').map(x => x.trim()).filter(Boolean) })}/><small>{t("Names provide context only; game-specific claims still require evidence.")}</small></label><label><span>{t("Anything else to preserve or improve?")}</span><textarea maxLength={1000} value={goals.notes} placeholder={t("Example: keep power use reasonable; Wi-Fi only")} onChange={event => onGoals({ ...goals, notes: event.target.value })}/></label></div><GoalContextEditor goals={goals} onGoals={onGoals}/><p className="muted-copy">{t("This scan collects Windows, hardware and configuration facts with local deterministic code. It does not call either AI. Record baselines, then run AI diagnosis to interpret the evidence.")}</p>{scan.profile.policyConflicts.length > 0 && <div className="local-observations"><strong>{t("Local conflicts and manual overrides")}</strong><ul>{scan.profile.policyConflicts.map(item => <li key={item}>{item}</li>)}</ul></div>}</section><div className="split-panels"><section className="section-card"><div className="section-heading"><div><span className="eyebrow">{t("Provider payload")}</span><h3>{t("Sanitized profile")}</h3></div><span className={`status-pill ${scan.payloadReport.fitsSinglePass ? 'good' : ''}`}>{t('{count} facts · {size} / {limit}', { count: scan.payloadReport.factCount, size: formatBytes(scan.payloadReport.utf8Bytes), limit: formatBytes(scan.payloadReport.singlePassLimitBytes) })}</span></div><div className="payload-privacy">{Object.entries(scan.payloadReport.privacyClasses).map(([privacy, count]) => <span key={privacy}>{statusLabel(privacy)}: {count}</span>)}</div><pre className="profile-json">{scan.sanitizedProfile}</pre></section><section className="section-card"><div className="section-heading"><div><span className="eyebrow">{t("Model output")}</span><h3>{t("Diagnosis")}</h3></div></div>{diagnosis ? <DiagnosisView diagnosis={diagnosis}/> : <div className="panel-placeholder"><Bot size={30}/><p>{t("No data has been sent yet.")}</p><span>{t("Your goals and this reviewed profile are sent only when you run diagnosis.")}</span></div>}</section></div></div>;
}

function GoalContextEditor({ goals, onGoals }: { goals: TuningGoals; onGoals: (value: TuningGoals) => void }) {
  const context = goals.gameContext;
  const metrics = goals.performanceInput;
  const setContext = (change: Partial<TuningGoals['gameContext']>) => onGoals({ ...goals, gameContext: { ...context, ...change } });
  const setMetrics = (change: Partial<TuningGoals['performanceInput']>) => onGoals({ ...goals, performanceInput: { ...metrics, ...change, userProvided: true } });
  return <section className="context-editor">
    <h3>{t("Optional game details and user-provided measurements")}</h3>
    <p>{t("These values improve context. Measurements are labelled as user-provided and are not treated as benchmark proof.")}</p>
    <div className="context-grid">
      <label><span>{t("Primary game")}</span><input maxLength={120} value={context.game} onChange={event => setContext({ game: event.target.value })}/></label>
      <label><span>{t("Game version")}</span><input maxLength={100} value={context.version} onChange={event => setContext({ version: event.target.value })}/></label>
      <label><span>{t("Launcher")}</span><input maxLength={100} value={context.launcher} onChange={event => setContext({ launcher: event.target.value })}/></label>
      <label><span>{t("Graphics API")}</span><input maxLength={40} placeholder={t('DirectX 12, Vulkan…')} value={context.graphicsApi} onChange={event => setContext({ graphicsApi: event.target.value })}/></label>
      <label><span>{t("Resolution")}</span><div className="inline-inputs"><input aria-label={t("Resolution width")} type="number" min="320" max="16384" value={context.width ?? ''} onChange={event => setContext({ width: optionalNumber(event.target.value) })}/><span>×</span><input aria-label={t("Resolution height")} type="number" min="200" max="16384" value={context.height ?? ''} onChange={event => setContext({ height: optionalNumber(event.target.value) })}/></div></label>
      <label><span>{t("Refresh rate")}</span><input type="number" min="20" max="1000" value={context.refreshRateHz ?? ''} onChange={event => setContext({ refreshRateHz: optionalNumber(event.target.value) })}/></label>
      <label><span>{t("Display mode")}</span><input maxLength={40} placeholder={t("Fullscreen, borderless…")} value={context.displayMode} onChange={event => setContext({ displayMode: event.target.value })}/></label>
      <label><span>{t('VRR / V-Sync')}</span><div className="inline-inputs"><input aria-label={t("VRR state")} maxLength={40} value={context.vrr} onChange={event => setContext({ vrr: event.target.value })}/><input aria-label={t("V-Sync state")} maxLength={40} value={context.vSync} onChange={event => setContext({ vSync: event.target.value })}/></div></label>
      <label><span>{t("Frame cap")}</span><input type="number" min="10" max="2000" value={context.frameCap ?? ''} onChange={event => setContext({ frameCap: optionalNumber(event.target.value) })}/></label>
      <label><span>{t("Symptoms")}</span><input maxLength={2400} value={context.symptoms.join(', ')} placeholder={t("Stutter, packet loss, input lag…")} onChange={event => setContext({ symptoms: event.target.value.split(',').map(value => value.trim().slice(0, 200)).filter(Boolean).slice(0, 12) })}/></label>
      <label className="wide"><span>{t("Preserve")}</span><input maxLength={500} value={context.preserve} placeholder={t("Security, image quality, battery life…")} onChange={event => setContext({ preserve: event.target.value })}/></label>
    </div>
    <p className="muted-copy">{t('User-provided · not benchmark proof')}</p>
    <div className="context-grid" aria-label={t("User-provided measurements")}>
      <label><span>{t("Average FPS")}</span><input type="number" min="0" step="0.1" value={metrics.averageFps ?? ''} onChange={event => setMetrics({ averageFps: optionalNumber(event.target.value) })}/></label>
      <label><span>{t("1% low FPS")}</span><input type="number" min="0" step="0.1" value={metrics.onePercentLowFps ?? ''} onChange={event => setMetrics({ onePercentLowFps: optionalNumber(event.target.value) })}/></label>
      <label><span>{t("Average frame time (ms)")}</span><input type="number" min="0" step="0.01" value={metrics.averageFrameTimeMs ?? ''} onChange={event => setMetrics({ averageFrameTimeMs: optionalNumber(event.target.value) })}/></label>
      <label><span>{t("Input latency (ms)")}</span><input type="number" min="0" step="0.1" value={metrics.inputLatencyMs ?? ''} onChange={event => setMetrics({ inputLatencyMs: optionalNumber(event.target.value) })}/></label>
      <label><span>{t("Network latency (ms)")}</span><input type="number" min="0" step="0.1" value={metrics.networkLatencyMs ?? ''} onChange={event => setMetrics({ networkLatencyMs: optionalNumber(event.target.value) })}/></label>
      <label><span>{t("Packet loss (%)")}</span><input type="number" min="0" max="100" step="0.01" value={metrics.packetLossPercent ?? ''} onChange={event => setMetrics({ packetLossPercent: optionalNumber(event.target.value) })}/></label>
      <label className="wide"><span>{t("Measurement notes")}</span><textarea maxLength={1000} value={metrics.notes} onChange={event => setMetrics({ notes: event.target.value })}/></label>
    </div>
  </section>;
}

function MeasurementsPage({ evidenceIds, onEvidenceIds, optimizationRun, onRun, preparingBaseline, onBaselinePrepared, analysisGoals, onRecording }: { evidenceIds: Set<string>; onEvidenceIds: (value: Set<string>) => void; optimizationRun?: OptimizationRun; onRun: (value: OptimizationRun) => void; preparingBaseline: boolean; onBaselinePrepared: () => void; analysisGoals: TuningGoals; onRecording: (value: boolean) => void }) {
  const [systemWide, setSystemWide] = useState(!optimizationRun && !preparingBaseline);
  const [workloads, setWorkloads] = useState<MeasurementWorkload[]>([]);
  const [sessions, setSessions] = useState<MeasurementSession[]>([]);
  const [selectedProcessId, setSelectedProcessId] = useState('');
  const [label, setLabel] = useState<MeasurementLabel>('baseline');
  const [durationSeconds, setDurationSeconds] = useState(180);
  const [keepRawTrace, setKeepRawTrace] = useState(false);
  const [focusedId, setFocusedId] = useState('');
  const [compareIds, setCompareIds] = useState<Set<string>>(new Set());
  const [comparison, setComparison] = useState<MeasurementComparison>();
  const [comparisonExplanation, setComparisonExplanation] = useState<string>();
  const [topology, setTopology] = useState<MachineTopology>();
  const [selectedGpu, setSelectedGpu] = useState('');
  const [gpuCandidates, setGpuCandidates] = useState<GpuCandidateSet>();
  const [gpuPolicy, setGpuPolicy] = useState<GpuAffinityPolicySnapshot>();
  const [busy, setBusy] = useState('');
  const [message, setMessage] = useState('');
  const [loading, setLoading] = useState(true);
  const [startedAt, setStartedAt] = useState(Date.now());
  const [operationLog, setOperationLog] = useState<Array<{ text: string; raw?: boolean }>>([]);
  const operationRequest = useRef<string | undefined>(undefined);
  const operationRevision = useRef(0);
  const analysisRequest = useRef<string | undefined>(undefined);

  useEffect(() => {
    const unlisten = listen<{ requestId: string; message: string }>('agent-progress', event => {
      if (event.payload.requestId === operationRequest.current)
        setOperationLog(items => [...items, { text: event.payload.message, raw: true }].slice(-12));
    });
    return () => { void unlisten.then(stop => stop()); };
  }, []);

  const refreshSessions = async () => {
    const items = await agent<MeasurementSession[]>('measurement-list');
    setSessions(items);
    if (!focusedId && items.length) setFocusedId(items[0].id);
  };
  const refreshWorkloads = async () => {
    const items = await agent<MeasurementWorkload[]>('measurement-workloads');
    setWorkloads(items);
    if (!items.some(item => String(item.processId) === selectedProcessId)) setSelectedProcessId(items[0] ? String(items[0].processId) : '');
  };
  useEffect(() => {
    let disposed = false;
    void Promise.all([
      agent<MeasurementWorkload[]>('measurement-workloads').then(processes => {
        if (disposed) return;
        setWorkloads(processes);
        setSelectedProcessId(processes[0] ? String(processes[0].processId) : '');
      }),
      agent<MeasurementSession[]>('measurement-list').then(history => {
        if (disposed) return;
        setSessions(history);
        setFocusedId(history[0]?.id ?? '');
      }),
    ]).catch(error => { if (!disposed) setMessage(String(error)); })
      .finally(() => { if (!disposed) setLoading(false); });
    return () => { disposed = true; };
  }, []);
  useEffect(() => {
    if (optimizationRun?.state === 'baselinePending' || optimizationRun?.state === 'baselineReady') setLabel('baseline');
    if (optimizationRun?.state === 'candidatePending') setLabel('candidate');
  }, [optimizationRun?.state]);

  const active = sessions.find(item => item.state === 'recording');
  const activeId = active?.id;
  useEffect(() => { if (!loading) onRecording(sessions.some(item => item.state === 'recording')); }, [sessions, loading, onRecording]);
  useEffect(() => {
    if (!activeId) return;
    let disposed = false;
    let timer: number | undefined;
    async function poll() {
      if (operationRequest.current) {
        timer = window.setTimeout(() => void poll(), 3000);
        return;
      }
      const revision = operationRevision.current;
      try {
        const items = await agent<MeasurementSession[]>('measurement-list');
        if (!disposed && !operationRequest.current && revision === operationRevision.current) setSessions(items);
      } catch (error) { if (!disposed) setMessage(String(error)); }
      if (!disposed) timer = window.setTimeout(() => void poll(), 3000);
    }
    timer = window.setTimeout(() => void poll(), 3000);
    return () => { disposed = true; window.clearTimeout(timer); };
  }, [activeId]);
  const focused = sessions.find(item => item.id === focusedId) ?? sessions[0];

  async function execute(text: string, operation: (requestId: string) => Promise<unknown>) {
    if (operationRequest.current || loading) return;
    const requestId = newRequestId();
    operationRequest.current = requestId;
    operationRevision.current++;
    setStartedAt(Date.now()); setOperationLog([{ text }]); setBusy(text); setMessage('');
    try {
      await operation(requestId);
      setOperationLog(items => [...items, { text: 'Refreshing local session history…' }]);
      await refreshSessions();
    }
    catch (error) { setMessage(error instanceof Error ? error.message : String(error)); }
    finally { operationRequest.current = undefined; setBusy(''); }
  }
  function updateSession(session: MeasurementSession) {
    setSessions(items => [session, ...items.filter(item => item.id !== session.id)].sort((a, b) => b.createdAtUtc.localeCompare(a.createdAtUtc)));
  }
  async function stopCapture() {
    if (!active) return;
    await execute('Stopping and saving the trace…', async requestId => {
      updateSession(await agent<MeasurementSession>('measurement-stop', { sessionId: active.id }, requestId));
    });
  }
  async function cancelCapture() {
    if (!active) return;
    await execute('Cancelling and deleting incomplete data…', async requestId => {
      await agent('measurement-cancel', { sessionId: active.id }, requestId);
      setSessions(items => items.filter(item => item.id !== active.id));
    });
  }
  async function loadTopology() {
    await execute('Reading optional GPU and processor topology locally…', async requestId => {
      const machine = await agent<MachineTopology>('measurement-topology', undefined, requestId);
      setTopology(machine); setSelectedGpu(machine.gpus[0]?.deviceKey ?? '');
    });
  }
  async function start() {
    const workload = workloads.find(item => String(item.processId) === selectedProcessId);
    const monitorSystem = systemWide;
    if (!monitorSystem && !workload) return;
    await execute('Preparing the local capture…', async requestId => {
      onRecording(true); // Freeze before WPR startup; an error stays quiet until the next authoritative session refresh.
      const session = await agent<MeasurementSession>('measurement-start', { processId: monitorSystem ? 0 : workload!.processId, processStartTimeUtc: monitorSystem ? '0001-01-01T00:00:00Z' : workload!.startTimeUtc, systemWide: monitorSystem, label, durationSeconds, keepRawTrace, optimizationRunId: monitorSystem ? undefined : optimizationRun?.id }, requestId);
      updateSession(session); setFocusedId(session.id);
    });
  }
  async function analyze(id: string) {
    await execute('Analyzing the ETL locally…', async requestId => {
      analysisRequest.current = requestId;
      try {
        const session = await agent<MeasurementSession>('measurement-analyze', { sessionId: id, optimizationRunId: sessions.find(item => item.id === id)?.optimizationRunId }, requestId);
        updateSession(session);
        if (preparingBaseline && !session.systemWide && session.label === 'baseline' && session.report?.quality.isValid) onEvidenceIds(new Set([...evidenceIds, session.id]));
        if (session.optimizationRunId) onRun(await agent<OptimizationRun>('run-get', { runId: session.optimizationRunId }));
      }
      catch (error) { if (!String(error).includes('Agent request cancelled')) throw error; }
      finally { analysisRequest.current = undefined; }
    });
  }
  async function importFrameTimes(file: File) {
    if (!focused) return;
    const csv = await file.text();
    await execute('Aggregating PresentMon frame times locally…', async () => {
      const session = await agent<MeasurementSession>('measurement-frame-import', {
        sessionId: focused.id,
        csv,
        optimizationRunId: optimizationRun &&
          (optimizationRun.baselineSessionIds.includes(focused.id) || optimizationRun.candidateSessionIds.includes(focused.id))
          ? optimizationRun.id : focused.optimizationRunId,
      });
      if (session.optimizationRunId) onRun(await agent<OptimizationRun>('run-get', { runId: session.optimizationRunId }));
    });
  }
  async function compare() {
    const chosen = optimizationRun?.state === 'candidatePending'
      ? sessions.filter(item => optimizationRun.baselineSessionIds.includes(item.id) || optimizationRun.candidateSessionIds.includes(item.id))
      : sessions.filter(item => compareIds.has(item.id));
    await execute('Comparing normalized session metrics…', async () => {
      const result = await agent<MeasurementComparison>('measurement-compare', {
        baselineSessionIds: chosen.filter(item => item.label === 'baseline').map(item => item.id),
        candidateSessionIds: chosen.filter(item => item.label === 'candidate').map(item => item.id),
        optimizationRunId: optimizationRun?.state === 'candidatePending' ? optimizationRun.id : undefined,
      });
      setComparison(result); setComparisonExplanation(undefined);
      if (optimizationRun?.state === 'candidatePending' && !result.rejectionReasons.length)
        onRun(await agent<OptimizationRun>('run-get', { runId: optimizationRun.id }));
    });
  }
  async function generateGpuCandidates() {
    const baselineSessionIds = sessions.filter(item => compareIds.has(item.id) && item.label === 'baseline' && item.state === 'completed').map(item => item.id);
    await execute('Ranking read-only GPU IRQ candidates…', async () => setGpuCandidates(await agent<GpuCandidateSet>('measurement-gpu-candidates', { deviceKey: selectedGpu, baselineSessionIds })));
  }
  async function inspectGpuPolicy() {
    await execute('Inspecting the current GPU IRQ policy…', async () => setGpuPolicy(await agent<GpuAffinityPolicySnapshot>('measurement-gpu-affinity-inspect', { deviceKey: selectedGpu })));
  }
  async function keepCandidate() {
    if (!optimizationRun || !window.confirm(t("Keep this measured candidate configuration?"))) return;
    await execute('Recording the Keep decision…', async () =>
      onRun(await agent<OptimizationRun>('run-keep', { runId: optimizationRun.id })));
  }
  async function resumeAfterRestart() {
    if (!optimizationRun) return;
    await execute('Verifying the Windows restart…', async () =>
      onRun(await agent<OptimizationRun>('run-resume-after-restart', { runId: optimizationRun.id })));
  }

  return <div className="stack-lg measurement-page">
    {preparingBaseline && <section className="section-card"><h3>{t("Measure first · prepare the diagnosis")}</h3><p>{t("Record three baselines of the same process, duration and repeatable workload. Analyze each trace; valid baselines are selected automatically. No AI request is sent until you continue.")}</p><strong>{t('{count} / 3 matching selected baselines', { count: preparatoryBaselines(sessions, evidenceIds).length })}</strong><div className="button-row"><button className="primary" disabled={Boolean(busy) || Boolean(active) || preparatoryBaselines(sessions, evidenceIds).length < 3} onClick={onBaselinePrepared}>{t("Analyze baselines with selected AI")}</button></div></section>}
    <div className="page-actions"><div><span className="eyebrow">{t("Measurement-first alpha")}</span><h2>{t("Capture facts before proposing changes")}</h2></div><button className="secondary" disabled={Boolean(busy) || loading} onClick={() => void execute('Refreshing running workloads and local history…', refreshWorkloads)}><RefreshCw size={16}/>{t("Refresh")}</button></div>
    {message && <div className="notice danger" role="alert"><span>{message}</span></div>}
    {(busy || loading) && <MeasurementFeedback key={startedAt} message={t(busy || 'Loading running workloads and local history…')} startedAt={startedAt} log={operationLog.map(item => item.raw ? item.text : t(item.text))}>
      {analysisRequest.current && <button className="secondary" onClick={() => void cancelAgent(analysisRequest.current!).catch(error => setMessage(String(error)))}><X size={14}/>{t("Cancel analysis")}</button>}
    </MeasurementFeedback>}
    {optimizationRun?.state === 'restartPending' && <section className="section-card"><div className="section-heading"><div><span className="eyebrow">{t("Restart gate")}</span><h3>{t("Verify the required Windows restart")}</h3></div></div><p className="muted-copy">{t("Candidate measurement stays blocked until NeuroTune detects a different Windows boot.")}</p><button className="primary" onClick={() => void resumeAfterRestart()}><RefreshCw size={16}/>{t("Verify restart")}</button></section>}
    <section className="section-card measurement-setup">
      <div className="section-heading"><div><span className="eyebrow">{t("1 · Prerequisites and workload")}</span><h3>{t("Monitor the system or a running workload")}</h3></div><span className="status-pill good">{t("WPR · local only")}</span></div>
      <p className="muted-copy">{t("Requires a supported Windows 11 x64 build and administrator privileges. NeuroTune does not launch or attach to the workload.")}</p>
      <fieldset className="form-grid" disabled={Boolean(active) || Boolean(busy) || loading}>
        <label className="wide consent-toggle"><input type="checkbox" checked={systemWide} disabled={Boolean(active)} onChange={event => setSystemWide(event.target.checked)}/><span><strong>{t("Monitor the entire system")}</strong><small>{t("Diagnostics without a selected process. Optimization runs require a repeatable target workload.")}</small></span></label>
        {!systemWide && <label className="wide"><span>{t("Active process")}</span><select value={selectedProcessId} disabled={Boolean(active)} onChange={event => setSelectedProcessId(event.target.value)}>{workloads.map(item => <option key={`${item.processId}-${item.startTimeUtc}`} value={item.processId}>{item.name} · {item.description} · PID {item.processId}</option>)}</select></label>}
        <label><span>{t("Side")}</span><select value={label} disabled={Boolean(active)} onChange={event => setLabel(event.target.value as MeasurementLabel)}><option value="baseline">{t("Baseline")}</option><option value="candidate">{t("Candidate")}</option></select></label>
        <label><span>{t("Duration (seconds)")}</span><input type="number" min="30" max="600" value={durationSeconds} disabled={Boolean(active)} onChange={event => setDurationSeconds(Math.max(30, Math.min(600, Number(event.target.value))))}/><small>{t("Default 180; maximum 600.")}</small></label>
        <label className="wide consent-toggle"><input type="checkbox" checked={keepRawTrace} disabled={Boolean(active)} onChange={event => setKeepRawTrace(event.target.checked)}/><span><strong>{t("Keep the raw ETL after successful analysis")}</strong><small>{t("Off by default. Failed analyses remain retryable for at most 24 hours.")}</small></span></label>
      </fieldset>
      <div className="button-row">{active ? <><button className="primary" disabled={Boolean(busy)} onClick={() => void stopCapture()}><Timer size={16}/>{t("Stop")}</button><button className="secondary" disabled={Boolean(busy)} onClick={() => void cancelCapture()}><X size={16}/>{t("Cancel & delete")}</button><CaptureCountdown startedAt={active.recordingStartedAtUtc} durationSeconds={active.durationSeconds}/></> : <button className="primary" disabled={(!systemWide && !selectedProcessId) || Boolean(busy) || loading} onClick={() => void start()}>{busy ? <LoaderCircle size={16} className="spin"/> : <Timer size={16}/>} {busy ? t("Operation in progress…") : t("Start measurement")}</button>}</div>
    </section>
    <LiveProcessorTimes recording={Boolean(active)}/>

    <MeasurementHistory sessions={sessions} focusedId={focused?.id} compareIds={compareIds} evidenceIds={evidenceIds} busy={Boolean(busy)} loading={loading}
      onFocus={id => { setFocusedId(id); if (sessions.find(item => item.id === id)?.report) requestAnimationFrame(() => document.getElementById('measurement-report')?.focus()); }} onCompareToggle={id => setCompareIds(toggle(compareIds, id))} onEvidenceToggle={id => onEvidenceIds(toggle(evidenceIds, id))}
      onAnalyze={id => void analyze(id)} onDelete={id => { if (window.confirm(t('Delete this measurement session and its local data?'))) void execute('Deleting measurement…', () => agent('measurement-delete', { sessionId: id })); }} onCompare={() => void compare()}/>

    {focused?.report && <>{!focused.systemWide && <section className="section-card"><div className="section-heading"><div><span className="eyebrow">{t("Optional frame evidence")}</span><h3>{t("Attach the matching PresentMon CSV")}</h3></div><span className="status-pill">{t("local only")}</span></div><p className="muted-copy">{t("The CSV must cover the same executable and duration. NeuroTune stores only aggregate FPS, 1% low, frame-time tails, stutter count, and present modes.")}</p><input type="file" accept=".csv,text/csv" disabled={Boolean(busy)} aria-label={t("Import matching PresentMon CSV")} onChange={event => { const file = event.currentTarget.files?.[0]; if (file) void importFrameTimes(file); event.currentTarget.value = ''; }}/></section>}<MeasurementReportView session={focused}/></>}
    {comparison && <section className="section-card"><div className="section-heading"><div><span className="eyebrow">{t("Comparison")}</span><h3>{t('{level} result', { level: statusLabel(comparison.level) })}</h3></div><span className={`status-pill ${comparison.rejectionReasons.length ? '' : 'good'}`}>{comparison.rejectionReasons.length ? t("Rejected") : t('{count} metrics', { count: comparison.metrics.length })}</span></div>{comparison.rejectionReasons.length ? <ul className="muted-copy">{comparison.rejectionReasons.map(reason => <li key={reason}>{reason}</li>)}</ul> : <><div className="consent-card"><Bot size={20}/><div><span className="eyebrow">{t("Automatic measured recommendation")}</span><strong>{statusLabel(comparison.recommendation)}</strong><small>{comparison.recommendationReason}</small></div></div><div className="measurement-table">{comparison.metrics.slice(0, 20).map(metric => <article key={metric.evidenceId}><code>{metric.evidenceId}</code><span>{metric.baselineMedian.toFixed(2)} → {metric.candidateMedian.toFixed(2)}</span><strong className={metric.outcome}>{metric.deltaPercent.toFixed(1)}% · {statusLabel(metric.outcome)}</strong></article>)}</div>{optimizationRun?.state === 'decisionPending' && <div className="button-row"><button className="primary" onClick={() => void keepCandidate()}><Check size={16}/>{t("Keep candidate")}</button><small>{t('Use Activity & restore to choose Rollback.')}</small></div>}</>}</section>}
    {!topology && <section className="section-card"><h3>{t("Optional GPU IRQ diagnostics")}</h3><p className="muted-copy">{t("GPU candidate diagnostics are read-only and are not required for a latency capture. Load hardware topology only when needed.")}</p><button className="secondary" disabled={Boolean(busy) || loading} onClick={() => void loadTopology()}><Cpu size={16}/>{t("Load GPU topology")}</button></section>}
    {comparison && comparison.rejectionReasons.length === 0 && <section className="section-card"><SystemOneNotes items={comparison.systemOneAdvisories}/><h3>{t("Selected AI · read-only interpretation")}</h3><p>{t("Send only this comparison's aggregates and your tuning goals to the selected provider. It may disagree with the numerical recommendation; it cannot replace metrics, authorize changes or apply anything.")}</p><button className="secondary" disabled={Boolean(busy) || Boolean(active)} onClick={() => void execute('Explaining original comparison metrics with the selected AI…', async () => setComparisonExplanation(await agent<string>('measurement-explain', { comparison: { baselineSessionIds: comparison.baselineSessionIds, candidateSessionIds: comparison.candidateSessionIds, optimizationRunId: optimizationRun?.id }, goals: analysisGoals, language: getLanguage() })))}>{t("Ask selected AI to explain")}</button>{comparisonExplanation && <p className="muted-copy" role="status">{comparisonExplanation}</p>}</section>}
    {topology && <section className="section-card"><div className="section-heading"><div><span className="eyebrow">{t("Next closed-loop tranche")}</span><h3>{t("GPU IRQ candidate preview")}</h3></div><span className="status-pill">{t("Read-only")}</span></div><p className="muted-copy">{t('Windows reports {processors} logical processors, {cores} physical cores, and {clusters} cache clusters. Cache clusters are not labelled as CCDs.', { processors: topology.processors.length, cores: new Set(topology.processors.map(item => `${item.processorGroup}:${item.physicalCore}`)).size, clusters: new Set(topology.processors.map(item => `${item.processorGroup}:${item.cacheCluster}`)).size })}</p><div className="form-grid"><label className="wide"><span>{t("Physical AMD/NVIDIA GPU")}</span><select value={selectedGpu} onChange={event => { setSelectedGpu(event.target.value); setGpuPolicy(undefined); setGpuCandidates(undefined); }}>{topology.gpus.map(gpu => <option key={gpu.deviceKey} value={gpu.deviceKey}>{t('{vendor} · {name} · driver {version}', { vendor: gpu.vendor, name: gpu.name, version: gpu.driverVersion })}</option>)}</select></label></div><div className="button-row"><button className="secondary" disabled={!selectedGpu || Boolean(busy)} onClick={() => void inspectGpuPolicy()}><ScanLine size={16}/>{t("Inspect current policy")}</button><button className="secondary" disabled={Boolean(busy) || !selectedGpu || sessions.filter(item => compareIds.has(item.id) && item.label === 'baseline' && item.state === 'completed').length < 3} onClick={() => void generateGpuCandidates()}><Cpu size={16}/>{t("Generate from 3+ selected baselines")}</button></div>{gpuPolicy && <div className="measurement-table"><article><strong>{gpuPolicy.deviceName} · {statusLabel(gpuPolicy.state)}</strong><span>AssignmentSetOverride: {gpuPolicy.assignmentSetOverride.exists ? `${gpuPolicy.assignmentSetOverride.kind} · ${gpuPolicy.assignmentSetOverride.hexValue}` : t("not set")}</span><code>DevicePolicy: {gpuPolicy.devicePolicy.exists ? `${gpuPolicy.devicePolicy.kind} · ${gpuPolicy.devicePolicy.hexValue}` : t('not set')} · {t('Exact restore: {status}', { status: gpuPolicy.restorable ? t('possible') : t('blocked') })}</code><small>{gpuPolicy.gateReason}</small></article></div>}{gpuCandidates && <div className="measurement-table">{gpuCandidates.candidates.map(candidate => <article key={candidate.candidateId}><strong>{t('Group {group} · LP {processor} · core {core} · SMT {smt}', { group: candidate.processorGroup, processor: candidate.logicalProcessor, core: candidate.physicalCore, smt: candidate.smtIndex })}</strong><span>{t('IRQ {irq}% · target {target} ms · overlap {overlap} µs', { irq: candidate.interruptSharePercent.toFixed(2), target: candidate.targetRunningMilliseconds.toFixed(1), overlap: candidate.readyOverlapMicroseconds.toFixed(1) })}</span><code>{t('{id} · cache cluster {cluster} · efficiency {efficiency}', { id: candidate.candidateId, cluster: candidate.cacheCluster, efficiency: candidate.efficiencyClass })}</code><small>{candidate.gateReason}</small></article>)}</div>}<p className="muted-copy">{t("No Registry value is written and no candidate is executable. The provider AI does not receive device IDs, Registry paths, masks, policy snapshots, or processor numbers.")}</p></section>}
  </div>;
}

function MeasurementReportView({ session }: { session: MeasurementSession }) {
  const report = session.report!;
  return <section className="section-card measurement-report" id="measurement-report" tabIndex={-1}><div className="section-heading"><div><span className="eyebrow">{t("3 · Deterministic report")}</span><h3>{session.processName}</h3><a href="#measurement-history-title">{t('Back to session history')}</a></div><span className={`status-pill ${report.quality.isValid ? 'good' : ''}`}>{report.quality.isValid ? t("Quality gate passed") : t("Invalid trace")}</span></div>
    <div className="metric-grid four"><Metric icon={Timer} label={t("Trace")} value={t('{seconds} s', { seconds: (report.quality.durationMilliseconds / 1000).toFixed(1) })}/><Metric icon={Activity} label={t("Events lost")} value={String(report.quality.eventsLost)} tone={report.quality.eventsLost ? 'warn' : 'good'}/><Metric icon={Target} label={t("Target presence")} value={session.systemWide ? t("System-wide") : `${report.quality.targetPresencePercent.toFixed(1)}%`}/><Metric icon={Cpu} label={t("Observed threads")} value={String(report.threads.length)}/></div>
    {report.frameTimes && <div className="metric-grid four"><Metric icon={CircleGauge} label={t("Average FPS")} value={report.frameTimes.averageFps.toFixed(1)}/><Metric icon={Target} label={t("1% low")} value={`${report.frameTimes.onePercentLowFps.toFixed(1)} FPS`}/><Metric icon={Timer} label={t("Frame-time P99")} value={`${report.frameTimes.p99Milliseconds.toFixed(2)} ms`}/><Metric icon={Activity} label={t("Stutters")} value={String(report.frameTimes.stutterCount)}/></div>}
    {report.frameTimes && <p className="muted-copy">{t('Present modes: {modes} · {count} local frames · raw CSV not stored.', { modes: report.frameTimes.presentModes.join(', ') || t('not reported'), count: report.frameTimes.sampleCount })}</p>}
    {report.quality.missingProviders.length > 0 && <p className="error-text">{t('Missing required streams: {streams}', { streams: report.quality.missingProviders.join(', ') })}</p>}
    <LatencyDetails key={session.id} report={report} hardFaultsEnabled={session.hardFaultsEnabled}/>
    {!session.systemWide && <><h4>{t("Target scheduling · top 10 by total ready time")}</h4><div className="measurement-table">{report.threads.slice(0, 10).map(item => <article key={item.threadKey}><strong>{item.threadKey}</strong><span>{t('{milliseconds} ms running · {count} migrations', { milliseconds: item.runningMilliseconds.toFixed(1), count: item.migrations })}</span><code>{t('Ready P99 {microseconds} µs', { microseconds: item.readyTime.p99Microseconds.toFixed(2) })}</code></article>)}</div></>}
    {report.observations.length > 0 && <div className="finding-list">{report.observations.map(item => <article key={item.evidenceIds.join('|')}><strong>{item.title}</strong><code>{item.evidenceIds.join(', ')}</code><p>{item.observedMetric}. {item.explanation}</p><p><strong>{t("Test:")}</strong> {item.verifiableHypothesis}</p></article>)}</div>}
    <p className="muted-copy">{t("“Use in AI” is explicit opt-in. Only normalized numeric evidence IDs are included; the ETL, PID, command line and full paths stay local.")}</p>
  </section>;
}

function ActivityPage({ history, onRefresh, onRollback }: { history: OperationManifest[]; onRefresh: () => void; onRollback: (id: string) => void }) {
  return <div className="stack-lg"><div className="page-actions"><div><span className="eyebrow">{t("Operation journal")}</span><h2>{t("Every attempted change remains traceable")}</h2></div><button className="secondary" onClick={onRefresh}><RefreshCw size={16}/>{t("Refresh")}</button></div>{history.length ? <div className="history-list">{history.map(item => <article className="history-card" key={item.id}><div className="history-icon"><Activity size={19}/></div><div className="history-main"><div><strong>{item.status}</strong><span>{formatDate(item.createdAt)}</span></div><p>{t('{count} journaled actions · {id}', { count: item.actions.length, id: item.id })}</p><small className="history-recovery-state">{item.actions.some(action => (action.applied || action.attempted) && !action.rolledBack) ? t('Restore available · confirmation required') : t('No changes to restore')}</small><SystemOneNotes items={item.systemOneAdvisories}/>{item.error && <small className="error-text">{item.error}</small>}</div><button className="secondary" disabled={!item.actions.some(action => (action.applied || action.attempted) && !action.rolledBack)} onClick={() => onRollback(item.id)}><RotateCcw size={15}/>{t("Restore")}</button></article>)}</div> : <EmptyState icon={Activity} title={t("No operations yet")} text={t("Completed and interrupted operations will appear here with their rollback state.")}/>}</div>;
}

function AdvancedToolsPage({ telemetryConsent, onTelemetryConsent, onActions }: { telemetryConsent: boolean; onTelemetryConsent: (value: boolean) => void; onActions: (value: OptimizationAction[]) => void }) {
  const [section, setSection] = useState('firmware');
  const [directory, setDirectory] = useState('');
  const [plans, setPlans] = useState<CustomPowerPlanFile[]>([]);
  const [selectedPath, setSelectedPath] = useState('');
  const [message, setMessage] = useState('');
  const [messageValues, setMessageValues] = useState<Record<string, string | number>>({});
  const [messageRaw, setMessageRaw] = useState(false);
  const loadPlans = async (requested?: string) => {
    setMessageRaw(false); setMessageValues({});
    setMessage('Reading .pow files…');
    try {
      const result = await agent<{ directory: string; plans: CustomPowerPlanFile[] }>('power-plan-list', requested ? { directory: requested } : undefined);
      setDirectory(result.directory);
      setPlans(result.plans);
      setSelectedPath(result.plans[0]?.path ?? '');
      setMessage('{count} power plans found.'); setMessageValues({ count: result.plans.length });
    } catch (error) { setMessageRaw(true); setMessage(String(error)); }
  };
  useEffect(() => { void loadPlans(); }, []);
  const selectedPlan = plans.find(plan => plan.path === selectedPath);
  const stagePlan = async () => {
    if (!selectedPlan || !window.confirm(t('Stage {name} as an opaque high-risk power-plan action?', { name: selectedPlan.name }))) return;
    try {
      const result = await agent<{ plan: CustomPowerPlanFile; actions: OptimizationAction[] }>('power-plan-stage', { path: selectedPlan.path });
      onActions(result.actions);
      setMessageRaw(false); setMessage('{name} staged. It can only be activated through a measured optimization run.'); setMessageValues({ name: result.plan.name });
    } catch (error) { setMessageRaw(true); setMessage(String(error)); }
  };
  return <div className="tools-page">
    <div className="section-switcher" role="group" aria-label={t('Advanced tools')}>
      <button aria-pressed={section === 'firmware'} onClick={() => setSection('firmware')}>{t('BIOS / UEFI inspection')}</button>
      <button aria-pressed={section === 'assistant'} onClick={() => setSection('assistant')}>{t('System One assistant')}</button>
      <button aria-pressed={section === 'power'} onClick={() => setSection('power')}>{t('Custom power plans')}</button>
      <button aria-pressed={section === 'telemetry'} onClick={() => setSection('telemetry')}>{t('Optional low-level telemetry')}</button>
    </div>
    <div hidden={section !== 'firmware'}><FirmwarePanel/></div>
    <div hidden={section !== 'assistant'}><SystemOnePanel/></div>
    <section className="section-card" hidden={section !== 'power'}>
      <div className="section-heading"><h2>{t('Stage a .pow file')}</h2><HardDrive size={22}/></div>
      <p className="muted-copy">{t('Files are copied into NeuroTune with their SHA-256. Their contents remain opaque, so activation is a high-risk action requiring a Baseline, separate confirmation, and rollback journal.')}</p>
      <div className="form-grid"><label className="wide"><span>{t('Power-plan folder')}</span><input value={directory} spellCheck={false} onChange={event => setDirectory(event.target.value)}/></label><label className="wide"><span>{t('Plan')}</span><select value={selectedPath} onChange={event => setSelectedPath(event.target.value)}>{plans.map(plan => <option key={plan.path} value={plan.path}>{plan.name}</option>)}</select></label></div>
      {selectedPlan && <p className="muted-copy"><code>{selectedPlan.sha256}</code><br/>{formatBytes(selectedPlan.sizeBytes)}</p>}
      <div className="button-row"><button className="secondary" onClick={() => void loadPlans(directory)}><RefreshCw size={16}/>{t('Read folder')}</button><button className="primary" disabled={!selectedPlan} onClick={() => void stagePlan()}><Download size={16}/>{t('Stage selected plan')}</button></div>
      {message && <p className="muted-copy" role="status">{messageRaw ? message : t(message, messageValues)}</p>}
    </section>
    <section className="section-card" hidden={section !== 'telemetry'}><h2>{t('Optional low-level telemetry')}</h2><label className="consent-toggle"><input type="checkbox" checked={telemetryConsent} onChange={event => onTelemetryConsent(event.target.checked)}/><span><strong>{t('Query isolated optional telemetry')}</strong><small>{t('Runs a no-network helper only during a scan. PawnIO remains blocked: no driver is installed or loaded.')}</small></span></label></section>
  </div>;
}

function SettingsPage({ theme, onTheme }: { theme: ThemePreference; onTheme: (value: ThemePreference) => void }) {
  return <div className="settings-grid">
    <section className="section-card language-settings"><div className="section-heading"><h2>{t('Language')}</h2></div><p className="muted-copy">{t('Choose the interface and AI response language. Changes are immediate and saved locally; provider settings and consent are unchanged.')}</p><div className="form-grid"><label className="wide"><span>{t('Application language')}</span><select value={getLanguage()} onChange={event => setLanguage(event.target.value as Language)}>{languages.map(item => <option key={item.id} value={item.id}>{item.label}</option>)}</select></label></div></section>
    <section className="section-card appearance-settings"><div className="section-heading"><h2>{t('Theme')}</h2><Palette size={22}/></div><p className="muted-copy">{t('Follow the Windows appearance automatically, or keep a manual override.')}</p><div className="theme-options"><ThemeOption active={theme === 'system'} icon={MonitorCog} title={t('Use Windows setting')} text={t('Switch automatically with the operating system')} onClick={() => onTheme('system')}/><ThemeOption active={theme === 'light'} icon={Sun} title={t('Light')} text={t('High-contrast light surfaces')} onClick={() => onTheme('light')}/><ThemeOption active={theme === 'dark'} icon={Moon} title={t('Dark')} text={t('Low-glare dark surfaces')} onClick={() => onTheme('dark')}/></div></section>
  </div>;
}

function ThemeOption({ active, icon: Icon, title, text, onClick }: { active: boolean; icon: typeof Sun; title: string; text: string; onClick: () => void }) { return <button aria-pressed={active} className={active ? 'theme-option active' : 'theme-option'} onClick={onClick}><Icon size={21}/><span><strong>{title}</strong><small>{text}</small></span>{active && <Check size={17}/>}</button>; }
function toggle(current: Set<string>, id: string) { const next = new Set(current); if (next.has(id)) next.delete(id); else next.add(id); return next; }
function optionalNumber(value: string): number | undefined { return value === '' ? undefined : Number(value); }
function formatBytes(bytes: number) { return bytes < 1024 ? `${bytes} B` : `${(bytes / 1024).toFixed(1)} KiB`; }
// Display labels only: enum IDs and unknown/raw evidence remain unchanged.
function statusLabel(status: string) {
  const labels: Record<string, string> = {
    gpuDriver: 'GPU driver', chipsetDriver: 'Chipset driver', bios: 'BIOS',
    updateAvailable: 'Update available', current: 'Current', comparisonUnavailable: 'Comparison unavailable',
    supported: 'Supported', unavailable: 'Unavailable', blockedByHvci: 'Blocked by HVCI', driverNotApproved: 'Driver not approved',
    exploratory: 'Exploratory', repeated: 'Repeated', improvement: 'Improvement', regression: 'Regression', inconclusive: 'Inconclusive',
    insufficientEvidence: 'Insufficient evidence', keep: 'Keep', rollback: 'Rollback',
    windowsDefault: 'Windows default', configured: 'Configured', unsupported: 'Unsupported',
    general: 'General', systemConfiguration: 'System configuration', softwareInventory: 'Software inventory',
  };
  return labels[status] ? t(labels[status]) : status;
}
function pageTitle(page: Page) { return t(({ overview: 'System control center', provider: 'Model connection', scan: 'Complete diagnosis', security: 'Privacy & security', advanced: 'Local system profile · advanced', measurements: 'ETW measurement lab', review: 'AI investigation & proposals', activity: 'Recovery and history', settings: 'Application preferences', tools: 'Advanced tools' } satisfies Record<Page, string>)[page]); }

export default App;
