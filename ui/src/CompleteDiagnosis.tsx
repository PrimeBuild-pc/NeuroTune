import { useEffect, useId, useState } from 'react';
import { Circle, CircleCheck, RefreshCw } from 'lucide-react';
import { agent } from './agent';
import { t } from './i18n';
import { DiagnosisProgressPanel } from './components/DiagnosisProgressPanel';
import { SupportingFiles } from './SupportingFiles';
import { analysisPresetsFor, scenarioProfiles } from './plan';
import type { DiagnosisProgress } from './diagnosisFlow';
import type { InvestigationMode, MeasurementWorkload, ScanResult, SupportingAttachment, TuningGoals } from './types';

export function CompleteDiagnosis({ goals, onGoals, onStart, progress, onCancel, children, blocked, mode = 'measuredOptimization', onMode, auditPreview, onAuditApprove, blockedReason, onProvider, consentKey }: {
  mode?: InvestigationMode; onMode?: (mode: InvestigationMode) => void; auditPreview?: ScanResult; onAuditApprove?: () => void;
  goals: TuningGoals; onGoals: (value: TuningGoals) => void; onStart: (workload: MeasurementWorkload | undefined, duration: number, attachments: SupportingAttachment[], imagesConfirmed: boolean, measurementConditions?: string) => void;
  progress?: DiagnosisProgress; onCancel: () => void; children?: React.ReactNode; blocked: boolean;
  blockedReason?: string; onProvider?: () => void; consentKey?: string;
}) {
  const durationId = useId();
  const [workloads, setWorkloads] = useState<MeasurementWorkload[]>([]);
  const [selected, setSelected] = useState('');
  const [duration, setDuration] = useState(60);
  const [measurementConditions, setMeasurementConditions] = useState('Idle');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const [attachments, setAttachments] = useState<SupportingAttachment[]>([]);
  const [supportReady, setSupportReady] = useState(true);
  const [imagesConfirmed, setImagesConfirmed] = useState(false);
  async function refresh() {
    setLoading(true); setError('');
    try { setWorkloads(await agent<MeasurementWorkload[]>('measurement-workloads')); }
    catch (reason) { setError(String(reason)); }
    finally { setLoading(false); }
  }
  useEffect(() => { if (mode !== 'auditOnly') void refresh(); }, [mode]);
  if (progress) return <div className="stack-lg">{auditPreview && <section className="section-card" aria-label={t('Audit preview before transmission')}><h2>{t('Audit collected locally · no AI transmission')}</h2><p>{t('Review this prepared evidence, including any antivirus metadata. Redaction is best-effort, not guaranteed anonymization. Continuing authorizes the selected provider and registered read-only follow-ups; no scanner or Windows change. Attachments remain subject to their separate preview and consent.')}</p><pre className="profile-json">{auditPreview.sanitizedProfile}</pre><div className="button-row"><button className="primary" onClick={onAuditApprove}>{t('I authorize evidence transmission · continue AI audit')}</button><button className="secondary" onClick={onCancel}>{t('Cancel without sending')}</button></div><p>{t('To avoid remote AI transmission, cancel, choose a local model and disable any optional cloud classifier before restarting.')}</p></section>}<DiagnosisProgressPanel progress={progress} onCancel={onCancel}/></div>;
  const target = workloads.find(item => String(item.processId) === selected);
  const presets = analysisPresetsFor(goals.priority);
  const captures = target ? 3 : 1;
  return <div className="stack-lg complete-diagnosis">
    {blocked && <div className="diagnosis-blocked" role="status"><p>{t(blockedReason ?? 'Configure a model, or finish/dismiss the existing plan or recovery before starting another diagnosis.')}</p>{onProvider && <button className="secondary" onClick={onProvider}>{t('Connect a provider')}</button>}</div>}
    <ol className="diagnosis-steps">
      <li className="section-card diagnosis-setup">
        <h2><span className="step-index" aria-hidden="true">1</span>{t('Goal')}</h2>
        <fieldset disabled={blocked || loading} className="form-grid">
          <ScenarioControl goals={goals} onGoals={onGoals}/>
          <label className="wide"><span id="diagnosis-priority">{t('Objective')}</span><select aria-labelledby="diagnosis-priority" aria-describedby="diagnosis-focus" value={goals.priority} onChange={event => onGoals({ ...goals, priority: event.target.value as TuningGoals['priority'] })}>{presets.map(item => <option key={item.id} value={item.id}>{t(item.label)}</option>)}</select><small id="diagnosis-focus">{t(presets.find(item => item.id === goals.priority)?.detail ?? '')}</small></label>
          <InvestigationModeControl mode={mode} onMode={onMode}/>
        </fieldset>
      </li>
      {mode !== 'auditOnly' && <li className="section-card diagnosis-measurement">
        <h2><span className="step-index" aria-hidden="true">2</span>{t('Measurement')}</h2>
        <fieldset disabled={blocked || loading} className="form-grid">
          <label className="wide"><span id="diagnosis-workload">{t('Running game / workload')}</span><span className="select-with-action">
            <select aria-labelledby="diagnosis-workload" value={selected} onChange={event => { setSelected(event.target.value); setMeasurementConditions(event.target.value ? '' : 'Idle'); }}><option value="">{t('System-wide diagnostic snapshot (not a game benchmark)')}</option>{workloads.map(item => <option key={item.processId} value={item.processId}>{item.name} · {item.processId}</option>)}</select>
            <button type="button" className="icon-button" aria-label={t('Refresh running workloads')} title={t('Refresh running workloads')} onClick={() => void refresh()}><RefreshCw size={16} aria-hidden="true"/></button>
          </span></label>
          <label className="wide"><span>{t('Repeatable conditions')}</span><input maxLength={240} value={measurementConditions} onChange={event => setMeasurementConditions(event.target.value)} placeholder={t('Idle, or the same game scene and graphics settings')}/></label>
          <fieldset className="duration-control wide" aria-describedby={`${durationId}-total`}>
            <legend>{t('Benchmark length')}</legend>
            <div className="duration-choice">{[30, 60, 180].map(seconds => <label key={seconds}><input type="radio" name={durationId} checked={duration === seconds} onChange={() => setDuration(seconds)}/><span>{seconds} s</span></label>)}</div>
            <small id={`${durationId}-total`} className="duration-total">{captures > 1
              ? t('{count} captures × {seconds} s ≈ {minutes} min of recording before the AI starts.', { count: captures, seconds: duration, minutes: Math.ceil(captures * duration / 60) })
              : t('One {seconds} s system capture before the AI starts.', { seconds: duration })}</small>
          </fieldset>
        </fieldset>
      </li>}
      <li className="section-card diagnosis-context">
        <h2><span className="step-index" aria-hidden="true">{mode === 'auditOnly' ? 2 : 3}</span>{t('Context for the AI')}</h2>
        <fieldset disabled={blocked || loading}>
          {goals.priority === 'hardwareLayout' && <HardwareGuide manual={attachments.some(item => item.name.toLowerCase().endsWith('.pdf.txt'))} photo={attachments.some(item => item.kind === 'image')}/>}
          <div className="form-grid">
            <label className="wide"><span>{t('Improve / preserve')}</span><textarea maxLength={1000} value={goals.notes} placeholder={t(goals.priority === 'hardwareLayout' ? 'Devices that matter: 8 kHz mouse, keyboard, USB mic, DAC, capture card…' : 'Lower latency without reducing image quality or security…')} onChange={event => onGoals({ ...goals, notes: event.target.value })}/></label>
            <label className="wide"><span>{t('Games or workloads')}</span><input maxLength={1200} value={goals.games.join(', ')} onChange={event => onGoals({ ...goals, games: event.target.value.split(',').map(value => value.trim()).filter(Boolean) })}/></label>
          </div>
        </fieldset>
        <SupportingFiles key={consentKey} files={attachments} onFiles={setAttachments} onReady={setSupportReady} onVision={setImagesConfirmed} disabled={blocked || loading}/>
        {mode !== 'auditOnly' && children && <details className="context-details"><summary>{t('Game details and your own measurements')}</summary><fieldset disabled={blocked || loading}>{children}</fieldset></details>}
      </li>
    </ol>
    {error && <p role="alert" className="error-text">{error}</p>}
    {loading && <p role="status" className="muted-copy">{t('Loading running workloads…')}</p>}
    <section className="diagnosis-submit" aria-label={t('Transmission consent')}>
      <p>{mode === 'auditOnly' ? t('The first click only collects local evidence; its preview requires a second consent before transmission to the provider.') : t('Starting sends the sanitized evidence and your files to the selected AI provider and may use its credits. Nothing on this PC is changed.')}</p>
      {!supportReady && <p role="status">{t('Review supporting files before starting diagnosis.')}</p>}
      <button className="primary" disabled={blocked || loading || !supportReady || Boolean(mode !== 'auditOnly' && (!measurementConditions.trim() || selected && !target))} onClick={() => onStart(mode === 'auditOnly' ? undefined : target, duration, attachments, imagesConfirmed, measurementConditions.trim())}>{mode === 'auditOnly' ? t('Start AI audit · no changes') : t('Complete diagnosis')}</button>
    </section>
  </div>;
}

