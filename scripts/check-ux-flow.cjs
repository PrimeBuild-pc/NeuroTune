// Focused UX regressions. All Agent commands are mocked; never starts WPR, a provider or a writer.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require(process.env.PLAYWRIGHT_PATH || 'playwright');
const url = process.env.UI_PREVIEW_URL || 'http://127.0.0.1:4173/';

(async () => {
  fs.mkdirSync('artifacts', { recursive: true });
  const browser = await chromium.launch({ headless: true, ...(process.env.CHROME_PATH ? { executablePath: process.env.CHROME_PATH } : {}) });
  try {
    for (const scenario of ['setup', 'draft', 'retained', 'layout']) {
      const page = await browser.newPage({ viewport: { width: 907, height: 573 }, reducedMotion: 'reduce', colorScheme: 'dark' });
      const errors = []; page.on('pageerror', error => errors.push(error.message));
      await page.addInitScript(scenario => {
        let next = 0;
        window.requests = [];
        const goals = { priority: 'balanced', riskProfile: 'safe', games: [], notes: '', gameContext: { game: '', version: '', launcher: '', graphicsApi: '', displayMode: '', vrr: '', vSync: '', symptoms: [], preserve: '' }, performanceInput: { userProvided: true, notes: '' } };
        const action = { id: 'safe', name: 'Safe registered action', description: 'Mock only', risk: 'low', requiresRestart: false, availability: { canApply: true, status: 'Ready', currentValue: 'Default' } };
        const diagnosis = { summary: 'Mock diagnosis', findings: [], conflicts: [], recommendations: [{ id: 'safe', kind: 'executableAction', actionId: 'safe', title: 'Mock proposal', reason: 'Unverified mock', risk: 'low', evidenceIds: [], prerequisites: [], tradeoffs: [], reviewWarnings: [], sourceReferences: [] }], consentQuestion: 'Review mock proposal' };
        const run = { id: 'retained', state: 'baselineReady', goals, diagnosis, approvedActionIds: [], baselineSessionIds: [], candidateSessionIds: [] };
        const report = valid => ({ quality: { durationMilliseconds: 60000, etlBytes: 1000, eventsLost: valid ? 0 : 8, missingProviders: [], targetPresencePercent: 99, isValid: valid }, threads: [], interrupts: [], processors: [], observations: [], limitations: [] });
        const sessions = Array.from({ length: 25 }, (_, index) => ({ id: `session${index}`, processName: `game${index}.exe`, processId: index + 1, processStartTimeUtc: '2026-01-01T00:00:00Z', createdAtUtc: '2026-01-01T00:00:00Z', durationSeconds: 60, keepRawTrace: false, label: index % 2 ? 'candidate' : 'baseline', state: index === 23 ? 'failed' : 'completed', ...(index === 23 ? { error: 'Mock retryable error' } : { report: report(index !== 24) }) }));
        window.__TAURI_EVENT_PLUGIN_INTERNALS__ = { unregisterListener() {} };
        window.__TAURI_INTERNALS__ = {
          transformCallback() { return ++next; },
          async invoke(command, args) {
            if (command.startsWith('plugin:event|')) return ++next;
            if (command !== 'agent') return true;
            window.requests.push(args.command);
            switch (args.command) {
              case 'get-state': return { settings: { provider: 'local', providerName: 'Mock local AI', model: scenario === 'setup' ? '' : 'test', protocol: 'openAiCompatible', baseUrl: 'http://127.0.0.1:11434/v1', requiresApiKey: false }, hasCredential: false, isRecording: false };
              case 'actions': return [action];
              case 'history': return [];
              case 'run-list': return scenario === 'retained' ? [run] : [];
              case 'run-reconcile': return run;
              case 'measurement-workloads': return [{ processId: 42, startTimeUtc: '2026-01-01T00:00:00Z', name: 'game.exe' }];
              case 'measurement-list': return sessions;
              case 'defender-scan-current': return null;
              case 'power-plan-list': return { directory: 'mock', plans: [] };
              case 'system-one-status': return { installed: false, running: false, health: 'notInstalled', cpuThreads: 0, installable: false, providers: [], description: 'Mock' };
              case 'support-preview': return args.payload.attachments.map(file => ({ ...file, sha256: 'a'.repeat(64) }));
              default: throw Error('Unexpected Agent operation: ' + args.command);
            }
          },
        };
      }, scenario);
      await page.goto(url); await page.locator('#startup').waitFor({ state: 'detached' });
      const nav = page.getByRole('navigation');
      if (scenario === 'setup') {
        assert.equal(await page.locator('.connection small').innerText(), 'Select a model');
        assert.equal(await page.locator('.hero-copy .primary').innerText(), 'Connect a provider');
        await nav.getByRole('button', { name: 'Complete diagnosis', exact: true }).click();
        await page.getByRole('button', { name: 'Complete diagnosis', exact: true }).last().waitFor();
        assert.equal(await page.locator('.diagnosis-submit > button.primary').isDisabled(), true);
        assert.equal(await page.locator('.diagnosis-blocked').innerText().then(text => text.includes('Select a model')), true);
        await page.getByRole('button', { name: 'Connect a provider', exact: true }).click();
        await page.getByRole('heading', { name: 'Model connection', exact: true }).waitFor();
      } else if (scenario === 'draft') {
        assert.equal(await page.locator('html').getAttribute('data-theme'), 'dark');
        const themeButton = page.getByRole('button', { name: 'Toggle light and dark theme', exact: true });
        await themeButton.click();
        assert.equal(await page.locator('html').getAttribute('data-theme'), 'light', 'Toggle resolves the dark Windows preference');
        await nav.getByRole('button', { name: 'Complete diagnosis', exact: true }).click();
        const form = page.locator('.complete-diagnosis');
        await form.getByRole('combobox', { name: 'Running game / workload', exact: true }).selectOption('42');
        await form.getByRole('textbox', { name: 'Improve / preserve', exact: true }).fill('Preserve this draft');
        await form.getByRole('group', { name: 'Seconds per trace', exact: true }).getByRole('radio', { name: '30 s', exact: true }).check();
        const sections = form.getByRole('group', { name: 'Diagnosis sections', exact: true });
        await sections.getByRole('button', { name: /^Supporting files/ }).click();
        await form.getByLabel('Add reports or screenshots', { exact: true }).setInputFiles({ name: 'context.txt', mimeType: 'text/plain', buffer: Buffer.from('Mock user context, not verified evidence.') });
        const consent = form.getByRole('checkbox', { name: 'I reviewed the previews and authorize sending this content to the main provider when I start diagnosis.', exact: true });
        await consent.check();
        await page.getByRole('button', { name: 'Quick Settings', exact: true }).click();
        assert.equal(await nav.locator('details').count(), 0, 'All destinations remain exposed without disclosure navigation');
        assert.equal(await page.locator('.settings-advanced').count(), 0, 'Application preferences contain no optional tools');
        assert.equal(await page.evaluate(() => window.requests.some(command => /^(power-plan-list|system-one-status)$/.test(command))), false, 'Opening preferences never initializes optional tools');
        assert.equal(await nav.evaluate(el => {
          const active = el.querySelector('[aria-current="page"]').getBoundingClientRect();
          const bounds = el.getBoundingClientRect();
          return active.top >= bounds.top && active.bottom <= bounds.bottom;
        }), true, 'Direct Settings navigation scrolls only the sidebar to expose its active item');
        const appearance = page.getByRole('button', { name: /Use Windows setting/ });
        const languageBox = await page.locator('.language-settings').boundingBox();
        const appearanceBox = await appearance.boundingBox();
        assert.ok(languageBox.y < appearanceBox.y);
        await appearance.click();
        await page.emulateMedia({ colorScheme: 'light' });
        await page.waitForFunction(() => document.documentElement.dataset.theme === 'light');
        assert.equal(await page.locator('html').getAttribute('data-theme'), 'light');
        await page.emulateMedia({ colorScheme: 'dark' });
        await page.waitForFunction(() => document.documentElement.dataset.theme === 'dark');
        assert.equal(await page.locator('html').getAttribute('data-theme'), 'dark');
        await nav.getByRole('button', { name: 'Complete diagnosis', exact: true }).click();
        assert.equal(await consent.isChecked(), true, 'Appearance navigation preserves draft consent');
        await sections.getByRole('button', { name: 'Diagnosis setup', exact: true }).click();
        assert.equal(await form.getByRole('combobox', { name: 'Running game / workload', exact: true }).inputValue(), '42');
        assert.equal(await form.getByRole('radio', { name: '30 s', exact: true }).isChecked(), true);
        assert.equal(await form.getByRole('textbox', { name: 'Improve / preserve', exact: true }).inputValue(), 'Preserve this draft');
        await sections.getByRole('button', { name: /^Supporting files/ }).click();
        await nav.getByRole('button', { name: 'AI provider', exact: true }).click();
        await page.getByLabel('Model', { exact: true }).fill('another-model');
        await nav.getByRole('button', { name: 'Complete diagnosis', exact: true }).click();
        assert.equal(await form.getByText('context.txt', { exact: true }).count(), 1);
        assert.equal(await consent.isChecked(), false, 'Changing the consented model requires fresh attachment approval');
        for (const theme of ['light', 'dark']) {
          await page.evaluate(theme => { document.documentElement.dataset.theme = theme; }, theme);
          await page.evaluate(() => { document.activeElement?.blur(); window.scrollTo(0, 0); });
          await page.screenshot({ path: `artifacts/ux-diagnosis-${theme}.png`, fullPage: true });
        }
        await nav.getByRole('button', { name: 'Measurements', exact: true }).click();
        await page.getByRole('searchbox', { name: 'Search sessions', exact: true }).waitFor();
        const rows = page.locator('.measurement-history-panel .measurement-row');
        assert.equal(await rows.count(), 12);
        await page.getByRole('checkbox', { name: 'Select session0 for comparison', exact: true }).check();
        await page.getByRole('button', { name: 'Show more', exact: true }).click();
        assert.equal(await rows.count(), 24);
        const beforeFilter = await page.evaluate(() => window.requests.length);
        await page.getByRole('combobox', { name: 'Show sessions', exact: true }).selectOption('needsAnalysis');
        assert.equal(await rows.count(), 1);
        await rows.getByText('Mock retryable error', { exact: true }).waitFor();
        assert.equal(await page.locator('.comparison-counts').innerText().then(text => text.includes('1 baseline selected')), true);
        await page.getByRole('combobox', { name: 'Show sessions', exact: true }).selectOption('all');
        await page.getByRole('searchbox', { name: 'Search sessions', exact: true }).fill('game24.exe');
        assert.equal(await rows.count(), 1);
        assert.equal(await page.evaluate(() => window.requests.length), beforeFilter, 'Filters are local and never opt evidence into AI');
        await rows.getByRole('button', { name: /game24.exe/ }).click();
        await page.waitForFunction(() => document.activeElement?.id === 'measurement-report');
        assert.equal(await page.locator('#measurement-report').evaluate(el => document.activeElement === el), true);
        await page.locator('#measurement-report').getByText('Invalid trace', { exact: true }).waitFor();
        await page.getByRole('link', { name: 'Back to session history', exact: true }).click();
        assert.equal(await page.locator('#measurement-history-title').evaluate(el => document.activeElement === el), true);
        for (const theme of ['light', 'dark']) {
          await page.evaluate(theme => { document.documentElement.dataset.theme = theme; }, theme);
          for (const width of [907, 760, 540]) {
            await page.setViewportSize({ width, height: 573 });
            assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false);
            await nav.getByRole('button', { name: 'Settings', exact: true }).focus();
            const activeBox = await nav.getByRole('button', { name: 'Settings', exact: true }).boundingBox();
            const footBox = await page.locator('.sidebar-foot').boundingBox();
            assert.ok(activeBox.y + activeBox.height <= footBox.y, `Grouped navigation never overlaps the sidebar footer (${width}: ${JSON.stringify({ activeBox, footBox })})`);
          }
          await page.setViewportSize({ width: 907, height: 573 });
          await page.evaluate(() => { document.activeElement?.blur(); window.scrollTo(0, 0); });
          await page.screenshot({ path: `artifacts/ux-measurements-${theme}.png`, fullPage: true });
        }
      } else if (scenario === 'retained') {
        await page.getByRole('button', { name: /Safe registered action/ }).click();
        await page.getByText('1 changes selected', { exact: true }).waitFor();
        await nav.getByRole('button', { name: 'Overview', exact: true }).click();
        assert.equal(await page.locator('.hero-copy .primary').innerText(), 'Review changes');
        await page.locator('.hero-copy .primary').click();
        await page.getByText('1 changes selected', { exact: true }).waitFor();
        await page.getByRole('link', { name: 'Review decision', exact: true }).click();
        assert.equal(await page.locator('#review-decision').evaluate(el => document.activeElement === el), true);
      } else {
        const targets = [
          ['provider', 'Connection'], ['provider', 'AI investigation budget'],
          ['scan', 'Diagnosis setup'], ['scan', 'Optional context'], ['scan', 'Supporting files'],
          ['security', 'Privacy & AI'], ['security', 'Defender status'], ['security', 'User-approved Defender scan'], ['security', 'Manual tools'],
          ['tools', 'BIOS / UEFI inspection'], ['tools', 'System One assistant'], ['tools', 'Custom power plans'], ['tools', 'Optional low-level telemetry'],
          ['settings', null],
        ];
        for (const theme of ['light', 'dark']) {
          await page.evaluate(theme => { document.documentElement.dataset.theme = theme; }, theme);
          for (const [destination, section] of targets) {
            await nav.locator(`[data-page="${destination}"]`).click();
            if (section) await page.locator('.page-content .section-switcher:visible').first().getByRole('button', { name: section, exact: true }).click();
            for (const width of [1360, 907, 760, 540]) {
              await page.setViewportSize({ width, height: width === 1360 ? 860 : 573 });
              const facts = await page.evaluate(() => {
                const visible = [...document.querySelectorAll('.app-shell *')].filter(el => el.getClientRects().length && getComputedStyle(el).visibility !== 'hidden');
                const expectedFont = getComputedStyle(document.body).fontFamily;
                const fonts = [...new Set(visible.filter(el => el.matches('button,input,select,textarea,pre,code,h1,h2,h3,p,span,label')).map(el => getComputedStyle(el).fontFamily))];
                const controls = visible.filter(el => el.matches('select,input:not([type="checkbox"]):not([type="radio"]):not([type="file"])')).map(el => ({ height: el.getBoundingClientRect().height, fontSize: getComputedStyle(el).fontSize }));
                const toggles = visible.filter(el => el.matches('input[type="checkbox"],input[type="radio"]')).map(el => ({ width: el.getBoundingClientRect().width, height: el.getBoundingClientRect().height }));
                const choiceWidths = visible.filter(el => el.matches('.choice-option > span')).map(el => ({ width: el.getBoundingClientRect().width, textLength: el.textContent.trim().length }));
                return { expectedFont, fonts, controls, toggles, choiceWidths, overflow: document.documentElement.scrollWidth > innerWidth };
              });
              assert.equal(facts.overflow, false, `${destination}/${section}/${theme}/${width}`);
              assert.deepEqual(facts.fonts, [facts.expectedFont], 'One font across labels, controls, headings and evidence');
              for (const control of facts.controls) assert.deepEqual(control, { height: 42, fontSize: '14px' }, `${destination}/${section}: uniform control sizes`);
              for (const toggle of facts.toggles) assert.deepEqual(toggle, { width: 18, height: 18 }, 'Choice controls cannot inherit full-width text-input styling');
              for (const choice of facts.choiceWidths) assert.ok(choice.width >= Math.min(80, choice.textLength * 4), `${destination}/${section}: choice labels remain readable, never wrapped one character per line`);
              if (width === 1360 || width === 907) {
                await page.evaluate(() => { document.activeElement?.blur(); window.scrollTo(0, 0); });
                await page.mouse.move(width - 2, 2);
                await page.screenshot({ path: `artifacts/ux-consistency-${destination}-${(section || 'preferences').toLowerCase().replace(/[^a-z0-9]+/g, '-')}-${width}-${theme}.png`, fullPage: true });
              }
            }
          }
        }
        await nav.locator('[data-page="tools"]').click();
        await page.getByRole('button', { name: 'Custom power plans', exact: true }).click();
        await page.getByLabel('Power-plan folder', { exact: true }).fill('Preserved folder draft');
        await nav.locator('[data-page="settings"]').click();
        await nav.locator('[data-page="tools"]').click();
        assert.equal(await page.getByLabel('Power-plan folder', { exact: true }).inputValue(), 'Preserved folder draft');
        assert.equal(await page.getByLabel('Power-plan folder', { exact: true }).isVisible(), true, 'Tool section and draft survive navigation');
        assert.equal(await page.evaluate(() => window.requests.filter(command => command === 'power-plan-list').length), 1, 'Retained tools are not remounted');
        assert.equal(await page.evaluate(() => window.requests.some(command => /^(system-one-(configure|install|start|models)|scewin-(configure|install|export)|power-plan-stage|firmware-scan|defender-status)$/.test(command))), false, 'Changing sections cannot authorize or start optional operations');
      }
      assert.deepEqual(errors, []);
      assert.equal(await page.evaluate(() => window.requests.some(command => /^(scan|diagnose|defender-scan|measurement-start|apply|run-approve|save-provider)$/.test(command))), false);
      await page.close();
    }
    console.log('PASS: truthful setup, resolved/live theme, draft preservation, attachment reconsent, grouped navigation, uniform fonts/42px controls across 14 sections at 1360/907/760/540px, retained optional tools, bounded local history, report focus and retained action selection. All Agent calls mocked.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
