import { useEffect, useMemo, useRef, useState } from 'react';
import { listen } from '@tauri-apps/api/event';
import {
  Activity, Bot, Check, ChevronRight, CircleGauge, Cloud, Copy, Cpu, Database, Download,
  FileText, HardDrive, KeyRound, Laptop, ListChecks, LoaderCircle, LockKeyhole, LogIn,
  Monitor, MonitorCog, Moon, Palette, Printer, RefreshCw, RotateCcw, ScanLine, Settings,
  ShieldCheck, SlidersHorizontal, Sun, Target, TerminalSquare, Timer, Wifi, X,
} from 'lucide-react';
import { agent, cancelAgent, newRequestId } from './agent';
import { planKindLabel, scriptArtifactFilename, selectActionIdsForProfile } from './plan';
import { applyTheme, loadThemePreference } from './theme';
import type {
  ConflictPattern, Diagnosis, OperationManifest, OptimizationAction, ProviderKind, ProviderSettings,
  GpuCandidateSet, MachineTopology, MeasurementComparison, MeasurementLabel, MeasurementSession, MeasurementWorkload, Recommendation,
  RiskProfile, ScanResult, ThemePreference, TuningGoals,
} from './types';
import './App.css';

type Page = 'overview' | 'provider' | 'scan' | 'measurements' | 'review' | 'activity' | 'settings';

const providers: Array<{ id: ProviderKind; name: string; detail: string; icon: typeof Cloud }> = [
  { id: 'openRouter', name: 'OpenRouter', detail: 'API key or browser sign-in', icon: Cloud },
  { id: 'openAI', name: 'OpenAI', detail: 'OpenAI API platform', icon: Bot },
  { id: 'anthropic', name: 'Anthropic', detail: 'Claude API', icon: Bot },
  { id: 'deepSeek', name: 'DeepSeek', detail: 'Native DeepSeek API', icon: Cpu },
  { id: 'custom', name: 'Custom', detail: 'Any compatible endpoint', icon: SlidersHorizontal },
  { id: 'local', name: 'Local', detail: 'Ollama, LM Studio, or vLLM', icon: Laptop },
];

const defaults: Record<ProviderKind, ProviderSettings> = {
  openRouter: { provider: 'openRouter', providerName: 'OpenRouter', baseUrl: 'https://openrouter.ai/api/v1', protocol: 'openAiCompatible', model: 'openai/gpt-4o-mini', requiresApiKey: true },
  openAI: { provider: 'openAI', providerName: 'OpenAI', baseUrl: 'https://api.openai.com/v1', protocol: 'openAiCompatible', model: 'gpt-4o-mini', requiresApiKey: true },
  anthropic: { provider: 'anthropic', providerName: 'Anthropic', baseUrl: 'https://api.anthropic.com/v1', protocol: 'anthropic', model: 'claude-3-5-haiku-latest', requiresApiKey: true },
  deepSeek: { provider: 'deepSeek', providerName: 'DeepSeek', baseUrl: 'https://api.deepseek.com/v1', protocol: 'openAiCompatible', model: 'deepseek-chat', requiresApiKey: true },
  custom: { provider: 'custom', providerName: 'Custom provider', baseUrl: 'https://api.example.com/v1', protocol: 'openAiCompatible', model: '', requiresApiKey: true },
  local: { provider: 'local', providerName: 'Local model', baseUrl: 'http://127.0.0.1:11434/v1', protocol: 'openAiCompatible', model: '', requiresApiKey: false },
};

const navigation: Array<{ id: Page; label: string; icon: typeof Activity }> = [
  { id: 'overview', label: 'Overview', icon: CircleGauge },
  { id: 'provider', label: 'AI provider', icon: Bot },
  { id: 'scan', label: 'System scan', icon: ScanLine },
  { id: 'measurements', label: 'Measurements', icon: Timer },
  { id: 'review', label: 'Review changes', icon: ListChecks },
  { id: 'activity', label: 'Activity & restore', icon: Activity },
  { id: 'settings', label: 'Settings', icon: Settings },
];

