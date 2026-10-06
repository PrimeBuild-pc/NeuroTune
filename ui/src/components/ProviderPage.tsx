import { useEffect, useState } from 'react';
import { Bot, Check, Cloud, Cpu, KeyRound, Laptop, LoaderCircle, LockKeyhole, LogIn, ShieldCheck, SlidersHorizontal, Wifi } from 'lucide-react';
import type { ChatGptAccountInfo, ProviderKind, ProviderSettings } from '../types';
import { t } from '../i18n';
import './ProviderPage.css';

const providers = [
  { id: 'openRouter', name: 'OpenRouter', detail: 'API key or browser sign-in', icon: Cloud },
  { id: 'openAI', name: 'OpenAI API', detail: 'API key · separate usage-based billing', icon: Bot },
  { id: 'chatGpt', name: 'ChatGPT plan', detail: 'Official browser sign-in · eligible Plus/Pro usage', icon: LogIn },
  { id: 'anthropic', name: 'Anthropic', detail: 'Claude API', icon: Bot },
  { id: 'deepSeek', name: 'DeepSeek', detail: 'Native DeepSeek API', icon: Cpu },
  { id: 'custom', name: 'Custom', detail: 'Any compatible endpoint', icon: SlidersHorizontal },
  { id: 'local', name: 'Local', detail: 'Ollama, LM Studio, or vLLM', icon: Laptop },
] satisfies Array<{ id: ProviderKind; name: string; detail: string; icon: typeof Cloud }>;

