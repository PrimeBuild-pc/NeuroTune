import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const css = readFileSync(new URL('./index.css', import.meta.url), 'utf8');

function tokens(theme: 'light' | 'dark') {
  const block = css.match(new RegExp(`:root\\[data-theme='${theme}'\\] \\{([\\s\\S]*?)\\n\\}`))?.[1] ?? '';
  return Object.fromEntries([...block.matchAll(/--([\w-]+):\s*(#[0-9a-f]{6})/gi)].map(match => [match[1], match[2]]));
}

function luminance(hex: string) {
  const channels = [1, 3, 5].map(index => parseInt(hex.slice(index, index + 2), 16) / 255)
    .map(value => value <= .04045 ? value / 12.92 : ((value + .055) / 1.055) ** 2.4);
  return .2126 * channels[0] + .7152 * channels[1] + .0722 * channels[2];
}

function contrast(a: string, b: string) {
  const [lighter, darker] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (lighter + .05) / (darker + .05);
}

describe('design token contrast', () => {
  for (const theme of ['light', 'dark'] as const) {
    it(`${theme} theme keeps readable text contrast`, () => {
      const value = tokens(theme);
      for (const [foreground, background] of [
        ['text', 'bg'], ['text', 'surface'], ['muted-strong', 'surface'],
        ['accent-contrast', 'accent'], ['success-text', 'success-soft'],
        ['warning-text', 'warning-soft'], ['danger-text', 'danger-soft'],
      ]) expect(contrast(value[foreground], value[background]), `${foreground} on ${background}`).toBeGreaterThanOrEqual(4.5);
    });
  }
});
