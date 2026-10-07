// Mocked model discovery only: no endpoint, key, inference, recorder or writer is contacted.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require(process.env.PLAYWRIGHT_PATH || 'playwright');
(async () => {
  const browser = await chromium.launch({ headless: true, ...(process.env.CHROME_PATH ? { executablePath: process.env.CHROME_PATH } : {}) });
  try {
    const page = await browser.newPage({ viewport: { width: 907, height: 573 }, reducedMotion: 'reduce' });
    const errors = []; page.on('pageerror', error => errors.push(error.message));
    await page.addInitScript(() => {
      let next = 0;
      window.requests = [];
      window.__TAURI_EVENT_PLUGIN_INTERNALS__ = { unregisterListener() {} };
      window.__TAURI_INTERNALS__ = {
        transformCallback() { return ++next; },
        async invoke(command, args) {
          if (command.startsWith('plugin:event|')) return ++next;
          if (command !== 'agent') throw Error('Unexpected bridge operation');
          window.requests.push({ command: args.command, payload: args.payload });
          switch (args.command) {
            case 'get-state': return { settings: { provider: 'deepSeek', providerName: 'DeepSeek', protocol: 'openAiCompatible', baseUrl: 'https://api.deepseek.com/v1', model: 'saved-unlisted-model', requiresApiKey: true }, hasCredential: true, isRecording: false };
            case 'history': case 'actions': case 'run-list': return [];
            case 'save-provider': return { saved: true, hasCredential: true };
            case 'models': return { models: ['deepseek-v4-flash', 'other-chat-model', 'z-model'] };
            default: throw Error('Unexpected Agent operation: ' + args.command);
          }
        },
      };
    });
    await page.goto(process.env.UI_PREVIEW_URL || 'http://127.0.0.1:4173/');
    await page.locator('#startup').waitFor({ state: 'detached' });
    await page.getByRole('navigation').getByRole('button', { name: 'AI provider', exact: true }).click();
    const catalog = page.getByLabel('Available models', { exact: true });
    const manual = page.getByLabel('Model', { exact: true });
    assert.equal(await catalog.isDisabled(), true);
    assert.equal(await page.evaluate(() => window.requests.some(r => r.command === 'models')), false);
    for (const name of ['DeepSeek', 'OpenAI API', 'OpenRouter', 'Anthropic', 'Custom', 'Local']) {
      if (name !== 'DeepSeek') await page.locator('.provider-picker').getByRole('button', { name, exact: true }).click();
      await manual.fill('saved-unlisted-model');
      await page.getByRole('button', { name: 'Test & discover models', exact: true }).click();
      await catalog.locator('option[value="deepseek-v4-flash"]').waitFor({ state: 'attached' });
      assert.equal(await catalog.isEnabled(), true);
      assert.equal(await manual.inputValue(), 'saved-unlisted-model', 'Discovery must not replace an API model silently');
      assert.equal(await catalog.locator('option:not([disabled])').count(), 3);
      await catalog.selectOption('deepseek-v4-flash');
      assert.equal(await manual.inputValue(), 'deepseek-v4-flash');
      await catalog.selectOption('other-chat-model');
      assert.equal(await manual.inputValue(), 'other-chat-model', 'Previous input must not filter catalog choices');
      await page.getByRole('button', { name: 'Save securely', exact: true }).click();
      const saved = await page.evaluate(() => window.requests.filter(r => r.command === 'save-provider').at(-1));
      assert.equal(saved.payload.settings.model, 'other-chat-model');
    }
    await page.getByLabel('Base URL', { exact: true }).fill('http://127.0.0.1:1234/v1');
    assert.equal(await catalog.isDisabled(), true, 'Endpoint change must invalidate stale choices');
    await page.locator('.provider-picker').getByRole('button', { name: 'DeepSeek', exact: true }).click();
    await page.getByRole('button', { name: 'Test & discover models', exact: true }).click();
    await catalog.locator('option[value="deepseek-v4-flash"]').waitFor({ state: 'attached' });
    await page.getByLabel('API key', { exact: true }).fill('dummy-mock-key');
    assert.equal(await catalog.isDisabled(), true, 'Credential change must invalidate stale choices');
    for (const theme of ['light', 'dark']) {
      await page.evaluate(theme => { document.documentElement.dataset.theme = theme; }, theme);
      for (const width of [907, 760, 540]) {
        await page.setViewportSize({ width, height: 573 });
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false);
        assert.ok(await page.locator('.brand span:visible, .compact-attribution:visible').filter({ hasText: 'by PrimeBuild' }).count() > 0);
      }
      fs.mkdirSync('artifacts', { recursive: true });
      await page.screenshot({ path: `artifacts/provider-models-${theme}.png`, fullPage: true });
    }
    assert.equal(await page.evaluate(() => window.requests.some(r => /^(diagnose|scan|apply|run-approve|measurement-start|oauth-)/.test(r.command))), false);
    assert.deepEqual(errors, []);
    console.log('PASS: complete catalogs for six API/local providers, manual IDs preserved, selection/save, endpoint/key invalidation, visible PrimeBuild attribution and narrow light/dark layouts. All Agent calls mocked.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
