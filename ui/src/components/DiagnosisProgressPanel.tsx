import { useEffect, useState } from 'react';
import { CaptureCountdown } from '../LatencyDetails';
import { t } from '../i18n';
import type { DiagnosisProgress } from '../diagnosisFlow';
import { NeuralVisualizer } from './NeuralVisualizer';
import './DiagnosisProgressPanel.css';

const stages = [
  ['scan', 'Local evidence', 'Hardware, Windows, drivers and configuration. No model inference.'],
  ['warmup', 'Prepare workload', 'Time to return to the game or application.'],
  ['capture', 'Measurements', 'Named WPR recordings; both assistants remain inactive.'],
  ['trace', 'Trace analysis', 'Local numerical analysis and quality checks.'],
  ['ai', 'AI investigation', 'The selected model interprets evidence and may request read-only follow-ups.'],
  ['result', 'Your proposal', 'Risk-ordered recommendations; nothing is applied automatically.'],
] as const;

export function DiagnosisProgressPanel({ progress, onCancel }: { progress: DiagnosisProgress; onCancel: () => void }) {
  const [now, setNow] = useState(Date.now());
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, []);
  return <section className="diagnosis-loading" aria-label={t('Complete diagnosis progress')}>
    <div className="diagnosis-stage-header">
      <div className="diagnosis-stage-copy">
        <span className="eyebrow">{t('One investigation · no system writes')}</span>
        <h2>{t('Complete diagnosis in progress')}</h2>
        <span className="diagnosis-elapsed" role="timer" aria-live="off">{t('{seconds} s elapsed', { seconds: Math.max(0, Math.floor((now - progress.startedAt) / 1000)) })}</span>
        <p className="diagnosis-current" role="status">{progress.message}</p>
        {progress.stage === 'warmup' && progress.warmupEndsAt && <p>{t('Capture starts in {seconds} s. Keep the same scene/settings throughout all repeats.', { seconds: Math.max(0, Math.ceil((progress.warmupEndsAt - now) / 1000)) })}</p>}
        {progress.stage === 'capture' && progress.recordingStartedAt && <CaptureCountdown startedAt={progress.recordingStartedAt} durationSeconds={progress.durationSeconds!}/>}
        {progress.repeats && <p className="diagnosis-repeat">{t('Trace {repeat} / {repeats}', { repeat: progress.repeat ?? 0, repeats: progress.repeats })}</p>}
      </div>
      <NeuralVisualizer state={progress.stage}/>
    </div>
    <div className="diagnosis-controls"><button className="secondary" onClick={onCancel}>{t('Cancel diagnosis')}</button></div>
    <ol className="diagnosis-stages">{stages.filter(([id]) => progress.mode !== 'auditOnly' || ['scan', 'ai', 'result'].includes(id)).map(([id, title, detail], index) => <li key={id} aria-current={progress.stage === id ? 'step' : undefined} className={progress.stage === id ? 'current' : ''}>
      <div className="diagnosis-stage-title"><span aria-hidden="true">{String(index + 1).padStart(2, '0')}</span><strong>{t(title)}</strong></div>
      <span>{t(detail)}</span>{progress.stage === id && <small>{t('Active now')}</small>}
    </li>)}</ol>
    <details className="diagnosis-log"><summary>{t('Actual operation log')}</summary><ol>{progress.log.map((line, index) => <li key={index}>{line}</li>)}</ol></details>
    <p className="muted-copy">{t("Cancellation stops read-only AI work and releases only this workflow's recorder. Completed reports remain local. App closure leaves the existing watchdog to finalize any capture.")}</p>
  </section>;
}
