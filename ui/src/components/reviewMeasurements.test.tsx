import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { ProviderPage } from './ProviderPage';
import { AuditCoverageView, ReviewPage } from './ReviewPage';
import { MeasurementHistory } from './MeasurementHistory';
import type { Diagnosis, MeasurementSession, Recommendation } from '../types';

const noop = () => {};
const proposal: Recommendation = { id: 'script', kind: 'scriptArtifact', title: 'Inspect configuration', actionId: '', resourceId: '', updateId: '', evidenceIds: ['local:fact'], reason: 'Review, not execution', risk: 'high', expectedImpact: 'Unknown', tradeoffs: [], prerequisites: [], requiresRestart: false, sourceReferences: [], scriptLanguage: 'PowerShell', script: 'Write-Output "review only"', reviewWarnings: ['No executor; unverified'] };

describe('Provider, review and measurement presentation preserves gates', () => {
  it('disables endpoint, key, model, budget and provider switching during an operation', () => {
    const html = renderToStaticMarkup(<ProviderPage provider={{ provider: 'custom', providerName: 'Private model', baseUrl: 'https://example.test/v1', protocol: 'openAiCompatible', model: 'test', requiresApiKey: true }} apiKey="" hasCredential={false} models={[]} modelLabels={{}} chatGptAccounts={[]} authBusy onSignOut={noop} onChoose={noop} onChange={noop} onKey={noop} onSave={noop} onLoadModels={noop} onBrowserSignIn={noop} onModel={noop}/>);
    expect(html.match(/<fieldset[^>]*disabled=""/g)).toHaveLength(3);
    const buttons = html.match(/<button\b[^>]*>/g) ?? [];
    expect(buttons).toHaveLength(12);
    for (const button of buttons) expect(button).toContain('disabled=""');
    expect(html).not.toContain('<details');
    expect(html).toContain('Provider operation in progress');
    expect(html).toContain('type="password"');
    expect(html).toContain('No automatic fallback');
  });
  it('keeps script text, uncertainty, warnings and inert download outside blocked apply', () => {
    const html = renderToStaticMarkup(<ReviewPage diagnosis={{ summary: 'Read-only', findings: [], conflicts: [], recommendations: [proposal], consentQuestion: 'Review first' }} actions={[]} recommendations={new Map()} selected={new Set(['not-executable'])} riskProfile="safe" canApply={false} onToggle={noop} onPreset={noop} onApply={noop}/>);
    expect(html).toContain('<dt>Review-only proposals</dt><dd>1</dd>');
    expect(html).toContain('Uncertainty');
    expect(html).toContain('establish a reversal procedure');
    expect(html).toContain('No executor; unverified');
    expect(html).toContain('Write-Output');
    expect(html).toContain('Save as .txt');
    expect(html).toContain('Evidence &amp; sources');
    expect(html).toMatch(/<button class="primary" disabled="">/);
    expect(html).not.toContain('class="action-card');
  });
  it('shows a failed investigation and a specific apply gate without a success badge', () => {
    const html = renderToStaticMarkup(<ReviewPage investigationFailed applyBlockedReason="A matching workload baseline is missing." diagnosis={{ summary: 'Provider unavailable', findings: [], conflicts: [], recommendations: [], consentQuestion: 'Retry or finish' }} actions={[]} recommendations={new Map()} selected={new Set()} riskProfile="safe" canApply={false} onToggle={noop} onPreset={noop} onApply={noop}/>);
    expect(html).toContain('AI investigation unavailable');
    expect(html).toContain('Not completed');
    expect(html).toContain('A matching workload baseline is missing.');
    expect(html).not.toContain('status-pill good');
    expect(html).toContain('href="#review-decision"');
  });
  it('audit review never exposes executable action selection even if UI props claim apply is available', () => {
    const html = renderToStaticMarkup(<ReviewPage auditOnly auditEvidence={{ 'audit:privacy.example': 'Not configured', 'audit:security.defender': 'Unavailable' }} diagnosis={{ summary: 'Audit', findings: [], conflicts: [], recommendations: [], consentQuestion: 'Review report' }} actions={[]} recommendations={new Map()} selected={new Set(['not-executable'])} riskProfile="safe" canApply onToggle={noop} onPreset={noop} onApply={noop}/>);
    expect(html).not.toContain('Back up &amp; apply selected'); expect(html).not.toContain('Select registered actions');
    expect(html).toContain('Privacy · preferences and policies'); expect(html).toContain('Security · protections and scanner summary');
    expect(html).toContain('Not configured'); expect(html).toContain('Unavailable'); expect(html).toContain('does not certify a clean PC');
  });
  it('exposes limited audit coverage, native sources, omissions and legacy uncertainty without a clean-PC badge', () => {
    const diagnosis: Diagnosis = { summary: 'AI interpretation', findings: [], recommendations: [], conflicts: [], consentQuestion: 'Review', auditCoverageComplete: false, auditCoverage: [
      { checkId: 'privacy', area: 'privacy', label: 'Privacy policies', status: 'partial', evidenceIds: ['audit:privacy.setting'], assessment: 'Effective policy unknown' },
      { checkId: 'antivirus-scan', area: 'detections', label: 'Scansione antivirus', status: 'notChecked', evidenceIds: [], assessment: '<script>Not executed</script>' },
    ] };
    const html = renderToStaticMarkup(<AuditCoverageView diagnosis={diagnosis}/>);
    expect(html).toContain('Limited coverage'); expect(html).toContain('Partial'); expect(html).toContain('Not checked');
    expect(html).toContain('Protections'); expect(html).toContain('Persistence'); expect(html).toContain('General Windows health');
    expect(html).toContain('audit:privacy.setting'); expect(html).toContain('&lt;script&gt;'); expect(html).not.toContain('<script>');
    expect(html).toContain('does not mean a secure system'); expect(html).not.toContain('status-pill good');
    const legacy = renderToStaticMarkup(<AuditCoverageView diagnosis={{ ...diagnosis, auditCoverage: undefined }}/>);
    expect(legacy).toContain('without a verified checklist'); expect(legacy).toContain('0 / 15');
  });
  it('separates comparison from AI evidence and never treats failed or unanalyzed traces as valid', () => {
    const sessions = [
      { id: 'failed', state: 'failed', label: 'baseline', error: 'Missing ETL' },
      { id: 'captured', state: 'captured', label: 'candidate' },
      { id: 'system', state: 'completed', label: 'baseline', systemWide: true },
    ].map(item => ({ processName: 'game.exe', processId: 1, processStartTimeUtc: '2026-01-01T00:00:00Z', createdAtUtc: '2026-01-01T00:00:00Z', durationSeconds: 180, keepRawTrace: false, ...item })) as MeasurementSession[];
    const html = renderToStaticMarkup(<MeasurementHistory sessions={sessions} compareIds={new Set(['system'])} evidenceIds={new Set()} busy={false} loading={false} onFocus={noop} onCompareToggle={noop} onEvidenceToggle={noop} onAnalyze={noop} onDelete={noop} onCompare={noop}/>);
    expect(html).toContain('Missing ETL');
    expect(html).toContain('<strong>1</strong> baseline selected');
    expect(html).toContain('<strong>0</strong> candidate selected');
    expect(html.match(/disabled=""[^>]*>Use in AI/g)).toHaveLength(3);
    expect(html).not.toContain('Quality gate passed');
    expect(html).not.toContain('status-pill good');
    expect(html).toMatch(/disabled=""[^>]*><svg[^]*?Compare selected/);
    const empty = renderToStaticMarkup(<MeasurementHistory sessions={[]} compareIds={new Set()} evidenceIds={new Set()} busy={false} loading onFocus={noop} onCompareToggle={noop} onEvidenceToggle={noop} onAnalyze={noop} onDelete={noop} onCompare={noop}/>);
    expect(empty).toContain('Loading local session history');
    expect(empty).not.toContain('No measurement has been captured');
  });
});
