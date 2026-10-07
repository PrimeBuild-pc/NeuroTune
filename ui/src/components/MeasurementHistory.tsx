import { useState } from 'react';
import { CircleGauge, Timer, X } from 'lucide-react';
import type { MeasurementSession } from '../types';
import { formatDate, t } from '../i18n';
import './MeasurementHistory.css';
import { analysisPresetsFor } from '../plan';

export function MeasurementHistory({ sessions, focusedId, compareIds, evidenceIds, busy, loading, onFocus, onCompareToggle, onEvidenceToggle, onAnalyze, onDelete, onCompare }: {
  sessions: MeasurementSession[]; focusedId?: string; compareIds: Set<string>; evidenceIds: Set<string>; busy: boolean; loading: boolean;
  onFocus: (id: string) => void; onCompareToggle: (id: string) => void; onEvidenceToggle: (id: string) => void;
  onAnalyze: (id: string) => void; onDelete: (id: string) => void; onCompare: () => void;
}) {
  const [query, setQuery] = useState('');
  const [filter, setFilter] = useState('all');
  const [visibleCount, setVisibleCount] = useState(12);
  const filtered = sessions.filter(session => {
    const matchesFilter = filter === 'all' || session.label === filter || filter === 'needsAnalysis' && ['captured', 'failed'].includes(session.state);
    return matchesFilter && `${session.processName} ${session.processId} ${session.id} ${session.conditions ?? ''} ${session.analysisPreset ? analysisPresetsFor(session.analysisPreset).find(item => item.id === session.analysisPreset)?.label : ''}`.toLowerCase().includes(query.trim().toLowerCase());
  });
  const baselines = sessions.filter(item => compareIds.has(item.id) && item.label === 'baseline').length;
  const candidates = sessions.filter(item => compareIds.has(item.id) && item.label === 'candidate').length;
  return <section className="section-card measurement-history-panel" aria-labelledby="measurement-history-title" aria-busy={loading}>
    <div className="section-heading"><div><span className="eyebrow">{t('2 · History and analysis')}</span><h3 id="measurement-history-title" tabIndex={-1}>{t('{count} measurement sessions', { count: sessions.length })}</h3></div><span className="history-local">{t('Stored on this PC')}</span></div>
    <p className="muted-copy">{t('Select a session to inspect its report. Comparison selection and AI evidence are separate.')}</p>
    <div className="form-grid history-filters">
      <label><span>{t('Search sessions')}</span><input type="search" value={query} onChange={event => { setQuery(event.target.value); setVisibleCount(12); }}/></label>
      <label><span>{t('Show sessions')}</span><select value={filter} onChange={event => { setFilter(event.target.value); setVisibleCount(12); }}><option value="all">{t('All sessions')}</option><option value="baseline">{t('Baseline')}</option><option value="candidate">{t('Candidate')}</option><option value="needsAnalysis">{t('Needs analysis')}</option></select></label>
    </div>
    <p className="history-results" role="status">{t('Showing {shown} of {total} sessions', { shown: Math.min(visibleCount, filtered.length), total: filtered.length })}</p>
    <div className="measurement-history">{filtered.slice(0, visibleCount).map(session => <article key={session.id} className={focusedId === session.id ? 'measurement-row selected' : 'measurement-row'}>
      <label><input aria-label={t('Select {id} for comparison', { id: session.id })} type="checkbox" disabled={busy || loading || session.state !== 'completed'} checked={compareIds.has(session.id)} onChange={() => onCompareToggle(session.id)}/></label>
      <button className="measurement-select" aria-pressed={focusedId === session.id} onClick={() => onFocus(session.id)}><strong>{session.conditions || session.processName || (session.systemWide ? t('Entire system') : t('Target workload'))}</strong>{session.analysisPreset && <small>{analysisPresetsFor(session.analysisPreset).find(item => item.id === session.analysisPreset)?.label}</small>}<small>{formatDate(session.createdAtUtc)} · {t('{seconds}s', { seconds: session.durationSeconds })} · {session.systemWide ? t('System-wide') : `PID ${session.processId}`}</small></button>
      <div className="measurement-state"><span className={`status-pill ${session.state === 'completed' && session.report?.quality.isValid ? 'good' : ''}`}>{t(session.label)} · {t(session.state)}</span>{session.report && <small className={session.report.quality.isValid ? 'quality-valid' : 'quality-invalid'}>{session.report.quality.isValid ? t('Quality gate passed') : t('Invalid trace')}</small>}</div>
      <div className="button-row measurement-row-actions">{session.state === 'captured' || session.state === 'failed' ? <button className="secondary" disabled={busy || loading} onClick={() => onAnalyze(session.id)}>{t('Analyze')}</button> : null}<button aria-pressed={evidenceIds.has(session.id)} className={evidenceIds.has(session.id) ? 'ghost active' : 'ghost'} disabled={busy || loading || session.state !== 'completed' || session.systemWide} onClick={() => onEvidenceToggle(session.id)}>{evidenceIds.has(session.id) ? t('Included in AI') : t('Use in AI')}</button><button className="ghost" aria-label={t('Delete measurement {id}', { id: session.id })} disabled={session.state === 'recording' || busy || loading} onClick={() => onDelete(session.id)}><X size={14} aria-hidden="true"/></button></div>
      {session.error && <p className="measurement-row-error" role="alert">{session.error}</p>}
    </article>)}</div>
    {sessions.length > 0 && !filtered.length && <p className="muted-copy">{t('No sessions match. Change the search or filter.')}</p>}
    {filtered.length > visibleCount && <button className="secondary history-more" onClick={() => setVisibleCount(count => count + 12)}>{t('Show more')}</button>}
    {!sessions.length && <div className="measurement-history-empty"><Timer size={22} aria-hidden="true"/><div><strong>{loading ? t('Loading local session history…') : t('No measurement has been captured yet')}</strong><p>{loading ? t('Waiting for the local agent.') : t('Start a capture above, then analyze the saved trace. No performance gain is assumed.')}</p></div></div>}
    <div className="comparison-actions"><div className="comparison-counts" aria-live="polite"><span><strong>{baselines}</strong> {t('baseline selected')}</span><span><strong>{candidates}</strong> {t('candidate selected')}</span></div><div className="button-row"><button className="secondary" disabled={busy || loading || !baselines || !candidates} onClick={onCompare}><CircleGauge size={16} aria-hidden="true"/>{t('Compare selected')}</button><small>{t('1+1 is exploratory. 3+3 enables repeated aggregation; quality gates still apply.')}</small></div></div>
  </section>;
}
