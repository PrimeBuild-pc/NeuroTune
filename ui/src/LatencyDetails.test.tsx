import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { CaptureCountdown, DriverDetails, FirmwareFacts, LatencyCharts, LatencyDetails, MeasurementBars, MeasurementFeedback } from './LatencyDetails';
import type { TraceReport } from './types';

const report: TraceReport = {
  sessionId: 'test', generatedAtUtc: '', targetExecutable: 'System-wide',
  quality: { durationMilliseconds: 1000, etlBytes: 1, eventsLost: 0, missingProviders: [], targetPresencePercent: 0, isValid: true },
  interrupts: [], processors: [{ logicalProcessor: 0, interruptSharePercent: 0, targetRunningMilliseconds: 0, readyOverlapMicroseconds: 0 }],
  threads: [], observations: [],
};

describe('latency measurement coverage', () => {
  it('charts exact interrupt totals and groups driver maxima across processors without summing maxima', () => {
    const distribution = { count: 2, eventsPerSecond: 2, totalMicroseconds: 3000, p50Microseconds: 50, p95Microseconds: 90, p99Microseconds: 95, maxMicroseconds: 100 };
    const html = renderToStaticMarkup(<LatencyCharts hardFaultsEnabled report={{ ...report,
      processors: [{ ...report.processors[0], dpc: distribution, isr: { ...distribution, totalMicroseconds: 1000 } }],
      interrupts: [0, 1].map(logicalProcessor => ({ kind: 'dpc', module: '<driver>.sys', logicalProcessor,
        distribution: { ...distribution, maxMicroseconds: logicalProcessor ? 200 : 100 } })),
      hardFaults: [{ processKey: 'process-1', processName: '<process>', resolution: distribution }],
    }}/>);
    expect(html).toContain('4.00 ms');
    expect(html).toContain('200.00 µs');
    expect(html).not.toContain('300.00 µs');
    expect(html).toContain('2 events');
    expect(html).toContain('&lt;driver&gt;.sys');
    expect(html).toContain('already included in scheduled busy time');
  });

  it('does not turn absent evidence into zero; observed zero has an empty finite bar', () => {
    expect(renderToStaticMarkup(<LatencyCharts report={report}/>)).toContain('No recorded data available');
    const html = renderToStaticMarkup(<MeasurementBars title="Test" unit="ms" note="Exact values" rows={[
      { label: 'Observed zero', value: 0 }, { label: 'Invalid', value: NaN }, { label: 'Infinite', value: Infinity },
    ]}/>);
    expect(html).toContain('0.00 ms');
    expect(html).toContain('scaleX(0)');
    expect(html).not.toContain('NaN');
    expect(html).not.toContain('Infinite');
  });

  it('shows local-operation feedback and treats recorder startup separately from countdown', () => {
    const html = renderToStaticMarkup(<MeasurementFeedback message="Starting WPR…" startedAt={Date.now()} log={['Reading hardware…']}/>);
    expect(html).toContain('role="status"');
    expect(html).toContain('Running locally, without AI');
    expect(html).toContain('Reading hardware…');
    expect(html).toContain('0 s elapsed');
    expect(renderToStaticMarkup(<CaptureCountdown durationSeconds={30}/>)).toContain('Waiting for recorder');
    expect(renderToStaticMarkup(<CaptureCountdown durationSeconds={30} startedAt={new Date(Date.now() - 31_000).toISOString()}/>)).toContain('Finalizing capture');
  });

  it('separates readable firmware identity from unsupported setup settings and documentation', () => {
    const html = renderToStaticMarkup(<FirmwareFacts result={{ readEnabled: true, writeSupported: false,
      facts: { Motherboard: '<board>', 'DIMM 1': '3200 MT/s', 'Secure Boot': 'Enabled' },
      interfaces: ['MSI_BiosSetting.GetBiosSetting'], settingsStatus: 'No validated item mapping.', guidanceUrl: 'https://www.msi.com/support',
    }}/>);
    expect(html).toContain('Exact BIOS setup values are not available');
    expect(html).toContain('MSI_BiosSetting.GetBiosSetting');
    expect(html).toContain('does not supply your current setup values');
    expect(html).toContain('&lt;board&gt;');
    expect(html).toContain('Memory reported by SMBIOS');
  });

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
