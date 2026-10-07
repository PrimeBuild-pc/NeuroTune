import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { MeasurementCompare } from './MeasurementCompare';
import { SetupGuide } from './SetupGuide';
import type { MeasurementComparison, MeasurementSession } from '../types';

const noop = () => {};
const session = (id: string, label: MeasurementSession['label'], state: MeasurementSession['state'] = 'completed'): MeasurementSession => ({
  id, label, state, processName: 'System-wide', processId: 0, systemWide: true, conditions: 'Idle', analysisPreset: 'systemLatency',
  createdAtUtc: '2026-10-07T00:00:00Z', processStartTimeUtc: '0001-01-01T00:00:00Z', durationSeconds: 30, keepRawTrace: false,
  report: state === 'completed' ? { sessionId: id, generatedAtUtc: '', targetExecutable: 'System-wide',
    quality: { durationMilliseconds: 30000, etlBytes: 1000, eventsLost: 0, missingProviders: [], targetPresencePercent: 0, isValid: true },
    interrupts: [], processors: [], threads: [], observations: [] } : undefined,
});
const props = { busy: false, locked: false, captureBlocked: false, restartPending: false, onSelect: noop, onCapture: noop, onCompare: noop, onAnalyze: noop };

describe('Saved before/after and setup boundaries', () => {
  it('shows saved data on the left and no synthetic result on the right', () => {
    const html = renderToStaticMarkup(<MeasurementCompare {...props} sessions={[session('before', 'baseline')]} baselineIds={['before']} candidateIds={[]}/>);
    expect(html).toContain('Before · saved'); expect(html).toContain('After · new measurement'); expect(html).toContain('No gain is assumed');
    expect(html).toContain('System latency'); expect(html).toContain('Hard pagefault count'); expect(html).toContain('Unavailable');
    expect(html).toMatch(/disabled=""[^>]*>[^]*?Calculate comparison/);
    const locked = renderToStaticMarkup(<MeasurementCompare {...props} locked captureBlocked restartPending sessions={[session('before', 'baseline')]} baselineIds={['before']} candidateIds={[]}/>);
    expect(locked).toContain('saved before result is retained'); expect(locked).toMatch(/disabled=""[^>]*>[^]*?Record after measurement/);
    const pending = renderToStaticMarkup(<MeasurementCompare {...props} sessions={[session('before', 'baseline'), session('after', 'candidate', 'captured')]} baselineIds={['before']} candidateIds={['after']}/>);
    expect(pending).toContain('must be analyzed'); expect(pending).toContain('Analyze');
  });
  it('labels idle deltas as observations without improvement colors or a Keep verdict', () => {
    const comparison: MeasurementComparison = { id: 'comparison', diagnosticOnly: true, level: 'exploratory', baselineSessionIds: ['before'], candidateSessionIds: ['after'], metrics: [{ evidenceId: 'comparison:test:interrupt:system:worst_module_p99_us:median_delta_percent', baselineMedian: 100, candidateMedian: 50, deltaPercent: -50, outcome: 'improvement' }], rejectionReasons: [], recommendation: 'insufficientEvidence', recommendationReason: 'Not a benchmark verdict' };
    const html = renderToStaticMarkup(<MeasurementCompare {...props} sessions={[session('before', 'baseline'), session('after', 'candidate')]} baselineIds={['before']} candidateIds={['after']} comparison={comparison}/>);
    expect(html).toContain('Diagnostic differences'); expect(html).toContain('Lower observed value'); expect(html).toContain('-50.0%');
    expect(html).not.toContain('class="improvement"'); expect(html).not.toContain('Keep candidate');
    const rejected = renderToStaticMarkup(<MeasurementCompare {...props} sessions={[]} baselineIds={[]} candidateIds={[]} comparison={{ ...comparison, rejectionReasons: ['Conditions differ'] }}/>);
    expect(rejected).toContain('Comparison rejected'); expect(rejected).toContain('Conditions differ'); expect(rejected).not.toContain('-50.0%');
  });
  it('provides a semantic setup dialog without consent-by-opening or installation claims', () => {
    const html = renderToStaticMarkup(<SetupGuide open={false} initial={{ telemetry: false, firmware: false }} onFinish={noop} onClose={noop}/>);
    expect(html).toContain('<dialog'); expect(html).toContain('Welcome to NeuroTune'); expect(html).toContain('Nothing is installed');
    expect(html).toContain('Skip without changing permissions'); expect(html).toContain('measurement and backup gates');
    expect(html).not.toContain('checked=""'); expect(html).not.toContain('System One is installed');
  });
});
