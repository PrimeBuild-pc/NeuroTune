// Mocked browser validation only: never calls Defender, WPR or a provider.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require(process.env.PLAYWRIGHT_PATH || 'playwright');
(async () => {
  const browser = await chromium.launch({ headless: true, ...(process.env.CHROME_PATH ? { executablePath: process.env.CHROME_PATH } : {}) });
  try {
    const page = await browser.newPage({ viewport: { width: 907, height: 573 }, reducedMotion: 'reduce' });
    const errors = []; page.on('pageerror', error => errors.push(error.message));
    await page.addInitScript(() => {
      let next = 0; let run; let scan;
      const report = { status: 'observed', readAtUtc: '2026-01-01T00:00:00Z', protection: { runningMode: 'Normal', antivirusEnabled: true, realTimeProtectionEnabled: true }, threats: [], truncated: false, scanState: 'idle', limitation: 'Mock only; empty detections do not certify a clean PC.' };
      const diagnosis = { summary: 'Mock privacy/security audit', findings: [], conflicts: [], recommendations: [], consentQuestion: 'Read-only review', auditCoverageComplete: false, auditCoverage: [
        ['privacy', 'privacy'], ['antivirus', 'protections'], ['firewall', 'protections'], ['smartscreen', 'protections'], ['updates', 'protections'], ['boot-isolation', 'protections'], ['detections', 'detections'], ['antivirus-scan', 'detections'], ['startup', 'persistence'], ['scheduled-tasks', 'persistence'], ['services', 'persistence'], ['events', 'health'], ['storage', 'health'], ['devices', 'health'], ['memory', 'health'],
      ].map(([checkId, area]) => ({ checkId, area, label: checkId, status: 'notChecked', evidenceIds: [], assessment: 'Required check not verified in this mocked report.' })) };
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
            case 'get-state': return { settings: { provider: 'local', providerName: 'Mock local AI', model: 'test', requiresApiKey: false }, hasCredential: false, isRecording: false };
            case 'actions': case 'history': case 'run-list': case 'measurement-list': case 'measurement-workloads': return [];
            case 'save-provider': return { saved: true, hasCredential: false };
            case 'defender-scan-current': return scan ?? null;
            case 'defender-status': return report;
            case 'defender-scan': scan = { schemaVersion: 1, id: 'mock-scan', scanType: args.payload.scanType, state: 'CommandReturned', requestedAtUtc: report.readAtUtc, detail: 'Mock command returned, not proof of clean PC.' }; return { operation: scan, report };
            case 'scan': return { profile: { privacySecurity: { 'privacy.example': 'Not configured; effective state unknown', 'security.defender': 'Mock evidence' } }, sanitizedProfile: '{"audit:privacy.example":"Not configured; effective state unknown"}', actions: [] };
            case 'run-create': run = { id: 'mock-run', state: 'scanned', mode: args.payload.mode, goals: args.payload.goals, approvedActionIds: [], baselineSessionIds: [], candidateSessionIds: [], evidenceFacts: { 'audit:privacy.example': 'Not configured; effective state unknown', 'audit:security.defender': 'Mock evidence' } }; return run;
            case 'diagnose': return diagnosis;
            case 'run-get': return { ...run, state: 'proposalReady', diagnosis };
            default: throw Error('Unexpected mock command: ' + args.command);
          }
        },
      };
    });
    await page.goto(process.env.UI_PREVIEW_URL || 'http://127.0.0.1:4173/');
    await page.locator('#startup').waitFor({ state: 'detached' });
    await page.getByRole('button', { name: 'Complete diagnosis', exact: true }).first().click();
    await page.getByRole('combobox', { name: 'Objective', exact: true }).selectOption('privacySecurity');
    const mode = page.getByRole('group', { name: 'Investigation mode', exact: true });
    assert.equal(await mode.getByRole('radio', { name: /^Measured optimization/ }).isChecked(), true, 'A prompt preset must not change the investigation mode');
    assert.equal(await page.evaluate(() => window.requests.some(item => ['scan', 'defender-scan', 'diagnose'].includes(item.command))), false);
    await page.getByRole('navigation').getByRole('button', { name: 'Privacy & security', exact: true }).click();
    await page.getByRole('button', { name: 'Defender status', exact: true }).click();
    await page.getByRole('button', { name: /Read status and detections/ }).click();
    await page.getByText('No items reported in the summary: this does not certify a clean PC.', { exact: true }).waitFor();
    await page.getByRole('button', { name: 'User-approved Defender scan', exact: true }).click();
    const checkboxes = page.locator('.scan-consents').getByRole('checkbox');
    assert.equal(await checkboxes.count(), 3);
    const start = page.getByRole('button', { name: /Start Defender scan/ });
    assert.equal(await start.isDisabled(), true);
    for (let i = 0; i < 2; i++) { assert.equal(await checkboxes.nth(i).isChecked(), false); await checkboxes.nth(i).check(); assert.equal(await start.isDisabled(), true); }
    await checkboxes.nth(2).check(); assert.equal(await start.isEnabled(), true);
    page.once('dialog', dialog => dialog.dismiss()); await start.click();
    assert.equal(await page.evaluate(() => window.requests.some(item => item.command === 'defender-scan')), false);
    page.once('dialog', dialog => dialog.accept()); await start.click();
    await page.getByText('Mock command returned, not proof of clean PC.', { exact: true }).waitFor();
    assert.deepEqual(await page.evaluate(() => window.requests.filter(item => item.command === 'defender-scan').map(item => item.payload)), [{ scanType: 'quick', scanConsent: true, remediationConsent: true, networkConsent: true }]);
    await page.getByRole('navigation').getByRole('button', { name: 'Complete diagnosis', exact: true }).click();
    await mode.getByRole('radio', { name: /^Advisory audit/ }).check();
    assert.equal(await page.getByRole('combobox', { name: 'Running game / workload', exact: true }).count(), 0);
    for (const theme of ['light', 'dark']) {
      await page.evaluate(theme => { document.documentElement.dataset.theme = theme; }, theme);
      fs.mkdirSync('artifacts', { recursive: true });
      await page.screenshot({ path: `artifacts/privacy-security-consent-${theme}.png`, fullPage: true });
    }
    await page.getByRole('button', { name: 'Start AI audit · no changes', exact: true }).click();
    await page.getByRole('heading', { name: 'Audit collected locally · no AI transmission', exact: true }).waitFor();
    assert.equal(await page.evaluate(() => window.requests.some(item => ['diagnose', 'run-create'].includes(item.command))), false);
    await page.getByRole('button', { name: 'Cancel without sending', exact: true }).click();
    await page.getByRole('button', { name: 'Start AI audit · no changes', exact: true }).click();
    await page.getByRole('heading', { name: 'Audit collected locally · no AI transmission', exact: true }).waitFor();
    assert.equal(await page.evaluate(() => window.requests.some(item => item.command === 'diagnose')), false);
    assert.ok(await page.locator('.profile-json').textContent().then(value => value.includes('Not configured')));
    for (const width of [907, 760, 540]) { await page.setViewportSize({ width, height: 573 }); assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false); }
    await page.setViewportSize({ width: 907, height: 573 });
    await page.screenshot({ path: 'artifacts/privacy-security-audit-preview.png', fullPage: true });
    await page.getByRole('button', { name: 'I authorize evidence transmission · continue AI audit', exact: true }).click();
    try { await page.getByText('Mock privacy/security audit', { exact: true }).waitFor(); }
    catch (error) { console.error(await page.evaluate(() => ({ requests: window.requests, text: document.body.innerText })), errors); throw error; }
    assert.equal(await page.getByRole('button', { name: 'Back up & apply selected', exact: true }).count(), 0);
    await page.getByText('Privacy · preferences and policies, not measured traffic', { exact: true }).waitFor();
    await page.getByText('Security · protections and scanner summary, not a clean-PC certificate', { exact: true }).waitFor();
    const coverage = page.getByRole('region', { name: 'System check coverage', exact: true });
    await coverage.getByRole('heading', { name: 'Investigation coverage', exact: true }).waitFor();
    assert.equal(await coverage.getByText('Limited coverage', { exact: true }).count(), 1);
    assert.equal(await coverage.locator('dt').count(), 15);
    assert.equal(await coverage.getByText('Not checked', { exact: true }).count(), 15);
    await page.emulateMedia({ media: 'print' }); assert.equal(await coverage.isVisible(), true);
    await page.emulateMedia({ media: 'screen' });
    for (const theme of ['light', 'dark']) { await page.evaluate(theme => { document.documentElement.dataset.theme = theme; }, theme); await page.screenshot({ path: `artifacts/privacy-security-coverage-${theme}.png`, fullPage: true }); }
    const requests = await page.evaluate(() => window.requests);
    assert.equal(requests.some(item => /^(measurement-start|measurement-analyze|apply|run-approve)$/.test(item.command)), false);
    assert.equal(requests.find(item => item.command === 'run-create').payload.mode, 'auditOnly');
    assert.equal(requests.find(item => item.command === 'scan').payload.privacySecurityReadConsent, true);
    for (const width of [907, 760, 540]) { await page.setViewportSize({ width, height: 573 }); assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false); }
    assert.deepEqual(errors, []);
    console.log('PASS: prompt-only preset, independent audit mode, three unchecked scan consents plus confirmation, prepared audit preview/consent/cancellation, no scan on dismissal, no WPR/Apply in audit, separated evidence, mandatory coverage/omissions retained in print and narrow layouts. All agent operations mocked.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
