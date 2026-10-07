import { useEffect, useRef, useState } from 'react';
import { Bot, Cpu, ShieldCheck } from 'lucide-react';
import { t } from '../i18n';
import './SetupGuide.css';

export interface SetupChoices { telemetry: boolean; firmware: boolean; assistant?: boolean; }
export function SetupGuide({ open, initial, onFinish, onClose }: {
  open: boolean; initial: SetupChoices; onFinish: (choices?: SetupChoices) => void; onClose: () => void;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const [step, setStep] = useState(0);
  const [choices, setChoices] = useState(initial);
  const [assistant, setAssistant] = useState(false);
  useEffect(() => {
    if (open && !dialog.current?.open) dialog.current?.showModal();
    if (!open && dialog.current?.open) dialog.current?.close();
  }, [open]);
  const titles = ['Welcome to NeuroTune', 'Optional sensor evidence', 'Firmware reading', 'Optional local assistant', 'Ready to start'];
  return <dialog ref={dialog} className="setup-guide" aria-labelledby="setup-title" onCancel={onClose}>
    <div className="setup-heading"><img src="/logo.svg" width="40" height="40" alt=""/><div><span className="eyebrow">{t('Guided setup')}</span><h2 id="setup-title">{t(titles[step])}</h2></div><span className="setup-count">{step + 1} / {titles.length}</span></div>
    <p className="muted-copy">{t('Optional means optional. Nothing is installed, sent to AI or applied just by opening this guide.')}</p>
    {step === 0 && <><ol className="setup-workflow"><li><Cpu size={20} aria-hidden="true"/><div><strong>{t('Scan and measure first')}</strong><p>{t('Complete diagnosis saves an initial latency report. Use idle conditions or a repeatable game scene; scheduling traces are not FPS or input latency.')}</p></div></li><li><Bot size={20} aria-hidden="true"/><div><strong>{t('Choose your main AI')}</strong><p>{t('Configure a provider and model, review transmission consent, then investigate. Local facts and optional tools do not replace that diagnosis.')}</p></div></li><li><ShieldCheck size={20} aria-hidden="true"/><div><strong>{t('Review, back up, apply and compare')}</strong><p>{t('Only selected registered Windows actions can be applied, with measurement and backup gates. Before results survive restart; compare new results afterward and choose Keep or Rollback.')}</p></div></li></ol></>}
    {step === 1 && <><Choice title={t('Allow optional sensor queries during scans?')} value={choices.telemetry} onChange={telemetry => setChoices(current => ({ ...current, telemetry }))}/><p>{t('Reads an already-running LibreHardwareMonitor WMI provider through an isolated helper. It may supply clocks, load, temperature and power; unavailable sensors remain unavailable. No driver or monitoring app is installed.')}</p><div className="setup-limit"><strong>PawnIO · {t('Not available')}</strong><p>{t('PawnIO is not approved or integrated for low-level reads. SPD, memory timings and voltage cannot be enabled by this choice. NeuroTune will not install or load a driver or weaken Windows protections.')}</p></div></>}
    {step === 2 && <><Choice title={t('Allow firmware identity and memory reading?')} value={choices.firmware} onChange={firmware => setChoices(current => ({ ...current, firmware }))}/><p>{t('Reads Windows-exposed BIOS/board identity and DIMM information in subsequent scans. Exact MSI setup values require a compatible export or physical BIOS verification. A VM cannot inspect its host BIOS.')}</p><div className="setup-limit"><strong>{t('BIOS modification · not supported')}</strong><p>{t('This consent is for reading only. NeuroTune does not write BIOS settings or flash firmware. SCEWIN export is a separate, privileged operation with package approval; importing an existing export runs no drivers.')}</p></div></>}
    {step === 3 && <><Choice title={t('Configure a local System One assistant?')} value={assistant} onChange={setAssistant}/><p>{t('It classifies troubleshooting topics, not causes. Better diagnosis or speed is not guaranteed. The main AI, exact evidence and approval gates remain authoritative.')}</p><p className="muted-copy">{t('After saving these choices, Yes opens System One configuration in Advanced tools. Installation and downloads require separate confirmation there; No leaves the existing assistant configuration unchanged.')}</p></>}
    {step === 4 && <><dl className="setup-summary"><div><dt>{t('Optional sensor queries')}</dt><dd>{t(choices.telemetry ? 'Yes' : 'No')}</dd></div><div><dt>{t('Firmware reading')}</dt><dd>{t(choices.firmware ? 'Yes' : 'No')}</dd></div><div><dt>PawnIO / {t('BIOS writing')}</dt><dd>{t('Not available')}</dd></div></dl><p>{t('These choices control subsequent local reads only. Existing reports are unchanged. You can reopen this guide in Settings and configure individual features in Advanced tools.')}</p><p className="muted-copy">{t('System One status is shown in its configuration panel; choosing Yes alone never means installed or enabled.')}</p></>}
    <div className="setup-actions"><button className="ghost" onClick={() => onFinish()}>{t('Skip without changing permissions')}</button><div className="button-row">{step > 0 && <button className="secondary" onClick={() => setStep(current => current - 1)}>{t('Back')}</button>}<button className="primary" onClick={() => step === titles.length - 1 ? onFinish({ ...choices, assistant }) : setStep(current => current + 1)}>{step === titles.length - 1 ? t('Save choices and continue') : t('Next')}</button></div></div>
  </dialog>;
}

function Choice({ title, value, onChange }: { title: string; value: boolean; onChange: (value: boolean) => void }) {
  return <fieldset className="setup-choice"><legend>{title}</legend><div className="button-row"><button aria-pressed={value} className={value ? 'secondary active' : 'secondary'} onClick={() => onChange(true)}>{t('Yes')}</button><button aria-pressed={!value} className={!value ? 'secondary active' : 'secondary'} onClick={() => onChange(false)}>{t('No')}</button></div></fieldset>;
}
