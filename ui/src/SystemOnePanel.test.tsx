import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { SystemOneNotes, SystemOnePanel } from './SystemOnePanel';
import { preparatoryBaselines } from './plan';
import type { MeasurementSession } from './types';

describe('optional local assistant and measure-first preparation', () => {
  it('does not start/download anything in the initial UI and labels scores as advisory', () => {
    const panel = renderToStaticMarkup(<SystemOnePanel/>);
    expect(panel).toContain('Not installed');
    expect(panel).not.toContain('checked=""');
    expect(panel).toContain('No resident server or automatic startup download');
    const note = renderToStaticMarkup(<SystemOneNotes items={[{ phase: 'analysis', domain: 'drivers', status: 'ok', score: 0.8, seconds: 2.1,
      detail: '<script>not executable</script>', evidenceIds: [] }]}/>);
    expect(note).toContain('uncalibrated option score 80%');
    expect(note).toContain('&lt;script&gt;');
    expect(note).toContain('No action, risk rating, user goal or measured result was changed');
  });
  it('selects only matching completed workload baselines, never system-wide, mixed hardware or invalid traces', () => {
    const make = (id: string, changes: Partial<MeasurementSession> = {}) => ({ id, label: 'baseline', state: 'completed', processName: 'Game.exe',
      durationSeconds: 180, createdAtUtc: '2026-10-01T00:00:00Z', hardwareFingerprint: 'hardware', configurationFingerprint: 'config',
      systemWide: false, report: { schemaVersion: 2, quality: { isValid: true } }, ...changes }) as MeasurementSession;
    const sessions = [make('1'), make('2'), make('3'), make('invalid', { state: 'failed' }), make('system', { systemWide: true }),
      make('different', { hardwareFingerprint: 'other' }), make('candidate', { label: 'candidate' })];
    const chosen = preparatoryBaselines(sessions, new Set(sessions.map(item => item.id)));
    expect(chosen.map(item => item.id)).toEqual(['1', '2', '3']);
    expect(preparatoryBaselines(sessions, new Set())).toEqual([]);
  });
});
