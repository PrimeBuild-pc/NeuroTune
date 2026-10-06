import { afterEach, describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import ts from 'typescript';
import { getLanguage, isLanguage, languages, messages, setLanguage, t } from './i18n';
import { appMessages } from './locales/app';
import { diagnosisMessages } from './locales/diagnosis';
import { reportMessages } from './locales/reports';
import { toolMessages } from './locales/tools';
import { CompleteDiagnosis } from './CompleteDiagnosis';
import { WindowsSecurityPanel } from './WindowsSecurityPanel';
import { ProviderPage } from './components/ProviderPage';
import type { TuningGoals } from './types';

const noop = () => {};
const goals: TuningGoals = { priority: 'privacySecurity', riskProfile: 'balanced', games: [], notes: '<script>user text</script>', gameContext: { game: '', version: '', launcher: '', graphicsApi: '', displayMode: '', vrr: '', vSync: '', symptoms: [], preserve: '' }, performanceInput: { userProvided: true, notes: '' } };
const placeholders = (value: string) => [...value.matchAll(/\{(\w+)\}/g)].map(match => match[1]).sort();
afterEach(() => { setLanguage('en'); vi.unstubAllGlobals(); });

describe('five-language interface without permission or evidence changes', () => {
  it('defaults to English, validates selection, persists it and formats only templates', () => {
    expect(getLanguage()).toBe('en'); expect(languages.map(item => item.id)).toEqual(['en', 'zh-CN', 'ja', 'es', 'ru']);
    expect(isLanguage('it')).toBe(false);
    const storage = new Map<string, string>();
    vi.stubGlobal('localStorage', { getItem: (key: string) => storage.get(key), setItem: (key: string, value: string) => storage.set(key, value) });
    setLanguage('es'); expect(getLanguage()).toBe('es'); expect(storage.get('neurotune.language')).toBe('es');
    expect(t('Privacy & security')).toBe(messages['Privacy & security'][2]);
    expect(t('Unknown native ID: {value}', { value: 'audit:privacy.test<script>' })).toBe('Unknown native ID: audit:privacy.test<script>');
    expect(t('Untouched {missing}')).toBe('Untouched {missing}');
    expect(() => setLanguage('it' as never)).toThrow('Unsupported language'); expect(getLanguage()).toBe('es');
    vi.stubGlobal('localStorage', { setItem: () => { throw Error('Blocked storage'); } });
    setLanguage('ja'); expect(getLanguage()).toBe('ja');
  });

  it('provides four real translations for every catalog key with exact placeholders', () => {
    const seen = new Map<string, readonly string[]>();
    for (const catalog of [appMessages, diagnosisMessages, reportMessages, toolMessages]) for (const [source, translations] of Object.entries(catalog)) {
      expect(translations, source).toHaveLength(4);
      seen.set(source, translations);
      for (const translated of translations) {
        expect(translated.trim(), source).not.toBe('');
        expect(placeholders(translated), source).toEqual(placeholders(source));
      }
    }
    expect(seen.size).toBeGreaterThan(800);
  });

  it('has catalog coverage for every literal t call in production UI', () => {
    const sources = import.meta.glob('./**/*.{ts,tsx}', { query: '?raw', import: 'default', eager: true }) as Record<string, string>;
    for (const [path, source] of Object.entries(sources)) {
        if (path.includes('/locales/') || path.includes('.test.')) continue;
        const file = ts.createSourceFile(path, source, ts.ScriptTarget.Latest, true, path.endsWith('.tsx') ? ts.ScriptKind.TSX : ts.ScriptKind.TS);
        function visit(node: ts.Node) {
          if (ts.isCallExpression(node) && ts.isIdentifier(node.expression) && node.expression.text === 't' && node.arguments.length && ts.isStringLiteralLike(node.arguments[0])) {
            const key = node.arguments[0].text;
            expect(key in messages, `${path}: untranslated ${key}`).toBe(true);
          }
          ts.forEachChild(node, visit);
        }
        visit(file);
    }
  });

  it('renders safety/consent/provider controls in all languages while preserving IDs, user content and disabled gates', () => {
    for (const { id } of languages) {
      setLanguage(id);
      const html = renderToStaticMarkup(<><CompleteDiagnosis mode="auditOnly" goals={goals} onGoals={noop} onStart={noop} onCancel={noop} blocked={false}/><WindowsSecurityPanel blocked onBusy={noop}/><ProviderPage provider={{ provider: 'local', providerName: 'Ollama', protocol: 'openAiCompatible', baseUrl: 'http://127.0.0.1:11434/v1', model: 'test', requiresApiKey: false }} apiKey="" hasCredential={false} models={[]} modelLabels={{}} chatGptAccounts={[]} authBusy onSignOut={noop} onChoose={noop} onChange={noop} onKey={noop} onSave={noop} onLoadModels={noop} onBrowserSignIn={noop} onModel={noop}/></>);
      expect(html).toContain('value="privacySecurity" selected=""'); expect(html.match(/<input[^>]*value="auditOnly"[^>]*>/)?.[0]).toContain('checked=""');
      expect(html.match(/type="checkbox"/g)).toHaveLength(3);
      for (const checkbox of html.match(/<input[^>]+type="checkbox"[^>]*>/g) ?? []) expect(checkbox).not.toContain('checked=""');
      expect(html).toContain('&lt;script&gt;user text&lt;/script&gt;'); expect(html).not.toContain('<script>');
      expect(html).toContain('<fieldset disabled=""'); expect(html).not.toContain('id="diagnosis-workload"');
      expect(html).toContain(renderToStaticMarkup(<span>{t('Start AI audit · no changes')}</span>).slice(6, -7));
      expect(html).not.toMatch(/Avvia|Diagnosi completa|sicurezza Windows|Annulla/);
    }
  });
});
