// Browser-only regression: all Agent calls are mocked; no provider, installer or Windows writer runs.
const assert = require('node:assert/strict');
const { chromium } = require(process.env.PLAYWRIGHT_PATH || 'playwright');
require('node:fs').mkdirSync('artifacts', { recursive: true });
(async () => {
  const browser = await chromium.launch({ headless: true, ...(process.env.CHROME_PATH ? { executablePath: process.env.CHROME_PATH } : {}) });
  try {
    for (const theme of ['light', 'dark']) {
      const page = await browser.newPage({ viewport: { width: 907, height: 573 }, reducedMotion: 'reduce' });
      const errors = []; page.on('pageerror', error => errors.push(error.message));
      await page.addInitScript(theme => {
        localStorage.setItem('neurotune.theme', theme);
        let status = JSON.parse(localStorage.getItem('mock.systemOne') || 'null') || { enabled: false, installed: false, hasFiles: false, source: 'local', size: '4b', device: 'auto', model: '', directory: '', detail: '', hasApiKey: false };
        let next = 0; window.requests = []; window.__TAURI_EVENT_PLUGIN_INTERNALS__ = { unregisterListener() {} };
        window.__TAURI_INTERNALS__ = { transformCallback() { return ++next; }, async invoke(command, args) {
          if (command.startsWith('plugin:event|')) return ++next;
          if (command !== 'agent') throw Error('Unexpected native command: ' + command);
          window.requests.push({ command: args.command, payload: args.payload });
          switch (args.command) {
            case 'get-state': return { settings: { provider: 'local', providerName: 'Mock main AI', model: 'main-model', baseUrl: 'http://127.0.0.1:11434/v1', protocol: 'openAiCompatible', requiresApiKey: false }, hasCredential: false, chatGptAccounts: [], isRecording: false };
            case 'history': case 'run-list': case 'actions': case 'measurement-workloads': case 'measurement-list': return [];
            case 'power-plan-list': return { directory: '', plans: [] };
            case 'system-one-status': return structuredClone(status);
            case 'system-one-models': return ['vendor/paid-model'];
            case 'system-one-configure': {
              const { options, apiKey, forgetApiKey } = args.payload;
              if (apiKey === 'invalid-test-key') throw Error('Simulated credential storage failure');
              if (options.enabled && !options.cloudConsent) throw Error('Missing separate cloud consent');
              if (options.source === 'openRouterFree' && options.model !== 'openrouter/free') throw Error('Free model changed');
              await new Promise(resolve => setTimeout(resolve, 150));
              status = { ...status, ...options, hasFiles: true, hasApiKey: forgetApiKey ? false : Boolean(apiKey || status.hasApiKey), detail: options.source === 'openRouter' ? 'Saved paid classifier: API calls consume credits.' : 'Saved free classifier: zero-price routing with rate limits.' };
              localStorage.setItem('mock.systemOne', JSON.stringify(status)); return structuredClone(status);
            }
            default: throw Error('Unexpected Agent command: ' + args.command);
          }
        } };
      }, theme);
      await page.goto(process.env.UI_PREVIEW_URL || 'http://127.0.0.1:4173/');
      await page.locator('#startup').waitFor({ state: 'detached' });
      const guide = page.getByRole('dialog');
      if (await guide.isVisible()) await guide.getByRole('button', { name: 'Skip without changing permissions', exact: true }).click();
      await page.getByRole('button', { name: 'Advanced tools', exact: true }).click();
      await page.getByRole('button', { name: 'System One assistant', exact: true }).click();
      const panel = page.locator('.system-one-panel');
      const free = panel.getByRole('radio', { name: 'OpenRouter Free · no local install', exact: true });
      const enabled = panel.getByRole('checkbox');
      await free.check();
      assert.equal(await enabled.isChecked(), false);
      assert.equal(await enabled.isDisabled(), true);
      const model = panel.getByLabel('OpenRouter classifier model');
      assert.equal(await model.inputValue(), 'openrouter/free');
      assert.equal(await model.getAttribute('readonly'), '');
      assert.equal(await panel.getByRole('combobox').count(), 0);
      assert.equal(await page.evaluate(() => window.requests.some(item => /^system-one-(configure|models|install)$/.test(item.command))), false, 'Selecting free source grants no consent and makes no network/catalog/install request');
      await panel.getByLabel('System One OpenRouter API key').fill('invalid-test-key');
      await panel.getByRole('button', { name: 'Save separate classifier key', exact: true }).click();
      await panel.getByRole('alert').waitFor(); await free.check();
      assert.equal(await panel.getByLabel('System One OpenRouter API key').inputValue(), 'invalid-test-key', 'Failed key storage preserves the credential draft');
      assert.equal(await enabled.isChecked(), false);
      await panel.getByLabel('System One OpenRouter API key').fill('fake-classifier-key');
      await panel.getByRole('button', { name: 'Save separate classifier key', exact: true }).click();
      assert.equal(await free.isDisabled(), true, 'Source and credential controls freeze while configuring');
      await panel.getByRole('button', { name: 'Save separate classifier key', exact: true }).waitFor({ state: 'visible' });
      await page.waitForFunction(() => !document.querySelector('.system-one-panel > fieldset').disabled);
      assert.equal(await enabled.isChecked(), false, 'Saving a key grants no cloud consent');
      assert.equal(await panel.getByLabel('System One OpenRouter API key').inputValue(), '');
      const count = () => page.evaluate(() => window.requests.filter(item => item.command === 'system-one-configure').length);
      const before = await count();
      page.once('dialog', async dialog => { assert.match(dialog.message(), /consent to sending sampled context/); assert.match(dialog.message(), /no paid\/local fallback/); await dialog.dismiss(); });
      await enabled.click();
      assert.equal(await count(), before, 'Rejected cloud consent makes no request');
      assert.equal(await enabled.isChecked(), false);
      page.once('dialog', dialog => dialog.accept()); await enabled.click();
      await page.waitForFunction(() => JSON.parse(localStorage.getItem('mock.systemOne')).enabled && !document.querySelector('.system-one-panel > fieldset').disabled);
      let saved = await page.evaluate(() => JSON.parse(localStorage.getItem('mock.systemOne')));
      assert.equal(saved.model, 'openrouter/free'); assert.equal(saved.cloudConsent, true); assert.equal(saved.source, 'openRouterFree');
      await page.reload(); await page.locator('#startup').waitFor({ state: 'detached' });
      await page.getByRole('button', { name: 'Advanced tools', exact: true }).click();
      await page.getByRole('button', { name: 'System One assistant', exact: true }).click();
      assert.equal(await enabled.isChecked(), true, 'Explicit free configuration survives reload');
      assert.equal(await model.inputValue(), 'openrouter/free');
      assert.equal(await page.evaluate(() => window.requests.some(item => item.command === 'system-one-models')), false, 'Free mode never discovers paid models');
      await panel.getByRole('radio', { name: 'OpenRouter API · cloud classifier', exact: true }).check();
      assert.equal(await enabled.isChecked(), false, 'Free consent does not enable the paid selection');
      assert.equal(await enabled.isDisabled(), true, 'Switching from free does not leave its router as a paid-mode model draft');
      await panel.getByRole('button', { name: 'Save key & load classifier models', exact: true }).click();
      await page.waitForFunction(() => !document.querySelector('.system-one-panel > fieldset').disabled);
      assert.equal(await model.inputValue(), 'vendor/paid-model');
      page.once('dialog', async dialog => { assert.match(dialog.message(), /consume API credits/); await dialog.accept(); });
      await enabled.click();
      await page.waitForFunction(() => JSON.parse(localStorage.getItem('mock.systemOne')).enabled && !document.querySelector('.system-one-panel > fieldset').disabled);
      const paidWrites = await count();
      await free.check(); assert.equal(await model.inputValue(), 'openrouter/free'); assert.equal(await enabled.isChecked(), false);
      assert.equal(await panel.locator('.status-pill').textContent(), 'Enabled · on demand', 'Free draft must not hide an enabled paid classifier');
      assert.equal(await panel.getByText('Saved assistant: OpenRouter API · cloud classifier · vendor/paid-model', { exact: true }).isVisible(), true);
      assert.equal(await panel.getByText('Saved paid classifier: API calls consume credits.', { exact: true }).isVisible(), true);
      assert.equal(await panel.getByRole('status').innerText(), 'Unsaved selection. Actual status and costs follow the saved configuration above, not this selection.');
      await panel.getByRole('radio', { name: 'Local · Rizzo Flow System One', exact: true }).check();
      assert.equal(await panel.locator('.status-pill').textContent(), 'Enabled · on demand');
      assert.equal(await panel.getByText('Saved paid classifier: API calls consume credits.', { exact: true }).isVisible(), true);
      await free.check();
      assert.equal(await count(), paidWrites, 'Draft radios must not write settings or disable the saved paid source');
      saved = await page.evaluate(() => JSON.parse(localStorage.getItem('mock.systemOne')));
      assert.equal(saved.source, 'openRouter'); assert.equal(saved.model, 'vendor/paid-model'); assert.equal(saved.enabled, true);
      for (const width of [907, 605, 540]) {
        await page.setViewportSize({ width, height: 573 });
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, 'Saved paid status and free draft must not overflow');
        await page.screenshot({ path: `artifacts/system-one-paid-free-draft-${theme}-${width}.png`, fullPage: true });
      }
      page.once('dialog', dialog => dialog.accept());
      await panel.getByRole('button', { name: 'Delete classifier API key', exact: true }).click();
      await page.waitForFunction(() => !document.querySelector('.system-one-panel > fieldset').disabled);
      saved = await page.evaluate(() => JSON.parse(localStorage.getItem('mock.systemOne')));
      assert.equal(saved.enabled, false); assert.equal(saved.cloudConsent, false); assert.equal(saved.hasApiKey, false);
      assert.equal(await enabled.isDisabled(), true);
      assert.equal(await page.evaluate(() => window.requests.some(item => /^(system-one-(install|remove)|save-settings|diagnose|measurement-start)$/.test(item.command))), false);
      for (const width of [907, 605, 540]) {
        await page.setViewportSize({ width, height: 573 });
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, 'No horizontal overflow');
        await page.screenshot({ path: `artifacts/system-one-free-${theme}-${width}.png`, fullPage: true });
      }
      assert.deepEqual(errors, []); await page.close();
    }
    console.log('System One free/paid choice, separate consent, fixed zero-price router, credentials, reload and narrow light/dark UI: passed (mocked).');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
