import { useEffect, useId, useRef, useState } from 'react';
import { listen } from '@tauri-apps/api/event';
import { agent, cancelAgent, newRequestId } from './agent';
import type { SystemOneAdvisory } from './types';
import { t } from './i18n';

function advisoryLabel(value: string) {
  const labels: Record<string, string> = {
    analysis: 'Analysis', installation: 'Installation',
    'evidence-analysis': 'Evidence analysis', 'measurement comparison': 'Measurement comparison',
    'comparison explanation preparation': 'Comparison explanation preparation',
    'application-result': 'Application result', 'application-error': 'Application error',
    memory: 'Memory', drivers: 'Drivers', network: 'Network', power: 'Power', software: 'Software', unknown: 'Unknown',
    ok: 'OK', unavailable: 'Unavailable', insufficient_evidence: 'Insufficient evidence', out_of_range: 'Out of range', uncertain: 'Uncertain',
  };
  return Object.hasOwn(labels, value) ? t(labels[value]) : value;
}

interface Status { enabled: boolean; installed: boolean; hasFiles: boolean; size: string; device: string; directory: string; detail: string; source: string; model: string; hasApiKey: boolean; }
export function SystemOneNotes({ items }: { items?: SystemOneAdvisory[] }) {
  if (!items?.length) return null;
  return <details className="system-one-notes"><summary>{t('Optional assistant · advisory only')}</summary>{items.map((item, index) => <article key={index}>
    <strong>{advisoryLabel(item.phase)} · {advisoryLabel(item.domain ?? item.status)}</strong><small> {item.seconds.toFixed(1)} s{item.score != null ? t(' · uncalibrated option score {score}%', { score: (item.score * 100).toFixed(0) }) : ''}</small>
    <p>{item.detail}</p><small>{t('No action, risk rating, user goal or measured result was changed by this classifier.')}</small>
  </article>)}</details>;
}
export function SystemOnePanel() {
  const id = useId();
  const [status, setStatus] = useState<Status>();
  const [source, setSource] = useState('local');
  const [size, setSize] = useState('4b');
  const [device, setDevice] = useState('auto');
  const [model, setModel] = useState('');
  const [models, setModels] = useState<string[]>([]);
  const [apiKey, setApiKey] = useState('');
  const [busy, setBusy] = useState(false);
  const [installing, setInstalling] = useState(false);
  const [error, setError] = useState('');
  const [logs, setLogs] = useState<string[]>([]);
  const current = useRef<string | undefined>(undefined);
  const free = source === 'openRouterFree';
  const clm = source === 'clm';
  const remote = free || source === 'openRouter';
  const selectedModel = free ? 'openrouter/free' : clm ? '' : model;
  const options = (enabled: boolean) => ({ enabled, source, size, device, model: selectedModel, cloudConsent: enabled && remote });
  async function refresh() {
    const value = await agent<Status>('system-one-status'); setStatus(value);
    setSize(value.size ?? '4b'); setDevice(value.device ?? 'auto'); setSource(value.source ?? 'local'); setModel(value.source === 'openRouterFree' ? '' : value.model ?? '');
  }
  useEffect(() => {
    void refresh().catch(reason => setError(String(reason)));
    const stop = listen<{ requestId: string; message: string }>('agent-progress', event => {
      if (event.payload.requestId === current.current) setLogs(items => [...items, event.payload.message].slice(-10));
    });
    return () => { void stop.then(unlisten => unlisten()); };
  }, []);
  async function run(command: string, payload?: unknown) {
    if (current.current) return false;
    const id = newRequestId(); current.current = id; setBusy(true); setInstalling(command === 'system-one-install'); setError(''); setLogs([]);
    try { const value = await agent<Status>(command, payload, id); setStatus(value); return true; }
    catch (reason) { setError(String(reason)); return false; }
    finally { current.current = undefined; setBusy(false); try { await refresh(); } catch (reason) { setError(String(reason)); } }
  }
  async function discoverCloudModels() {
    if (current.current) return;
    const id = newRequestId(); current.current = id; setBusy(true); setInstalling(false); setError('');
    try {
      const value = await agent<Status>('system-one-configure', { options: options(false), apiKey: apiKey || null }, id);
      setStatus(value); setApiKey('');
      const ids = await agent<string[]>('system-one-models'); setModels(ids);
      setModel(ids.includes(model) ? model : ids[0] ?? '');
    } catch (reason) { setError(String(reason)); }
    finally { current.current = undefined; setBusy(false); }
  }
  const selectedEnabled = Boolean(status?.enabled && (status.source ?? 'local') === source && (remote ? status.model === selectedModel : status.size === size));
  const draftChanged = Boolean(status && ((status.source ?? 'local') !== source || (remote ? status.model !== selectedModel : status.size !== size || status.device !== device)));
  return <section className="section-card system-one-panel"><div className="section-heading"><div><span className="eyebrow">{t('Optional · separate from main AI')}</span><h2>{t('System One assistant')}</h2></div><span className="status-pill">{t(status?.enabled ? 'Enabled · on demand' : status?.source === 'clm' ? (status.installed ? 'Installed · disabled' : 'Not installed') : status && status.source !== 'local' ? 'Cloud · disabled' : status?.installed ? 'Installed · disabled' : 'Not installed')}</span></div>
    {status && <p className="muted-copy">{t('Saved assistant: {source} · {model}', { source: t(status.source === 'clm' ? 'CLM · local contrastive model' : status.source === 'openRouterFree' ? 'OpenRouter Free · no local install' : status.source === 'openRouter' ? 'OpenRouter API · cloud classifier' : 'Local · Rizzo Flow System One'), model: status.source === 'local' ? `${status.size} · ${status.device}` : status.model })}</p>}
    {status?.detail && <p className="muted-copy">{status.detail}</p>}
    {draftChanged && <p role="status" className="muted-copy">{t('Unsaved selection. Actual status and costs follow the saved configuration above, not this selection.')}</p>}
    <p className="muted-copy">{t('An auxiliary classifier organizes topics and application results. The selected main AI still diagnoses; this assistant cannot execute changes or overrule your choices. Neither assistant runs during System scan.')}</p>
    <fieldset disabled={busy || !status} className="form-grid">
      <fieldset className="mode-control"><legend>{t('Assistant source')}</legend><div className="choice-grid">{(['clm', 'local', 'openRouterFree', 'openRouter'] as const).map(value => <label className="choice-option" key={value}><input type="radio" name={`${id}-source`} checked={source === value} onChange={() => { setSource(value); setError(''); }}/><span>{t(value === 'clm' ? 'CLM · local contrastive model' : value === 'local' ? 'Local · Rizzo Flow System One' : value === 'openRouterFree' ? 'OpenRouter Free · no local install' : 'OpenRouter API · cloud classifier')}</span></label>)}</div></fieldset>
      {clm ? <p className="muted-copy wide">{t('Runs on this PC only. NeuroTune loads the CLM model (about 9-10 GB of VRAM) only while it classifies results and unloads it right after, so it never runs during measurements, and stops it when NeuroTune closes. No cloud or API key. Installing downloads about 9 GB once, each file checked against a pinned SHA-256.')}</p> : remote ? <>
        <p className="muted-copy wide">{t(free ? 'openrouter/free randomly selects an available compatible free model at zero price. A separate API key and cloud consent are required: sampled context goes to OpenRouter and the routed provider. Rate limits, availability and latency vary. No local install or paid/local fallback; not Rizzo scoring.' : "Cloud API calls cost OpenRouter credits and send sampled context to OpenRouter/the selected model provider. This is a token-generating classifier, not Rizzo's local probability scorer. No model download and no automatic provider fallback.")}</p>
        <label className="wide"><span id="system-one-cloud-key">{t('System One OpenRouter API key')}</span><input aria-labelledby="system-one-cloud-key" type="password" autoComplete="off" value={apiKey} placeholder={t(status?.hasApiKey ? 'Separate encrypted key already saved' : 'Paste OpenRouter API key')} onChange={event => setApiKey(event.target.value)}/><small>{t('Saved with Windows DPAPI, separately from the main AI key. Never sent to ChatGPT.')}</small></label>
        <button className="secondary wide" disabled={!apiKey.trim() && !status?.hasApiKey} onClick={() => { if (free) void run('system-one-configure', { options: options(false), apiKey: apiKey || null }).then(saved => { if (saved) setApiKey(''); }); else void discoverCloudModels(); }}>{t(free ? 'Save separate classifier key' : 'Save key & load classifier models')}</button>
        <label className="wide"><span id="system-one-cloud-model">{t('OpenRouter classifier model')}</span>{free ? <input aria-labelledby="system-one-cloud-model" value="openrouter/free" readOnly/> : models.length ? <select aria-labelledby="system-one-cloud-model" value={model} onChange={event => setModel(event.target.value)}>{models.map(id => <option key={id} value={id}>{id}</option>)}</select> : <input aria-labelledby="system-one-cloud-model" value={model} maxLength={512} placeholder={t('Load models or enter an exact model ID')} onChange={event => setModel(event.target.value)}/>}</label>
      </> : <>
        <fieldset className="mode-control"><legend>{t('Local model')}</legend><div className="choice-grid">{['4b', '1.7b'].map(value => <label className="choice-option" key={value}><input type="radio" name={`${id}-size`} checked={size === value} onChange={() => setSize(value)}/><span>{t(value === '4b' ? '4B Q8 · ~4.4 GB download' : '1.7B Q8 · ~1.8 GB · lower accuracy')}</span></label>)}</div></fieldset>
        <fieldset className="mode-control"><legend>{t('Compute mode')}</legend><div className="choice-grid">{['auto', 'cpu'].map(value => <label className="choice-option" key={value}><input type="radio" name={`${id}-device`} checked={device === value} onChange={() => setDevice(value)}/><span>{t(value === 'auto' ? 'Automatic · Vulkan GPU or CPU' : 'CPU only')}</span></label>)}</div></fieldset>
      </>}
      <label className="consent-toggle wide"><input type="checkbox" checked={selectedEnabled} disabled={remote && (!selectedModel.trim() || (!status?.hasApiKey && !apiKey.trim()))} onChange={event => {
        if (!event.target.checked) { void run('system-one-configure', { options: options(false) }); return; }
        if (clm) {
          if (status?.installed) void run('system-one-configure', { options: options(true) });
          else if (window.confirm(t('Install the local CLM assistant? NeuroTune downloads the pinned llama.cpp runtime, the Qwen3-8B encoder (about 9 GB), the CLM head and a private Python environment, checks every SHA-256 and runs one local test. Reserve 14 GiB of disk. Nothing is sent to the cloud.')))
            void run('system-one-install', options(true));
          return;
        }
        if (remote) {
          if (window.confirm(t(free ? 'Enable the optional free OpenRouter classifier and consent to sending sampled context to OpenRouter and its randomly selected free provider? openrouter/free uses only zero-price models, with rate/availability limits and no paid/local fallback. Main AI credentials/model remain unchanged.' : 'Enable the optional OpenRouter classifier? Each auxiliary analysis may send sampled context to the cloud and consume API credits. Main AI credentials/model remain unchanged.')))
            void run('system-one-configure', { options: options(true), apiKey: apiKey || null }).then(saved => { if (saved) setApiKey(''); });
          return;
        }
        if (status?.installed && status.size === size) { void run('system-one-configure', { options: options(true) }); return; }
        if (window.confirm(t('Install the optional {size} local assistant? NeuroTune downloads pinned Rizzo/llama.cpp, private Python/dependencies and model weights. Reserve {disk} GiB of disk. Analysis will use local CPU/GPU and may be slower on first load. No API charge or security setting change.', { size, disk: size === '4b' ? '7' : '4' })))
          void run('system-one-install', options(true));
      }}/><span><strong>{t(clm ? 'Enable optional CLM classifier' : free ? 'Enable optional free OpenRouter classifier' : remote ? 'Enable optional OpenRouter classifier' : 'Enable optional local assistant')}</strong><small>{t(clm ? 'Local only. If CLM is not installed the assistant is skipped and the original evidence and main AI are used. No inferred probabilities or action authority.' : free ? 'Separate cloud consent; zero-price router only. No inferred probabilities or action authority.' : remote ? 'Explicit cloud consent; separate API billing. No inferred probabilities or action authority.' : 'Installation, verification and runtime setup are automatic. GitHub/PyPI/Hugging Face; Apache/MIT/PSF terms. Non-administrator helper, not a filesystem sandbox.')}</small></span></label>
      <div className="button-row wide">{clm ? <button className="ghost" disabled={!status?.hasFiles} onClick={() => { if (window.confirm(t('Remove the local CLM model, Python environment and downloads? Measurement reports and AI credentials are preserved.'))) void run('system-one-remove'); }}>{t('Remove local component / partial downloads')}</button> : remote ? <button className="ghost" disabled={!status?.hasApiKey} onClick={() => { if (window.confirm(t('Disable this assistant and delete only its separate OpenRouter key? The main AI key/account is preserved.'))) void run('system-one-configure', { options: options(false), forgetApiKey: true }); }}>{t('Delete classifier API key')}</button> : <>
        <button className="secondary" disabled={!status?.installed || status.size !== size} onClick={() => void run('system-one-configure', { options: options(selectedEnabled) })}>{t('Save compute mode')}</button>
        <button className="ghost" disabled={!status?.hasFiles} onClick={() => { if (window.confirm(t('Remove the optional model, Python environment and managed download cache? Measurement reports and AI credentials are preserved.'))) void run('system-one-remove'); }}>{t('Remove local component / partial downloads')}</button>
      </>}</div>
    </fieldset>
    {busy && <p role="status">{t(installing ? 'Installing locally; enabling occurs only after verification. Do not record benchmarks during installation.' : 'Updating optional configuration / loading model catalog…')}</p>}
    {busy && installing && <button className="secondary" onClick={() => { if (current.current) void cancelAgent(current.current).catch(reason => setError(String(reason))); }}>{t('Cancel installation')}</button>}
    {!!logs.length && <ol className="operation-log" aria-label={t('Optional component progress')}>{logs.map((line, index) => <li key={index}>{line}</li>)}</ol>}
    {error && <p className="error-text" role="alert">{error}</p>}
    <p className="muted-copy">{t('Speed gains are not guaranteed. Optional failure preserves original evidence and the selected main AI. No resident server or automatic startup download.')}</p>
  </section>;
}
