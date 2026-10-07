import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { ProviderPage } from './components/ProviderPage';
import type { ProviderKind } from './types';

const noop = () => {};
describe('explicit provider model catalog', () => {
  it('exposes every discovered model independently of the current exact ID and keeps manual entry', () => {
    for (const provider of ['deepSeek', 'openAI', 'openRouter', 'anthropic', 'custom', 'local'] as ProviderKind[]) {
      const html = renderToStaticMarkup(<ProviderPage provider={{ provider, providerName: provider, model: 'saved-model', protocol: 'openAiCompatible', baseUrl: 'https://example.com/v1', requiresApiKey: true }} apiKey="" hasCredential models={['deepseek-v4-flash', 'different-model']} modelLabels={{}} chatGptAccounts={[]} authBusy={false} onSignOut={noop} onChoose={noop} onChange={noop} onKey={noop} onSave={noop} onLoadModels={noop} onBrowserSignIn={noop} onModel={noop}/>);
      expect(html).toContain('id="provider-model-catalog"');
      expect(html).toMatch(/<select[^>]*>[\s\S]*<option value="deepseek-v4-flash">deepseek-v4-flash<\/option>/);
      expect(html).toContain('value="different-model"');
      expect(html).toContain('value="saved-model"');
      expect(html).not.toContain('<datalist');
    }
  });
});
