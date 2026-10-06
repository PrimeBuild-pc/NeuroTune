import { renderToStaticMarkup } from 'react-dom/server';
import { Bot, CircleGauge, Timer } from 'lucide-react';
import { describe, expect, it } from 'vitest';
import { DiagnosisProgressPanel } from './DiagnosisProgressPanel';
import { Navigation } from './Navigation';
import { NeuralVisualizer } from './NeuralVisualizer';
import { Overview } from './Overview';
import { InvestigationModeControl } from '../CompleteDiagnosis';
import { MeasurementHistory } from './MeasurementHistory';
import type { MeasurementSession } from '../types';
import type { DiagnosisStage } from '../diagnosisFlow';

describe('UI polish preserves truthful, accessible states', () => {
  it('ties the core and one active step to each real diagnosis stage without manufactured progress', () => {
    for (const stage of ['scan', 'warmup', 'capture', 'trace', 'ai', 'result'] satisfies DiagnosisStage[]) {
      const html = renderToStaticMarkup(<DiagnosisProgressPanel progress={{ stage, message: `Actual ${stage} operation`, startedAt: Date.now(), log: ['Actual agent event'], repeat: 2, repeats: 3 }} onCancel={() => {}}/>);
      expect(html).toContain(`data-state="${stage}"`);
      expect(html.match(/aria-current="step"/g)).toHaveLength(1);
      expect(html).toContain(`Actual ${stage} operation`);
      expect(html).toContain('Trace 2 / 3');
      expect(html).toContain('Actual agent event');
      expect(html).toContain('Cancel diagnosis');
      expect(html).not.toContain('role="progressbar"');
      if (stage === 'capture') expect(html).toContain('Motion paused · AI inactive');
    }
    expect(renderToStaticMarkup(<NeuralVisualizer/>)).toContain('Local control');
  });
  it('exposes the active advanced destination and labels compact navigation', () => {
    const html = renderToStaticMarkup(<Navigation items={[{ id: 'measurements', label: 'Measurements', icon: Timer }]} page="measurements" onPage={() => {}} disabled={false} quiet/>);
    expect(html).toContain('class="nav-group"');
    expect(html).not.toContain('<details');
    expect(html).toContain('aria-label="Diagnostics"');
    expect(html).toContain('title="Measurements"');
  });
  it('keeps radio groups independent when a retained diagnosis and local evidence coexist', () => {
    const html = renderToStaticMarkup(<><InvestigationModeControl mode="measuredOptimization" onMode={() => {}}/><InvestigationModeControl mode="auditOnly" onMode={() => {}}/></>);
    const names = [...html.matchAll(/type="radio" name="([^"]+)"/g)].map(match => match[1]);
    expect(names).toHaveLength(4);
    expect(names[0]).toBe(names[1]);
    expect(names[2]).toBe(names[3]);
    expect(names[0]).not.toBe(names[2]);
    expect(html).not.toContain('<select');
  });
  it('bounds a long measurement history without losing selected comparison counts', () => {
    const sessions = Array.from({ length: 25 }, (_, index) => ({ id: String(index), label: 'baseline', state: 'completed', createdAtUtc: '2026-01-01T00:00:00Z', durationSeconds: 60, processName: 'game.exe' })) as MeasurementSession[];
    const html = renderToStaticMarkup(<MeasurementHistory sessions={sessions} compareIds={new Set(['24'])} evidenceIds={new Set()} busy={false} loading={false} onFocus={() => {}} onCompareToggle={() => {}} onEvidenceToggle={() => {}} onAnalyze={() => {}} onDelete={() => {}} onCompare={() => {}}/>);
    expect(html.match(/class="measurement-row/g)).toHaveLength(12);
    expect(html).toContain('Show more');
    expect(html).toContain('<strong>1</strong> baseline selected');
  });
  it('offers one clear setup action rather than a duplicate provider shortcut', () => {
    const html = renderToStaticMarkup(<Overview hasProvider={false} history={[]} coreState="idle" onProvider={() => {}} onScan={() => {}}/>);
    expect(html).toContain('Connect a provider');
    expect(html).not.toContain('Provider settings');
  });
  it('keeps navigation labels and disabled/current semantics even with a static quiet indicator', () => {
    const html = renderToStaticMarkup(<Navigation items={[
      { id: 'overview', label: 'Overview', icon: CircleGauge },
      { id: 'provider', label: 'AI provider', icon: Bot },
      { id: 'measurements', label: 'Measurements', icon: Timer },
    ]} page="provider" onPage={() => {}} disabled quiet/>);
    expect(html.match(/aria-current="page"/g)).toHaveLength(1);
    expect(html.match(/nav-active-background/g)).toHaveLength(1);
    expect(html).toContain('aria-label="AI provider"');
    expect(html).toContain('disabled=""');
    expect(html).toContain('Diagnostics');
    expect(html).not.toContain('<summary');
    expect(html).not.toContain('transform:');
  });
});