function App() {
  const [page, setPage] = useState<Page>('overview');
  const [theme, setTheme] = useState<ThemePreference>(loadThemePreference);
  const [telemetryConsent, setTelemetryConsent] = useState(
    () => localStorage.getItem('neurotune.optionalTelemetryConsent') === 'true',
  );
  const [provider, setProvider] = useState<ProviderSettings>(defaults.openRouter);
  const [apiKey, setApiKey] = useState('');
  const [hasCredential, setHasCredential] = useState(false);
  const [models, setModels] = useState<string[]>([]);
  const [scan, setScan] = useState<ScanResult>();
  const [diagnosis, setDiagnosis] = useState<Diagnosis>();
  const [goals, setGoals] = useState<TuningGoals>({
    priority: 'balanced', riskProfile: 'balanced', games: [], notes: '',
    gameContext: { game: '', version: '', launcher: '', graphicsApi: '', displayMode: '', vrr: '', vSync: '', symptoms: [], preserve: '' },
    performanceInput: { userProvided: true, notes: '' },
  });
  const [actions, setActions] = useState<OptimizationAction[]>([]);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [history, setHistory] = useState<OperationManifest[]>([]);
  const [measurementEvidenceIds, setMeasurementEvidenceIds] = useState<Set<string>>(new Set());
  const [busy, setBusy] = useState('');
  const [scanRequestId, setScanRequestId] = useState<string>();
  const activeScan = useRef<string | undefined>(undefined);
  const cancellationPending = useRef(false);
  const [notice, setNotice] = useState<{ tone: 'success' | 'danger' | 'info'; text: string }>();

  useEffect(() => applyTheme(theme), [theme]);
  useEffect(() => {
    const unlisten = listen<{ requestId: string; message: string }>('agent-progress', event => {
      if (event.payload.requestId === activeScan.current) setBusy(`Deep scan · ${event.payload.message}`);
    });
    return () => { void unlisten.then(stop => stop()); };
  }, []);
  useEffect(() => {
    Promise.all([
      agent<{ settings: ProviderSettings; hasCredential: boolean }>('get-state'),
      agent<OptimizationAction[]>('actions'),
      agent<OperationManifest[]>('history'),
    ]).then(([state, availableActions, operations]) => {
      setProvider(state.settings);
      setHasCredential(state.hasCredential);
      setActions(availableActions);
      setHistory(operations);
    }).catch(showError);
  }, []);

  const recommendations = useMemo(() => new Map(
    diagnosis?.recommendations.filter(item => item.kind === 'executableAction').map(item => [item.actionId, item.reason]) ?? [],
  ), [diagnosis]);
  const pendingRecovery = history.find(item =>
    item.actions.some(action => (action.applied || action.attempted) && !action.rolledBack) &&
    /(applying|rolling back|incomplete|in corso|applicazione)/i.test(item.status));

  function showError(error: unknown) {
    setNotice({ tone: 'danger', text: error instanceof Error ? error.message : String(error) });
  }

  async function run<T>(label: string, operation: () => Promise<T>): Promise<T | undefined> {
    setBusy(label);
    setNotice(undefined);
    try { return await operation(); }
    catch (error) { showError(error); return undefined; }
    finally { setBusy(''); }
  }

  function chooseProvider(kind: ProviderKind) {
    setProvider({ ...defaults[kind] });
    setModels([]);
    setApiKey('');
    setHasCredential(false);
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
    const saved = await saveProvider();
    if (!saved) return;
    const result = await run('Testing the endpoint and discovering models…', () =>
      agent<{ models: string[] }>('models'));
    if (result) {
      setModels(result.models);
      if (!result.models.includes(provider.model)) setProvider(current => ({ ...current, model: result.models[0] ?? '' }));
      setNotice({ tone: 'success', text: `Connected. ${result.models.length} models discovered.` });
    }
  }

  async function browserSignIn() {
    const result = await run('Waiting for OpenRouter browser authorization…', () =>
      agent<{ settings: ProviderSettings; hasCredential: boolean }>('oauth-openrouter'));
    if (result) {
      setProvider(result.settings);
      setHasCredential(true);
      setNotice({ tone: 'success', text: 'OpenRouter account connected. The issued key is encrypted locally.' });
      await loadModelsAfterOAuth();
    }
  }

  async function loadModelsAfterOAuth() {
    const result = await run('Loading OpenRouter models…', () => agent<{ models: string[] }>('models'));
    if (result) setModels(result.models);
  }

  async function scanSystem() {
    if (activeScan.current) return cancelScan();
    const requestId = newRequestId();
    activeScan.current = requestId;
    setScanRequestId(requestId);
    setBusy('Deep scan · starting hardware inventory…');
    setNotice(undefined);
    try {
      const result = await agent<ScanResult>('scan', { optionalTelemetryConsent: telemetryConsent }, requestId);
      setScan(result);
      setActions(result.actions);
      setDiagnosis(undefined);
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

  async function diagnose() {
    if (!scan) return scanSystem();
    const conflicts = await run('Building the local objective-aware conflict graph…', () =>
      agent<ConflictPattern[]>('analyze-local', { profile: scan.profile, goals }));
    if (!conflicts) return;
    const result = await run('AI synthesis · checking every claim against local evidence…', () =>
      agent<Diagnosis>('diagnose', { profile: scan.profile, goals, measurementSessionIds: [...measurementEvidenceIds] }));
    setDiagnosis(result ?? {
      summary: 'The provider diagnosis failed, but the deterministic local conflict graph is still available.',
      findings: [], recommendations: [], conflicts,
      consentQuestion: 'Review the local conflicts and choose any supported reversible actions to apply.',
    });
    setSelected(new Set());
    setPage('review');
  }

  function applyPreset(mode: RiskProfile | 'none') {
    const ids = selectActionIdsForProfile(actions, diagnosis?.recommendations ?? [], mode);
    if (mode !== 'none') setGoals(current => ({ ...current, riskProfile: mode }));
    setSelected(new Set(ids));
  }

  async function applyChanges() {
    const highRisk = actions.filter(action => selected.has(action.id) && action.risk === 'high').length;
    if (!selected.size || !window.confirm(`Apply ${selected.size} selected changes after creating a verified restore point?`)) return;
    if (highRisk && !window.confirm(`Separate high-risk confirmation: apply ${highRisk} HIGH RISK action(s)? Review evidence, side effects, and rollback notes before continuing.`)) return;
    const result = await run('Creating backups and applying verified changes…', () =>
      agent<OperationManifest>('apply', { actionIds: [...selected], highRiskConfirmed: highRisk > 0 }));
    if (result) {
      setHistory(current => [result, ...current]);
      setSelected(new Set());
      setPage('activity');
      setNotice({ tone: 'success', text: 'Operation completed. Restart Windows if a selected action requires it.' });
      const refreshed = await agent<OptimizationAction[]>('actions');
      setActions(refreshed);
    }
  }

  async function rollback(operationId: string) {
    if (!window.confirm('Create another restore point and restore this operation?')) return;
    const result = await run('Restoring the saved system state…', () => agent<null>('rollback', { operationId }));
    if (result !== undefined) {
      setHistory(await agent<OperationManifest[]>('history'));
      setActions(await agent<OptimizationAction[]>('actions'));
      setNotice({ tone: 'success', text: 'Rollback completed and verified.' });
    }
  }

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="brand"><div className="brand-mark">N</div><div><strong>NeuroTune</strong><span>Windows intelligence</span></div></div>
        <nav aria-label="Main navigation">
          {navigation.map(item => <button key={item.id} aria-current={page === item.id ? 'page' : undefined} className={page === item.id ? 'nav-item active' : 'nav-item'} onClick={() => setPage(item.id)}><item.icon size={18}/><span>{item.label}</span></button>)}
        </nav>
        <div className="sidebar-foot">
          <div className="security-chip"><ShieldCheck size={16}/><span>Allowlisted actions</span></div>
          <small>v0.6.0-alpha.1</small>
        </div>
      </aside>

      <main className="workspace">
        <header className="topbar">
          <div><span className="eyebrow">{navigation.find(item => item.id === page)?.label}</span><h1>{pageTitle(page)}</h1></div>
          <div className="topbar-actions">
            <button className="icon-button" aria-label="Toggle light and dark theme" onClick={() => setTheme(theme === 'dark' ? 'light' : 'dark')}>{theme === 'dark' ? <Sun size={19}/> : <Moon size={19}/>}</button>
            <div className={hasCredential || !provider.requiresApiKey ? 'connection online' : 'connection'}><span/><div><strong>{provider.providerName}</strong><small>{hasCredential || !provider.requiresApiKey ? 'Ready' : 'Needs setup'}</small></div></div>
          </div>
        </header>

        {notice && <div className={`notice ${notice.tone}`} role="status"><span>{notice.text}</span><button aria-label="Dismiss message" onClick={() => setNotice(undefined)}>×</button></div>}
        {busy && <div className="busy-bar" role="status"><LoaderCircle size={16} className="spin"/><span>{busy}</span>{scanRequestId && <button className="ghost" onClick={cancelScan}><X size={14}/>Cancel scan</button>}</div>}
        {pendingRecovery && <div className="recovery-banner" role="alert"><div><RotateCcw size={18}/><span><strong>An interrupted operation needs attention.</strong><small>{pendingRecovery.id}</small></span></div><button className="secondary" onClick={() => setPage('activity')}>Review recovery</button></div>}

        <section className="page-content">
          {page === 'overview' && <Overview hasProvider={hasCredential || !provider.requiresApiKey} scan={scan} history={history} scanning={Boolean(scanRequestId)} onProvider={() => setPage('provider')} onScan={scanSystem}/>}
          {page === 'provider' && <ProviderPage provider={provider} apiKey={apiKey} hasCredential={hasCredential} models={models} onChoose={chooseProvider} onChange={setProvider} onKey={setApiKey} onSave={saveProvider} onLoadModels={loadModels} onBrowserSignIn={browserSignIn}/>}
          {page === 'scan' && <ScanPage scan={scan} diagnosis={diagnosis} goals={goals} scanning={Boolean(scanRequestId)} onGoals={setGoals} onScan={scanSystem} onDiagnose={diagnose}/>}
          {page === 'measurements' && <MeasurementsPage evidenceIds={measurementEvidenceIds} onEvidenceIds={setMeasurementEvidenceIds}/>}
          {page === 'review' && <ReviewPage diagnosis={diagnosis} actions={actions} recommendations={recommendations} selected={selected} riskProfile={goals.riskProfile} onToggle={id => setSelected(current => toggle(current, id))} onPreset={applyPreset} onApply={applyChanges}/>}
          {page === 'activity' && <ActivityPage history={history} onRefresh={async () => setHistory(await agent<OperationManifest[]>('history'))} onRollback={rollback}/>}
          {page === 'settings' && <SettingsPage theme={theme} onTheme={setTheme} telemetryConsent={telemetryConsent} onTelemetryConsent={value => {
            setTelemetryConsent(value);
            localStorage.setItem('neurotune.optionalTelemetryConsent', String(value));
          }}/>}
        </section>
      </main>
    </div>
  );
}

function Overview({ hasProvider, scan, history, scanning, onProvider, onScan }: { hasProvider: boolean; scan?: ScanResult; history: OperationManifest[]; scanning: boolean; onProvider: () => void; onScan: () => void }) {
  return <div className="stack-xl">
    <section className="hero-panel"><div className="hero-copy"><span className="kicker">CONTROLLED SYSTEM TUNING</span><h2>Understand the machine.<br/>Change only what is safe.</h2><p>NeuroTune combines a local Windows profile with your chosen AI model, then limits execution to compatible, reversible actions.</p><div className="button-row"><button className="primary" onClick={hasProvider ? onScan : onProvider}>{hasProvider ? (scanning ? 'Cancel scan' : 'Scan this PC') : 'Connect a provider'}{scanning ? <X size={17}/> : <ChevronRight size={17}/>}</button><button className="secondary" onClick={onProvider}>Provider settings</button></div></div><div className="hero-visual"><div className="orbit one"/><div className="orbit two"/><Cpu size={48}/><span>LOCAL<br/>CONTROL</span></div></section>
    <div className="metric-grid three"><Metric icon={Bot} label="AI provider" value={hasProvider ? 'Connected' : 'Not configured'} tone={hasProvider ? 'good' : 'warn'}/><Metric icon={ScanLine} label="System profile" value={scan ? 'Ready' : 'Not scanned'}/><Metric icon={RotateCcw} label="Recoverable operations" value={String(history.filter(item => item.actions.some(action => action.applied && !action.rolledBack)).length)}/></div>
    <section className="section-card"><div className="section-heading"><div><span className="eyebrow">How it works</span><h3>A visible boundary at every step</h3></div></div><div className="steps"><Step number="01" title="Profile locally" text="Review the exact sanitized system data before it leaves the PC."/><Step number="02" title="Ask your model" text="Use OpenRouter, a direct API, a custom endpoint, or a local model."/><Step number="03" title="Review compatibility" text="Unsupported and already-configured actions remain unavailable."/><Step number="04" title="Back up and apply" text="A verified restore point and per-action journal are mandatory."/></div></section>
  </div>;
}

function ProviderPage({ provider, apiKey, hasCredential, models, onChoose, onChange, onKey, onSave, onLoadModels, onBrowserSignIn }: { provider: ProviderSettings; apiKey: string; hasCredential: boolean; models: string[]; onChoose: (id: ProviderKind) => void; onChange: (value: ProviderSettings) => void; onKey: (value: string) => void; onSave: () => void; onLoadModels: () => void; onBrowserSignIn: () => void }) {
  const editable = provider.provider === 'custom' || provider.provider === 'local';
  return <div className="provider-layout">
    <section className="section-card provider-picker"><div className="section-heading"><div><span className="eyebrow">Connection type</span><h3>Choose where inference runs</h3></div></div><div className="provider-list">{providers.map(item => <button key={item.id} className={provider.provider === item.id ? 'provider-option selected' : 'provider-option'} onClick={() => onChoose(item.id)}><item.icon size={20}/><div><strong>{item.name}</strong><span>{item.detail}</span></div>{provider.provider === item.id && <Check size={17}/>}</button>)}</div></section>
    <section className="section-card provider-form"><div className="section-heading"><div><span className="eyebrow">Provider details</span><h3>{provider.providerName}</h3></div><div className={hasCredential || !provider.requiresApiKey ? 'status-pill good' : 'status-pill'}>{hasCredential || !provider.requiresApiKey ? 'Credential ready' : 'Not connected'}</div></div>
      {provider.provider === 'openRouter' && <div className="oauth-panel"><div><LogIn size={20}/><div><strong>Sign in with OpenRouter</strong><span>Authorize in your browser. NeuroTune stores the issued key with DPAPI.</span></div></div><button className="secondary" onClick={onBrowserSignIn}>Continue in browser</button></div>}
      <div className="form-grid">
        <label><span>Display name</span><input value={provider.providerName} disabled={!editable} onChange={event => onChange({ ...provider, providerName: event.target.value })}/></label>
        <label><span>API protocol</span><select value={provider.protocol} disabled={!editable} onChange={event => onChange({ ...provider, protocol: event.target.value as ProviderSettings['protocol'] })}><option value="openAiCompatible">OpenAI-compatible</option><option value="anthropic">Anthropic Messages</option></select></label>
        <label className="wide"><span>Base URL</span><input value={provider.baseUrl} disabled={!editable} spellCheck={false} onChange={event => onChange({ ...provider, baseUrl: event.target.value })}/><small>Remote custom endpoints require HTTPS. HTTP is accepted only on loopback addresses.</small></label>
        {provider.provider === 'local' && <div className="wide quick-presets"><span>Local presets</span><button onClick={() => onChange({ ...provider, providerName: 'Ollama', baseUrl: 'http://127.0.0.1:11434/v1' })}>Ollama</button><button onClick={() => onChange({ ...provider, providerName: 'LM Studio', baseUrl: 'http://127.0.0.1:1234/v1' })}>LM Studio</button><button onClick={() => onChange({ ...provider, providerName: 'vLLM', baseUrl: 'http://127.0.0.1:8000/v1' })}>vLLM</button></div>}
        {provider.requiresApiKey && <label className="wide"><span>API key</span><div className="secret-field"><KeyRound size={17}/><input type="password" value={apiKey} placeholder={hasCredential ? 'Encrypted credential already saved' : 'Paste API key'} onChange={event => onKey(event.target.value)}/></div></label>}
        <label className="wide"><span>Model</span><input list="model-options" value={provider.model} placeholder="Exact model ID" onChange={event => onChange({ ...provider, model: event.target.value })}/><datalist id="model-options">{models.map(model => <option key={model} value={model}/>)}</datalist></label>
      </div>
      <div className="form-actions"><button className="primary" onClick={onLoadModels}><Wifi size={17}/>Test & discover models</button><button className="secondary" onClick={onSave}><LockKeyhole size={17}/>Save securely</button></div>
      <div className="subscription-note"><ShieldCheck size={18}/><p><strong>About browser subscriptions</strong><br/>ChatGPT Plus and Claude Pro subscriptions do not grant third-party API access. Browser sign-in is shown only where the provider offers an official authorization flow. OpenRouter supports this today; other providers require their API credential.</p></div>
    </section>
  </div>;
}

function ScanPage({ scan, diagnosis, goals, scanning, onGoals, onScan, onDiagnose }: { scan?: ScanResult; diagnosis?: Diagnosis; goals: TuningGoals; scanning: boolean; onGoals: (value: TuningGoals) => void; onScan: () => void; onDiagnose: () => void }) {
  if (!scan) return <EmptyState icon={ScanLine} title="No local profile yet" text="Scan Windows locally first. NeuroTune will not contact your AI provider during this step." action={scanning ? 'Cancel scan' : 'Scan this PC'} onAction={onScan}/>;
  const priorities: Array<{ id: TuningGoals['priority']; label: string; detail: string }> = [
    { id: 'balanced', label: 'Balanced', detail: 'No single metric at any cost' },
    { id: 'fps', label: 'Frame rate', detail: 'Prioritize consistent gaming throughput' },
    { id: 'systemLatency', label: 'System latency', detail: 'Favor responsiveness and input latency' },
    { id: 'networkLatency', label: 'Network', detail: 'Focus on measured connection conditions' },
    { id: 'efficiency', label: 'Efficiency', detail: 'Protect battery life and thermals' },
  ];
  return <div className="stack-lg"><div className="page-actions"><div><span className="eyebrow">Local profile</span><h2>Set the target before diagnosis</h2></div><div className="button-row"><button className="secondary" onClick={onScan}>{scanning ? <X size={16}/> : <RefreshCw size={16}/>} {scanning ? 'Cancel scan' : 'Scan again'}</button><button className="primary" disabled={scanning || !scan.payloadReport.fitsSinglePass} onClick={onDiagnose}><Bot size={16}/>{scan.payloadReport.fitsSinglePass ? 'Run AI diagnosis' : 'Payload exceeds single-pass limit'}</button></div></div><div className="metric-grid four"><Metric icon={Monitor} label="Windows" value={scan.profile.operatingSystem}/><Metric icon={Cpu} label="Processor" value={scan.profile.cpu}/><Metric icon={Database} label="Memory" value={scan.profile.memory}/><Metric icon={HardDrive} label="Registry checks" value={`${Object.keys(scan.profile.performanceRegistry).length} inspected`}/></div><section className="scan-summary"><div className="scan-phases">{scan.profile.scanPhases.map(phase => <article key={phase.name}><Check size={15}/><div><strong>{phase.name}</strong><small>{phase.factsCollected} facts · {(phase.durationMilliseconds / 1000).toFixed(1)} s</small></div></article>)}</div><div className="inventory-counts"><span><strong>{scan.profile.installedSoftware.length}</strong> applications</span><span><strong>{scan.profile.relevantDrivers.length}</strong> relevant drivers</span><span><strong>{scan.profile.softwareSignals.length}</strong> tuning/overlay signals</span><span><strong>{scan.profile.deviceIssues.length}</strong> device issues</span></div></section>{scan.updateNotices.length > 0 && <section className="section-card update-notices"><div className="section-heading"><div><span className="eyebrow">Official manual updates</span><h3>Driver, chipset, and BIOS advisor</h3></div><span className="status-pill">Never auto-installed</span></div><div className="plan-item-list">{scan.updateNotices.map(notice => <article className="plan-item updateNotice" key={notice.id}><div className="plan-item-heading"><Download size={18}/><div><span>{notice.vendor} · {notice.kind.replace(/([A-Z])/g, ' $1')}</span><strong>{notice.model}</strong></div><span className="status-pill">{notice.status.replace(/([A-Z])/g, ' $1')}</span></div><p>{notice.reason}</p><small>Installed: {notice.installedVersion || 'unavailable'}{notice.latestVersion && ` · Latest verified: ${notice.latestVersion}`}</small><a href={notice.officialUrl} target="_blank" rel="noreferrer">Open official {notice.vendor} support</a></article>)}</div></section>}<section className="section-card telemetry-card"><div className="section-heading"><div><span className="eyebrow">Optional low-level telemetry</span><h3>Read-only support matrix</h3></div><span className="status-pill">No driver installation</span></div><div className="telemetry-grid">{scan.profile.telemetryCapabilities.map(capability => <article key={capability.name}><div><strong>{capability.name}</strong><span className={`telemetry-status ${capability.status}`}>{capability.status.replace(/([A-Z])/g, ' $1')}</span></div><p>{capability.detail}</p></article>)}</div></section><section className="section-card goals-card"><div className="section-heading"><div><span className="eyebrow">Optimization intent</span><h3>What matters on this PC?</h3></div><Target size={22}/></div><div className="priority-options">{priorities.map(item => <button key={item.id} aria-pressed={goals.priority === item.id} className={goals.priority === item.id ? 'priority-option active' : 'priority-option'} onClick={() => onGoals({ ...goals, priority: item.id })}><strong>{item.label}</strong><small>{item.detail}</small></button>)}</div><div className="goal-fields"><label><span>Games or workloads</span><input maxLength={1200} value={goals.games.join(', ')} placeholder="Example: Valorant, Cyberpunk 2077" onChange={event => onGoals({ ...goals, games: event.target.value.split(',').map(x => x.trim()).filter(Boolean) })}/><small>Names provide context only; game-specific claims still require evidence.</small></label><label><span>Anything else to preserve or improve?</span><textarea maxLength={1000} value={goals.notes} placeholder="Example: keep power use reasonable; Wi-Fi only" onChange={event => onGoals({ ...goals, notes: event.target.value })}/></label></div><GoalContextEditor goals={goals} onGoals={onGoals}/>{scan.profile.policyConflicts.length > 0 && <div className="local-observations"><strong>Local conflicts and manual overrides</strong><ul>{scan.profile.policyConflicts.map(item => <li key={item}>{item}</li>)}</ul></div>}</section><div className="split-panels"><section className="section-card"><div className="section-heading"><div><span className="eyebrow">Provider payload</span><h3>Sanitized profile</h3></div><span className={`status-pill ${scan.payloadReport.fitsSinglePass ? 'good' : ''}`}>{scan.payloadReport.factCount} facts · {formatBytes(scan.payloadReport.utf8Bytes)} / {formatBytes(scan.payloadReport.singlePassLimitBytes)}</span></div><div className="payload-privacy">{Object.entries(scan.payloadReport.privacyClasses).map(([privacy, count]) => <span key={privacy}>{privacy.replace(/([A-Z])/g, ' $1')}: {count}</span>)}</div><pre className="profile-json">{scan.sanitizedProfile}</pre></section><section className="section-card"><div className="section-heading"><div><span className="eyebrow">Model output</span><h3>Diagnosis</h3></div></div>{diagnosis ? <DiagnosisView diagnosis={diagnosis}/> : <div className="panel-placeholder"><Bot size={30}/><p>No data has been sent yet.</p><span>Your goals and this reviewed profile are sent only when you run diagnosis.</span></div>}</section></div></div>;
}

function GoalContextEditor({ goals, onGoals }: { goals: TuningGoals; onGoals: (value: TuningGoals) => void }) {
  const context = goals.gameContext;
  const metrics = goals.performanceInput;
  const setContext = (change: Partial<TuningGoals['gameContext']>) => onGoals({ ...goals, gameContext: { ...context, ...change } });
  const setMetrics = (change: Partial<TuningGoals['performanceInput']>) => onGoals({ ...goals, performanceInput: { ...metrics, ...change, userProvided: true } });
  return <details className="context-editor">
    <summary>Optional game details and user-provided measurements</summary>
    <p>These values improve context. Measurements are labelled as user-provided and are not treated as benchmark proof.</p>
    <div className="context-grid">
      <label><span>Primary game</span><input maxLength={120} value={context.game} onChange={event => setContext({ game: event.target.value })}/></label>
      <label><span>Game version</span><input maxLength={100} value={context.version} onChange={event => setContext({ version: event.target.value })}/></label>
      <label><span>Launcher</span><input maxLength={100} value={context.launcher} onChange={event => setContext({ launcher: event.target.value })}/></label>
      <label><span>Graphics API</span><input maxLength={40} placeholder="DirectX 12, Vulkan…" value={context.graphicsApi} onChange={event => setContext({ graphicsApi: event.target.value })}/></label>
      <label><span>Resolution</span><div className="inline-inputs"><input aria-label="Resolution width" type="number" min="320" max="16384" value={context.width ?? ''} onChange={event => setContext({ width: optionalNumber(event.target.value) })}/><span>×</span><input aria-label="Resolution height" type="number" min="200" max="16384" value={context.height ?? ''} onChange={event => setContext({ height: optionalNumber(event.target.value) })}/></div></label>
      <label><span>Refresh rate</span><input type="number" min="20" max="1000" value={context.refreshRateHz ?? ''} onChange={event => setContext({ refreshRateHz: optionalNumber(event.target.value) })}/></label>
      <label><span>Display mode</span><input maxLength={40} placeholder="Fullscreen, borderless…" value={context.displayMode} onChange={event => setContext({ displayMode: event.target.value })}/></label>
      <label><span>VRR / V-Sync</span><div className="inline-inputs"><input aria-label="VRR state" maxLength={40} value={context.vrr} onChange={event => setContext({ vrr: event.target.value })}/><input aria-label="V-Sync state" maxLength={40} value={context.vSync} onChange={event => setContext({ vSync: event.target.value })}/></div></label>
      <label><span>Frame cap</span><input type="number" min="10" max="2000" value={context.frameCap ?? ''} onChange={event => setContext({ frameCap: optionalNumber(event.target.value) })}/></label>
      <label><span>Symptoms</span><input maxLength={2400} value={context.symptoms.join(', ')} placeholder="Stutter, packet loss, input lag…" onChange={event => setContext({ symptoms: event.target.value.split(',').map(value => value.trim().slice(0, 200)).filter(Boolean).slice(0, 12) })}/></label>
      <label className="wide"><span>Preserve</span><input maxLength={500} value={context.preserve} placeholder="Security, image quality, battery life…" onChange={event => setContext({ preserve: event.target.value })}/></label>
    </div>
    <div className="context-grid measurement-grid" aria-label="User-provided measurements">
      <label><span>Average FPS</span><input type="number" min="0" step="0.1" value={metrics.averageFps ?? ''} onChange={event => setMetrics({ averageFps: optionalNumber(event.target.value) })}/></label>
      <label><span>1% low FPS</span><input type="number" min="0" step="0.1" value={metrics.onePercentLowFps ?? ''} onChange={event => setMetrics({ onePercentLowFps: optionalNumber(event.target.value) })}/></label>
      <label><span>Average frame time (ms)</span><input type="number" min="0" step="0.01" value={metrics.averageFrameTimeMs ?? ''} onChange={event => setMetrics({ averageFrameTimeMs: optionalNumber(event.target.value) })}/></label>
      <label><span>Input latency (ms)</span><input type="number" min="0" step="0.1" value={metrics.inputLatencyMs ?? ''} onChange={event => setMetrics({ inputLatencyMs: optionalNumber(event.target.value) })}/></label>
      <label><span>Network latency (ms)</span><input type="number" min="0" step="0.1" value={metrics.networkLatencyMs ?? ''} onChange={event => setMetrics({ networkLatencyMs: optionalNumber(event.target.value) })}/></label>
      <label><span>Packet loss (%)</span><input type="number" min="0" max="100" step="0.01" value={metrics.packetLossPercent ?? ''} onChange={event => setMetrics({ packetLossPercent: optionalNumber(event.target.value) })}/></label>
      <label className="wide"><span>Measurement notes</span><textarea maxLength={1000} value={metrics.notes} onChange={event => setMetrics({ notes: event.target.value })}/></label>
    </div>
  </details>;
}

function ReviewPage({ diagnosis, actions, recommendations, selected, riskProfile, onToggle, onPreset, onApply }: { diagnosis?: Diagnosis; actions: OptimizationAction[]; recommendations: Map<string, string>; selected: Set<string>; riskProfile: RiskProfile; onToggle: (id: string) => void; onPreset: (mode: RiskProfile | 'none') => void; onApply: () => void }) {
  const [view, setView] = useState<'recommended' | 'conflicts' | 'all'>('recommended');
  if (!diagnosis) return <EmptyState icon={Bot} title="No diagnosis yet" text="Scan the PC, choose your priorities, and ask the configured model for an evidence-backed diagnosis."/>;
  const conflictActionIds = new Set(diagnosis.conflicts.flatMap(conflict => conflict.suggestedActionIds));
  const visible = actions.filter(action => view === 'all' || (view === 'recommended' ? recommendations.has(action.id) : conflictActionIds.has(action.id)));
  const selectedHighRisk = actions.filter(action => selected.has(action.id) && action.risk === 'high').length;
  return <div className="stack-lg report-root">
    <div className="page-actions"><div><span className="eyebrow">Contextual plan</span><h2>Evidence guides the plan; you decide</h2></div><div className="button-row"><button className="secondary" onClick={() => window.print()}><Printer size={16}/>Print report</button><button className={riskProfile === 'safe' ? 'ghost active' : 'ghost'} onClick={() => onPreset('safe')}>Safe</button><button className={riskProfile === 'balanced' ? 'ghost active' : 'ghost'} onClick={() => onPreset('balanced')}>Balanced</button><button className={riskProfile === 'aggressive' ? 'ghost active' : 'ghost'} onClick={() => onPreset('aggressive')}>Aggressive</button><button className="ghost" onClick={() => onPreset('none')}>Clear</button></div></div>
    <section className="section-card report-summary"><div className="section-heading"><div><span className="eyebrow">Evidence-backed report</span><h3>AI diagnosis</h3></div><span className="status-pill good">No changes made</span></div><DiagnosisView diagnosis={diagnosis}/></section>
    <PlanItemReview recommendations={diagnosis.recommendations}/>
    {diagnosis.conflicts.length > 0 && <ConflictView conflicts={diagnosis.conflicts}/>}
    <div className="plan-tabs" role="tablist" aria-label="Action visibility"><button role="tab" aria-selected={view === 'recommended'} className={view === 'recommended' ? 'active' : ''} onClick={() => setView('recommended')}>AI recommended ({recommendations.size})</button><button role="tab" aria-selected={view === 'conflicts'} className={view === 'conflicts' ? 'active' : ''} onClick={() => setView('conflicts')}>Conflict fixes ({conflictActionIds.size})</button><button role="tab" aria-selected={view === 'all'} className={view === 'all' ? 'active' : ''} onClick={() => setView('all')}>All supported ({actions.length})</button></div>
    {visible.length > 0 ? <div className="action-list">{visible.map(action => { const related = diagnosis.conflicts.filter(conflict => conflict.suggestedActionIds.includes(action.id)).map(conflict => conflict.title); const reason = recommendations.get(action.id) ?? (related.join(' · ') || 'Registered reversible capability; not selected by this diagnosis.'); return <button key={action.id} aria-pressed={selected.has(action.id)} className={`action-card ${selected.has(action.id) ? 'selected' : ''} ${!action.availability.canApply ? 'disabled' : ''}`} disabled={!action.availability.canApply} onClick={() => onToggle(action.id)}><span className="check-box">{selected.has(action.id) && <Check size={15}/>}</span><div className="action-main"><div><strong>{action.name}</strong>{action.requiresRestart && <span className="tag">Restart</span>}</div><p>{action.description}</p><small>{reason}</small></div><div className="action-meta"><span className={`risk ${action.risk}`}>{action.risk} risk</span><strong>{action.availability.status}</strong><small>Current: {action.availability.currentValue}</small></div></button>; })}</div> : <section className="section-card no-fixes"><ShieldCheck size={22}/><div><strong>No executable capability in this view.</strong><p>Manual guidance and scripts remain visible above but cannot enter the apply transaction.</p></div></section>}
    {selectedHighRisk > 0 && <div className="high-risk-warning" role="alert"><ShieldCheck size={19}/><span><strong>{selectedHighRisk} high-risk action(s) selected</strong><small>They remain selectable, but require an additional explicit confirmation before backup and execution.</small></span></div>}
    <section className="consent-card"><Bot size={20}/><div><span className="eyebrow">Model request</span><strong>{diagnosis.consentQuestion}</strong><small>Only selected registered actions enter the verified backup/apply/rollback transaction.</small></div></section>
    <div className="sticky-apply"><div><strong>{selected.size} changes selected</strong><span>A verified restore point and Registry exports are mandatory.</span></div><button className="primary" disabled={!selected.size} onClick={onApply}><ShieldCheck size={17}/>Back up & apply selected</button></div>
  </div>;
}

function MeasurementsPage({ evidenceIds, onEvidenceIds }: { evidenceIds: Set<string>; onEvidenceIds: (value: Set<string>) => void }) {
  const [workloads, setWorkloads] = useState<MeasurementWorkload[]>([]);
  const [sessions, setSessions] = useState<MeasurementSession[]>([]);
  const [selectedProcessId, setSelectedProcessId] = useState('');
  const [label, setLabel] = useState<MeasurementLabel>('baseline');
  const [durationSeconds, setDurationSeconds] = useState(180);
  const [keepRawTrace, setKeepRawTrace] = useState(false);
  const [focusedId, setFocusedId] = useState('');
  const [compareIds, setCompareIds] = useState<Set<string>>(new Set());
  const [comparison, setComparison] = useState<MeasurementComparison>();
  const [topology, setTopology] = useState<MachineTopology>();
  const [selectedGpu, setSelectedGpu] = useState('');
  const [gpuCandidates, setGpuCandidates] = useState<GpuCandidateSet>();
  const [busy, setBusy] = useState('');
  const [message, setMessage] = useState('');
  const [now, setNow] = useState(Date.now());
  const analysisRequest = useRef<string | undefined>(undefined);

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
    void Promise.all([
      agent<MeasurementWorkload[]>('measurement-workloads'),
      agent<MeasurementSession[]>('measurement-list'),
      agent<MachineTopology>('measurement-topology'),
    ]).then(([processes, history, machine]) => {
      setWorkloads(processes);
      setSessions(history);
      setTopology(machine);
      setSelectedProcessId(processes[0] ? String(processes[0].processId) : '');
      setFocusedId(history[0]?.id ?? '');
      setSelectedGpu(machine.gpus[0]?.deviceKey ?? '');
    }).catch(error => setMessage(String(error)));
  }, []);
  useEffect(() => { const timer = window.setInterval(() => setNow(Date.now()), 1000); return () => window.clearInterval(timer); }, []);

  const active = sessions.find(item => item.state === 'recording');
  const focused = sessions.find(item => item.id === focusedId) ?? sessions[0];
  const remaining = active?.recordingStartedAtUtc
    ? Math.max(0, active.durationSeconds - Math.floor((now - new Date(active.recordingStartedAtUtc).getTime()) / 1000)) : 0;

  async function execute(text: string, operation: () => Promise<unknown>) {
    setBusy(text); setMessage('');
    try { await operation(); await refreshSessions(); }
    catch (error) { setMessage(error instanceof Error ? error.message : String(error)); }
    finally { setBusy(''); }
  }
  async function start() {
    const workload = workloads.find(item => String(item.processId) === selectedProcessId);
    if (!workload) return;
    await execute('Starting the named WPR session…', async () => {
      const session = await agent<MeasurementSession>('measurement-start', { processId: workload.processId, processStartTimeUtc: workload.startTimeUtc, label, durationSeconds, keepRawTrace });
      setFocusedId(session.id);
    });
  }
  async function analyze(id: string) {
    const requestId = newRequestId(); analysisRequest.current = requestId;
    await execute('Analyzing the ETL locally…', async () => {
      try { await agent<MeasurementSession>('measurement-analyze', { sessionId: id }, requestId); }
      catch (error) { if (!String(error).includes('Agent request cancelled')) throw error; }
      finally { analysisRequest.current = undefined; }
    });
  }
  async function compare() {
    const chosen = sessions.filter(item => compareIds.has(item.id));
    await execute('Comparing normalized session metrics…', async () => setComparison(await agent<MeasurementComparison>('measurement-compare', {
      baselineSessionIds: chosen.filter(item => item.label === 'baseline').map(item => item.id),
      candidateSessionIds: chosen.filter(item => item.label === 'candidate').map(item => item.id),
    })));
  }
  async function generateGpuCandidates() {
    const baselineSessionIds = sessions.filter(item => compareIds.has(item.id) && item.label === 'baseline' && item.state === 'completed').map(item => item.id);
    await execute('Ranking read-only GPU IRQ candidates…', async () => setGpuCandidates(await agent<GpuCandidateSet>('measurement-gpu-candidates', { deviceKey: selectedGpu, baselineSessionIds })));
  }

  return <div className="stack-lg measurement-page">
    <div className="page-actions"><div><span className="eyebrow">Measurement-first alpha</span><h2>Capture facts before proposing changes</h2></div><button className="secondary" disabled={Boolean(busy)} onClick={() => void Promise.all([refreshWorkloads(), refreshSessions()])}><RefreshCw size={16}/>Refresh</button></div>
    {message && <div className="notice danger" role="alert"><span>{message}</span></div>}
    {busy && <div className="busy-bar" role="status"><LoaderCircle size={16} className="spin"/><span>{busy}</span>{analysisRequest.current && <button className="ghost" onClick={() => void cancelAgent(analysisRequest.current!)}><X size={14}/>Cancel analysis</button>}</div>}
    <section className="section-card measurement-setup">
      <div className="section-heading"><div><span className="eyebrow">1 · Prerequisites and workload</span><h3>Select an already-running process</h3></div><span className="status-pill good">WPR · local only</span></div>
      <p className="muted-copy">Requires Windows 10/11 x64 and administrator privileges. NeuroTune does not launch or attach to the workload.</p>
      <div className="form-grid">
        <label className="wide"><span>Active process</span><select value={selectedProcessId} disabled={Boolean(active)} onChange={event => setSelectedProcessId(event.target.value)}>{workloads.map(item => <option key={`${item.processId}-${item.startTimeUtc}`} value={item.processId}>{item.name} · {item.description} · PID {item.processId}</option>)}</select></label>
        <label><span>Side</span><select value={label} disabled={Boolean(active)} onChange={event => setLabel(event.target.value as MeasurementLabel)}><option value="baseline">Baseline</option><option value="candidate">Candidate</option></select></label>
        <label><span>Duration (seconds)</span><input type="number" min="30" max="600" value={durationSeconds} disabled={Boolean(active)} onChange={event => setDurationSeconds(Math.max(30, Math.min(600, Number(event.target.value))))}/><small>Default 180; maximum 600.</small></label>
        <label className="wide consent-toggle"><input type="checkbox" checked={keepRawTrace} disabled={Boolean(active)} onChange={event => setKeepRawTrace(event.target.checked)}/><span><strong>Keep the raw ETL after successful analysis</strong><small>Off by default. Failed analyses remain retryable for at most 24 hours.</small></span></label>
      </div>
      <div className="button-row">{active ? <><button className="primary" onClick={() => void execute('Stopping and saving the trace…', () => agent('measurement-stop', { sessionId: active.id }))}><Timer size={16}/>Stop · {remaining}s</button><button className="secondary" onClick={() => void execute('Cancelling and deleting incomplete data…', () => agent('measurement-cancel', { sessionId: active.id }))}><X size={16}/>Cancel & delete</button></> : <button className="primary" disabled={!selectedProcessId || Boolean(busy)} onClick={() => void start()}><Timer size={16}/>Start measurement</button>}</div>
    </section>

    <section className="section-card">
      <div className="section-heading"><div><span className="eyebrow">2 · History and analysis</span><h3>{sessions.length} measurement sessions</h3></div></div>
      <div className="measurement-history">{sessions.map(session => <article key={session.id} className={focused?.id === session.id ? 'measurement-row selected' : 'measurement-row'} onClick={() => setFocusedId(session.id)}>
        <label onClick={event => event.stopPropagation()}><input aria-label={`Select ${session.id} for comparison`} type="checkbox" disabled={session.state !== 'completed'} checked={compareIds.has(session.id)} onChange={() => setCompareIds(toggle(compareIds, session.id))}/></label>
        <div><strong>{session.processName}</strong><small>{new Date(session.createdAtUtc).toLocaleString()} · {session.durationSeconds}s</small></div>
        <span className={`status-pill ${session.report?.quality.isValid ? 'good' : ''}`}>{session.label} · {session.state}</span>
        <div className="button-row" onClick={event => event.stopPropagation()}>{session.state === 'captured' || session.state === 'failed' ? <button className="secondary" onClick={() => void analyze(session.id)}>Analyze</button> : null}<button className={evidenceIds.has(session.id) ? 'ghost active' : 'ghost'} disabled={session.state !== 'completed'} onClick={() => onEvidenceIds(toggle(evidenceIds, session.id))}>{evidenceIds.has(session.id) ? 'Included in AI' : 'Use in AI'}</button><button className="ghost" disabled={session.state === 'recording'} onClick={() => { if (window.confirm('Delete this measurement session and its local data?')) void execute('Deleting measurement…', () => agent('measurement-delete', { sessionId: session.id })); }}><X size={14}/></button></div>
      </article>)}</div>
      {!sessions.length && <p className="muted-copy">No measurement has been captured yet.</p>}
      <div className="button-row comparison-actions"><button className="secondary" disabled={!sessions.some(item => compareIds.has(item.id) && item.label === 'baseline') || !sessions.some(item => compareIds.has(item.id) && item.label === 'candidate')} onClick={() => void compare()}><CircleGauge size={16}/>Compare selected</button><small>1+1 is exploratory. 3+3 enables repeated aggregation.</small></div>
    </section>

    {focused?.report && <MeasurementReportView session={focused}/>}
    {comparison && <section className="section-card"><div className="section-heading"><div><span className="eyebrow">Comparison</span><h3>{comparison.level} result</h3></div><span className={`status-pill ${comparison.rejectionReasons.length ? '' : 'good'}`}>{comparison.rejectionReasons.length ? 'Rejected' : `${comparison.metrics.length} metrics`}</span></div>{comparison.rejectionReasons.length ? <ul className="muted-copy">{comparison.rejectionReasons.map(reason => <li key={reason}>{reason}</li>)}</ul> : <div className="measurement-table">{comparison.metrics.slice(0, 20).map(metric => <article key={metric.evidenceId}><code>{metric.evidenceId}</code><span>{metric.baselineMedian.toFixed(2)} → {metric.candidateMedian.toFixed(2)}</span><strong className={metric.outcome}>{metric.deltaPercent.toFixed(1)}% · {metric.outcome}</strong></article>)}</div>}</section>}
    {topology && <section className="section-card"><div className="section-heading"><div><span className="eyebrow">Next closed-loop tranche</span><h3>GPU IRQ candidate preview</h3></div><span className="status-pill">Read-only</span></div><p className="muted-copy">Windows reports {topology.processors.length} logical processors, {new Set(topology.processors.map(item => `${item.processorGroup}:${item.physicalCore}`)).size} physical cores, and {new Set(topology.processors.map(item => `${item.processorGroup}:${item.cacheCluster}`)).size} cache clusters. Cache clusters are not labelled as CCDs.</p><div className="form-grid"><label className="wide"><span>Physical AMD/NVIDIA GPU</span><select value={selectedGpu} onChange={event => setSelectedGpu(event.target.value)}>{topology.gpus.map(gpu => <option key={gpu.deviceKey} value={gpu.deviceKey}>{gpu.vendor} · {gpu.name} · driver {gpu.driverVersion}</option>)}</select></label></div><div className="button-row"><button className="secondary" disabled={!selectedGpu || sessions.filter(item => compareIds.has(item.id) && item.label === 'baseline' && item.state === 'completed').length < 3} onClick={() => void generateGpuCandidates()}><Cpu size={16}/>Generate from 3+ selected baselines</button></div>{gpuCandidates && <div className="measurement-table">{gpuCandidates.candidates.map(candidate => <article key={candidate.candidateId}><strong>Group {candidate.processorGroup} · LP {candidate.logicalProcessor} · core {candidate.physicalCore} · SMT {candidate.smtIndex}</strong><span>IRQ {candidate.interruptSharePercent.toFixed(2)}% · target {candidate.targetRunningMilliseconds.toFixed(1)} ms · overlap {candidate.readyOverlapMicroseconds.toFixed(1)} µs</span><code>{candidate.candidateId} · cache cluster {candidate.cacheCluster} · efficiency {candidate.efficiencyClass}</code><small>{candidate.gateReason}</small></article>)}</div>}<p className="muted-copy">No Registry value is written and no candidate is executable. The provider AI does not receive device IDs, Registry paths, masks, or processor numbers.</p></section>}
  </div>;
}

