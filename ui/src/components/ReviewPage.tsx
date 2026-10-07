import { useState } from 'react';
import { Bot, Check, Copy, Download, FileText, Printer, ShieldCheck } from 'lucide-react';
import { planKindLabel, scriptArtifactFilename } from '../plan';
import { t } from '../i18n';
import type { ConflictPattern, Diagnosis, OptimizationAction, Recommendation, RiskProfile, SupportingAttachmentInfo } from '../types';
import { DiagnosisView } from './DiagnosisView';
import { EmptyState } from './EmptyState';
import './ReviewPage.css';

export function ReviewPage({ diagnosis, supporting = [], actions, recommendations, selected, riskProfile, canApply, onToggle, onPreset, onApply, onDismiss, auditOnly = false, auditEvidence = {}, applyBlockedReason, investigationFailed = false, onDiagnosis }: {
  auditOnly?: boolean; auditEvidence?: Record<string, string>; applyBlockedReason?: string; investigationFailed?: boolean; onDiagnosis?: () => void;
  diagnosis?: Diagnosis; supporting?: SupportingAttachmentInfo[]; actions: OptimizationAction[]; recommendations: Map<string, string>;
  selected: Set<string>; riskProfile: RiskProfile; canApply: boolean; onToggle: (id: string) => void;
  onPreset: (mode: RiskProfile | 'none') => void; onApply: () => void; onDismiss?: () => void;
}) {
  const [view, setView] = useState<'recommended' | 'conflicts' | 'all'>('recommended');
  const [showConfigured, setShowConfigured] = useState(false);
  if (!diagnosis) return <EmptyState icon={Bot} title={t('No diagnosis yet')} text={t('Scan the PC, choose your priorities, and ask the configured model for an evidence-backed diagnosis.')} action={onDiagnosis ? t('Complete diagnosis') : undefined} onAction={onDiagnosis}/>;
  const conflictActionIds = new Set(diagnosis.conflicts.flatMap(conflict => conflict.suggestedActionIds));
  const visible = auditOnly ? [] : actions.filter(action => (showConfigured || !action.availability.alreadyApplied) &&
    (view === 'all' || (view === 'recommended' ? recommendations.has(action.id) : conflictActionIds.has(action.id))));
  const selectedHighRisk = actions.filter(action => selected.has(action.id) && action.risk === 'high').length;
  const executable = diagnosis.recommendations.filter(item => item.kind === 'executableAction').length;
  return <div className="stack-lg report-root">
    <div className="page-actions"><div><span className="eyebrow">{t('Contextual plan')}</span><h2>{t('Evidence guides the plan; you decide')}</h2></div><button className="secondary" onClick={() => window.print()}><Printer size={16} aria-hidden="true"/>{t('Print report')}</button></div>
    <dl className="review-ledger"><div><dt>{t('Total proposals')}</dt><dd>{diagnosis.recommendations.length}</dd></div><div><dt>{t('Executable proposals')}</dt><dd>{executable}</dd></div><div><dt>{t('Review-only proposals')}</dt><dd>{diagnosis.recommendations.length - executable}</dd></div></dl>
    <a className="review-decision-link" href="#review-decision">{t('Review decision')}<span aria-hidden="true"> →</span></a>
    {(auditOnly || diagnosis.auditCoverage?.length) && <AuditCoverageView diagnosis={diagnosis}/>}
    <section className="section-card report-summary"><div className="section-heading"><h3>{investigationFailed ? t('AI investigation unavailable') : t('AI diagnosis')}</h3><span className={`status-pill${investigationFailed ? '' : ' good'}`}>{investigationFailed ? t('Not completed') : t('Diagnosis was read-only')}</span></div><DiagnosisView diagnosis={diagnosis}/></section>
    {supporting.length > 0 && <details className="review-support"><summary>{t('Supporting attachments · user-provided, unverified ({count})', { count: supporting.length })}</summary><p>{t('Reports do not replace local benchmarks. Screenshot pixels are not retained in history; SHA-256 identifies only the prepared content.')}</p><ul>{supporting.map(item => <li key={item.id}><strong>{item.name}</strong> · {t(item.kind)} · <code>{item.sha256}</code></li>)}</ul></details>}
    {auditOnly && <section className="section-card"><h3>{t('Privacy and security audit · no changes')}</h3><p>{t('Observed settings do not prove actual traffic. Defender detections are distinct from suspicious metadata; an absence of detections does not certify a clean PC. Scanning and remediation require separate consent.')}</p></section>}
    {auditOnly && <section className="section-card"><h3>{t('Local observations · separate from AI interpretation')}</h3>{(['privacy', 'security'] as const).map(domain => <details key={domain}><summary>{domain === 'privacy' ? t('Privacy · preferences and policies, not measured traffic') : t('Security · protections and scanner summary, not a clean-PC certificate')}</summary><dl className="firmware-facts">{Object.entries(auditEvidence).filter(([id]) => id.startsWith(`audit:${domain}.`)).map(([id, value]) => <div key={id}><dt>{id.slice('audit:'.length)}</dt><dd>{value}</dd></div>)}</dl></details>)}</section>}
    {diagnosis.recommendations.some(unresolvedExecutor) && <section className="section-card" role="status"><h3>{t('Some proposals have no validated action link')}</h3><p>{t('The AI supplied a missing or unknown action reference. Those proposals stay review-only, even if a similarly named capability exists. NeuroTune never guesses an executor or grants permission from a title.')}</p></section>}
    <PlanItemReview recommendations={diagnosis.recommendations}/>
    {diagnosis.conflicts.length > 0 && <details className="review-local"><summary>{t('Local hypotheses · not AI conclusions')}</summary><ConflictView conflicts={diagnosis.conflicts}/></details>}
    {!auditOnly && <section className="review-selection" aria-label={t('Executable action selection')}>
      <div className="section-heading"><div><h3>{t('Select registered actions')}</h3><p>{t('Presets change your selection, not the machine. Availability and backup gates still apply.')}</p></div></div>
      <div className="review-toolbar"><div className="button-row" role="group" aria-label={t('Risk selection preset')}>{(['safe', 'balanced', 'aggressive'] as const).map(mode => <button key={mode} aria-pressed={riskProfile === mode} className={riskProfile === mode ? 'ghost active' : 'ghost'} onClick={() => onPreset(mode)}>{t(mode[0].toUpperCase() + mode.slice(1))}</button>)}</div><div className="button-row"><button className="ghost" onClick={() => onPreset('aggressive')}>{t('Select all recommended')}</button><button className="ghost" onClick={() => onPreset('none')}>{t('Select none')}</button></div></div>
      <div className="plan-tabs" role="group" aria-label={t('Action visibility')}>
        <button aria-pressed={view === 'recommended'} className={view === 'recommended' ? 'active' : ''} onClick={() => setView('recommended')}>{t('AI recommended ({count})', { count: recommendations.size })}</button>
        <button aria-pressed={view === 'conflicts'} className={view === 'conflicts' ? 'active' : ''} onClick={() => setView('conflicts')}>{t('Local heuristic candidates ({count})', { count: conflictActionIds.size })}</button>
        <button aria-pressed={view === 'all'} className={view === 'all' ? 'active' : ''} onClick={() => setView('all')}>{t('Capability catalog ({count})', { count: actions.length })}</button>
      </div>
      {view === 'all' && <p className="muted-copy">{t('This is the capability catalog, not a personalized recommendation. Ready means the registered operation is available, not that it will improve performance or that hardware support is certified.')}</p>}
      <label className="consent-toggle"><input type="checkbox" checked={showConfigured} onChange={event => setShowConfigured(event.target.checked)}/><span><strong>{t('Show already-configured actions')}</strong><small>{t('These actions would not change their inspected target. Already configured does not mean NeuroTune applied them or that the PC is optimal.')}</small></span></label>
    </section>}
    {visible.length > 0 ? <div className="action-list">{visible.map(action => {
      const related = diagnosis.conflicts.filter(conflict => conflict.suggestedActionIds.includes(action.id)).map(conflict => conflict.title);
      const reason = recommendations.get(action.id) ?? (related.join(' · ') || t('Registered reversible capability; not selected by this diagnosis.'));
      return <button key={action.id} aria-pressed={selected.has(action.id)} className={`action-card ${selected.has(action.id) ? 'selected' : ''} ${!action.availability.canApply ? 'disabled' : ''}`} disabled={!action.availability.canApply} onClick={() => onToggle(action.id)}>
        <span className="check-box" aria-hidden="true">{selected.has(action.id) && <Check size={15}/>}</span>
        <div className="action-main"><div><strong>{action.name}</strong>{action.requiresRestart && <span className="tag">{t('Restart')}</span>}</div><p>{action.description}</p><small>{reason}</small></div>
        <div className="action-meta"><span className={`risk ${action.risk}`}>{t(`${action.risk} risk`)}</span><strong>{['Ready', 'Already configured'].includes(action.availability.status) ? t(action.availability.status) : action.availability.status}</strong><small>{t('Current: {value}', { value: action.availability.currentValue })}</small></div>
      </button>;
    })}</div> : <section className="section-card no-fixes"><ShieldCheck size={22} aria-hidden="true"/><div><strong>{t('No executable capability in this view.')}</strong><p>{t('Manual guidance and scripts remain visible above but cannot enter the apply transaction.')}</p></div></section>}
    {selectedHighRisk > 0 && <div className="high-risk-warning" role="alert"><ShieldCheck size={19} aria-hidden="true"/><span><strong>{t('{count} high-risk action(s) selected', { count: selectedHighRisk })}</strong><small>{t('They remain selectable, but require an additional explicit confirmation before backup and execution.')}</small></span></div>}
    <section className="consent-card"><Bot size={20} aria-hidden="true"/><div><span className="eyebrow">{t('Model request')}</span><strong>{diagnosis.consentQuestion}</strong><small>{auditOnly ? t('Audit-only is enforced locally. No optimization, antivirus scan or removal is authorized by this report.') : t('Only selected registered actions enter the verified backup/apply/rollback transaction.')}</small></div></section>
    <div className="sticky-apply" id="review-decision" tabIndex={-1} aria-label={t('Review decision')}><div><strong>{auditOnly ? t('Advisory audit') : t('{count} changes selected', { count: selected.size })}</strong><span>{canApply && !auditOnly ? t('A verified restore point and Registry exports are mandatory.') : t(applyBlockedReason ?? 'Apply unavailable: diagnostic-only, closed, or awaiting a valid workload baseline.')}</span></div><div className="button-row">{onDismiss && <button className="secondary" onClick={onDismiss}>{t('Finish without changes')}</button>}{!auditOnly && <button className="primary" disabled={!selected.size || !canApply} onClick={onApply}><ShieldCheck size={17} aria-hidden="true"/>{t('Back up & apply selected')}</button>}</div></div>
  </div>;
}