// The AI gets a single pass, so everything the port plan needs is requested here, before the start.
function HardwareGuide({ manual, photo }: { manual: boolean; photo: boolean }) {
  const pick = () => document.getElementById('support-files-input')?.click();
  const items: Array<{ done: boolean; title: string; detail: string; action?: string; optional?: boolean }> = [
    { done: true, title: 'Device, USB and PCIe map', detail: 'Read automatically from Windows when you start.' },
    { done: manual, title: 'Motherboard manual (PDF)', detail: manual ? 'Added. Only the lane, slot and port pages are sent.' : 'Download it from the maker’s support page. Only the lane, slot and port pages are sent.', action: 'Add manual' },
    { done: false, title: 'Devices that matter', detail: 'List them in the notes below: mouse, keyboard, mic, DAC, capture card…' },
    { done: photo, title: 'Rear panel photo', detail: 'Optional. Lets the AI name ports by position; needs a model that reads images.', action: 'Add photo', optional: true },
  ];
  return <section className="input-checklist" aria-labelledby="hardware-guide-title">
    <h3 id="hardware-guide-title">{t('What the AI receives')}</h3>
    <ul>{items.map(item => <li key={item.title} data-done={item.done}>
      {item.done ? <CircleCheck size={18} aria-hidden="true"/> : <Circle size={18} aria-hidden="true"/>}
      <span><strong>{t(item.title)}</strong><small>{t(item.detail)}</small></span>
      {item.action && !item.done && <button type="button" className={item.optional ? 'ghost' : 'secondary'} onClick={pick}>{t(item.action)}</button>}
    </li>)}</ul>
  </section>;
}

