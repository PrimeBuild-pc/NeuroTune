// Attach only to a locally launched, instrumented Tauri Release WebView2.
// NATIVE_CAPTURE=1 explicitly enables real, system-wide WPR smoke captures.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { createHash } = require('node:crypto');
const { chromium } = require(process.env.PLAYWRIGHT_PATH || 'playwright');
const endpoint = process.env.NATIVE_CDP_URL || 'http://127.0.0.1:9224';
const capture = process.env.NATIVE_CAPTURE === '1';
const measurements = path.join(process.env.LOCALAPPDATA, 'NeuroTune', 'measurements');

(async () => {
  fs.mkdirSync('artifacts', { recursive: true });
  const browser = await chromium.connectOverCDP(endpoint);
  const page = browser.contexts().flatMap(context => context.pages()).find(page => page.url().startsWith('http://tauri.localhost/'));
  if (!page) { await browser.close(); throw Error('A real packaged Tauri page is required; browser mocks are not accepted.'); }
  const errors = []; page.on('pageerror', error => errors.push(error.message));
  const report = { scope: 'Instrumented Release WebView2; real agent/WPR, no mocks. Logical viewport emulation is not Windows DPI or performance-overhead acceptance.', captureEnabled: capture, checks: [], captures: [], startResponses: [] };
  let originalTheme;
  const owned = new Set();
  const commands = [];
  let originalHistory;
  async function agent(command, payload = null) {
    commands.push(command);
    return page.evaluate(({ command, payload }) => window.__TAURI_INTERNALS__.invoke('agent', { requestId: crypto.randomUUID(), command, payload }), { command, payload });
  }
  async function layoutCheck(name) {
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, `${name}: no horizontal overflow`);
    report.checks.push(name);
  }
  async function startCapture() {
    const started = Date.now();
    await page.getByRole('button', { name: 'Start measurement', exact: true }).click();
    await page.getByRole('button', { name: 'Stop', exact: true }).waitFor({ timeout: 90000 });
    const selection = await page.locator('.measurement-row.selected input').getAttribute('aria-label');
    const id = selection.match(/^Select ([a-f0-9-]{36}) for comparison$/)?.[1];
    assert.ok(id, 'The real Start response focuses its own session in history.');
    owned.add(id); // Register cleanup before any subsequent assertion can fail.
    assert.ok((await page.locator('.measurement-row.selected .status-pill').textContent()).includes('recording'));
    assert.equal(await page.locator('html').getAttribute('data-motion'), 'quiet');
    assert.equal(await page.evaluate(() => document.getAnimations().filter(item => item.playState === 'running').length), 0);
    const elapsedMilliseconds = Date.now() - started;
    report.startResponses.push({ elapsedMilliseconds, returnedWhileRecording: true, motionFrozen: true });
    console.log(`Native Start response after ${elapsedMilliseconds} ms; motion frozen while recording.`);
    return id;
  }
  try {
    await page.locator('#startup').waitFor({ state: 'detached', timeout: 60000 });
    originalTheme = await page.locator('html').getAttribute('data-theme');
    report.agentSha256 = createHash('sha256').update(fs.readFileSync('ui/src-tauri/target/release/agent/NeuroTune.Agent.exe')).digest('hex');
    report.frontendAssets = fs.readdirSync('ui/dist/assets').map(name => ({ name, sha256: createHash('sha256').update(fs.readFileSync(path.join('ui/dist/assets', name))).digest('hex') }));
    report.webView = await browser.version();
    report.nativeViewport = await page.evaluate(() => ({ width: innerWidth, height: innerHeight, devicePixelRatio }));
    // Tauri's production invoke is read-only. Do not replace, mock or weaken its bridge.
    originalHistory = await agent('history');
    const state = await agent('get-state');
    assert.equal(state.isRecording, false, 'Do not interfere with an existing capture.');
    assert.equal(await page.locator('.app-shell').getAttribute('inert'), null);
    await page.getByRole('button', { name: 'Overview', exact: true }).click();
    assert.equal(await page.locator('.brand-mark').getAttribute('src'), '/logo.svg');
    assert.equal(await page.locator('.core-chip img').evaluate(img => img.complete && img.naturalWidth > 0), true);
    await page.waitForTimeout(300);
    assert.ok(await page.evaluate(() => document.getAnimations().some(item => item.playState === 'running')), 'Idle signature motion renders natively');
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await page.waitForTimeout(100);
    assert.equal(await page.evaluate(() => document.getAnimations().filter(item => item.playState === 'running').length), 0);
    for (const theme of ['light', 'dark']) {
      await page.evaluate(theme => { document.documentElement.dataset.theme = theme; }, theme);
      await page.setViewportSize({ width: 907, height: 573 });
      await page.evaluate(() => { window.scrollTo(0, 0); document.activeElement?.blur(); });
      await layoutCheck(`overview-${theme}-907`);
      await page.screenshot({ path: `artifacts/native-polish-overview-${theme}.png`, fullPage: true });
      await page.getByRole('button', { name: 'AI provider', exact: true }).click();
      await layoutCheck(`provider-${theme}-907`);
      await page.evaluate(() => { window.scrollTo(0, 0); document.activeElement?.blur(); });
      await page.screenshot({ path: `artifacts/native-polish-provider-${theme}.png`, fullPage: true });
      await page.getByRole('button', { name: 'Review changes', exact: true }).click();
      await layoutCheck(`review-${theme}-907`);
      await page.getByRole('button', { name: 'Overview', exact: true }).click();
    }
    await page.keyboard.press('Tab');
    await page.getByRole('button', { name: 'AI provider', exact: true }).focus();
    assert.notEqual(await page.locator('.nav-item[aria-label="AI provider"]').evaluate(el => getComputedStyle(el).outlineStyle), 'none');
    await page.getByRole('button', { name: 'Measurements', exact: true }).click();
    await page.locator('.measurement-history-panel[aria-busy="false"]').waitFor({ timeout: 60000 });
    await layoutCheck('real-history-907');
    await page.evaluate(() => { window.scrollTo(0, 0); document.activeElement?.blur(); });
    await page.screenshot({ path: 'artifacts/native-polish-measurements.png', fullPage: true });
    if (capture) {
      const sessionsBefore = await agent('measurement-list');
      assert.equal(sessionsBefore.some(item => item.state === 'recording'), false);
      await page.getByRole('checkbox', { name: /Monitor the entire system/ }).check();
      await page.getByRole('checkbox', { name: /Keep the raw ETL/ }).uncheck();
      await page.getByLabel(/Duration \(seconds\)/).fill('30');
      await page.emulateMedia({ reducedMotion: 'no-preference' });
      const id = await startCapture();
      // No extra native-agent polling during the measured interval. Existing UI/watchdog owns completion.
      const row = page.locator('.measurement-row').filter({ has: page.getByLabel(`Select ${id} for comparison`, { exact: true }) });
      await row.getByRole('button', { name: 'Analyze', exact: true }).waitFor({ timeout: 90000 });
      await row.getByRole('button', { name: 'Analyze', exact: true }).click();
      await row.getByText('Quality gate passed', { exact: true }).waitFor({ timeout: 120000 });
      const completed = (await agent('measurement-list')).find(item => item.id === id);
      assert.equal(completed.state, 'completed');
      assert.equal(completed.report.quality.isValid, true);
      assert.equal(completed.report.quality.eventsLost, 0);
      assert.equal(fs.existsSync(path.join(measurements, id, 'capture.etl')), false);
      report.captures.push({ path: 'watchdog/analyze', quality: completed.report.quality, processors: completed.report.processors.length, interrupts: completed.report.interrupts.length, rawTraceDeleted: true });
      // Give explicit Stop/Cancel a wider watchdog budget; they must not race automatic 30s completion.
      await page.getByLabel(/Duration \(seconds\)/).fill('120');
      const stoppedId = await startCapture();
      await page.getByRole('button', { name: 'Stop', exact: true }).click({ timeout: 90000 });
      await page.locator('.measurement-row').filter({ has: page.getByLabel(`Select ${stoppedId} for comparison`, { exact: true }) }).getByRole('button', { name: 'Analyze', exact: true }).waitFor({ timeout: 90000 });
      assert.equal((await agent('measurement-list')).find(item => item.id === stoppedId).state, 'captured');
      report.captures.push({ path: 'explicit-stop', saved: true, shortenedSmokeOnly: true });
      const cancelledId = await startCapture();
      await page.getByRole('button', { name: 'Cancel & delete', exact: true }).click({ timeout: 90000 });
      await page.locator('.measurement-feedback').waitFor({ state: 'detached', timeout: 90000 });
      assert.equal((await agent('measurement-list')).some(item => item.id === cancelledId), false);
      assert.equal(fs.existsSync(path.join(measurements, cancelledId)), false);
      report.captures.push({ path: 'cancel', incompleteDataDeleted: true });
      assert.equal(await page.locator('html').getAttribute('data-motion'), 'full');
    }
    assert.deepEqual(await agent('history'), originalHistory, 'Windows operation/rollback history remains unchanged.');
    report.directReadCommands = [...new Set(commands)];
    report.windowsOperationHistoryUnchanged = true;
    assert.deepEqual(errors, []);
    report.passed = true;
  } catch (error) {
    report.passed = false; report.error = error.message; throw error;
  } finally {
    try {
      // Cleanup is restricted to IDs returned by this test's real measurement-start calls.
      const sessions = owned.size ? await agent('measurement-list') : [];
      for (const id of owned) {
        const session = sessions.find(item => item.id === id);
        if (!session) continue;
        if (session.state === 'recording') await agent('measurement-cancel', { sessionId: id });
        else await agent('measurement-delete', { sessionId: id });
        assert.equal(fs.existsSync(path.join(measurements, id)), false);
      }
      if (owned.size) assert.equal((await agent('get-state')).isRecording, false);
      report.ownedSessionCleanup = true;
      await page.emulateMedia({ reducedMotion: null });
      await page.setViewportSize({ width: report.nativeViewport?.width || 1360, height: report.nativeViewport?.height || 860 });
      await page.evaluate(theme => {
        if (theme) document.documentElement.dataset.theme = theme;
      }, originalTheme);
    } catch (cleanupError) { report.passed = false; report.cleanupError = cleanupError.message; process.exitCode = 1; }
    fs.writeFileSync('artifacts/native-polish-validation.json', JSON.stringify(report, null, 2));
    await browser.close();
  }
  if (!report.passed) throw Error(report.cleanupError || 'Native validation failed.');
  console.log('PASS: native Release WebView2 rendering, logos, light/dark, keyboard, reduced motion and real local history' + (capture ? '; elevated WPR watchdog/analyze/Stop/Cancel, quiet freeze, zero lost events, owned-only cleanup.' : '. No capture requested.'));
})().catch(error => { console.error(error); process.exitCode = 1; });
