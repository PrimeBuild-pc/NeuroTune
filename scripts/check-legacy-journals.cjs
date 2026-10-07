// Browser-only regression: every Agent call is mocked; no guest/host journals, WPR or provider are touched.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require(process.env.PLAYWRIGHT_PATH || 'playwright');
(async () => {
  const browser = await chromium.launch({ headless: true, ...(process.env.CHROME_PATH ? { executablePath: process.env.CHROME_PATH } : {}) });
  try {
    const page = await browser.newPage({ viewport: { width: 907, height: 573 }, reducedMotion: 'reduce' });
    const errors = []; page.on('pageerror', error => errors.push(error.message));
    await page.addInitScript(() => {
      let next = 0; window.requests = []; window.legacyReviewPending = true;
      window.__TAURI_EVENT_PLUGIN_INTERNALS__ = { unregisterListener() {} };
      window.__TAURI_INTERNALS__ = {
        transformCallback() { return ++next; },
        async invoke(command, args) {
          if (command.startsWith('plugin:event|')) return ++next;
          if (command !== 'agent') throw Error('Unexpected bridge command: ' + command);
          window.requests.push(args.command);
          switch (args.command) {
            case 'get-state': return { settings: { provider: 'local', providerName: 'Mock saved provider', protocol: 'openAiCompatible', baseUrl: 'http://127.0.0.1:11434/v1', model: 'saved-model', requiresApiKey: false }, hasCredential: false, isRecording: false, chatGptAccounts: [] };
            case 'history': case 'run-list':
              if (window.legacyReviewPending) throw Error('Legacy journals need manual review. Blocking paths for the Windows account running NeuroTune: C:\\Users\\VM admin\\AppData\\Local\\NeuroTune\\runs. Finish or review recovery with the previous build.');
              return [];
            case 'actions': case 'measurement-workloads': return [];
            case 'save-provider': return { saved: true, hasCredential: false };
            default: throw Error('Unexpected Agent operation: ' + args.command);
          }
        },
      };
    });
    await page.goto(process.env.UI_PREVIEW_URL || 'http://127.0.0.1:4173/');
    await page.locator('#startup').waitFor({ state: 'detached' });
    assert.equal(await page.locator('.connection strong').innerText(), 'Mock saved provider', 'Journal failure must not discard loaded provider settings');
    await page.getByRole('navigation').getByRole('button', { name: 'Complete diagnosis', exact: true }).click();
    const start = page.locator('.diagnosis-submit').getByRole('button');
    const banner = page.getByRole('alert').filter({ hasText: 'Blocking paths' });
    await banner.waitFor();
    assert.ok((await banner.innerText()).includes('C:\\Users\\VM admin\\AppData\\Local\\NeuroTune\\runs'));
    assert.equal(await start.isDisabled(), true);
    assert.ok((await banner.getByRole('button', { name: 'Refresh', exact: true }).boundingBox()).height <= 52, 'Recheck label must not wrap into multiple lines');
    fs.mkdirSync('artifacts', { recursive: true });
    for (const theme of ['light', 'dark']) {
      await page.evaluate(theme => { document.documentElement.dataset.theme = theme; }, theme);
      for (const width of [907, 760, 540]) {
        await page.setViewportSize({ width, height: 573 });
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, `Path overflow ${theme}/${width}`);
      }
      await page.setViewportSize({ width: 907, height: 573 });
      await page.screenshot({ path: `artifacts/legacy-journals-${theme}.png`, fullPage: true });
    }
    await page.evaluate(() => { window.legacyReviewPending = false; }); // Simulated external manual review, not an application migration.
    await banner.getByRole('button', { name: 'Refresh', exact: true }).click();
    await banner.waitFor({ state: 'hidden' });
    assert.equal(await start.isEnabled(), true, 'Successful explicit recheck clears the blocker without restarting');
    await page.getByRole('radio', { name: /^Advisory audit/ }).check();
    await page.evaluate(() => { window.legacyReviewPending = true; }); // Journals changed after startup: native preflight still refuses.
    await start.click();
    await banner.waitFor();
    assert.equal(await start.isDisabled(), true);
    assert.equal(await page.locator('.complete-diagnosis').isVisible(), true);
    const calls = await page.evaluate(() => window.requests);
    assert.equal(calls.some(command => /^(scan|measurement-start|measurement-cancel|diagnose|apply|rollback|defender-scan)$/.test(command)), false);
    assert.deepEqual(errors, []);
    console.log('PASS: native journal error paths visible, saved provider retained, diagnosis disabled, explicit recheck, late journal race refused before collection/AI, no navigation loss; light/dark and 907/760/540px. All Agent operations mocked.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