export function ProviderPage({ provider, apiKey, hasCredential, models, modelLabels, chatGptAccounts, authBusy, onSignOut, onChoose, onChange, onKey, onSave, onLoadModels, onBrowserSignIn, onModel }: {
  provider: ProviderSettings; apiKey: string; hasCredential: boolean; models: string[]; modelLabels: Record<string, string>;
  chatGptAccounts: ChatGptAccountInfo[]; authBusy: boolean; onSignOut: () => void; onChoose: (id: ProviderKind) => void;
  onChange: (value: ProviderSettings) => void; onKey: (value: string) => void; onSave: () => void;
  onLoadModels: () => void; onBrowserSignIn: (addAccount?: boolean) => void; onModel: (model: string) => void;
}) {
  const [section, setSection] = useState('connection');
  useEffect(() => setSection('connection'), [provider.provider]);
  const editable = provider.provider === 'custom' || provider.provider === 'local';
  const selectedProvider = providers.find(item => item.id === provider.provider);
  return <div className="provider-layout">
    <section className="provider-picker" aria-labelledby="provider-picker-title">
      <div className="section-heading"><h2 id="provider-picker-title">{t('Choose where inference runs')}</h2></div>
      <p className="muted-copy">{t('One selected provider. No automatic fallback.')}</p>
      <div className="provider-list">{providers.map(item => <button key={item.id} disabled={authBusy} aria-pressed={provider.provider === item.id} className={`provider-option${provider.provider === item.id ? ' selected' : ''}`} onClick={() => onChoose(item.id)}>
        <item.icon size={18} aria-hidden="true"/><strong>{t(item.name)}</strong>{provider.provider === item.id && <Check size={16} aria-hidden="true"/>}
      </button>)}</div>
    </section>
    <section className="section-card provider-form" aria-labelledby="provider-form-title">
      <div className="section-heading"><div><h2 id="provider-form-title">{provider.providerName}</h2><p className="muted-copy">{t(selectedProvider?.detail ?? 'One selected provider. No automatic fallback.')}</p></div><span className={`status-pill${hasCredential || !provider.requiresApiKey ? ' good' : ''}`}>{!provider.requiresApiKey ? t('No API key required') : hasCredential ? t('Credential ready') : t('Not connected')}</span></div>
      <div className="section-switcher" role="group" aria-label={t('Provider sections')}>
        <button aria-pressed={section === 'connection'} disabled={authBusy} onClick={() => setSection('connection')}>{t('Connection')}</button>
        {provider.provider === 'chatGpt' && <button aria-pressed={section === 'accounts'} disabled={authBusy} onClick={() => setSection('accounts')}>{t('Account options')}</button>}
        <button aria-pressed={section === 'budget'} disabled={authBusy} onClick={() => setSection('budget')}>{t('AI investigation budget')}</button>
      </div>
      {authBusy && <p className="provider-working" role="status"><LoaderCircle size={16} className="spin" aria-hidden="true"/>{t('Provider operation in progress…')}</p>}
      <div hidden={section !== 'connection'} className="provider-connection stack-lg">
        {provider.provider === 'chatGpt' ? <div className="chatgpt-connection">
          <p className="muted-copy">{t('Sign in through your browser, then choose a model. Eligible usage comes from your ChatGPT plan/credits, not API-key billing.')}</p>
          <button className="primary" disabled={authBusy} onClick={() => onBrowserSignIn()}>{t('Continue with ChatGPT')}</button>
          <div className="form-grid"><label className="wide"><span id="chatgpt-model-label">{t('Model')}</span><select aria-labelledby="chatgpt-model-label" disabled={authBusy || !hasCredential || !models.length} value={models.includes(provider.model) ? provider.model : ''} onChange={event => onModel(event.target.value)}><option value="" disabled>{hasCredential ? t('Load available models') : t('Sign in to choose a model')}</option>{models.map(model => <option key={model} value={model}>{modelLabels[model] ?? model}</option>)}</select><small>{t('Selection is saved automatically.')}</small></label></div>
          <div className="button-row">{hasCredential && <button className="secondary" disabled={authBusy} onClick={onLoadModels}>{t('Reload models')}</button>}<a href="https://chatgpt.com/settings/usage" target="_blank" rel="noreferrer">{t('Manage usage')}</a></div>
        </div> : <>
          {provider.provider === 'openRouter' && <div className="oauth-panel"><div><LogIn size={20} aria-hidden="true"/><div><strong>{t('Sign in with OpenRouter')}</strong><span>{t('Authorize in your browser. NeuroTune stores the issued key with DPAPI.')}</span></div></div><button className="secondary" disabled={authBusy} onClick={() => onBrowserSignIn()}>{t('Continue in browser')}</button></div>}
          {editable ? <fieldset className="form-grid provider-fieldset" disabled={authBusy}>
            <legend>{t('Endpoint')}</legend>
            <label><span>{t('Display name')}</span><input value={provider.providerName} onChange={event => onChange({ ...provider, providerName: event.target.value })}/></label>
            <label><span>{t('API protocol')}</span><select value={provider.protocol} onChange={event => onChange({ ...provider, protocol: event.target.value as ProviderSettings['protocol'] })}><option value="openAiCompatible">{t('OpenAI-compatible')}</option><option value="anthropic">{t('Anthropic Messages')}</option></select></label>
            <label className="wide"><span id="provider-endpoint-label">{t('Base URL')}</span><input aria-labelledby="provider-endpoint-label" aria-describedby="provider-endpoint-hint" value={provider.baseUrl} spellCheck={false} onChange={event => onChange({ ...provider, baseUrl: event.target.value })}/><small id="provider-endpoint-hint">{t('Remote custom endpoints require HTTPS. HTTP is accepted only on loopback addresses.')}</small></label>
            {provider.provider === 'local' && <div className="wide quick-presets"><span>{t('Local presets')}</span><button onClick={() => onChange({ ...provider, providerName: 'Ollama', baseUrl: 'http://127.0.0.1:11434/v1' })}>Ollama</button><button onClick={() => onChange({ ...provider, providerName: 'LM Studio', baseUrl: 'http://127.0.0.1:1234/v1' })}>LM Studio</button><button onClick={() => onChange({ ...provider, providerName: 'vLLM', baseUrl: 'http://127.0.0.1:8000/v1' })}>vLLM</button></div>}
          </fieldset> : <dl className="provider-endpoint-info"><div><dt>{t('Endpoint')}</dt><dd><code>{provider.baseUrl}</code></dd></div><div><dt>{t('API protocol')}</dt><dd>{t(provider.protocol === 'anthropic' ? 'Anthropic Messages' : 'OpenAI-compatible')}</dd></div></dl>}
          <fieldset className="form-grid provider-fieldset" disabled={authBusy}>
            <legend>{provider.requiresApiKey ? t('Credential & model') : t('Model')}</legend>
            {provider.requiresApiKey && <label className="wide"><span>{t('API key')}</span><div className="secret-field"><KeyRound size={17} aria-hidden="true"/><input type="password" autoComplete="off" value={apiKey} placeholder={hasCredential ? t('Encrypted credential already saved') : t('Paste API key')} onChange={event => onKey(event.target.value)}/></div></label>}
            <label className="wide"><span id="provider-model-label">{t('Model')}</span><input aria-labelledby="provider-model-label" aria-describedby={modelLabels[provider.model] ? 'provider-model-name' : undefined} list="model-options" value={provider.model} placeholder={t('Exact model ID')} onChange={event => onChange({ ...provider, model: event.target.value })}/><datalist id="model-options">{models.map(model => <option key={model} value={model} label={modelLabels[model] ?? model}/>)}</datalist>{modelLabels[provider.model] && <small id="provider-model-name">{modelLabels[provider.model]}</small>}</label>
          </fieldset>
          <div><p className="muted-copy">{t('Save securely stores configuration locally. Test & discover models contacts this endpoint.')}</p><div className="form-actions"><button className="primary" disabled={authBusy} onClick={onLoadModels}><Wifi size={17} aria-hidden="true"/>{t('Test & discover models')}</button><button className="secondary" disabled={authBusy} onClick={onSave}><LockKeyhole size={17} aria-hidden="true"/>{t('Save securely')}</button></div></div>
          <div className="subscription-note"><ShieldCheck size={18} aria-hidden="true"/><p><strong>{t('About browser subscriptions')}</strong><br/>{t('Eligible ChatGPT Plus/Pro users can authorize the official ChatGPT plan connection for Responses requests in this open-source app. OpenAI API-key billing remains separate. OpenRouter also supports browser authorization. Other providers require their supported API credential; no subscription or cookie workaround is used.')}</p></div>
        </>}
      </div>
      {provider.provider === 'chatGpt' && <section hidden={section !== 'accounts'} className="provider-accounts stack-lg" aria-label={t('Account options')}>
        <div className="form-grid"><label className="wide"><span id="chatgpt-account-label">{t('Saved ChatGPT account / workspace registration')}</span><select aria-labelledby="chatgpt-account-label" disabled={authBusy} value={provider.chatGptAccountId ?? ''} onChange={event => onChange({ ...provider, chatGptAccountId: event.target.value || undefined, model: '' })}><option value="">{t('New registration')}</option>{chatGptAccounts.map(account => <option key={account.id} value={account.id}>{account.label}{account.connected ? '' : t(' · sign-in required')}</option>)}</select></label></div>
        <div className="button-row"><button className="secondary" disabled={authBusy} onClick={() => onBrowserSignIn(true)}>{t('Add another account')}</button><button className="ghost" disabled={authBusy || !provider.chatGptAccountId} onClick={onSignOut}>{t('Sign out selected account')}</button></div>
        <a href="https://developers.openai.com/siwc/token-sharing-open-source" target="_blank" rel="noreferrer">{t('Official authorization contract')}</a>
      </section>}
      <section className="investigation-budget" hidden={section !== 'budget'} aria-label={t('AI investigation budget')}>
        <h3>{t('AI investigation budget')}</h3><p>{t('Resource limits, not a tuning policy. No automatic provider fallback.')}</p>
        <fieldset className="form-grid" disabled={authBusy}>
          <label><span>{t('Maximum turns')}</span><input type="number" min="2" max="32" value={provider.investigationMaxTurns ?? 12} onChange={event => onChange({ ...provider, investigationMaxTurns: Number(event.target.value) })}/></label>
          <label><span>{t('Maximum minutes (after measurement)')}</span><input type="number" min="1" max="30" value={provider.investigationMaxMinutes ?? 10} onChange={event => onChange({ ...provider, investigationMaxMinutes: Number(event.target.value) })}/></label>
        </fieldset>
        <button className="secondary" disabled={authBusy || (provider.requiresApiKey && !hasCredential)} onClick={onSave}>{t('Save investigation budget')}</button>
      </section>
    </section>
    <div className="provider-trust"><ShieldCheck size={18} aria-hidden="true"/><p>{t('Credentials stay encrypted on this PC. Only consented evidence reaches your model.')}</p></div>
  </div>;
}
