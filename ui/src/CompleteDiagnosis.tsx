import { useEffect, useId, useState } from 'react';
import { agent } from './agent';
import { t } from './i18n';
import { DiagnosisProgressPanel } from './components/DiagnosisProgressPanel';
import { SupportingFiles } from './SupportingFiles';
import { analysisPresetsFor } from './plan';
import type { DiagnosisProgress } from './diagnosisFlow';
import type { InvestigationMode, MeasurementWorkload, ScanResult, SupportingAttachment, TuningGoals } from './types';

export function CompleteDiagnosis({ goals, onGoals, onStart, progress, onCancel, children, blocked, mode = 'measuredOptimization', onMode, auditPreview, onAuditApprove, blockedReason, onProvider, consentKey, onSecurity }: {
  mode?: InvestigationMode; onMode?: (mode: InvestigationMode) => void; auditPreview?: ScanResult; onAuditApprove?: () => void;
  goals: TuningGoals; onGoals: (value: TuningGoals) => void; onStart: (workload: MeasurementWorkload | undefined, duration: number, attachments: SupportingAttachment[], imagesConfirmed: boolean, measurementConditions?: string) => void;
  progress?: DiagnosisProgress; onCancel: () => void; children?: React.ReactNode; blocked: boolean;
  blockedReason?: string; onProvider?: () => void; consentKey?: string; onSecurity?: () => void;
}) {
  const durationId = useId();
  const [section, setSection] = useState('setup');
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
  return <div className="stack-lg complete-diagnosis">
    <p className="diagnosis-intro">{t('NeuroTune collects local facts. Measured optimization adds workload traces; audit-only skips benchmarks and cannot apply changes. Your selected AI investigates and proposes improvements for review.')}</p>
    {blocked && <div className="diagnosis-blocked" role="status"><p>{t(blockedReason ?? 'Configure a model, or finish/dismiss the existing plan or recovery before starting another diagnosis.')}</p>{onProvider && <button className="secondary" onClick={onProvider}>{t('Connect a provider')}</button>}</div>}
    <div className="section-switcher" role="group" aria-label={t('Diagnosis sections')}>
      <button aria-pressed={section === 'setup'} onClick={() => setSection('setup')}>{t('Diagnosis setup')}</button>
      <button aria-pressed={section === 'context'} onClick={() => setSection('context')}>{t('Optional context')}</button>
      <button aria-pressed={section === 'files'} onClick={() => setSection('files')}>{t('Supporting files')}{attachments.length > 0 && ` · ${attachments.length}`}</button>
    </div>
    <section className="section-card diagnosis-setup" hidden={section !== 'setup'}>
      <h2>{t('Objective and workload')}</h2>
      <fieldset disabled={blocked || loading} className="form-grid">
        <label className="wide"><span id="diagnosis-priority">{t('Objective')}</span><select aria-labelledby="diagnosis-priority" aria-describedby="diagnosis-focus" value={goals.priority} onChange={event => onGoals({ ...goals, priority: event.target.value as TuningGoals['priority'] })}>{presets.map(item => <option key={item.id} value={item.id}>{t(item.label)}</option>)}</select><small id="diagnosis-focus">{t(presets.find(item => item.id === goals.priority)?.detail ?? '')} {t('The preset specializes the AI prompt: it does not apply tweaks, start scanners or grant permissions. Investigation mode and budget remain separate. Your notes specify what to improve and preserve.')}</small></label>
        <InvestigationModeControl mode={mode} onMode={onMode}/>
        {mode !== 'auditOnly' && <>
          <label className="wide"><span id="diagnosis-workload">{t('Running game / workload')}</span><select aria-labelledby="diagnosis-workload" value={selected} onChange={event => { setSelected(event.target.value); setMeasurementConditions(event.target.value ? '' : 'Idle'); }}><option value="">{t('System-wide diagnostic snapshot (not a game benchmark)')}</option>{workloads.map(item => <option key={item.processId} value={item.processId}>{item.name} · {item.processId}</option>)}</select><small>{target ? t('Three automatic scheduling/interrupt traces, not an FPS or end-to-end input-latency measurement. Keep a representative scene running; do not change settings.') : t('One snapshot; game-specific performance and automatic apply remain unavailable. Select a running game for workload baselines.')}</small></label>
          <label className="wide"><span>{t('Repeatable conditions')}</span><input maxLength={240} value={measurementConditions} onChange={event => setMeasurementConditions(event.target.value)} placeholder={t('Idle, or the same game scene and graphics settings')}/><small>{t('For idle captures, close unnecessary activity and wait for background work to settle. In game, use the same repeatable scene, duration and settings before and after.')} {t('The initial latency report is saved locally before AI diagnosis and remains available after restart in Measurements.')}</small></label>
          <fieldset className="duration-control"><legend>{t('Seconds per trace')}</legend><div className="duration-choice">{[30, 60, 180].map(seconds => <label key={seconds}><input type="radio" name={durationId} checked={duration === seconds} onChange={() => setDuration(seconds)}/><span>{seconds} s</span></label>)}</div></fieldset>
          <button className="secondary workload-refresh" onClick={() => void refresh()}>{t('Refresh running workloads')}</button>
        </>}
        <label className="wide"><span>{t('Improve / preserve')}</span><textarea maxLength={1000} value={goals.notes} placeholder={t('Lower latency without reducing image quality or security…')} onChange={event => onGoals({ ...goals, notes: event.target.value })}/></label>
      </fieldset>
    </section>
    <section className="section-card diagnosis-context" hidden={section !== 'context'}>
      <h2>{t('Optional context')}</h2>
      <fieldset disabled={blocked || loading}>
        <div className="form-grid"><label className="wide"><span>{t('Games or workloads')}</span><input maxLength={1200} value={goals.games.join(', ')} onChange={event => onGoals({ ...goals, games: event.target.value.split(',').map(value => value.trim()).filter(Boolean) })}/></label></div>
        {mode !== 'auditOnly' && children}
      </fieldset>
    </section>
    <section className="section-card" hidden={section !== 'files'}>
      <SupportingFiles key={consentKey} files={attachments} onFiles={setAttachments} onReady={setSupportReady} onVision={setImagesConfirmed} disabled={blocked || loading}/>
    </section>
    {error && <p role="alert" className="error-text">{error}</p>}
    {loading && <p role="status" className="muted-copy">{t('Loading running workloads…')}</p>}
    {mode === 'auditOnly' && <p className="muted-copy">{t('Privacy/security audit: no ETW trace, benchmark, Windows change or antivirus scan. Settings and Defender summaries are read without detected file paths. The report may contain sensitive data; choose a local model and disable any optional cloud classifier if you prefer not to transmit it to remote AI.')}</p>}
    <section className="diagnosis-submit" aria-label={t('Transmission consent')}>
      <p>{mode === 'auditOnly' ? t('The first click only collects local evidence; its preview requires a second consent before transmission to the provider.') : t('Clicking consents to sending sanitized scan/measurement evidence and permitted read-only follow-ups to the configured AI provider.')} {t('Provider/optional cloud API usage may consume credits. No firmware tool, model download or system change is started.')}</p>
      {!supportReady && <p role="status">{t('Review supporting files before starting diagnosis.')} <button className="ghost" onClick={() => setSection('files')}>{t('Supporting files')}</button></p>}
      <button className="primary" disabled={blocked || loading || !supportReady || Boolean(mode !== 'auditOnly' && (!measurementConditions.trim() || selected && !target))} onClick={() => onStart(mode === 'auditOnly' ? undefined : target, duration, attachments, imagesConfirmed, measurementConditions.trim())}>{mode === 'auditOnly' ? t('Start AI audit · no changes') : t('Complete diagnosis')}</button>
    </section>
    {onSecurity && <div><button className="ghost" onClick={onSecurity}>{t('Windows security tools')}</button></div>}
  </div>;
}

export function InvestigationModeControl({ mode, onMode }: { mode: InvestigationMode; onMode?: (mode: InvestigationMode) => void }) {
  const id = useId();
  return <fieldset className="mode-control" aria-describedby={`${id}-detail`}>
    <legend>{t('Investigation mode')}</legend>
    <div className="choice-grid">
      <label className="choice-option"><input type="radio" name={id} value="measuredOptimization" checked={mode === 'measuredOptimization'} onChange={() => onMode?.('measuredOptimization')}/><span><strong>{t('Measured optimization')}</strong><small>{t('Benchmarks and approval')}</small></span></label>
      <label className="choice-option"><input type="radio" name={id} value="auditOnly" checked={mode === 'auditOnly'} onChange={() => onMode?.('auditOnly')}/><span><strong>{t('Advisory audit')}</strong><small>{t('No benchmarks · Apply unavailable')}</small></span></label>
    </div>
    <small id={`${id}-detail`}>{t('The mode is saved in the run and cannot be changed by the model or by replacing the preset. For privacy/security, choose Advisory audit if benchmarks are not needed.')}</small>
  </fieldset>;
}
