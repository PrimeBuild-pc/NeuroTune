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
  });
});
