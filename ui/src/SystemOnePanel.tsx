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
  const options = (enabled: boolean) => ({ enabled, source, size, device, model });
  async function refresh() {
    const value = await agent<Status>('system-one-status'); setStatus(value);
    setSize(value.size ?? '4b'); setDevice(value.device ?? 'auto'); setSource(value.source ?? 'local'); setModel(value.model ?? '');
  }
  useEffect(() => {
    void refresh().catch(reason => setError(String(reason)));
    const stop = listen<{ requestId: string; message: string }>('agent-progress', event => {
      if (event.payload.requestId === current.current) setLogs(items => [...items, event.payload.message].slice(-10));
    });
    return () => { void stop.then(unlisten => unlisten()); };
  }, []);
  async function run(command: string, payload?: unknown) {
    if (current.current) return;
    const id = newRequestId(); current.current = id; setBusy(true); setInstalling(command === 'system-one-install'); setError(''); setLogs([]);
    try { const value = await agent<Status>(command, payload, id); setStatus(value); }
    catch (reason) { setError(String(reason)); }
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
  const remote = source === 'openRouter';
  const selectedEnabled = Boolean(status?.enabled && (status.source ?? 'local') === source && (remote ? status.model === model : status.size === size));
  return <section className="section-card system-one-panel"><div className="section-heading"><div><span className="eyebrow">{t('Optional · separate from main AI')}</span><h2>{t('System One assistant')}</h2></div><span className="status-pill">{t(busy ? 'Installing / configuring' : selectedEnabled ? 'Enabled · on demand' : remote ? 'Cloud · disabled' : status?.installed ? 'Installed · disabled' : 'Not installed')}</span></div>
    <p className="muted-copy">{t('An auxiliary classifier organizes topics and application results. The selected main AI still diagnoses; this assistant cannot execute changes or overrule your choices. Neither assistant runs during System scan.')}</p>
    <fieldset disabled={busy || !status} className="form-grid">
      <fieldset className="mode-control"><legend>{t('Assistant source')}</legend><div className="choice-grid">{(['local', 'openRouter'] as const).map(value => <label className="choice-option" key={value}><input type="radio" name={`${id}-source`} checked={source === value} onChange={() => { setSource(value); setError(''); }}/><span>{t(value === 'local' ? 'Local · Rizzo Flow System One' : 'OpenRouter API · cloud classifier')}</span></label>)}</div></fieldset>
      {remote ? <>
        <p className="muted-copy wide">{t("Cloud API calls cost OpenRouter credits and send sampled context to OpenRouter/the selected model provider. This is a token-generating classifier, not Rizzo's local probability scorer. No model download and no automatic provider fallback.")}</p>
        <label className="wide"><span id="system-one-cloud-key">{t('System One OpenRouter API key')}</span><input aria-labelledby="system-one-cloud-key" type="password" autoComplete="off" value={apiKey} placeholder={t(status?.hasApiKey ? 'Separate encrypted key already saved' : 'Paste OpenRouter API key')} onChange={event => setApiKey(event.target.value)}/><small>{t('Saved with Windows DPAPI, separately from the main AI key. Never sent to ChatGPT.')}</small></label>
        <button className="secondary wide" disabled={!apiKey.trim() && !status?.hasApiKey} onClick={() => void discoverCloudModels()}>{t('Save key & load classifier models')}</button>
        <label className="wide"><span id="system-one-cloud-model">{t('OpenRouter classifier model')}</span>{models.length ? <select aria-labelledby="system-one-cloud-model" value={model} onChange={event => setModel(event.target.value)}>{models.map(id => <option key={id} value={id}>{id}</option>)}</select> : <input aria-labelledby="system-one-cloud-model" value={model} maxLength={512} placeholder={t('Load models or enter an exact model ID')} onChange={event => setModel(event.target.value)}/>}</label>
      </> : <>
        <fieldset className="mode-control"><legend>{t('Local model')}</legend><div className="choice-grid">{['4b', '1.7b'].map(value => <label className="choice-option" key={value}><input type="radio" name={`${id}-size`} checked={size === value} onChange={() => setSize(value)}/><span>{t(value === '4b' ? '4B Q8 · ~4.4 GB download' : '1.7B Q8 · ~1.8 GB · lower accuracy')}</span></label>)}</div></fieldset>
        <fieldset className="mode-control"><legend>{t('Compute mode')}</legend><div className="choice-grid">{['auto', 'cpu'].map(value => <label className="choice-option" key={value}><input type="radio" name={`${id}-device`} checked={device === value} onChange={() => setDevice(value)}/><span>{t(value === 'auto' ? 'Automatic · Vulkan GPU or CPU' : 'CPU only')}</span></label>)}</div></fieldset>
      </>}
      <label className="consent-toggle wide"><input type="checkbox" checked={selectedEnabled} disabled={remote && (!model.trim() || (!status?.hasApiKey && !apiKey.trim()))} onChange={event => {
        if (!event.target.checked) { void run('system-one-configure', { options: options(false) }); return; }
        if (remote) {
          if (window.confirm(t('Enable the optional OpenRouter classifier? Each auxiliary analysis may send sampled context to the cloud and consume API credits. Main AI credentials/model remain unchanged.')))
            void run('system-one-configure', { options: options(true), apiKey: apiKey || null }).then(() => setApiKey(''));
          return;
        }
        if (status?.installed && status.size === size) { void run('system-one-configure', { options: options(true) }); return; }
        if (window.confirm(t('Install the optional {size} local assistant? NeuroTune downloads pinned Rizzo/llama.cpp, private Python/dependencies and model weights. Reserve {disk} GiB of disk. Analysis will use local CPU/GPU and may be slower on first load. No API charge or security setting change.', { size, disk: size === '4b' ? '7' : '4' })))
          void run('system-one-install', options(true));
      }}/><span><strong>{t(remote ? 'Enable optional OpenRouter classifier' : 'Enable optional local assistant')}</strong><small>{t(remote ? 'Explicit cloud consent; separate API billing. No inferred probabilities or action authority.' : 'Installation, verification and runtime setup are automatic. GitHub/PyPI/Hugging Face; Apache/MIT/PSF terms. Non-administrator helper, not a filesystem sandbox.')}</small></span></label>
      <div className="button-row wide">{remote ? <button className="ghost" disabled={!status?.hasApiKey} onClick={() => { if (window.confirm(t('Disable this assistant and delete only its separate OpenRouter key? The main AI key/account is preserved.'))) void run('system-one-configure', { options: options(false), forgetApiKey: true }); }}>{t('Delete classifier API key')}</button> : <>
        <button className="secondary" disabled={!status?.installed || status.size !== size} onClick={() => void run('system-one-configure', { options: options(selectedEnabled) })}>{t('Save compute mode')}</button>
        <button className="ghost" disabled={!status?.hasFiles} onClick={() => { if (window.confirm(t('Remove the optional model, Python environment and managed download cache? Measurement reports and AI credentials are preserved.'))) void run('system-one-remove'); }}>{t('Remove local component / partial downloads')}</button>
      </>}</div>
    </fieldset>
    {busy && <p role="status">{t(installing ? 'Installing locally; enabling occurs only after verification. Do not record benchmarks during installation.' : 'Updating optional configuration / loading model catalog…')}</p>}
    {busy && installing && <button className="secondary" onClick={() => { if (current.current) void cancelAgent(current.current).catch(reason => setError(String(reason))); }}>{t('Cancel installation')}</button>}
    {!!logs.length && <ol className="operation-log" aria-label={t('Optional component progress')}>{logs.map((line, index) => <li key={index}>{line}</li>)}</ol>}
    {error && <p className="error-text" role="alert">{error}</p>}
    {status?.detail && (status.source ?? 'local') === source && <p className="muted-copy">{status.detail}</p>}
    <p className="muted-copy">{t('Speed gains are not guaranteed. Optional failure preserves original evidence and the selected main AI. No resident server or automatic startup download.')}</p>
  </section>;
}