function MeasurementReportView({ session }: { session: MeasurementSession }) {
  const report = session.report!;
  return <section className="section-card"><div className="section-heading"><div><span className="eyebrow">3 · Deterministic report</span><h3>{session.processName}</h3></div><span className={`status-pill ${report.quality.isValid ? 'good' : ''}`}>{report.quality.isValid ? 'Quality gate passed' : 'Invalid trace'}</span></div>
    <div className="metric-grid four"><Metric icon={Timer} label="Trace" value={`${(report.quality.durationMilliseconds / 1000).toFixed(1)} s`}/><Metric icon={Activity} label="Events lost" value={String(report.quality.eventsLost)} tone={report.quality.eventsLost ? 'warn' : 'good'}/><Metric icon={Target} label="Target presence" value={`${report.quality.targetPresencePercent.toFixed(1)}%`}/><Metric icon={Cpu} label="Observed threads" value={String(report.threads.length)}/></div>
    {report.quality.missingProviders.length > 0 && <p className="error-text">Missing required streams: {report.quality.missingProviders.join(', ')}</p>}
    <div className="measurement-report-grid"><div><h4>Top ISR/DPC pressure</h4><div className="measurement-table">{report.interrupts.slice(0, 10).map((item, index) => <article key={`${item.kind}-${item.module}-${item.logicalProcessor}-${index}`}><strong>{item.kind.toUpperCase()} · {item.module}</strong><span>LP {item.logicalProcessor} · {item.distribution.count} events</span><code>P95 {item.distribution.p95Microseconds.toFixed(2)} µs · P99 {item.distribution.p99Microseconds.toFixed(2)} µs</code></article>)}</div></div><div><h4>Target scheduling</h4><div className="measurement-table">{report.threads.slice(0, 10).map(item => <article key={item.threadKey}><strong>{item.threadKey}</strong><span>{item.runningMilliseconds.toFixed(1)} ms running · {item.migrations} migrations</span><code>Ready P99 {item.readyTime.p99Microseconds.toFixed(2)} µs</code></article>)}</div></div></div>
    {report.observations.length > 0 && <div className="finding-list">{report.observations.map(item => <article key={item.evidenceIds.join('|')}><strong>{item.title}</strong><code>{item.evidenceIds.join(', ')}</code><p>{item.observedMetric}. {item.explanation}</p><p><strong>Test:</strong> {item.verifiableHypothesis}</p></article>)}</div>}
    <p className="muted-copy">“Use in AI” is explicit opt-in. Only normalized numeric evidence IDs are included; the ETL, PID, command line and full paths stay local.</p>
  </section>;
}

