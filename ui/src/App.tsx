import { useEffect, useMemo, useState } from 'react';
import { listen } from '@tauri-apps/api/event';
import {
  Activity, Bot, Check, ChevronRight, CircleGauge, Cloud, Cpu, Database,
  HardDrive, KeyRound, Laptop, ListChecks, LoaderCircle, LockKeyhole, LogIn,
  Monitor, MonitorCog, Moon, Palette, Printer, RefreshCw, RotateCcw, ScanLine, Settings,
  ShieldCheck, SlidersHorizontal, Sun, Target, TerminalSquare, Wifi,
} from 'lucide-react';
import { agent } from './agent';
import { applyTheme, loadThemePreference } from './theme';
import type {
  ConflictPattern, Diagnosis, OperationManifest, OptimizationAction, ProviderKind, ProviderSettings,
  ScanResult, ThemePreference, TuningGoals,
} from './types';
import './App.css';

type Page = 'overview' | 'provider' | 'scan' | 'review' | 'activity' | 'settings';

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
  { id: 'review', label: 'Review changes', icon: ListChecks },
  { id: 'activity', label: 'Activity & restore', icon: Activity },
  { id: 'settings', label: 'Settings', icon: Settings },
];

function App() {
  const [page, setPage] = useState<Page>('overview');
  const [theme, setTheme] = useState<ThemePreference>(loadThemePreference);
  const [provider, setProvider] = useState<ProviderSettings>(defaults.openRouter);
  const [apiKey, setApiKey] = useState('');
  const [hasCredential, setHasCredential] = useState(false);
  const [models, setModels] = useState<string[]>([]);
  const [scan, setScan] = useState<ScanResult>();
  const [diagnosis, setDiagnosis] = useState<Diagnosis>();
  const [goals, setGoals] = useState<TuningGoals>({ priority: 'balanced', games: [], notes: '' });
  const [actions, setActions] = useState<OptimizationAction[]>([]);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [history, setHistory] = useState<OperationManifest[]>([]);
  const [busy, setBusy] = useState('');
  const [notice, setNotice] = useState<{ tone: 'success' | 'danger' | 'info'; text: string }>();

  useEffect(() => applyTheme(theme), [theme]);
  useEffect(() => {
    const unlisten = listen<string>('agent-progress', event => setBusy(`Deep scan · ${event.payload}`));
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
    diagnosis?.recommendations.map(item => [item.actionId, item.reason]) ?? [],
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
    const result = await run('Deep scan · starting hardware inventory…', () => agent<ScanResult>('scan'));
    if (result) {
      setScan(result);
      setActions(result.actions);
      setDiagnosis(undefined);
      setSelected(new Set());
      setPage('scan');
      setNotice({ tone: 'success', text: 'Local scan complete. No profile was sent to an AI provider.' });
    }
  }

  async function diagnose() {
    if (!scan) return scanSystem();
    const conflicts = await run('Building the local objective-aware conflict graph…', () =>
      agent<ConflictPattern[]>('analyze-local', { profile: scan.profile, goals }));
    if (!conflicts) return;
    const result = await run('AI synthesis · checking every claim against local evidence…', () =>
      agent<Diagnosis>('diagnose', { profile: scan.profile, goals }));
    setDiagnosis(result ?? {
      summary: 'The provider diagnosis failed, but the deterministic local conflict graph is still available.',
      findings: [], recommendations: [], conflicts,
      consentQuestion: 'Review the local conflicts and choose any supported reversible actions to apply.',
    });
    setSelected(new Set());
    setPage('review');
  }

  function applyPreset(mode: 'all' | 'safe' | 'none') {
    const ids = actions.filter(action => {
      if (!action.availability.canApply) return false;
      if (mode === 'all') return true;
      if (mode === 'safe') return action.risk === 'low';
      return false;
    }).map(action => action.id);
    setSelected(new Set(ids));
  }

  async function applyChanges() {
    const highRisk = actions.filter(action => selected.has(action.id) && action.risk === 'high').length;
    const warning = highRisk ? `\n\n${highRisk} selected action(s) are HIGH RISK. Review their evidence and rollback notes carefully.` : '';
    if (!selected.size || !window.confirm(`Apply ${selected.size} selected changes after creating a verified restore point?${warning}`)) return;
    const result = await run('Creating backups and applying verified changes…', () =>
      agent<OperationManifest>('apply', { actionIds: [...selected] }));
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
          {navigation.map(item => <button key={item.id} className={page === item.id ? 'nav-item active' : 'nav-item'} onClick={() => setPage(item.id)}><item.icon size={18}/><span>{item.label}</span></button>)}
        </nav>
        <div className="sidebar-foot">
          <div className="security-chip"><ShieldCheck size={16}/><span>Allowlisted actions</span></div>
          <small>v0.5.0-alpha.1</small>
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
        {busy && <div className="busy-bar" role="status"><LoaderCircle size={16} className="spin"/><span>{busy}</span></div>}
        {pendingRecovery && <div className="recovery-banner" role="alert"><div><RotateCcw size={18}/><span><strong>An interrupted operation needs attention.</strong><small>{pendingRecovery.id}</small></span></div><button className="secondary" onClick={() => setPage('activity')}>Review recovery</button></div>}

        <section className="page-content">
          {page === 'overview' && <Overview hasProvider={hasCredential || !provider.requiresApiKey} scan={scan} history={history} onProvider={() => setPage('provider')} onScan={scanSystem}/>}
          {page === 'provider' && <ProviderPage provider={provider} apiKey={apiKey} hasCredential={hasCredential} models={models} onChoose={chooseProvider} onChange={setProvider} onKey={setApiKey} onSave={saveProvider} onLoadModels={loadModels} onBrowserSignIn={browserSignIn}/>}
          {page === 'scan' && <ScanPage scan={scan} diagnosis={diagnosis} goals={goals} onGoals={setGoals} onScan={scanSystem} onDiagnose={diagnose}/>}
          {page === 'review' && <ReviewPage diagnosis={diagnosis} actions={actions} recommendations={recommendations} selected={selected} onToggle={id => setSelected(current => toggle(current, id))} onPreset={applyPreset} onApply={applyChanges}/>}
          {page === 'activity' && <ActivityPage history={history} onRefresh={async () => setHistory(await agent<OperationManifest[]>('history'))} onRollback={rollback}/>}
          {page === 'settings' && <SettingsPage theme={theme} onTheme={setTheme}/>}
        </section>
      </main>
    </div>
  );
}

function Overview({ hasProvider, scan, history, onProvider, onScan }: { hasProvider: boolean; scan?: ScanResult; history: OperationManifest[]; onProvider: () => void; onScan: () => void }) {
  return <div className="stack-xl">
    <section className="hero-panel"><div className="hero-copy"><span className="kicker">CONTROLLED SYSTEM TUNING</span><h2>Understand the machine.<br/>Change only what is safe.</h2><p>NeuroTune combines a local Windows profile with your chosen AI model, then limits execution to compatible, reversible actions.</p><div className="button-row"><button className="primary" onClick={hasProvider ? onScan : onProvider}>{hasProvider ? 'Scan this PC' : 'Connect a provider'}<ChevronRight size={17}/></button><button className="secondary" onClick={onProvider}>Provider settings</button></div></div><div className="hero-visual"><div className="orbit one"/><div className="orbit two"/><Cpu size={48}/><span>LOCAL<br/>CONTROL</span></div></section>
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

function ScanPage({ scan, diagnosis, goals, onGoals, onScan, onDiagnose }: { scan?: ScanResult; diagnosis?: Diagnosis; goals: TuningGoals; onGoals: (value: TuningGoals) => void; onScan: () => void; onDiagnose: () => void }) {
  if (!scan) return <EmptyState icon={ScanLine} title="No local profile yet" text="Scan Windows locally first. NeuroTune will not contact your AI provider during this step." action="Scan this PC" onAction={onScan}/>;
  const priorities: Array<{ id: TuningGoals['priority']; label: string; detail: string }> = [
    { id: 'balanced', label: 'Balanced', detail: 'No single metric at any cost' },
    { id: 'fps', label: 'Frame rate', detail: 'Prioritize consistent gaming throughput' },
    { id: 'systemLatency', label: 'System latency', detail: 'Favor responsiveness and input latency' },
    { id: 'networkLatency', label: 'Network', detail: 'Focus on measured connection conditions' },
    { id: 'efficiency', label: 'Efficiency', detail: 'Protect battery life and thermals' },
  ];
  return <div className="stack-lg"><div className="page-actions"><div><span className="eyebrow">Local profile</span><h2>Set the target before diagnosis</h2></div><div className="button-row"><button className="secondary" onClick={onScan}><RefreshCw size={16}/>Scan again</button><button className="primary" onClick={onDiagnose}><Bot size={16}/>Run AI diagnosis</button></div></div><div className="metric-grid four"><Metric icon={Monitor} label="Windows" value={scan.profile.operatingSystem}/><Metric icon={Cpu} label="Processor" value={scan.profile.cpu}/><Metric icon={Database} label="Memory" value={scan.profile.memory}/><Metric icon={HardDrive} label="Registry checks" value={`${Object.keys(scan.profile.performanceRegistry).length} inspected`}/></div><section className="scan-summary"><div className="scan-phases">{scan.profile.scanPhases.map(phase => <article key={phase.name}><Check size={15}/><div><strong>{phase.name}</strong><small>{phase.factsCollected} facts · {(phase.durationMilliseconds / 1000).toFixed(1)} s</small></div></article>)}</div><div className="inventory-counts"><span><strong>{scan.profile.installedSoftware.length}</strong> applications</span><span><strong>{scan.profile.relevantDrivers.length}</strong> relevant drivers</span><span><strong>{scan.profile.softwareSignals.length}</strong> tuning/overlay signals</span><span><strong>{scan.profile.deviceIssues.length}</strong> device issues</span></div></section><section className="section-card goals-card"><div className="section-heading"><div><span className="eyebrow">Optimization intent</span><h3>What matters on this PC?</h3></div><Target size={22}/></div><div className="priority-options">{priorities.map(item => <button key={item.id} aria-pressed={goals.priority === item.id} className={goals.priority === item.id ? 'priority-option active' : 'priority-option'} onClick={() => onGoals({ ...goals, priority: item.id })}><strong>{item.label}</strong><small>{item.detail}</small></button>)}</div><div className="goal-fields"><label><span>Games or workloads</span><input maxLength={1200} defaultValue={goals.games.join(', ')} placeholder="Example: Valorant, Cyberpunk 2077" onChange={event => onGoals({ ...goals, games: event.target.value.split(',').map(x => x.trim()).filter(Boolean) })}/><small>Names provide context only; NeuroTune will not assume engine-specific behavior.</small></label><label><span>Anything else to preserve or improve?</span><textarea maxLength={1000} value={goals.notes} placeholder="Example: keep power use reasonable; Wi-Fi only" onChange={event => onGoals({ ...goals, notes: event.target.value })}/></label></div>{scan.profile.policyConflicts.length > 0 && <div className="local-observations"><strong>Local conflicts and manual overrides</strong><ul>{scan.profile.policyConflicts.map(item => <li key={item}>{item}</li>)}</ul></div>}</section><div className="split-panels"><section className="section-card"><div className="section-heading"><div><span className="eyebrow">Provider payload</span><h3>Sanitized profile</h3></div><span className="status-pill good">Reviewable</span></div><pre className="profile-json">{scan.sanitizedProfile}</pre></section><section className="section-card"><div className="section-heading"><div><span className="eyebrow">Model output</span><h3>Diagnosis</h3></div></div>{diagnosis ? <DiagnosisView diagnosis={diagnosis}/> : <div className="panel-placeholder"><Bot size={30}/><p>No data has been sent yet.</p><span>Your goals and this reviewed profile are sent only when you run diagnosis.</span></div>}</section></div></div>;
}

function ReviewPage({ diagnosis, actions, recommendations, selected, onToggle, onPreset, onApply }: { diagnosis?: Diagnosis; actions: OptimizationAction[]; recommendations: Map<string, string>; selected: Set<string>; onToggle: (id: string) => void; onPreset: (mode: 'all' | 'safe' | 'none') => void; onApply: () => void }) {
  const [view, setView] = useState<'recommended' | 'conflicts' | 'all'>('recommended');
  if (!diagnosis) return <EmptyState icon={Bot} title="No diagnosis yet" text="Scan the PC, choose your priorities, and ask the configured model for an evidence-backed diagnosis."/>;
  const conflictActionIds = new Set(diagnosis.conflicts.flatMap(conflict => conflict.suggestedActionIds));
  const visible = actions.filter(action => view === 'all' || (view === 'recommended' ? recommendations.has(action.id) : conflictActionIds.has(action.id)));
  const selectedHighRisk = actions.filter(action => selected.has(action.id) && action.risk === 'high').length;
  return <div className="stack-lg report-root">
    <div className="page-actions"><div><span className="eyebrow">User-controlled plan</span><h2>Evidence guides the plan; you decide</h2></div><div className="button-row"><button className="secondary" onClick={() => window.print()}><Printer size={16}/>Print report</button><button className="ghost" onClick={() => onPreset('safe')}>Select safe only</button><button className="ghost" onClick={() => onPreset('all')}>Select all supported</button><button className="ghost" onClick={() => onPreset('none')}>Clear</button></div></div>
    <section className="section-card report-summary"><div className="section-heading"><div><span className="eyebrow">Evidence-backed report</span><h3>AI diagnosis</h3></div><span className="status-pill good">No changes made</span></div><DiagnosisView diagnosis={diagnosis}/></section>
    {diagnosis.conflicts.length > 0 && <ConflictView conflicts={diagnosis.conflicts}/>}
    <div className="plan-tabs" role="tablist" aria-label="Action visibility"><button role="tab" aria-selected={view === 'recommended'} className={view === 'recommended' ? 'active' : ''} onClick={() => setView('recommended')}>AI recommended ({recommendations.size})</button><button role="tab" aria-selected={view === 'conflicts'} className={view === 'conflicts' ? 'active' : ''} onClick={() => setView('conflicts')}>Conflict fixes ({conflictActionIds.size})</button><button role="tab" aria-selected={view === 'all'} className={view === 'all' ? 'active' : ''} onClick={() => setView('all')}>All supported ({actions.length})</button></div>
    {visible.length > 0 ? <div className="action-list">{visible.map(action => { const related = diagnosis.conflicts.filter(conflict => conflict.suggestedActionIds.includes(action.id)).map(conflict => conflict.title); const reason = recommendations.get(action.id) ?? (related.join(' · ') || 'Supported reversible action; not selected by the current AI diagnosis.'); return <button key={action.id} aria-pressed={selected.has(action.id)} className={`action-card ${selected.has(action.id) ? 'selected' : ''} ${!action.availability.canApply ? 'disabled' : ''}`} disabled={!action.availability.canApply} onClick={() => onToggle(action.id)}><span className="check-box">{selected.has(action.id) && <Check size={15}/>}</span><div className="action-main"><div><strong>{action.name}</strong>{action.requiresRestart && <span className="tag">Restart</span>}</div><p>{action.description}</p><small>{reason}</small></div><div className="action-meta"><span className={`risk ${action.risk}`}>{action.risk} risk</span><strong>{action.availability.status}</strong><small>Current: {action.availability.currentValue}</small></div></button>; })}</div> : <section className="section-card no-fixes"><ShieldCheck size={22}/><div><strong>No supported action in this view.</strong><p>The conflict report remains visible; unsupported arbitrary writes are not generated.</p></div></section>}
    {selectedHighRisk > 0 && <div className="high-risk-warning" role="alert"><ShieldCheck size={19}/><span><strong>{selectedHighRisk} high-risk action(s) selected</strong><small>They remain selectable, but require an additional explicit confirmation before backup and execution.</small></span></div>}
    <section className="consent-card"><Bot size={20}/><div><span className="eyebrow">Model request</span><strong>{diagnosis.consentQuestion}</strong><small>The model cannot execute commands. Your selection is mapped to compiled, reversible actions only.</small></div></section>
    <div className="sticky-apply"><div><strong>{selected.size} changes selected</strong><span>A verified restore point and Registry exports are mandatory.</span></div><button className="primary" disabled={!selected.size} onClick={onApply}><ShieldCheck size={17}/>Back up & apply selected</button></div>
  </div>;
}

function ActivityPage({ history, onRefresh, onRollback }: { history: OperationManifest[]; onRefresh: () => void; onRollback: (id: string) => void }) {
  return <div className="stack-lg"><div className="page-actions"><div><span className="eyebrow">Operation journal</span><h2>Every attempted change remains traceable</h2></div><button className="secondary" onClick={onRefresh}><RefreshCw size={16}/>Refresh</button></div>{history.length ? <div className="history-list">{history.map(item => <article className="history-card" key={item.id}><div className="history-icon"><Activity size={19}/></div><div className="history-main"><div><strong>{item.status}</strong><span>{new Date(item.createdAt).toLocaleString()}</span></div><p>{item.actions.length} journaled actions · {item.id}</p>{item.error && <small className="error-text">{item.error}</small>}</div><button className="secondary" disabled={!item.actions.some(action => (action.applied || action.attempted) && !action.rolledBack)} onClick={() => onRollback(item.id)}><RotateCcw size={15}/>Restore</button></article>)}</div> : <EmptyState icon={Activity} title="No operations yet" text="Completed and interrupted operations will appear here with their rollback state."/>}</div>;
}

function SettingsPage({ theme, onTheme }: { theme: ThemePreference; onTheme: (value: ThemePreference) => void }) {
  return <div className="settings-grid"><section className="section-card"><div className="section-heading"><div><span className="eyebrow">Appearance</span><h3>Theme</h3></div><Palette size={22}/></div><p className="muted-copy">Follow the Windows appearance automatically, or keep a manual override.</p><div className="theme-options"><ThemeOption active={theme === 'system'} icon={MonitorCog} title="Use Windows setting" text="Switch automatically with the operating system" onClick={() => onTheme('system')}/><ThemeOption active={theme === 'light'} icon={Sun} title="Light" text="High-contrast light surfaces" onClick={() => onTheme('light')}/><ThemeOption active={theme === 'dark'} icon={Moon} title="Dark" text="Low-glare dark surfaces" onClick={() => onTheme('dark')}/></div></section><section className="section-card"><div className="section-heading"><div><span className="eyebrow">Security posture</span><h3>Local enforcement</h3></div><ShieldCheck size={22}/></div><div className="settings-lines"><div><LockKeyhole size={18}/><span><strong>Credentials</strong><small>Encrypted with Windows DPAPI for this user</small></span></div><div><TerminalSquare size={18}/><span><strong>Model output</strong><small>Cannot introduce executable commands or unknown action IDs</small></span></div><div><RotateCcw size={18}/><span><strong>Recovery</strong><small>Verified restore point, Registry exports, and action journal</small></span></div></div></section></div>;
}

function Metric({ icon: Icon, label, value, tone }: { icon: typeof Bot; label: string; value: string; tone?: string }) { return <article className="metric-card"><div className={`metric-icon ${tone ?? ''}`}><Icon size={19}/></div><div><span>{label}</span><strong>{value}</strong></div></article>; }
function Step({ number, title, text }: { number: string; title: string; text: string }) { return <article><span>{number}</span><strong>{title}</strong><p>{text}</p></article>; }
function EmptyState({ icon: Icon, title, text, action, onAction }: { icon: typeof Activity; title: string; text: string; action?: string; onAction?: () => void }) { return <div className="empty-state"><div><Icon size={30}/></div><h2>{title}</h2><p>{text}</p>{action && <button className="primary" onClick={onAction}>{action}<ChevronRight size={17}/></button>}</div>; }
function DiagnosisView({ diagnosis }: { diagnosis: Diagnosis }) { return <div className="diagnosis"><p>{diagnosis.summary}</p>{diagnosis.findings.length > 0 && <><h4>Verified findings</h4><div className="finding-list">{diagnosis.findings.map(item => <article key={item.evidenceId}><strong>{item.title}</strong><code>{item.evidenceId}: {item.currentValue}</code><p>{item.assessment}</p></article>)}</div></>}{diagnosis.recommendations.length > 0 && <><h4>Allowlisted recommendations</h4><ul>{diagnosis.recommendations.map(item => <li key={item.actionId}>{item.reason}</li>)}</ul></>}</div>; }
function ConflictView({ conflicts }: { conflicts: ConflictPattern[] }) { return <section className="section-card conflict-section"><div className="section-heading"><div><span className="eyebrow">Local conflict graph</span><h3>{conflicts.length} objective-aware relationships</h3></div><span className="status-pill">Deterministic rules</span></div><div className="conflict-list">{conflicts.map(conflict => <article key={conflict.id} className={`conflict-card ${conflict.kind}`}><div className="conflict-title"><div><span>{conflict.kind.replace(/([A-Z])/g, ' $1')}</span><strong>{conflict.title}</strong></div><small>{conflict.confidence} confidence</small></div><p>{conflict.explanation}</p><p><strong>Why it may be counterproductive:</strong> {conflict.whyCounterproductive}</p><div className="conflict-evidence">{Object.entries(conflict.evidence).map(([id, value]) => <code key={id}>{id} = {value}</code>)}</div><small>Objectives: {conflict.objectives.join(', ')}</small></article>)}</div></section>; }
function ThemeOption({ active, icon: Icon, title, text, onClick }: { active: boolean; icon: typeof Sun; title: string; text: string; onClick: () => void }) { return <button className={active ? 'theme-option active' : 'theme-option'} onClick={onClick}><Icon size={21}/><span><strong>{title}</strong><small>{text}</small></span>{active && <Check size={17}/>}</button>; }
function toggle(current: Set<string>, id: string) { const next = new Set(current); if (next.has(id)) next.delete(id); else next.add(id); return next; }
function pageTitle(page: Page) { return ({ overview: 'System control center', provider: 'Model connection', scan: 'Local system profile', review: 'Safe optimization plan', activity: 'Recovery and history', settings: 'Application preferences' } satisfies Record<Page, string>)[page]; }

export default App;