export function AuditCoverageView({ diagnosis }: { diagnosis: Diagnosis }) {
  const rows = diagnosis.auditCoverage ?? [];
  const status = { reviewed: 'Evidence reviewed', partial: 'Partial', unavailable: 'Unavailable', notChecked: 'Not checked', consentDenied: 'Consent denied' };
  const areas = { privacy: 'Privacy', protections: 'Protections', detections: 'Detections and scanning', persistence: 'Persistence', health: 'General Windows health' };
  // Native checklist labels are Italian; canonical IDs provide local UI labels.
  const checks: Record<string, string> = {
    privacy: 'Privacy · preferences and policies',
    antivirus: 'Antivirus · mode, signatures and protections',
    firewall: 'Firewall · policies and effective-state limitations',
    smartscreen: 'SmartScreen · preferences and policies',
    updates: 'Updates · policies, not proof of installed patches',
    'boot-isolation': 'Secure Boot and isolation · Windows metadata',
    detections: 'Detections · scanner summary',
    'antivirus-scan': 'Antivirus scan · separate consent and coverage',
    startup: 'Startup · inventory',
    'scheduled-tasks': 'Scheduled tasks · inventory',
    services: 'Services · state and startup',
    events: 'Windows events · recent errors and warnings',
    storage: 'Storage · health and space, not a surface test',
    devices: 'Devices · state and error codes',
    memory: 'Memory · pressure and paging',
  };
  return <section className="section-card audit-coverage" aria-label={t('System check coverage')}>
    <div className="section-heading"><h3>{t('Investigation coverage')}</h3><span className="status-pill">{diagnosis.auditCoverageComplete === true ? t('Evidence checklist reviewed') : t('Limited coverage')}</span></div>
    <p>{t('{reviewed} / {total} checks with evidence reviewed. This does not mean a secure system, a completed antivirus scan or a full forensic examination. Metadata does not prove the effective state.', { reviewed: rows.filter(row => row.status === 'reviewed').length, total: rows.length || 15 })}</p>
    {!rows.length && <p role="status">{t('Report without a verified checklist: no complete coverage is attributed to history or an incomplete result.')}</p>}
    {Object.entries(areas).map(([area, label]) => <details key={area} open><summary>{t(label)}</summary><dl className="firmware-facts">{rows.filter(row => row.area === area).map(row => <div key={row.checkId}><dt>{checks[row.checkId] ? t(checks[row.checkId]) : row.checkId}<br/><span className="muted-copy">{t(status[row.status] ?? 'Not checked')}</span></dt><dd>{auditAssessment(row.assessment)}{row.evidenceIds.length > 0 && <details><summary>{t('Native evidence')}</summary><div className="conflict-evidence">{row.evidenceIds.map(id => <code key={id}>{id}</code>)}</div></details>}</dd></div>)}</dl></details>)}
  </section>;
}