function ActivityPage({ history, onRefresh, onRollback }: { history: OperationManifest[]; onRefresh: () => void; onRollback: (id: string) => void }) {
  return <div className="stack-lg"><div className="page-actions"><div><span className="eyebrow">Operation journal</span><h2>Every attempted change remains traceable</h2></div><button className="secondary" onClick={onRefresh}><RefreshCw size={16}/>Refresh</button></div>{history.length ? <div className="history-list">{history.map(item => <article className="history-card" key={item.id}><div className="history-icon"><Activity size={19}/></div><div className="history-main"><div><strong>{item.status}</strong><span>{new Date(item.createdAt).toLocaleString()}</span></div><p>{item.actions.length} journaled actions · {item.id}</p>{item.error && <small className="error-text">{item.error}</small>}</div><button className="secondary" disabled={!item.actions.some(action => (action.applied || action.attempted) && !action.rolledBack)} onClick={() => onRollback(item.id)}><RotateCcw size={15}/>Restore</button></article>)}</div> : <EmptyState icon={Activity} title="No operations yet" text="Completed and interrupted operations will appear here with their rollback state."/>}</div>;
}

function SettingsPage({ theme, onTheme, telemetryConsent, onTelemetryConsent }: { theme: ThemePreference; onTheme: (value: ThemePreference) => void; telemetryConsent: boolean; onTelemetryConsent: (value: boolean) => void }) {
  return <div className="settings-grid"><section className="section-card"><div className="section-heading"><div><span className="eyebrow">Appearance</span><h3>Theme</h3></div><Palette size={22}/></div><p className="muted-copy">Follow the Windows appearance automatically, or keep a manual override.</p><div className="theme-options"><ThemeOption active={theme === 'system'} icon={MonitorCog} title="Use Windows setting" text="Switch automatically with the operating system" onClick={() => onTheme('system')}/><ThemeOption active={theme === 'light'} icon={Sun} title="Light" text="High-contrast light surfaces" onClick={() => onTheme('light')}/><ThemeOption active={theme === 'dark'} icon={Moon} title="Dark" text="Low-glare dark surfaces" onClick={() => onTheme('dark')}/></div></section><section className="section-card"><div className="section-heading"><div><span className="eyebrow">Security posture</span><h3>Local enforcement</h3></div><ShieldCheck size={22}/></div><div className="settings-lines"><div><LockKeyhole size={18}/><span><strong>Credentials</strong><small>Encrypted with Windows DPAPI for this user</small></span></div><div><TerminalSquare size={18}/><span><strong>Model output</strong><small>Cannot introduce executable commands or unknown action IDs</small></span></div><div><RotateCcw size={18}/><span><strong>Recovery</strong><small>Verified restore point, Registry exports, and action journal</small></span></div></div><label className="consent-toggle"><input type="checkbox" checked={telemetryConsent} onChange={event => onTelemetryConsent(event.target.checked)}/><span><strong>Query isolated optional telemetry</strong><small>Runs a no-network helper only during a scan. PawnIO remains blocked: no driver is installed or loaded.</small></span></label></section></div>;
}

