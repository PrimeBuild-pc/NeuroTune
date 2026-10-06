import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { DefenderReportView, WindowsSecurityPanel } from './WindowsSecurityPanel';
import type { DefenderReport } from './types';

const unavailable: DefenderReport = { status: 'unavailable', readAtUtc: '2026-01-01T00:00:00Z', scanState: 'unknown', threats: [], truncated: false, limitation: 'Unavailable is not disabled or clean' };

describe('Windows security tools have separate consent and honest evidence', () => {
  it('never turns unavailable/empty/unsigned metadata into a clean-PC claim or an automatic removal', () => {
    const html = renderToStaticMarkup(<DefenderReportView report={unavailable}/>);
    expect(html).toContain('Defender unavailable'); expect(html).toContain('Unknown / unknown');
    expect(html).toContain('does not certify a clean PC'); expect(html).not.toContain('false / false');
    const hostile = renderToStaticMarkup(<DefenderReportView report={{ ...unavailable, status: 'observed', truncated: true, threats: [{ name: '<script>remove-files</script>', isActive: true }] }}/>);
    expect(hostile).toContain('&lt;script&gt;'); expect(hostile).not.toContain('<script>');
    expect(hostile).toContain('other detections may be present'); expect(hostile).toContain('not AI verdicts');
  });
  it('keeps scan/resource, remediation and network permissions unchecked, with no silent external-tool download', () => {
    const html = renderToStaticMarkup(<WindowsSecurityPanel blocked onBusy={() => {}}/>);
    expect(html.match(/type="checkbox"/g)).toHaveLength(3);
    for (const checkbox of html.match(/<input[^>]+type="checkbox"[^>]*>/g) ?? []) expect(checkbox).not.toContain('checked=""');
    expect(html).not.toContain('<details');
    expect(html).toContain('No scan started');
    expect(html).toContain('does not download, install or run them');
    expect(html).toContain('Closing NeuroTune does not guarantee'); expect(html).toContain('no rollback');
    expect(html).toMatch(/<fieldset disabled="">/); expect(html).toContain('Protection history');
  });
});
