import { useEffect, useRef, useState } from 'react';
import { listen } from '@tauri-apps/api/event';
import { agent, cancelAgent, newRequestId } from './agent';
import type { SystemOneAdvisory } from './types';

interface Status { enabled: boolean; installed: boolean; hasFiles: boolean; size: string; device: string; directory: string; detail: string; source: string; model: string; hasApiKey: boolean; }
export function SystemOneNotes({ items }: { items?: SystemOneAdvisory[] }) {
  if (!items?.length) return null;
  return <details className="system-one-notes"><summary>Optional assistant · advisory only</summary>{items.map((item, index) => <article key={index}>
    <strong>{item.phase} · {item.domain ?? item.status}</strong><small> {item.seconds.toFixed(1)} s{item.score != null ? ` · uncalibrated option score ${(item.score * 100).toFixed(0)}%` : ''}</small>
    <p>{item.detail}</p><small>No action, risk rating, user goal or measured result was changed by this classifier.</small>
  </article>)}</details>;
}
export function SystemOnePanel() {
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
  return <section className="section-card system-one-panel"><div className="section-heading"><div><span className="eyebrow">Optional · separate from main AI</span><h3>System One assistant</h3></div><span className="status-pill">{busy ? 'Installing / configuring' : selectedEnabled ? 'Enabled · on demand' : remote ? 'Cloud · disabled' : status?.installed ? 'Installed · disabled' : 'Not installed'}</span></div>
    <p className="muted-copy">An auxiliary classifier organizes topics and application results. The selected main AI still diagnoses; this assistant cannot execute changes or overrule your choices. Neither assistant runs during System scan.</p>
    <fieldset disabled={busy || !status} className="form-grid">
      <label className="wide"><span id="system-one-source">Assistant source</span><select aria-labelledby="system-one-source" value={source} onChange={event => { setSource(event.target.value); setError(''); }}><option value="local">Local · Rizzo Flow System One</option><option value="openRouter">OpenRouter API · cloud classifier</option></select></label>
      {remote ? <>
        <p className="muted-copy wide">Cloud API calls cost OpenRouter credits and send sampled context to OpenRouter/the selected model provider. This is a token-generating classifier, not Rizzo's local probability scorer. No model download and no automatic provider fallback.</p>
        <label className="wide"><span id="system-one-cloud-key">System One OpenRouter API key</span><input aria-labelledby="system-one-cloud-key" type="password" autoComplete="off" value={apiKey} placeholder={status?.hasApiKey ? 'Separate encrypted key already saved' : 'Paste OpenRouter API key'} onChange={event => setApiKey(event.target.value)}/><small>Saved with Windows DPAPI, separately from the main AI key. Never sent to ChatGPT.</small></label>
        <button className="secondary wide" disabled={!apiKey.trim() && !status?.hasApiKey} onClick={() => void discoverCloudModels()}>Save key & load classifier models</button>
        <label className="wide"><span id="system-one-cloud-model">OpenRouter classifier model</span>{models.length ? <select aria-labelledby="system-one-cloud-model" value={model} onChange={event => setModel(event.target.value)}>{models.map(id => <option key={id} value={id}>{id}</option>)}</select> : <input aria-labelledby="system-one-cloud-model" value={model} maxLength={512} placeholder="Load models or enter an exact model ID" onChange={event => setModel(event.target.value)}/>}</label>
      </> : <>
        <label><span id="system-one-size">Local model</span><select aria-labelledby="system-one-size" value={size} onChange={event => setSize(event.target.value)}><option value="4b">4B Q8 · ~4.4 GB download</option><option value="1.7b">1.7B Q8 · ~1.8 GB · lower accuracy</option></select></label>
        <label><span id="system-one-device">Compute mode</span><select aria-labelledby="system-one-device" value={device} onChange={event => setDevice(event.target.value)}><option value="auto">Automatic · Vulkan GPU or CPU</option><option value="cpu">CPU only</option></select></label>
      </>}
      <label className="consent-toggle wide"><input type="checkbox" checked={selectedEnabled} disabled={remote && (!model.trim() || (!status?.hasApiKey && !apiKey.trim()))} onChange={event => {
        if (!event.target.checked) { void run('system-one-configure', { options: options(false) }); return; }
        if (remote) {
          if (window.confirm('Enable the optional OpenRouter classifier? Each auxiliary analysis may send sampled context to the cloud and consume API credits. Main AI credentials/model remain unchanged.'))
            void run('system-one-configure', { options: options(true), apiKey: apiKey || null }).then(() => setApiKey(''));
          return;
        }
        if (status?.installed && status.size === size) { void run('system-one-configure', { options: options(true) }); return; }
        if (window.confirm(`Install the optional ${size} local assistant? NeuroTune downloads pinned Rizzo/llama.cpp, private Python/dependencies and model weights. Reserve ${size === '4b' ? '7' : '4'} GiB of disk. Analysis will use local CPU/GPU and may be slower on first load. No API charge or security setting change.`))
          void run('system-one-install', options(true));
      }}/><span><strong>{remote ? 'Enable optional OpenRouter classifier' : 'Enable optional local assistant'}</strong><small>{remote ? 'Explicit cloud consent; separate API billing. No inferred probabilities or action authority.' : 'Installation, verification and runtime setup are automatic. GitHub/PyPI/Hugging Face; Apache/MIT/PSF terms. Non-administrator helper, not a filesystem sandbox.'}</small></span></label>
      <div className="button-row wide">{remote ? <button className="ghost" disabled={!status?.hasApiKey} onClick={() => { if (window.confirm('Disable this assistant and delete only its separate OpenRouter key? The main AI key/account is preserved.')) void run('system-one-configure', { options: options(false), forgetApiKey: true }); }}>Delete classifier API key</button> : <>
        <button className="secondary" disabled={!status?.installed || status.size !== size} onClick={() => void run('system-one-configure', { options: options(selectedEnabled) })}>Save compute mode</button>
        <button className="ghost" disabled={!status?.hasFiles} onClick={() => { if (window.confirm('Remove the optional model, Python environment and managed download cache? Measurement reports and AI credentials are preserved.')) void run('system-one-remove'); }}>Remove local component / partial downloads</button>
      </>}</div>
    </fieldset>
    {busy && <p role="status">{installing ? 'Installing locally; enabling occurs only after verification. Do not record benchmarks during installation.' : 'Updating optional configuration / loading model catalog…'}</p>}
    {busy && installing && <button className="secondary" onClick={() => { if (current.current) void cancelAgent(current.current).catch(reason => setError(String(reason))); }}>Cancel installation</button>}
    {!!logs.length && <ol className="operation-log" aria-label="Optional component progress">{logs.map((line, index) => <li key={index}>{line}</li>)}</ol>}
    {error && <p className="error-text" role="alert">{error}</p>}
    {status?.detail && (status.source ?? 'local') === source && <p className="muted-copy">{status.detail}</p>}
    <p className="muted-copy">Speed gains are not guaranteed. Optional failure preserves original evidence and the selected main AI. No resident server or automatic startup download.</p>
  </section>;
}
