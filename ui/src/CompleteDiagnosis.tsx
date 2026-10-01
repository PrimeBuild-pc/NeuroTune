import { useEffect, useState } from 'react';
import { agent } from './agent';
import { CaptureCountdown } from './LatencyDetails';
import { SupportingFiles } from './SupportingFiles';
import { analysisPresetsFor } from './plan';
import type { DiagnosisProgress } from './diagnosisFlow';
import type { MeasurementWorkload, SupportingAttachment, TuningGoals } from './types';

const stages = [
  ['scan', 'Local evidence', 'Hardware, Windows, drivers and configuration. No model inference.'],
  ['warmup', 'Prepare workload', 'Time to return to the game or application.'],
  ['capture', 'Measurements', 'Named WPR recordings; both assistants remain inactive.'],
  ['trace', 'Trace analysis', 'Local numerical analysis and quality checks.'],
  ['ai', 'AI investigation', 'The selected model interprets evidence and may request read-only follow-ups.'],
  ['result', 'Your proposal', 'Risk-ordered recommendations; nothing is applied automatically.'],
] as const;
export function CompleteDiagnosis({ goals, onGoals, onStart, progress, onCancel, children, blocked }: {
  goals: TuningGoals; onGoals: (value: TuningGoals) => void; onStart: (workload: MeasurementWorkload | undefined, duration: number, attachments: SupportingAttachment[], imagesConfirmed: boolean) => void;
  progress?: DiagnosisProgress; onCancel: () => void; children?: React.ReactNode; blocked: boolean;
}) {
  const [workloads, setWorkloads] = useState<MeasurementWorkload[]>([]);
  const [selected, setSelected] = useState('');
  const [duration, setDuration] = useState(60);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const [now, setNow] = useState(Date.now());
  const [attachments, setAttachments] = useState<SupportingAttachment[]>([]);
  const [supportReady, setSupportReady] = useState(true);
  const [imagesConfirmed, setImagesConfirmed] = useState(false);
  async function refresh() {
    setLoading(true); setError('');
    try { setWorkloads(await agent<MeasurementWorkload[]>('measurement-workloads')); }
    catch (reason) { setError(String(reason)); }
    finally { setLoading(false); }
  }
  useEffect(() => { void refresh(); }, []);
  const running = Boolean(progress);
  useEffect(() => {
    if (!running) return;
    const timer = setInterval(() => setNow(Date.now()), 1000); return () => clearInterval(timer);
  }, [running]);
  if (progress) return <section className="diagnosis-loading" aria-label="Complete diagnosis progress">
    <div className="page-actions"><div><span className="eyebrow">One investigation · no system writes</span><h2>Diagnosi completa in corso</h2></div><span role="timer" aria-live="off">{Math.max(0, Math.floor((now - progress.startedAt) / 1000))} s elapsed</span></div>
    <p className="diagnosis-current" role="status">{progress.message}</p>
    {progress.stage === 'warmup' && progress.warmupEndsAt && <p>Capture starts in {Math.max(0, Math.ceil((progress.warmupEndsAt - now) / 1000))} s. Keep the same scene/settings throughout all repeats.</p>}
    {progress.stage === 'capture' && progress.recordingStartedAt && <CaptureCountdown startedAt={progress.recordingStartedAt} durationSeconds={progress.durationSeconds!}/>}
    {progress.repeats && <p>Trace {progress.repeat} / {progress.repeats}</p>}
    <ol className="diagnosis-stages">{stages.map(([id, title, detail]) => <li key={id} aria-current={progress.stage === id ? 'step' : undefined} className={progress.stage === id ? 'current' : ''}><strong>{title}</strong><span>{detail}</span>{progress.stage === id && <small>Active now</small>}</li>)}</ol>
    <details open className="diagnosis-log"><summary>Actual operation log</summary><ol>{progress.log.map((line, index) => <li key={index}>{line}</li>)}</ol></details>
    <button className="secondary" onClick={onCancel}>Annulla diagnosi</button><p className="muted-copy">Cancellation stops read-only AI work and releases only this workflow's recorder. Completed reports remain local. App closure leaves the existing watchdog to finalize any capture.</p>
  </section>;
  const target = workloads.find(item => String(item.processId) === selected);
  const presets = analysisPresetsFor(goals.priority);
  return <div className="stack-lg complete-diagnosis">
    <section className="section-card"><span className="eyebrow">AI-led investigation</span><h2>One diagnosis, from evidence to proposal</h2><p>NeuroTune collects facts and measures automatically. Your selected AI investigates, challenges hypotheses and proposes improvements; you review the final result before any change.</p>
      <fieldset disabled={blocked || loading} className="form-grid">
        <label className="wide"><span id="diagnosis-priority">Objective</span><select aria-labelledby="diagnosis-priority" aria-describedby="diagnosis-focus" value={goals.priority} onChange={event => onGoals({ ...goals, priority: event.target.value as TuningGoals['priority'] })}>{presets.map(item => <option key={item.id} value={item.id}>{item.label}</option>)}</select><small id="diagnosis-focus">{presets.find(item => item.id === goals.priority)?.detail} Il preset specializza il prompt dell’AI: non applica tweak, non cambia misure obbligatorie, budget o autorizzazioni. Le tue note specificano cosa migliorare e preservare.</small></label>
        <label className="wide"><span id="diagnosis-workload">Running game / workload</span><select aria-labelledby="diagnosis-workload" value={selected} onChange={event => setSelected(event.target.value)}><option value="">System-wide diagnostic snapshot (not a game benchmark)</option>{workloads.map(item => <option key={item.processId} value={item.processId}>{item.name} · {item.processId}</option>)}</select><small>{target ? 'Three automatic scheduling/interrupt traces, not an FPS or end-to-end input-latency measurement. Keep a representative scene running; do not change settings.' : 'One snapshot; game-specific performance and automatic apply remain unavailable. Select a running game for workload baselines.'}</small></label>
        <label className="wide"><span>Games or workloads</span><input maxLength={1200} value={goals.games.join(', ')} onChange={event => onGoals({ ...goals, games: event.target.value.split(',').map(value => value.trim()).filter(Boolean) })}/></label>
        <label className="wide"><span>Improve / preserve</span><textarea maxLength={1000} value={goals.notes} placeholder="Lower latency without reducing image quality or security…" onChange={event => onGoals({ ...goals, notes: event.target.value })}/></label>
        <details className="wide"><summary>Measurement options / refresh running games</summary><button className="secondary" onClick={() => void refresh()}>Refresh running workloads</button><label><span id="diagnosis-duration">Seconds per trace</span><select aria-labelledby="diagnosis-duration" value={duration} onChange={event => setDuration(Number(event.target.value))}><option value="30">30</option><option value="60">60</option><option value="180">180</option></select></label></details>
      </fieldset>
      {children}
      <SupportingFiles files={attachments} onFiles={setAttachments} onReady={setSupportReady} onVision={setImagesConfirmed} disabled={blocked || loading}/>
      {error && <p role="alert" className="error-text">{error}</p>}
      <p className="muted-copy">Clicking consents to sending sanitized scan/measurement evidence and permitted read-only follow-ups to the configured AI provider. Provider/optional cloud API usage may consume credits. No firmware tool, model download or system change is started.</p>
      <button className="primary" disabled={blocked || loading || !supportReady || Boolean(selected && !target)} onClick={() => onStart(target, duration, attachments, imagesConfirmed)}>Diagnosi completa</button>
      {blocked && <p className="muted-copy">Configure a model, or finish/dismiss the existing plan or recovery before starting another diagnosis.</p>}
    </section>
  </div>;
}