function auditAssessment(value: string) {
  const fixed = [
    'The AI did not assess this required check. No verification is claimed.',
    'No consented scanner operation is linked to this audit. Status/history is not proof of a scan performed for this report. Use the separate Windows scanner controls and review Windows Security.',
    'No application-owned consent denial was recorded for this check; the model cannot invent user consent or refusal.',
    'No provided native evidence supports this check; unrelated metadata or imported reports cannot verify it.',
  ];
  if (fixed.includes(value)) return t(value);
  const prefix = 'Native evidence contains unknown/unavailable/empty/truncated or uncited relevant fields.';
  return value.startsWith(`${prefix} `) ? `${t(prefix)} ${value.slice(prefix.length + 1)}` : value;
}

function sourceGrade(value: string) {
  return ['Official', 'Reproducible', 'Corroborated', 'Anecdotal', 'Unrated', 'Unverified'].includes(value) ? t(value) : value;
}

function unresolvedExecutor(item: Recommendation) {
  return Boolean(item.executionIssue || item.reviewWarnings.some(warning => warning.startsWith('No registered executor exists.')));
}

function PlanItemReview({ recommendations }: { recommendations: Recommendation[] }) {
  const [copiedId, setCopiedId] = useState('');
  const [copyError, setCopyError] = useState('');
  const reviewOnly = [...recommendations].sort((a, b) => ({ low: 0, medium: 1, high: 2 }[a.risk] - { low: 0, medium: 1, high: 2 }[b.risk]));
  if (!reviewOnly.length) return null;
  return <section className="plan-item-section"><div className="section-heading"><div><h3>{t('Risk-ordered personalized plan')}</h3><p className="muted-copy">{t('Manual guidance and scripts stay outside the apply transaction.')}</p></div><span className="status-pill">{t('Nothing auto-applied')}</span></div>
    {copyError && <p role="alert" className="error-text">{t(copyError)}</p>}
    <div className="plan-item-list">{reviewOnly.map(item => <article aria-label={t('{kind}: {title}', { kind: t(planKindLabel(item.kind)), title: item.title })} key={item.id} className={`plan-item ${item.kind}`}>
      <div className="plan-item-heading"><FileText size={18} aria-hidden="true"/><div><span>{t(planKindLabel(item.kind))}</span><strong>{item.title}</strong></div><span className={`risk ${unresolvedExecutor(item) ? 'medium' : item.risk}`}>{unresolvedExecutor(item) ? t('Unverified execution') : t(`${item.risk} risk`)}</span></div>
      <p>{item.reason}</p>
      <dl className="proposal-facts">
        {item.expectedImpact && <div><dt>{t('Expected impact')}</dt><dd>{item.expectedImpact}</dd></div>}
        <div><dt>{t('Uncertainty')}</dt><dd>{item.uncertainty || t('Not specified by the model; benefit must be verified, not assumed.')}</dd></div>
        <div><dt>{t('Reversibility')}</dt><dd>{item.reversibility || (item.kind === 'executableAction' ? t('Registered backup/rollback executor; verification remains mandatory.') : t('Unverified manual proposal; establish a reversal procedure before acting.'))}</dd></div>
      </dl>
      {item.prerequisites.length > 0 && <small><strong>{t('Prerequisites:')}</strong> {item.prerequisites.join(' · ')}</small>}
      {item.tradeoffs.length > 0 && <small><strong>{t('Trade-offs:')}</strong> {item.tradeoffs.join(' · ')}</small>}
      {item.reviewWarnings.length > 0 && <div className="script-warnings" role="alert">{item.reviewWarnings.map(warning => <span key={warning}>{t(warning)}</span>)}</div>}
      {item.kind === 'scriptArtifact' && <><pre className="script-preview" tabIndex={0} aria-label={t('Full {language} script preview', { language: item.scriptLanguage })}>{item.script}</pre><div className="button-row">
        <button className="secondary" aria-label={t('Copy script {title}', { title: item.title })} onClick={async () => { try { await navigator.clipboard.writeText(item.script); setCopiedId(item.id); setCopyError(''); } catch { setCopyError('Could not copy the script. Use the full text preview or save it as .txt.'); } }}><Copy size={15} aria-hidden="true"/>{t('Copy')}</button>
        <button className="secondary" aria-label={t('Save script {title} as an inert text file', { title: item.title })} onClick={() => saveScriptArtifact(item)}><Download size={15} aria-hidden="true"/>{t('Save as .txt')}</button>
      </div>{copiedId === item.id && <span className="sr-only" role="status">{t('Script copied to clipboard.')}</span>}</>}
      {(item.sourceReferences.length > 0 || item.evidenceIds.length > 0) && <details className="proposal-evidence"><summary>{t('Evidence & sources')}</summary>
        {item.sourceReferences.length > 0 && <div className="source-list">{item.sourceReferences.map(source => source.url ? <a key={`${item.id}-${source.url}-${source.title}`} href={source.url} target="_blank" rel="noreferrer">{source.title} · {sourceGrade(source.grade)}</a> : <span key={`${item.id}-${source.title}`}>{source.title} · {sourceGrade(source.grade)}</span>)}</div>}
        <div className="conflict-evidence">{item.evidenceIds.map(id => <code key={id}>{id}</code>)}</div>
      </details>}
    </article>)}</div>
  </section>;
}

