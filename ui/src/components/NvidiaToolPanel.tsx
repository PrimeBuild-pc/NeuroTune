import { useEffect, useState } from 'react';
import { Download } from 'lucide-react';
import { agent } from '../agent';
import { t } from '../i18n';

// F5: NVIDIA driver profiles need NVIDIA Profile Inspector (MIT); it is downloaded only on request and pinned by SHA-256.
export function NvidiaToolPanel() {
  const [status, setStatus] = useState<{ installed: boolean; reason: string }>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  useEffect(() => { agent<{ installed: boolean; reason: string }>('nvpi-status').then(setStatus).catch(reason => setError(String(reason))); }, []);
  const install = () => {
    setBusy(true); setError('');
    agent<{ installed: boolean; reason: string }>('nvpi-install').then(setStatus).catch(reason => setError(String(reason))).finally(() => setBusy(false));
  };
  return <section className="section-card">
    <h2>{t('NVIDIA Profile Inspector')}</h2>
    <p className="muted-copy">{t('Lets the AI propose NVIDIA driver profile settings (power mode, low latency, V-Sync, frame limiter). Each change is journaled and restored by re-importing the saved profile. Downloaded from the official GitHub release and checked against a pinned SHA-256.')}</p>
    {status && <p role="status">{status.installed ? t('Installed and verified.') : status.reason}</p>}
    {error && <p className="error-text" role="alert">{error}</p>}
    {status && !status.installed && <button className="primary" disabled={busy} onClick={install}><Download size={16}/>{t('Install NVIDIA Profile Inspector')}</button>}
  </section>;
}
