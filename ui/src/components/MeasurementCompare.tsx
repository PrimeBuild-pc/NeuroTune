import { Download, GitCompareArrows, Timer } from 'lucide-react';
import { formatDate, t } from '../i18n';
import type { MeasurementComparison, MeasurementSession } from '../types';
import { CaptureCountdown } from '../LatencyDetails';
import './MeasurementCompare.css';
import { analysisPresetsFor } from '../plan';

function saveMeasurementReport(session: MeasurementSession) {
  const url = URL.createObjectURL(new Blob([JSON.stringify(session, null, 2)], { type: 'application/json' }));
  const link = document.createElement('a');
  link.href = url; link.download = `NeuroTune-${session.label}-${session.id}.json`; link.click();
  URL.revokeObjectURL(url);
}

function metricName(id: string) {
  const key = id.replace(/^comparison:[^:]+:/, '').replace(/:median_delta_percent$/, '');
  const labels: Record<string, string> = {
    'interrupt:system:worst_module_p99_us': 'Worst driver interrupt P99',
    'fault:system:count': 'Hard pagefault count',
    'fault:system:worst_process_p99_us': 'Worst hard pagefault P99',
    'target:worst_thread_ready_p99_us': 'Worst target Ready Time P99',
    'target:migrations': 'Target thread migrations',
    'frames:average_fps': 'Average FPS', 'frames:one_percent_low_fps': '1% low',
    'frames:p99_ms': 'Frame-time P99', 'frames:stutter_count': 'Stutters',
  };
  return { name: labels[key] ? t(labels[key]) : key.replace(/^interrupt:/, '').replace(/:worst_core_p99_us$/, ' · P99'),
    unit: key.endsWith('_us') ? 'µs' : key.endsWith('_ms') ? 'ms' : key.endsWith('_fps') ? 'FPS' : '' };
}

