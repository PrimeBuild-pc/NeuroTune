// Mocked presentation checks; never opens OAuth, starts WPR or writes Windows settings.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require(process.env.PLAYWRIGHT_PATH || 'playwright');
const url = process.env.UI_PREVIEW_URL || 'http://127.0.0.1:4173/';

(async () => {
  fs.mkdirSync('artifacts', { recursive: true });
  const browser = await chromium.launch({ headless: true, ...(process.env.CHROME_PATH ? { executablePath: process.env.CHROME_PATH } : {}) });
  try {
    for (const ready of [false, true]) {
      const page = await browser.newPage({ viewport: { width: 907, height: 573 }, reducedMotion: 'reduce' });
      const errors = []; page.on('pageerror', error => errors.push(error.message));
      await page.addInitScript(ready => {
        let next = 0;
        const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
        const proposal = (id, kind, risk, actionId = '') => ({ id, kind, risk, actionId, title: `${kind} ${risk} proposal`, reason: 'Evidence-backed hypothesis; no performance gain is assumed.', evidenceIds: ['test:local:fact'], resourceId: '', updateId: '', expectedImpact: 'Must be measured', uncertainty: 'Benefit is uncalibrated', reversibility: 'Review reversal procedure first', prerequisites: [], tradeoffs: ['Workload dependent'], requiresRestart: false, sourceReferences: [{ title: 'Official documentation', url: 'https://example.com/', grade: 'primary' }], scriptLanguage: 'PowerShell', script: 'Write-Output "Read-only preview"', reviewWarnings: kind === 'scriptArtifact' ? ['Unverified script; no executor'] : [] });
        const diagnosis = { summary: 'Read-only diagnosis with manual guidance, scripts and registered actions.', findings: [], conflicts: [], recommendations: [proposal('low', 'executableAction', 'low', 'low'), proposal('high', 'executableAction', 'high', 'high'), proposal('manual', 'manualGuidance', 'medium'), proposal('script', 'scriptArtifact', 'high')], consentQuestion: 'Review the evidence before any changes.' };
        const run = { id: 'mock-run', state: ready ? 'baselineReady' : 'proposalReady', goals: { priority: 'balanced', riskProfile: 'balanced', games: [], notes: '', gameContext: { game: '', version: '', launcher: '', graphicsApi: '', displayMode: '', vrr: '', vSync: '', symptoms: [], preserve: '' }, performanceInput: { userProvided: true, notes: '' } }, approvedActionIds: [], baselineSessionIds: [], candidateSessionIds: [], diagnosis, supportingAttachments: [{ id: 'support', name: 'user-report.txt', kind: 'report', sha256: 'a'.repeat(64) }] };
        const report = valid => ({ quality: { durationMilliseconds: 180000, etlBytes: 1000, eventsLost: valid ? 0 : 12, missingProviders: [], targetPresencePercent: 99, isValid: valid }, threads: [], interrupts: [], processors: [], observations: [], limitations: [] });
        const session = (id, label, state, extra = {}) => ({ id, label, state, processName: 'game_with_long_representative_name.exe', processId: 12, processStartTimeUtc: '2026-01-01T00:00:00Z', createdAtUtc: '2026-01-01T00:00:00Z', durationSeconds: 180, keepRawTrace: false, ...extra });
        let sessions = [session('baseline', 'baseline', 'completed', { report: report(true) }), session('candidate', 'candidate', 'completed', { report: report(false) }), session('system', 'baseline', 'completed', { systemWide: true, report: report(true) }), session('captured', 'candidate', 'captured'), session('failed', 'baseline', 'failed', { error: 'Trace analysis failed. The local ETL remains retryable.' })];
        window.requests = [];
        window.__TAURI_EVENT_PLUGIN_INTERNALS__ = { unregisterListener() {} };
        window.__TAURI_INTERNALS__ = {
          transformCallback() { return ++next; },
          async invoke(command, args) {
            if (command === 'plugin:event|listen') return ++next;
            if (command === 'plugin:event|unlisten') return;
            if (command !== 'agent') return true;
            window.requests.push({ command: args.command, payload: args.payload });
            switch (args.command) {
              case 'get-state': return { settings: { provider: 'openRouter', providerName: 'OpenRouter', model: 'test', baseUrl: 'https://openrouter.ai/api/v1', protocol: 'openAiCompatible', requiresApiKey: true }, hasCredential: false, chatGptAccounts: [], isRecording: false };
              case 'actions': return ['low', 'high', 'unavailable'].map(id => ({ id, name: `${id} registered action`, description: 'Registered capability; backup and consent required.', category: 'test', risk: id === 'high' ? 'high' : 'low', requiresRestart: id === 'high', availability: { canApply: id !== 'unavailable', status: id === 'unavailable' ? 'Not supported' : 'Available', currentValue: 'Default' } }));
              case 'history': return [];
              case 'run-list': return [run];
              case 'run-reconcile': return run;
              case 'save-provider': await pause(500); return { saved: true, hasCredential: false };
              case 'models': await pause(500); throw Error('Model discovery unavailable in this mock. No fallback.');
              case 'measurement-workloads': return [{ processId: 12, name: 'game.exe', description: 'Repeatable workload', startTimeUtc: '2026-01-01T00:00:00Z' }];
              case 'measurement-list': await pause(100); return sessions;
              case 'measurement-compare': await pause(300); return { level: 'exploratory', rejectionReasons: ['Candidate trace failed the quality gate.'], metrics: [], baselineSessionIds: ['baseline'], candidateSessionIds: ['candidate'] };
              case 'measurement-analyze': await pause(300); throw Error('Mock analysis failure; retry is available.');
              case 'measurement-delete': sessions = sessions.filter(item => item.id !== args.payload.sessionId); return true;
              default: throw Error('Unexpected mock command: ' + args.command);
            }
          },
        };
      }, ready);
      await page.goto(url);
      await page.locator('#startup').waitFor({ state: 'detached' });
      await page.getByRole('button', { name: 'Review changes', exact: true }).click();
      assert.equal(await page.locator('.review-ledger dd').allTextContents().then(values => values.join(',')), '4,2,2');
      await page.getByRole('button', { name: 'Aggressive', exact: true }).click();
      await page.getByText('2 changes selected', { exact: true }).waitFor();
      assert.equal(await page.locator('.high-risk-warning').count(), 1);
      const apply = page.getByRole('button', { name: 'Back up & apply selected', exact: true });
      assert.equal(await apply.isDisabled(), !ready);
      if (ready) {
        const dialogs = []; page.on('dialog', dialog => { dialogs.push(dialog.message()); dialogs.length === 1 ? dialog.accept() : dialog.dismiss(); });
        await apply.click();
        assert.equal(dialogs.length, 2);
        assert.ok(dialogs[1].includes('HIGH RISK'));
      } else {
        await page.getByRole('button', { name: /All supported/ }).click();
        assert.equal(await page.getByRole('button', { name: /unavailable registered action/ }).isDisabled(), true);
        const script = page.getByRole('article', { name: 'Unverified script: scriptArtifact high proposal' });
        assert.equal(await script.getByRole('button', { name: /Save script.*inert text file/ }).count(), 1);
        await page.evaluate(() => { Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText: async () => { throw Error('Denied'); } } }); });
        await script.getByRole('button', { name: /Copy script/ }).click();
        await page.getByText(/Could not copy the script/).waitFor();
        const download = page.waitForEvent('download');
        await script.getByRole('button', { name: /Save script/ }).click();
        const saved = await download;
        assert.equal(saved.suggestedFilename(), 'script.txt');
        await saved.saveAs('artifacts/polish-review-script.txt');
        assert.equal(fs.readFileSync('artifacts/polish-review-script.txt', 'utf8'), 'Write-Output "Read-only preview"');
        await page.evaluate(() => { window.scrollTo(0, 0); document.activeElement?.blur(); });
        for (const theme of ['light', 'dark']) {
          await page.evaluate(theme => { document.documentElement.dataset.theme = theme; }, theme);
          await page.screenshot({ path: `artifacts/polish-review-150-${theme}.png`, fullPage: true });
        }
        await page.emulateMedia({ media: 'print' });
        assert.equal(await script.isVisible(), true, 'Print retains review-only scripts');
        assert.equal(await page.locator('.proposal-evidence').first().getByRole('link').isVisible(), true, 'Print expands collapsed sources');
        assert.equal(await page.locator('.review-support li').isVisible(), true, 'Print retains user-supplied support provenance');
        await page.emulateMedia({ media: 'screen' });
        for (const width of [907, 760, 540]) {
          await page.setViewportSize({ width, height: 573 });
          assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false);
        }
        await page.setViewportSize({ width: 907, height: 573 });
        await page.getByRole('button', { name: 'AI provider', exact: true }).click();
        assert.equal(await page.getByLabel('Base URL', { exact: true }).count(), 0, 'Fixed endpoints are facts, not misleading editable fields');
        await page.locator('.provider-endpoint-info').getByText('https://openrouter.ai/api/v1', { exact: true }).waitFor();
        assert.equal(await page.getByLabel('API key', { exact: true }).evaluate(input => getComputedStyle(input).paddingLeft), '36px', 'Unified controls retain space for the credential icon');
        await page.getByRole('button', { name: 'Local', exact: true }).click();
        await page.getByRole('button', { name: 'LM Studio', exact: true }).click();
        assert.equal(await page.getByLabel('Base URL', { exact: true }).inputValue(), 'http://127.0.0.1:1234/v1');
        await page.getByRole('group', { name: 'Provider sections', exact: true }).getByRole('button', { name: 'AI investigation budget', exact: true }).click();
        assert.equal(await page.getByLabel('Maximum turns', { exact: true }).isVisible(), true);
        await page.getByRole('button', { name: 'Connection', exact: true }).click();
        await page.getByRole('button', { name: 'Test & discover models', exact: true }).click();
        await page.getByText('Provider operation in progress…', { exact: true }).waitFor();
        assert.equal(await page.getByLabel('Maximum turns', { exact: true }).isDisabled(), true);
        assert.equal(await page.getByLabel('Model', { exact: true }).isDisabled(), true);
        await page.getByText('Error: Model discovery unavailable in this mock. No fallback.', { exact: true }).waitFor();
        for (const width of [1365, 907, 540]) {
          await page.setViewportSize({ width, height: 573 });
          assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false);
        }
        await page.setViewportSize({ width: 907, height: 573 });
        await page.evaluate(() => { window.scrollTo(0, 0); document.activeElement?.blur(); });
        for (const theme of ['light', 'dark']) {
          await page.evaluate(theme => { document.documentElement.dataset.theme = theme; }, theme);
          await page.screenshot({ path: `artifacts/polish-provider-150-${theme}.png`, fullPage: true });
        }
        await page.getByRole('button', { name: 'Measurements', exact: true }).click();
        await page.getByRole('heading', { name: '5 measurement sessions', exact: true }).waitFor();
        assert.equal(await page.getByRole('button', { name: 'Compare selected', exact: true }).isDisabled(), true);
        await page.getByLabel('Select baseline for comparison', { exact: true }).check();
        await page.getByLabel('Select candidate for comparison', { exact: true }).check();
        assert.equal(await page.getByLabel('Select captured for comparison', { exact: true }).isDisabled(), true);
        assert.equal(await page.locator('.measurement-row').nth(2).getByRole('button', { name: 'Use in AI', exact: true }).isDisabled(), true);
        assert.equal(await page.locator('.measurement-row').nth(1).getByText('Invalid trace', { exact: true }).count(), 1);
        await page.getByRole('button', { name: 'Compare selected', exact: true }).click();
        await page.getByText('Candidate trace failed the quality gate.', { exact: true }).waitFor();
        assert.equal(await page.getByRole('button', { name: 'Ask selected AI to explain', exact: true }).count(), 0);
        await page.locator('.measurement-row').last().getByRole('button', { name: 'Analyze', exact: true }).click();
        await page.getByText('Mock analysis failure; retry is available.', { exact: true }).waitFor();
        assert.equal(await page.locator('.measurement-row').last().getByRole('button', { name: 'Analyze', exact: true }).isEnabled(), true);
        await page.evaluate(() => { window.scrollTo(0, 0); document.activeElement?.blur(); });
        for (const theme of ['light', 'dark']) {
          await page.evaluate(theme => { document.documentElement.dataset.theme = theme; }, theme);
          await page.screenshot({ path: `artifacts/polish-measurements-150-${theme}.png`, fullPage: true });
        }
        for (const width of [907, 760, 540]) {
          await page.setViewportSize({ width, height: 573 });
          assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false);
        }
        const logo = await page.request.get(url + 'logo.svg'); const favicon = await page.request.get(url + 'favicon.svg');
        assert.equal(await logo.text(), await favicon.text());
        assert.equal(await page.locator('.brand-mark').getAttribute('src'), '/logo.svg');
      }
      assert.equal(await page.evaluate(() => window.requests.some(item => /^(apply|run-approve|measurement-start|oauth-)/.test(item.command))), false);
      assert.deepEqual(errors, []);
      await page.close();
    }
    console.log('PASS: provider loading/errors, blocked/high-risk apply, inert scripts/copy failure/print provenance, measurement history/quality/retry/comparison, logo consistency and narrow light/dark layouts. No system writes.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