function ConflictView({ conflicts }: { conflicts: ConflictPattern[] }) {
  return <section className="section-card conflict-section"><div className="section-heading"><div><span className="eyebrow">{t('Contestable local hypotheses')}</span><h3>{t('{count} heuristic relationships', { count: conflicts.length })}</h3></div><span className="status-pill">{t('Deterministic rules')}</span></div><div className="conflict-list">{conflicts.map(conflict => <article key={conflict.id} className={`conflict-card ${conflict.kind}`}><div className="conflict-title"><div><span>{t(conflict.kind.replace(/([A-Z])/g, ' $1'))}</span><strong>{conflict.title}</strong></div><small>{t('{confidence} heuristic confidence · uncalibrated', { confidence: ['Low', 'Medium', 'High'].includes(conflict.confidence) ? t(conflict.confidence) : conflict.confidence })}</small></div><p>{conflict.explanation}</p><p><strong>{t('Why it may be counterproductive:')}</strong> {conflict.whyCounterproductive}</p><div className="conflict-evidence">{Object.entries(conflict.evidence).map(([id, value]) => <code key={id}>{id} = {value}</code>)}</div><small>{t('Objectives: {objectives}', { objectives: conflict.objectives.map(objective => t(objective)).join(', ') })}</small></article>)}</div></section>;
}

function saveScriptArtifact(item: Recommendation) {
  const blob = new Blob([item.script], { type: 'text/plain;charset=utf-8' });
  const url = URL.createObjectURL(blob); const link = document.createElement('a');
  link.href = url; link.download = scriptArtifactFilename(item.id); link.click(); URL.revokeObjectURL(url);
}