export function MeasurementCompare({ sessions, baselineIds, candidateIds, comparison, busy, locked, captureBlocked, restartPending, onSelect, onCapture, onCompare, onAnalyze }: {
  sessions: MeasurementSession[]; baselineIds: string[]; candidateIds: string[]; comparison?: MeasurementComparison;
  busy: boolean; locked: boolean; captureBlocked: boolean; restartPending: boolean;
  onSelect: (side: 'baseline' | 'candidate', id: string) => void;
  onCapture: (baseline: MeasurementSession) => void; onCompare: () => void; onAnalyze: (id: string) => void;
}) {
  const baseline = sessions.find(item => item.id === baselineIds[0]);
  const candidate = sessions.find(item => item.id === candidateIds.at(-1));
  const recording = sessions.find(item => item.state === 'recording');
  const canCompare = baselineIds.length > 0 && candidateIds.length > 0 && [...baselineIds, ...candidateIds].every(id => sessions.some(item => item.id === id && item.state === 'completed')) && (!locked || baselineIds.length >= 3 && candidateIds.length >= 3);
  return <section className="section-card compare-workbench" aria-labelledby="compare-title">
    <div className="section-heading"><div><span className="eyebrow">{t('Before / after')}</span><h3 id="compare-title">{t('Compare saved measurements')}</h3></div><GitCompareArrows size={24} aria-hidden="true"/></div>
    <p className="muted-copy">{t('The left result stays saved across restarts. Record the same idle conditions or repeatable game scene on the right; a lower DPC value alone is not proof of lower input latency.')}</p>
    <div className="compare-sides">{(['baseline', 'candidate'] as const).map(side => {
      const ids = side === 'baseline' ? baselineIds : candidateIds;
      const session = side === 'baseline' ? baseline : candidate;
      const report = session?.report;
      const metrics = comparison && !comparison.rejectionReasons.length ? comparison.metrics : [];
      return <div className={`compare-side ${side}`} key={side}>
        <div className="compare-side-title"><span>{side === 'baseline' ? t('Before · saved') : t('After · new measurement')}</span><strong>{ids.length > 1 ? t('{count} repeats', { count: ids.length }) : session ? t(session.state) : t('Not recorded')}</strong></div>
        <label><span className="sr-only">{side === 'baseline' ? t('Before measurement') : t('After measurement')}</span><select disabled={busy || locked} value={session?.id ?? ids[0] ?? ''} onChange={event => onSelect(side, event.target.value)}><option value="">{t('Choose a saved session')}</option>{sessions.filter(item => item.label === side && item.state === 'completed').map(item => <option key={item.id} value={item.id}>{formatDate(item.createdAtUtc)} · {item.conditions || item.processName} · {item.analysisPreset ? analysisPresetsFor(item.analysisPreset).find(preset => preset.id === item.analysisPreset)?.label : ''}</option>)}</select></label>
        {session && <p className="compare-context">{session.conditions || t('Conditions not recorded')}<br/>{session.systemWide ? t('System-wide diagnostic') : session.processName} · {session.durationSeconds} s{session.analysisPreset && ` · ${analysisPresetsFor(session.analysisPreset).find(preset => preset.id === session.analysisPreset)?.label}`}</p>}
        {session?.state === 'recording' && <CaptureCountdown startedAt={session.recordingStartedAtUtc} durationSeconds={session.durationSeconds}/>}
        {report ? <><span className={`status-pill ${report.quality.isValid ? 'good' : ''}`}>{report.quality.isValid ? t('Quality gate passed') : t('Invalid trace')}</span>
          <dl className="compare-numbers">{metrics.length ? metrics.map(metric => { const { name, unit } = metricName(metric.evidenceId); return <div key={metric.evidenceId}><dt>{name}</dt><dd>{(side === 'baseline' ? metric.baselineMedian : metric.candidateMedian).toFixed(2)} <small>{unit}</small></dd></div>; }) : <>
            <div><dt>{t('Longest DPC')}</dt><dd>{Math.max(0, ...report.interrupts.filter(item => item.kind.toLowerCase() === 'dpc').map(item => item.distribution.maxMicroseconds)).toFixed(2)} <small>µs</small></dd></div>
            <div><dt>{t('Longest ISR')}</dt><dd>{Math.max(0, ...report.interrupts.filter(item => item.kind.toLowerCase() === 'isr').map(item => item.distribution.maxMicroseconds)).toFixed(2)} <small>µs</small></dd></div>
            <div><dt>{t('Events lost')}</dt><dd>{report.quality.eventsLost}</dd></div>
            <div><dt>{t('Hard pagefault count')}</dt><dd>{session.hardFaultsEnabled ? (report.hardFaults ?? []).reduce((total, item) => total + item.resolution.count, 0) : t('Unavailable')}</dd></div>
          </>}</dl><button className="ghost" disabled={busy || Boolean(recording)} onClick={() => { if (window.confirm(t('Save this local report as JSON? Process and driver names may be sensitive. Review it before sharing.'))) saveMeasurementReport(session); }}><Download size={15} aria-hidden="true"/>{t('Save report as JSON')}</button></> : <p className="compare-placeholder">{session ? t('The captured trace must be analyzed before a result exists.') : side === 'baseline' ? t('Choose the saved baseline from diagnosis or capture one below.') : t('No gain is assumed. The new result appears only after capture and analysis.')}</p>}
        {session?.state === 'captured' && <button className="secondary" disabled={busy || Boolean(recording)} onClick={() => onAnalyze(session.id)}>{t('Analyze')}</button>}
        {side === 'candidate' && <button className="primary" disabled={busy || Boolean(recording) || baseline?.state !== 'completed' || !baseline?.report?.quality.isValid || captureBlocked || restartPending || locked && baseline?.systemWide === true} onClick={() => baseline && onCapture(baseline)}><Timer size={16} aria-hidden="true"/>{t('Record after measurement')}</button>}
      </div>;
    })}</div>
    {restartPending && <p className="error-text">{t('Restart Windows and verify the restart before recording the after result. The saved before result is retained.')}</p>}
    {locked && <p className="muted-copy">{t('This comparison belongs to the active optimization run. Three matching before and three matching after reports are required; history selections cannot replace them.')}</p>}
    <div className="button-row"><button className="secondary" disabled={busy || Boolean(recording) || !canCompare || restartPending} onClick={onCompare}><GitCompareArrows size={16} aria-hidden="true"/>{t('Calculate comparison')}</button><small>{t('Results are local observations, not guaranteed performance gains.')}</small></div>
    {comparison && <div className="compare-result" role="status"><strong>{comparison.rejectionReasons.length ? t('Comparison rejected') : comparison.diagnosticOnly ? t('Diagnostic differences · not a benchmark verdict') : comparison.level === 'repeated' ? t('Repeated comparison') : t('Exploratory comparison')}</strong><p>{comparison.recommendationReason}</p>
      {comparison.rejectionReasons.length > 0 ? <ul>{comparison.rejectionReasons.map(reason => <li key={reason}>{reason}</li>)}</ul> : <table><caption className="sr-only">{t('Measured differences')}</caption><thead><tr><th>{t('Metric')}</th><th>{t('Change')}</th><th>{t('Interpretation')}</th></tr></thead><tbody>{comparison.metrics.map(metric => <tr key={metric.evidenceId}><th scope="row">{metricName(metric.evidenceId).name}</th><td>{metric.baselineMedian === 0 && metric.candidateMedian !== 0 ? '—' : `${metric.deltaPercent.toFixed(1)}%`}</td><td className={comparison.diagnosticOnly ? '' : metric.outcome}>{comparison.diagnosticOnly ? metric.candidateMedian < metric.baselineMedian ? t('Lower observed value') : metric.candidateMedian > metric.baselineMedian ? t('Higher observed value') : t('Unchanged') : t(metric.outcome === 'improvement' ? 'Improvement' : metric.outcome === 'regression' ? 'Regression' : 'Inconclusive')}</td></tr>)}</tbody></table>}
    </div>}
  </section>;
}