function ScenarioControl({ goals, onGoals }: { goals: TuningGoals; onGoals: (value: TuningGoals) => void }) {
  const id = useId();
  // Prefix match: the ports scenario invites the user to append their devices to its notes.
  const active = scenarioProfiles.find(item => item.priority === goals.priority && item.riskProfile === goals.riskProfile && goals.notes.startsWith(t(item.notes)))?.id ?? 'custom';
  return <fieldset className="mode-control wide">
    <legend>{t('Scenario')}</legend>
    <div className="choice-grid">
      {scenarioProfiles.map(item => <label className="choice-option" key={item.id}><input type="radio" name={id} value={item.id} checked={active === item.id}
        onChange={() => onGoals({ ...goals, priority: item.priority, riskProfile: item.riskProfile, notes: t(item.notes) })}/><span><strong>{t(item.label)}</strong><small>{t(item.detail)}</small></span></label>)}
      <label className="choice-option"><input type="radio" name={id} value="custom" checked={active === 'custom'} readOnly/><span><strong>{t('Custom')}</strong><small>{t('Your own objective and notes below.')}</small></span></label>
    </div>
  </fieldset>;
}

export function InvestigationModeControl({ mode, onMode }: { mode: InvestigationMode; onMode?: (mode: InvestigationMode) => void }) {
  const id = useId();
  return <fieldset className="mode-control">
    <legend>{t('Investigation mode')}</legend>
    <div className="choice-grid">
      <label className="choice-option"><input type="radio" name={id} value="measuredOptimization" checked={mode === 'measuredOptimization'} onChange={() => onMode?.('measuredOptimization')}/><span><strong>{t('Measured optimization')}</strong><small>{t('Benchmarks and approval')}</small></span></label>
      <label className="choice-option"><input type="radio" name={id} value="auditOnly" checked={mode === 'auditOnly'} onChange={() => onMode?.('auditOnly')}/><span><strong>{t('Advisory audit')}</strong><small>{t('No benchmarks · Apply unavailable')}</small></span></label>
    </div>
  </fieldset>;
}
