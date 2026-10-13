import { useState } from 'react';
import { Cpu, Download, RefreshCw } from 'lucide-react';
import { agent } from '../agent';
import { t } from '../i18n';
import { biosChecklistHtml, downloadFile } from '../report';
import type { BiosChecklistItem } from '../types';

const sourceLabel = { scewin: 'From the BIOS dump', manual: 'From the manual', general: 'Unverified (general knowledge)' } as const;

// F8 phase 1: NeuroTune writes nothing to the firmware; it lists the settings, restarts into setup and reads back what Windows can see.
export function BiosChecklist({ items }: { items: BiosChecklistItem[] }) {
  const [observed, setObserved] = useState<Record<string, string>>();
  const [error, setError] = useState('');
  const call = (work: () => Promise<void>) => { setError(''); work().catch(reason => setError(String(reason))); };
  return <section className="section-card">
    <div className="section-heading"><div><span className="eyebrow">{t('BIOS checklist')}</span><h3>{t('Change these by hand in the firmware setup')}</h3></div></div>
    <p className="muted-copy">{t('NeuroTune changes nothing in the BIOS. After EXPO/PBO/Curve Optimizer, run a stability test (for example OCCT or TM5) before measuring.')}</p>
    <div className="measurement-table">{items.map(item => <article key={item.setting}>
      <strong>{item.setting} → {item.value}</strong>
      {item.menuPath && <code>{item.menuPath}</code>}
      <span>{item.reason}</span>
      <small>{t(sourceLabel[item.source] ?? sourceLabel.general)}</small>
    </article>)}</div>
    <div className="button-row">
      <button className="secondary" onClick={() => downloadFile(biosChecklistHtml(items), 'text/html', 'neurotune-bios-checklist.html')}><Download size={16}/>{t('Download checklist')}</button>
      <button className="secondary" onClick={() => { if (window.confirm(t('Save your work. Windows restarts into the firmware setup in a few seconds.'))) call(async () => { await agent('reboot-firmware'); }); }}><Cpu size={16}/>{t('Restart into BIOS setup')}</button>
      <button className="secondary" onClick={() => call(async () => setObserved(await agent<Record<string, string>>('bios-verify')))}><RefreshCw size={16}/>{t('Check after restart')}</button>
    </div>
    {error && <p className="error-text" role="alert">{error}</p>}
    {observed && <dl className="review-ledger">{Object.entries(observed).map(([key, value]) => <div key={key}><dt>{key}</dt><dd>{value}</dd></div>)}</dl>}
  </section>;
}