function Metric({ icon: Icon, label, value, tone }: { icon: typeof Bot; label: string; value: string; tone?: string }) { return <article className="metric-card"><div className={`metric-icon ${tone ?? ''}`}><Icon size={19}/></div><div><span>{label}</span><strong>{value}</strong></div></article>; }
function Step({ number, title, text }: { number: string; title: string; text: string }) { return <article><span>{number}</span><strong>{title}</strong><p>{text}</p></article>; }
function EmptyState({ icon: Icon, title, text, action, onAction }: { icon: typeof Activity; title: string; text: string; action?: string; onAction?: () => void }) { return <div className="empty-state"><div><Icon size={30}/></div><h2>{title}</h2><p>{text}</p>{action && <button className="primary" onClick={onAction}>{action}<ChevronRight size={17}/></button>}</div>; }
function DiagnosisView({ diagnosis }: { diagnosis: Diagnosis }) { return <div className="diagnosis"><p>{diagnosis.summary}</p>{diagnosis.findings.length > 0 && <><h4>Verified findings</h4><div className="finding-list">{diagnosis.findings.map(item => <article key={item.evidenceId}><strong>{item.title}</strong><code>{item.evidenceId}: {item.currentValue}</code><p>{item.assessment}</p></article>)}</div></>}{diagnosis.recommendations.length > 0 && <><h4>Contextual plan</h4><ul>{diagnosis.recommendations.map(item => <li key={item.id}><strong>{planKindLabel(item.kind)}:</strong> {item.reason}</li>)}</ul></>}</div>; }

