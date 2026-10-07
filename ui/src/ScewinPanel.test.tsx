import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { ScewinSettings } from './ScewinPanel';

describe('SCEWIN read-only observations', () => {
  it('escapes labels, exposes unknown values without defaults, and paginates the entire export', () => {
    const html = renderToStaticMarkup(<ScewinSettings report={{ source: 'Imported — not verified', readAtUtc: '2026-09-30T00:00:00Z',
      exportSha256: 'a'.repeat(64), sensitiveQuestionsOmitted: 2, toolFiles: [], notes: ['No tool executed.'],
      settings: Array.from({ length: 51 }, (_, index) => ({ question: index ? `Question ${index}` : '<script>label</script>', token: String(index), currentValue: null, status: 'Unknown — no marked current value' })),
    }}/>);
    expect(html).toContain('51 questions');
    expect(html).toContain('Page 1 / 2');
    expect(html).toContain('&lt;script&gt;label&lt;/script&gt;');
    expect(html).not.toContain('<script>');
    expect(html).toContain('2 sensitive questions omitted');
    expect(html).toContain('No setting can be edited or applied here');
    expect(html).not.toContain('Question 50');
    expect(html).toContain('Unknown — no marked current value');
    expect(html).toContain('Reported current values: 0/51');
  });
  it('counts only reported values and labels an explicit simulated fixture, never as native BIOS observations', () => {
    const html = renderToStaticMarkup(<ScewinSettings report={{ source: 'SIMULATED MSI TEST · not live BIOS data', readAtUtc: '2026-10-07T00:00:00Z', exportSha256: 'b'.repeat(64), sensitiveQuestionsOmitted: 0, toolFiles: [], notes: ['Synthetic test values, no driver executed.'], settings: [
      { question: 'Precision Boost Overdrive', token: '1', currentValue: '[01]Disabled', status: 'Reported by export' },
      { question: 'Global C-state Control', token: '2', currentValue: null, status: 'Unknown — no marked current value' },
    ] }}/>);
    expect(html).toContain('SIMULATED MSI TEST'); expect(html).toContain('Reported current values: 1/2');
    expect(html).toContain('Missing or conflicting values remain unknown'); expect(html).toContain('No setting can be edited or applied here');
    expect(html).not.toContain('BIOS Default'); expect(html).not.toContain('Install PawnIO');
  });
});
