import { useEffect, useState } from 'react';
import { agent } from '../agent';
import { t } from '../i18n';

// F9: optional. Without a key the selected provider's own web search is used where it exists (Anthropic, OpenRouter, ChatGPT).
export function TavilyKey({ disabled = false }: { disabled?: boolean }) {
  const [configured, setConfigured] = useState(false);
  const [key, setKey] = useState('');
  const [error, setError] = useState('');
  useEffect(() => { agent<{ configured: boolean }>('tavily-status').then(status => setConfigured(status.configured)).catch(() => setConfigured(false)); }, []);
  const save = (value: string) => {
    setError('');
    agent<{ configured: boolean }>('tavily-save', { key: value }).then(status => { setConfigured(status.configured); setKey(''); }).catch(reason => setError(String(reason)));
  };
  return <section className="section-card">
    <h3>{t('Web search')}</h3>
    <p className="muted-copy">{t('The AI can search the web for games and settings NeuroTune does not know yet. By default it uses the search built into your provider, when it has one. With a Tavily key, NeuroTune uses Tavily instead. Web results are treated as unverified data.')}</p>
    <p role="status">{configured ? t('Tavily key saved and encrypted on this PC.') : t('No Tavily key: provider web search is used where available.')}</p>
    <div className="button-row">
      <input type="password" disabled={disabled} autoComplete="off" spellCheck={false} aria-label={t('Tavily API key')} placeholder={t('Tavily API key')} value={key} onChange={event => setKey(event.target.value)}/>
      <button className="secondary" disabled={disabled || !key.trim()} onClick={() => save(key)}>{t('Save key')}</button>
      {configured && <button className="secondary" disabled={disabled} onClick={() => save('')}>{t('Remove key')}</button>}
    </div>
    {error && <p className="error-text" role="alert">{error}</p>}
  </section>;
}
