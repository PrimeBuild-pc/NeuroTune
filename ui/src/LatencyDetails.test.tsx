import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { DriverDetails, LatencyDetails } from './LatencyDetails';
import type { TraceReport } from './types';

const report: TraceReport = {
  sessionId: 'test', generatedAtUtc: '', targetExecutable: 'System-wide',
  quality: { durationMilliseconds: 1000, etlBytes: 1, eventsLost: 0, missingProviders: [], targetPresencePercent: 0, isValid: true },
  interrupts: [], processors: [{ logicalProcessor: 0, interruptSharePercent: 0, targetRunningMilliseconds: 0, readyOverlapMicroseconds: 0 }],
  threads: [], observations: [],
};

describe('latency measurement coverage', () => {
  it('shows current driver metadata separately and does not invent absent device associations', () => {
    const html = renderToStaticMarkup(<DriverDetails result={{ module: 'dxgkrnl.sys', inspectedAtUtc: '2026-09-07T00:00:00Z',
      limitation: 'Not causality.', services: [{ name: 'dxgkrnl', displayName: '<driver>', state: 'Running', startMode: 'Boot',
        fileVersion: '1.2', company: 'Microsoft', devices: [] }] }}/>);
    expect(html).toContain('current Windows inventory');
    expect(html).toContain('&lt;driver&gt;');
    expect(html).toContain('shared/framework users may be unlisted');
    expect(html).toContain('Not causality.');
  });
  it('distinguishes legacy missing evidence from an observed zero', () => {
    const legacy = renderToStaticMarkup(<LatencyDetails report={report}/>);
    expect(legacy).toContain('Unavailable in this legacy capture');
    expect(legacy).toContain('Scheduled busy Unavailable');
    expect(legacy).not.toContain('No hard pagefaults observed');
    expect(renderToStaticMarkup(<LatencyDetails report={report} hardFaultsEnabled/>)).toContain('No hard pagefaults observed');
  });

  it('escapes process/module text and exposes pagination for complete driver aggregates', () => {
    const distribution = { count: 1, eventsPerSecond: 1, totalMicroseconds: 100, p50Microseconds: 100, p95Microseconds: 100, p99Microseconds: 100, maxMicroseconds: 100 };
    const html = renderToStaticMarkup(<LatencyDetails hardFaultsEnabled report={{ ...report,
      interrupts: Array.from({ length: 21 }, (_, index) => ({ kind: 'dpc', module: `driver-${index}.sys`, logicalProcessor: 0, distribution })),
      hardFaults: [{ processKey: 'process-1', processName: '<script>example</script>', resolution: distribution }],
    }}/>);
    expect(html).toContain('Page 1 / 2');
    expect(html).toContain('driver-19.sys');
    expect(html).not.toContain('driver-20.sys');
    expect(html).toContain('&lt;script&gt;example&lt;/script&gt;');
    expect(html).not.toContain('<script>');
  });
});
