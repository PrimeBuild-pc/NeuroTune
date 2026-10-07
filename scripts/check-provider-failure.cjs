// Offline browser regression: all native commands and provider responses are mocked.
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
      window.failedRun = JSON.parse(sessionStorage.getItem('failed-run') || 'null');
      const reason = 'ChatGPT request failed: subscription_sharing_usage_limit_exceeded. Review NeuroTune limit in ChatGPT Settings > Usage.';
      const diagnosis = { summary: reason + ' The AI diagnosis is incomplete; local observations only.', findings: [], recommendations: [], conflicts: [], consentQuestion: 'Finish without changes?' };
      window.__TAURI_EVENT_PLUGIN_INTERNALS__ = { unregisterListener() {} };
      window.__TAURI_INTERNALS__ = {
        transformCallback() { return ++next; },
        async invoke(command, args) {
          if (command.startsWith('plugin:event|')) return ++next;
          if (command !== 'agent') throw Error('Unexpected bridge command: ' + command);
          window.requests.push(args.command);
          switch (args.command) {
            case 'get-state': return { settings: { provider: 'local', providerName: 'Mock provider', model: 'test', requiresApiKey: false }, hasCredential: false, isRecording: false };
            case 'history': case 'actions': case 'measurement-list': case 'measurement-workloads': return [];
            case 'run-list': return window.failedRun ? [window.failedRun] : [];
            case 'save-provider': return { saved: true, hasCredential: false };
            case 'scan': return { profile: { privacySecurity: { 'security.example': 'Unavailable' } }, sanitizedProfile: '{"security.example":"Unavailable"}', actions: [] };
            case 'run-create': window.failedRun = { id: 'mock-failure', mode: args.payload.mode, state: 'scanned', goals: args.payload.goals, evidenceFacts: { 'audit:security.example': 'Unavailable' }, requiresRecovery: false, approvedActionIds: [], baselineSessionIds: [], candidateSessionIds: [], diagnosticSessionIds: [] }; return window.failedRun;
            case 'diagnose': Object.assign(window.failedRun, { state: 'hypothesizing', usedLocalFallback: true, error: reason, diagnosis }); sessionStorage.setItem('failed-run', JSON.stringify(window.failedRun)); return diagnosis;
            case 'run-get': return window.failedRun;
            case 'run-dismiss': window.failedRun.state = 'completed'; sessionStorage.setItem('failed-run', JSON.stringify(window.failedRun)); return window.failedRun;
            default: throw Error('Unexpected Agent operation: ' + args.command);
          }
        },
      };
    });
    await page.goto(process.env.UI_PREVIEW_URL || 'http://127.0.0.1:4173/');
    await page.locator('#startup').waitFor({ state: 'detached' });
    await page.getByRole('navigation').getByRole('button', { name: 'Complete diagnosis', exact: true }).click();
    await page.getByRole('radio', { name: /^Advisory audit/ }).check();
    await page.getByRole('button', { name: 'Start AI audit · no changes', exact: true }).click();
    await page.getByRole('button', { name: 'I authorize evidence transmission · continue AI audit', exact: true }).click();
    await page.getByRole('heading', { name: 'AI investigation unavailable', exact: true }).waitFor();
    assert.equal(await page.locator('.report-summary .status-pill.good').count(), 0);
    assert.ok((await page.locator('.report-summary').innerText()).includes('subscription_sharing_usage_limit_exceeded'));
    assert.ok((await page.getByRole('status').filter({ hasText: 'subscription_sharing_usage_limit_exceeded' }).innerText()).includes('Settings > Usage'));
    let calls = await page.evaluate(() => window.requests);
    assert.equal(calls.filter(command => command === 'diagnose').length, 1);
    assert.equal(calls.includes('run-dismiss'), false, 'Failure must not silently discard the review');
    for (const theme of ['light', 'dark']) {
      await page.evaluate(theme => { document.documentElement.dataset.theme = theme; }, theme);
      for (const width of [907, 760, 540]) {
        await page.setViewportSize({ width, height: 573 });
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false);
      }
      fs.mkdirSync('artifacts', { recursive: true });
      await page.screenshot({ path: `artifacts/provider-failure-${theme}.png`, fullPage: true });
    }
    await page.reload(); await page.locator('#startup').waitFor({ state: 'detached' });
    await page.getByRole('heading', { name: 'AI investigation unavailable', exact: true }).waitFor();
    assert.equal(await page.evaluate(() => window.requests.includes('diagnose')), false, 'Restart must not retry inference');
    await page.getByLabel('Review decision', { exact: true }).getByRole('button', { name: 'Finish without changes', exact: true }).click();
    await page.getByText('Diagnosis closed without applying changes. The report remains reviewable.', { exact: true }).waitFor();
    calls = await page.evaluate(() => window.requests);
    assert.equal(calls.filter(command => command === 'run-dismiss').length, 1);
    assert.equal(calls.some(command => /^(apply|rollback|run-approve|measurement-start|defender-scan|models)$/.test(command)), false);
    assert.deepEqual(errors, []);
    console.log('PASS: explicit quota cause, incomplete review without success badge, retained report across restart, no automatic retry or silent dismissal, explicit no-change closure; light/dark and 907/760/540px. All Agent calls mocked.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