function PlanItemReview({ recommendations }: { recommendations: Recommendation[] }) {
  const [copiedId, setCopiedId] = useState('');
  const reviewOnly = recommendations.filter(item => item.kind !== 'executableAction');
  if (!reviewOnly.length) return null;
  return <section className="section-card plan-item-section"><div className="section-heading"><div><span className="eyebrow">Review-only plan items</span><h3>Guidance, scripts, resources, and notices</h3></div><span className="status-pill">Never auto-run</span></div><div className="plan-item-list">{reviewOnly.map(item => <article aria-label={`${planKindLabel(item.kind)}: ${item.title}`} key={item.id} className={`plan-item ${item.kind}`}><div className="plan-item-heading"><FileText size={18}/><div><span>{planKindLabel(item.kind)}</span><strong>{item.title}</strong></div><span className={`risk ${item.risk}`}>{item.risk} risk</span></div><p>{item.reason}</p>{item.expectedImpact && <small><strong>Expected impact:</strong> {item.expectedImpact}</small>}{item.tradeoffs.length > 0 && <small><strong>Trade-offs:</strong> {item.tradeoffs.join(' · ')}</small>}{item.reviewWarnings.length > 0 && <div className="script-warnings" role="alert">{item.reviewWarnings.map(warning => <span key={warning}>{warning}</span>)}</div>}{item.kind === 'scriptArtifact' && <><pre className="script-preview" tabIndex={0} aria-label={`Full ${item.scriptLanguage} script preview`}>{item.script}</pre><div className="button-row"><button className="secondary" aria-label={`Copy script ${item.title}`} onClick={async () => { await navigator.clipboard.writeText(item.script); setCopiedId(item.id); }}><Copy size={15}/>Copy</button><button className="secondary" aria-label={`Save script ${item.title} as an inert text file`} onClick={() => saveScriptArtifact(item)}><Download size={15}/>Save as .txt</button></div>{copiedId === item.id && <span className="sr-only" role="status">Script copied to clipboard.</span>}</>}{item.sourceReferences.length > 0 && <div className="source-list">{item.sourceReferences.map(source => <a key={`${item.id}-${source.url}-${source.title}`} href={source.url || undefined} target="_blank" rel="noreferrer">{source.title} · {source.grade}</a>)}</div>}<div className="conflict-evidence">{item.evidenceIds.map(id => <code key={id}>{id}</code>)}</div></article>)}</div></section>;
}
function ConflictView({ conflicts }: { conflicts: ConflictPattern[] }) { return <section className="section-card conflict-section"><div className="section-heading"><div><span className="eyebrow">Local conflict graph</span><h3>{conflicts.length} objective-aware relationships</h3></div><span className="status-pill">Deterministic rules</span></div><div className="conflict-list">{conflicts.map(conflict => <article key={conflict.id} className={`conflict-card ${conflict.kind}`}><div className="conflict-title"><div><span>{conflict.kind.replace(/([A-Z])/g, ' $1')}</span><strong>{conflict.title}</strong></div><small>{conflict.confidence} confidence</small></div><p>{conflict.explanation}</p><p><strong>Why it may be counterproductive:</strong> {conflict.whyCounterproductive}</p><div className="conflict-evidence">{Object.entries(conflict.evidence).map(([id, value]) => <code key={id}>{id} = {value}</code>)}</div><small>Objectives: {conflict.objectives.join(', ')}</small></article>)}</div></section>; }
function ThemeOption({ active, icon: Icon, title, text, onClick }: { active: boolean; icon: typeof Sun; title: string; text: string; onClick: () => void }) { return <button className={active ? 'theme-option active' : 'theme-option'} onClick={onClick}><Icon size={21}/><span><strong>{title}</strong><small>{text}</small></span>{active && <Check size={17}/>}</button>; }
function toggle(current: Set<string>, id: string) { const next = new Set(current); if (next.has(id)) next.delete(id); else next.add(id); return next; }
function optionalNumber(value: string): number | undefined { return value === '' ? undefined : Number(value); }
function saveScriptArtifact(item: Recommendation) { const blob = new Blob([item.script], { type: 'text/plain;charset=utf-8' }); const url = URL.createObjectURL(blob); const link = document.createElement('a'); link.href = url; link.download = scriptArtifactFilename(item.id); link.click(); URL.revokeObjectURL(url); }
function formatBytes(bytes: number) { return bytes < 1024 ? `${bytes} B` : `${(bytes / 1024).toFixed(1)} KiB`; }
function pageTitle(page: Page) { return ({ overview: 'System control center', provider: 'Model connection', scan: 'Local system profile', measurements: 'ETW measurement lab', review: 'Safe optimization plan', activity: 'Recovery and history', settings: 'Application preferences' } satisfies Record<Page, string>)[page]; }

export default App;
