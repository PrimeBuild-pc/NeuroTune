import { describe, expect, it } from 'vitest';
import { comparisonReportHtml } from './report';
import type { MeasurementComparison, MeasurementSession } from './types';

describe('before/after report', () => {
  it('escapes every user and evidence value and loads nothing', () => {
    const session = (id: string, label: 'baseline' | 'candidate') => ({ id, label, state: 'completed', processName: '<img src=x onerror=alert(1)>.exe',
      conditions: 'Scene "A" & <b>bold</b>', durationSeconds: 180, createdAtUtc: '2026-10-12T10:00:00Z' }) as unknown as MeasurementSession;
    const comparison: MeasurementComparison = { id: 'c1', level: 'repeated', baselineSessionIds: ['b'], candidateSessionIds: ['a'], rejectionReasons: [],
      metrics: [{ evidenceId: 'comparison:x:frames:average_fps:median_delta_percent', baselineMedian: 100, candidateMedian: 110, deltaPercent: 10, outcome: 'improvement' }],
      recommendation: 'keep', recommendationReason: 'Better <script>alert(1)</script>' };
    const html = comparisonReportHtml(comparison, [session('b', 'baseline'), session('a', 'candidate')], ['Disable <Wi-Fi>']);
    expect(html).not.toMatch(/<script|<img|<b>|onerror=alert\(1\)>/);
    expect(html).toContain('&#60;script&#62;');
    expect(html).toContain("default-src 'none'");
    expect(html).toContain('100.00'); expect(html).toContain('110.00'); expect(html).toContain('10.0%');
    expect(html).toContain('Average FPS'); expect(html).toContain('Disable &#60;Wi-Fi&#62;');
  });
});
