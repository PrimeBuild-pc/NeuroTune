// Run against `npm run preview` with Playwright available (or set PLAYWRIGHT_PATH).
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require(process.env.PLAYWRIGHT_PATH || 'playwright');
const url = process.env.UI_PREVIEW_URL || 'http://127.0.0.1:4173/';

(async () => {
  fs.mkdirSync('artifacts', { recursive: true });
  const browser = await chromium.launch({ headless: true, ...(process.env.CHROME_PATH ? { executablePath: process.env.CHROME_PATH } : {}) });
  try {
    for (const startup of ['normal', 'recording', 'reduced', 'reduce-during', 'quiet-during', 'failure']) {
      const recording = startup === 'recording';
      const animated = ['normal', 'reduce-during', 'quiet-during'].includes(startup);
      const page = await browser.newPage({ viewport: { width: 1365, height: 768 }, reducedMotion: startup === 'reduced' ? 'reduce' : 'no-preference' });
      const errors = []; page.on('pageerror', error => errors.push(error.message));
      const media = await page.context().newCDPSession(page);
      // Test the glass path independently of the host's Windows transparency preference.
      await media.send('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-reduced-transparency', value: 'no-preference' }, { name: 'prefers-reduced-motion', value: startup === 'reduced' ? 'reduce' : 'no-preference' }] });
      await page.addInitScript(startup => {
        localStorage.setItem('neurotune.setupReviewed.v1', 'true'); // Existing configured session; onboarding has its own check.
        const recording = startup === 'recording';
        const stateGate = new Promise(resolve => { window.releaseStartupState = resolve; });
        const bootGate = new Promise(resolve => { window.releaseStartupBoot = resolve; });
        let next = 0; let sessions = []; let run;
        const callbacks = new Map();
        const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
        window.requests = [];
        window.__TAURI_EVENT_PLUGIN_INTERNALS__ = { unregisterListener() {} };
        window.__TAURI_INTERNALS__ = {
          transformCallback(fn) { const id = ++next; callbacks.set(id, fn); return id; },
          async invoke(command, args) {
            if (command === 'plugin:event|listen') return ++next;
            if (command === 'plugin:event|unlisten') return;
            if (command !== 'agent') return true;
            window.requests.push(args.command);
            switch (args.command) {
              case 'get-state': await stateGate; if (startup === 'failure') throw Error('Startup mock failure'); return { settings: { provider: 'local', providerName: 'Mock local AI', model: 'test', requiresApiKey: false }, hasCredential: false, isRecording: recording };
              case 'actions': await bootGate; return [];
              case 'history': case 'run-list': return [];
              case 'measurement-workloads': return [];
              case 'defender-scan-current': return null;
              case 'save-provider': return { saved: true, hasCredential: false };
              case 'scan': await pause(600); return { profile: {}, actions: [] };
              case 'measurement-start': await pause(600); sessions = [{ id: 'mock', state: 'recording', systemWide: true, durationSeconds: 60, recordingStartedAtUtc: new Date(Date.now() - 60000).toISOString() }]; return sessions[0];
              case 'measurement-list': return sessions.map(session => ({ ...session, state: 'captured' }));
              case 'measurement-analyze': await pause(400); return { ...sessions[0], state: 'completed', report: { quality: { isValid: true } } };
              case 'run-create': run = { id: 'mock-run', state: 'scanned', goals: args.payload.goals, approvedActionIds: [], baselineSessionIds: [], candidateSessionIds: [] }; return run;
              case 'diagnose': await pause(1200); return { summary: 'Mock read-only diagnosis', findings: [], conflicts: [], recommendations: [], consentQuestion: 'Review before changes' };
              case 'run-get': return { ...run, state: 'baselinePending' };
              default: throw Error('Unexpected mock command: ' + args.command);
            }
          },
        };
      }, startup);
      await page.goto(url);
      await page.locator('.app-shell').waitFor({ state: 'attached' });
      const idle = () => page.evaluate(() => document.getAnimations().filter(animation => animation.playState === 'running').length);
      assert.equal(await idle(), 0, 'Unknown recorder state must never animate the opening');
      assert.equal(await page.locator('html').evaluate(el => getComputedStyle(el).overflowY), 'hidden');
      assert.equal(await page.locator('.sidebar nav').evaluate(el => getComputedStyle(el).overflowY), 'hidden');
      assert.equal(await page.locator('#root').evaluate(el => getComputedStyle(el).visibility), 'hidden');
      assert.equal(await page.locator('.app-shell').evaluate(el => el.inert), true);
      await page.keyboard.press('Tab');
      assert.equal(await page.locator('.app-shell').evaluate(el => el.contains(document.activeElement)), false, 'No focus behind the opening curtain');
      if (animated) await page.evaluate(() => {
        const observer = new MutationObserver(() => {
          if (document.documentElement.dataset.motion !== 'full') return;
          window.startupAnimations = document.getElementById('startup').getAnimations({ subtree: true });
          for (const animation of window.startupAnimations) { animation.pause(); animation.currentTime = 180; }
          observer.disconnect();
        });
        observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-motion'] });
      });
      await page.evaluate(() => window.releaseStartupState());
      if (animated) {
        await page.waitForFunction(() => window.startupAnimations?.length > 0);
        assert.notEqual(await page.locator('.startup-logo').evaluate(el => getComputedStyle(el).transform), 'none', 'The actual brand mark animates, not only its surrounding rings');
        assert.equal(await page.locator('.app-shell').evaluate(el => el.inert), true);
        if (startup === 'normal') await page.screenshot({ path: 'artifacts/polish-startup-logo.png' });
        await page.evaluate(() => window.releaseStartupBoot());
        await page.waitForTimeout(50);
        assert.equal(await page.locator('#startup').count(), 1, 'Fast initialization still lets the finite intro finish');
        if (startup === 'reduce-during') await page.emulateMedia({ reducedMotion: 'reduce' });
        else if (startup === 'quiet-during') await page.evaluate(() => { document.documentElement.dataset.motion = 'quiet'; });
        else await page.evaluate(() => window.startupAnimations.forEach(animation => animation.play()));
      } else await page.evaluate(() => window.releaseStartupBoot());
      await page.locator('#startup').waitFor({ state: 'detached' });
      assert.equal(await page.locator('html').getAttribute('data-startup'), null);
      assert.notEqual(await page.locator('html').evaluate(el => getComputedStyle(el).overflowY), 'hidden', 'Document scrolling returns only after entering');
      assert.equal(await page.locator('.sidebar nav').evaluate(el => getComputedStyle(el).overflowY), 'auto');
      assert.equal(await page.locator('.app-shell').evaluate(el => el.inert), false);
      const blur = () => page.locator('.sidebar, .topbar, .hero-panel').evaluateAll(elements => elements.map(el => getComputedStyle(el).backdropFilter));
      if (!['normal', 'recording'].includes(startup)) {
        assert.equal(await idle(), 0, 'Reduced/quiet/failure paths release the UI without animation');
        assert.ok((await blur()).every(value => value === 'none'));
        if (startup === 'failure') await page.getByText('Startup mock failure', { exact: true }).waitFor();
        assert.deepEqual(errors, []);
        await page.close();
        continue;
      }
      const initialBlur = await blur();
      assert.ok(initialBlur.every(value => recording ? value === 'none' : value.includes('blur(12px)')), `Chrome policy ${startup}: ${JSON.stringify(initialBlur)}`);
      if (recording) {
        assert.equal(await page.locator('html').getAttribute('data-motion'), 'quiet');
        assert.equal(await idle(), 0);
        await page.getByRole('button', { name: 'AI provider', exact: true }).click();
        assert.equal(await idle(), 0);
        assert.equal(await page.locator('.nav-active-background').evaluate(el => getComputedStyle(el).transform), 'none');
      } else {
        assert.ok(await idle() > 0, 'Normal mode has core orbit motion');
        for (const theme of ['light', 'dark']) {
          await page.evaluate(theme => { document.documentElement.dataset.theme = theme; }, theme);
          await page.waitForTimeout(300);
          await page.screenshot({ path: `artifacts/polish-overview-${theme}.png`, fullPage: true });
        }
        // Advance JS frames deterministically: real CPU contention may skip a whole 220 ms animation.
        // Observe the first actual transform and freeze in that task to prove in-flight cancellation.
        await page.clock.install({ time: new Date('2026-01-01T00:00:00Z') });
        await page.clock.pauseAt(new Date('2026-01-01T00:01:00Z')); // Future in the installed clock, even under CPU contention.
        await page.evaluate(() => {
          window.navigationMoved = false;
          const observer = new MutationObserver(() => {
            const indicator = document.querySelector('.nav-active-background');
            if (!indicator || getComputedStyle(indicator).transform === 'none') return;
            window.navigationMoved = true;
            document.documentElement.dataset.motion = 'quiet';
            observer.disconnect();
          });
          observer.observe(document.body, { subtree: true, childList: true, attributes: true, attributeFilter: ['style'] });
        });
        await page.evaluate(() => document.querySelector('.nav-item[aria-label="AI provider"]').click());
        await page.getByRole('heading', { name: 'Model connection', exact: true }).waitFor();
        await page.clock.runFor(240);
        assert.equal(await page.evaluate(() => window.navigationMoved), true, 'Active indicator shares layout between navigation items');
        await page.clock.resume();
        await page.waitForTimeout(80);
        assert.equal(await idle(), 0);
        assert.ok((await blur()).every(value => value === 'none'), 'Quiet cancels all backdrop blur');
        assert.equal(await page.locator('.nav-active-background').evaluate(el => getComputedStyle(el).transform), 'none');
        await page.evaluate(() => { document.documentElement.dataset.motion = 'full'; });
        await page.emulateMedia({ reducedMotion: 'reduce' });
        await page.getByRole('button', { name: 'Overview', exact: true }).click();
        await page.waitForTimeout(80);
        assert.ok((await blur()).every(value => value === 'none'), 'Reduced motion also removes transparent backdrops');
        assert.equal(await idle(), 0, 'Reduced motion works even when changed at runtime');
        assert.equal(await page.locator('.nav-active-background').evaluate(el => getComputedStyle(el).transform), 'none');
        await page.keyboard.press('Tab');
        await page.getByRole('button', { name: 'Overview', exact: true }).focus();
        assert.notEqual(await page.locator('.nav-item.active').evaluate(el => getComputedStyle(el).outlineStyle), 'none');
        for (const width of [907, 760, 540]) {
          await page.setViewportSize({ width, height: 573 });
          assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false);
          assert.equal(await page.getByRole('button', { name: 'Overview', exact: true }).count(), 1);
          if (width === 907) {
            const action = await page.locator('.hero-copy .primary').boundingBox();
            assert.ok(action.y + action.height < 573, 'Hero action fits a small laptop at 150% scaling');
            await page.screenshot({ path: 'artifacts/polish-overview-150-dark.png', fullPage: true });
          }
        }
        await page.setViewportSize({ width: 907, height: 573 });
        await page.emulateMedia({ reducedMotion: 'no-preference' });
        await media.send('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-reduced-transparency', value: 'reduce' }] });
        assert.equal(await page.evaluate(() => matchMedia('(prefers-reduced-transparency: reduce)').matches), true);
        assert.ok((await blur()).every(value => value === 'none'), 'Reduced transparency has an independent solid fallback');
        await media.send('Emulation.setEmulatedMedia', { features: [] });
        await page.getByRole('button', { name: 'Complete diagnosis', exact: true }).first().click();
        await page.getByRole('button', { name: 'Complete diagnosis', exact: true }).last().click();
        await page.locator('.neural-core[data-state="scan"]').waitFor();
        await page.locator('.neural-core[data-state="capture"]').waitFor({ timeout: 15000 });
        assert.equal(await page.locator('html').getAttribute('data-motion'), 'quiet');
        assert.equal(await idle(), 0);
        await page.locator('.neural-core[data-state="ai"]').waitFor();
        assert.equal(await page.locator('html').getAttribute('data-motion'), 'full');
        await page.screenshot({ path: 'artifacts/polish-diagnosis-dark.png', fullPage: true });
        await page.getByText('Mock read-only diagnosis', { exact: true }).waitFor();
        assert.equal(await page.evaluate(() => window.requests.some(command => /^(apply|run-approve)$/.test(command))), false);
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false);
      }
      assert.deepEqual(errors, []);
      await page.close();
    }
    console.log('UI polish passed: finite logo opening, no startup scrollbar/focus leak, unknown/existing capture and startup failure, live intro cancellation, glass/solid fallbacks, light/dark, narrow layout, keyboard focus, in-flight quiet cancellation, actual AI stages and no writes.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
