// Browser-only locale validation: every Agent operation is mocked. No native probes/scans/provider calls.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require(process.env.PLAYWRIGHT_PATH || 'playwright');
(async () => {
  const browser = await chromium.launch({ headless: true, ...(process.env.CHROME_PATH ? { executablePath: process.env.CHROME_PATH } : {}) });
  try {
    const page = await browser.newPage({ viewport: { width: 907, height: 573 }, reducedMotion: 'reduce' });
    const errors = []; page.on('pageerror', error => errors.push(error.message));
    await page.addInitScript(() => {
      let next = 0; window.requests = []; let run;
      const diagnosis = { summary: 'Mock evidence, not a real audit', recommendations: [], findings: [], conflicts: [], consentQuestion: 'Mock review' };
      window.__TAURI_EVENT_PLUGIN_INTERNALS__ = { unregisterListener() {} };
      window.__TAURI_INTERNALS__ = {
        transformCallback() { return ++next; },
        async invoke(command, args) {
          if (command.startsWith('plugin:event|')) return ++next;
          if (command !== 'agent') return true;
          window.requests.push({ command: args.command, payload: args.payload });
          switch (args.command) {
            case 'get-state': return { settings: { provider: 'local', providerName: 'Mock local AI', protocol: 'openAiCompatible', baseUrl: 'http://127.0.0.1:11434/v1', model: 'test', requiresApiKey: false }, hasCredential: false, isRecording: false, chatGptAccounts: [] };
            case 'actions': case 'history': case 'run-list': case 'measurement-list': case 'measurement-workloads': return [];
            case 'power-plan-list': return { directory: 'mock folder', plans: [] };
            case 'system-one-status': return { installed: false, running: false, health: 'notInstalled', cpuThreads: 0, installable: false, providers: [], description: 'Raw mock status stays unchanged' };
            case 'system-one-models': return [];
            case 'defender-scan-current': return null;
            case 'save-provider': return { saved: true, hasCredential: false };
            case 'scan': return { profile: { privacySecurity: { 'privacy.example': 'Unknown native value' } }, sanitizedProfile: '{"native":"Unknown native value"}', actions: [] };
            case 'run-create': run = { id: 'locale-mock', state: 'scanned', mode: args.payload.mode, goals: args.payload.goals, approvedActionIds: [], baselineSessionIds: [], candidateSessionIds: [] }; return run;
            case 'diagnose': return diagnosis;
            case 'run-get': return { ...run, state: 'proposalReady', diagnosis };
            default: throw Error('Unexpected operation in language mock: ' + args.command);
          }
        },
      };
    });
    await page.goto(process.env.UI_PREVIEW_URL || 'http://127.0.0.1:4173/');
    await page.locator('#startup').waitFor({ state: 'detached' });
    assert.equal(await page.locator('html').getAttribute('lang'), 'en');
    const navSecurity = page.getByRole('navigation').getByRole('button', { name: 'Privacy & security', exact: true });
    assert.equal(await navSecurity.isVisible(), true); await navSecurity.click();
    await page.getByRole('heading', { name: 'Privacy & security', exact: true }).first().waitFor();
    assert.equal(await page.evaluate(() => window.requests.some(item => /^(scan|defender-status|defender-scan|diagnose|apply|measurement-start)$/.test(item.command))), false);
    await page.getByRole('button', { name: 'Choose privacy & security diagnosis', exact: true }).click();
    assert.equal(await page.getByRole('combobox', { name: 'Objective', exact: true }).inputValue(), 'privacySecurity');
    assert.equal(await page.getByRole('group', { name: 'Investigation mode', exact: true }).getByRole('radio', { name: /^Measured optimization/ }).isChecked(), true);
    await page.getByRole('textbox', { name: 'Improve / preserve', exact: true }).fill('Keep this draft: <script>not executable</script>');
    await page.getByRole('button', { name: 'Quick Settings', exact: true }).click();
    let select = page.locator('.settings-grid').getByRole('combobox').first();
    assert.equal(await select.inputValue(), 'en');
    assert.deepEqual(await select.locator('option').evaluateAll(items => items.map(item => item.value)), ['en', 'zh-CN', 'ja', 'es', 'ru']);
    fs.mkdirSync('artifacts', { recursive: true });
    for (const lang of ['en', 'zh-CN', 'ja', 'es', 'ru']) {
      await select.selectOption(lang);
      assert.equal(await page.locator('html').getAttribute('lang'), lang);
      assert.equal(await page.evaluate(() => localStorage.getItem('neurotune.language')), lang);
      const before = await page.evaluate(() => window.requests.length);
      for (const theme of ['light', 'dark']) {
        await page.evaluate(theme => { document.documentElement.dataset.theme = theme; }, theme);
        for (const width of [907, 760, 540]) {
          await page.setViewportSize({ width, height: 573 });
          assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, `Settings overflow ${lang}/${width}/${theme}`);
        }
        await page.setViewportSize({ width: 907, height: 573 });
        await page.screenshot({ path: `artifacts/language-settings-${lang}-${theme}.png`, fullPage: true });
      }
      assert.equal(await page.evaluate(() => window.requests.length), before, 'Language/theme changes cannot call Agent or reset consent');
      await page.reload(); await page.locator('#startup').waitFor({ state: 'detached' });
      assert.equal(await page.locator('html').getAttribute('lang'), lang, 'Reload preserves locale');
      const body = await page.locator('body').innerText(); assert.doesNotMatch(body, /Diagnosi completa|sicurezza Windows|Annulla diagnosi/);
      if (lang !== 'en') assert.doesNotMatch(body, /Understand the machine\.|Change only what is safe\.|Advanced tools/);
      await page.getByRole('navigation').locator('[data-page="security"]').click(); // Stable destination, independent of locale and grouping.
      const text = await page.locator('.page-content').innerText();
      if (lang !== 'en') assert.doesNotMatch(text, /Opening this page does not start|Read status and detections|No scan started/);
      await page.screenshot({ path: `artifacts/language-security-${lang}.png`, fullPage: true });
      await page.locator('.topbar-actions .icon-button').first().click(); // Quick Settings, stable control not locale text.
      select = page.locator('.settings-grid').getByRole('combobox').first();
    }
    assert.equal(await page.evaluate(() => window.requests.some(item => /^(scan|defender-status|defender-scan|diagnose|apply|measurement-start|save-provider)$/.test(item.command))), false);
    // Exercise selected language on a consented MOCK audit; no actual provider or native scan is contacted.
    await select.selectOption('es');
    await page.getByRole('navigation').locator('[data-page="security"]').click();
    await page.locator('.security-panel > div:not([role]) .section-card > button.primary').click();
    await page.locator('.complete-diagnosis .mode-control input[type="radio"]').nth(1).check();
    await page.locator('.diagnosis-submit > button.primary').click();
    await page.locator('.profile-json').waitFor();
    assert.equal(await page.evaluate(() => window.requests.some(item => item.command === 'diagnose')), false);
    await page.locator('section[aria-label] .button-row > button.primary').first().click();
    await page.getByText('Mock evidence, not a real audit', { exact: true }).waitFor();
    const request = await page.evaluate(() => window.requests.find(item => item.command === 'diagnose'));
    assert.equal(request.payload.language, 'es');
    assert.equal(request.payload.goals.priority, 'privacySecurity');
    assert.equal(await page.evaluate(() => window.requests.some(item => /^(defender-scan|apply|measurement-start)$/.test(item.command))), false);
    assert.deepEqual(errors, []);
    console.log('PASS: five complete languages, English default, live/persisted locale, primary privacy/security, independent mode, no execution on selection, locale passed to mocked consented audit, light/dark and 907/760/540px. All Agent calls mocked.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
