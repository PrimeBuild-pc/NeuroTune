// Browser-only regression. Every Agent command is mocked; no AI, WPR, installer, driver or Windows writer runs.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require(process.env.PLAYWRIGHT_PATH || 'playwright');
const url = process.env.UI_PREVIEW_URL || 'http://127.0.0.1:4173/';
(async () => {
  const browser = await chromium.launch({ headless: true, ...(process.env.CHROME_PATH ? { executablePath: process.env.CHROME_PATH } : {}) });
  try {
    for (const theme of ['light', 'dark']) {
      const page = await browser.newPage({ viewport: { width: 907, height: 573 }, reducedMotion: 'reduce', acceptDownloads: true });
      const errors = []; page.on('pageerror', error => { errors.push(error.message); console.error('Mock page error:', error.message); });
      await page.addInitScript(theme => {
        localStorage.setItem('neurotune.theme', theme);
        const report = (id, dpc) => ({ sessionId: id, schemaVersion: 2, generatedAtUtc: '2026-10-07T00:00:00Z', targetExecutable: 'System-wide', quality: { durationMilliseconds: 30000, etlBytes: 1000, eventsLost: 0, missingProviders: [], targetPresencePercent: 0, isValid: true }, threads: [], processors: [], observations: [], hardFaults: [], limitations: [], interrupts: [{ kind: 'dpc', module: 'mock.sys', logicalProcessor: 0, distribution: { count: 1, eventsPerSecond: 1, totalMicroseconds: dpc, p50Microseconds: dpc, p95Microseconds: dpc, p99Microseconds: dpc, maxMicroseconds: dpc } }] });
        let sessions = JSON.parse(localStorage.getItem('mock.measurements') || 'null') || [{ id: 'saved-before', label: 'baseline', state: 'completed', systemWide: true, conditions: 'Idle', analysisPreset: 'balanced', processName: 'System-wide', processId: 0, processStartTimeUtc: '0001-01-01T00:00:00Z', createdAtUtc: '2026-10-07T00:00:00Z', durationSeconds: 30, keepRawTrace: false, hardFaultsEnabled: true, report: report('saved-before', 100) }];
        const persist = () => localStorage.setItem('mock.measurements', JSON.stringify(sessions));
        window.simulatedBiosText = '// SIMULATED MSI test export — NOT physical BIOS data\nSetup Question = Precision Boost Overdrive\nBIOS Default = [00]Auto\nOptions = [00]Auto\n          *[01]Disabled\nSetup Question = Global C-state Control\nBIOS Default = [00]Enabled\nOptions = [00]Enabled\nSetup Question = A-XMP メモリ\nOptions = *[01]Profile 1\nSetup Question = CPU voltage offset\nValue = <0x0064>\nSetup Question = Ambiguous current state\nOptions = *[00]Enabled\n          *[01]Disabled\nSetup Question = Admin Password\nValue = <abcd>';
        let next = 0; window.requests = []; window.__TAURI_EVENT_PLUGIN_INTERNALS__ = { unregisterListener() {} };
        window.__TAURI_INTERNALS__ = { transformCallback() { return ++next; }, async invoke(command, args) {
          if (command.startsWith('plugin:event|')) return ++next;
          if (command !== 'agent') return true;
          window.requests.push({ command: args.command, payload: args.payload });
          switch (args.command) {
            case 'get-state': return { settings: { provider: 'local', providerName: 'Mock local AI', model: 'test', baseUrl: 'http://127.0.0.1:11434/v1', protocol: 'openAiCompatible', requiresApiKey: false }, hasCredential: false, chatGptAccounts: [], isRecording: false };
            case 'history': case 'run-list': case 'actions': case 'measurement-workloads': return [];
            case 'power-plan-list': return { directory: '', plans: [] };
            case 'system-one-status': return { enabled: false, installed: false, hasFiles: false, source: 'local', size: '4b', device: 'auto', directory: '', model: '', detail: 'Mock local status only', hasApiKey: false };
            case 'firmware-read': {
              if (!args.payload.readConsent) throw Error('Mock firmware consent missing');
              return { readEnabled: true, writeSupported: false, interfaces: [], guidanceUrl: '', settingsStatus: 'SIMULATED MSI reading — not physical-machine data; exact setup values are unavailable to this Windows reader.', facts: { Motherboard: 'SIMULATED MSI MPG X570 GAMING EDGE WIFI (MS-7C37)', BIOS: 'SIMULATED AMI version for tests', 'Firmware mode': 'UEFI (simulated)', 'Secure Boot': 'Enabled (simulated)', 'DIMM 1': 'SIMULATED 8 GB module; timings and XMP unknown', 'PBO/CPU overclock assessment': 'Unknown; a PawnIO service does not confirm BIOS settings' } };
            }
            case 'scewin-import': {
              if (!args.payload.readConsent || args.payload.text !== window.simulatedBiosText) throw Error('Consent or exact decoded simulation text was lost');
              await new Promise(resolve => setTimeout(resolve, 500));
              const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(args.payload.text));
              return { source: 'SIMULATED MSI import — test data, not live BIOS observations', readAtUtc: '2026-10-07T00:00:00Z', exportSha256: Array.from(new Uint8Array(digest), byte => byte.toString(16).padStart(2, '0')).join(''), sensitiveQuestionsOmitted: 1, toolFiles: [], notes: ['Simulation only; board and freshness unverified, no driver executed.'], settings: [
                { question: 'Precision Boost Overdrive', token: '', currentValue: '[01]Disabled', status: 'Reported by export' },
                { question: 'Global C-state Control', token: '', currentValue: null, status: 'Unknown — no marked current value' },
                { question: 'A-XMP メモリ', token: '', currentValue: '[01]Profile 1', status: 'Reported by export' },
                { question: 'CPU voltage offset', token: '', currentValue: '<0x0064>', status: 'Reported by export' },
                { question: 'Ambiguous current state', token: '', currentValue: null, status: 'Unknown — conflicting current-value markers' },
              ] };
            }
            case 'measurement-list': return structuredClone(sessions);
            case 'measurement-start': {
              const session = { id: 'saved-after', ...args.payload, state: 'recording', processName: 'System-wide', createdAtUtc: '2026-10-07T01:00:00Z', recordingStartedAtUtc: new Date().toISOString(), hardFaultsEnabled: true };
              sessions = [session, ...sessions]; persist();
              setTimeout(() => { session.state = 'captured'; persist(); }, 700);
              return structuredClone(session);
            }
            case 'measurement-analyze': {
              const session = sessions.find(item => item.id === args.payload.sessionId);
              session.state = 'completed'; session.report = report(session.id, 50); persist(); return structuredClone(session);
            }
            case 'measurement-compare': return { id: 'comparison', diagnosticOnly: true, level: 'exploratory', ...args.payload, metrics: [{ evidenceId: 'comparison:test:interrupt:system:worst_module_p99_us:median_delta_percent', baselineMedian: 100, candidateMedian: 50, deltaPercent: -50, outcome: 'improvement' }], rejectionReasons: [], recommendation: 'insufficientEvidence', recommendationReason: 'Diagnostic differences do not prove FPS/input-latency gains or authorize Apply/Keep.' };
            default: throw Error('Unexpected mock command: ' + args.command);
          }
        } };
      }, theme);
      await page.goto(url); await page.locator('#startup').waitFor({ state: 'detached' });
      const guide = page.getByRole('dialog'); await guide.waitFor({ state: 'visible' });
      await page.screenshot({ path: `artifacts/contextual-setup-${theme}.png` });
      await guide.getByRole('button', { name: 'Next', exact: true }).click();
      await guide.getByRole('button', { name: 'Yes', exact: true }).click();
      await guide.getByRole('button', { name: 'Next', exact: true }).click();
      await guide.getByText('BIOS modification · not supported').waitFor();
      assert.equal(await page.evaluate(() => localStorage.getItem('neurotune.optionalTelemetryConsent')), null, 'Draft choices are not applied early');
      await guide.getByRole('button', { name: 'Next', exact: true }).click();
      await guide.getByRole('button', { name: 'Next', exact: true }).click();
      await guide.getByRole('button', { name: 'Save choices and continue', exact: true }).click();
      assert.equal(await page.evaluate(() => localStorage.getItem('neurotune.optionalTelemetryConsent')), 'true');
      assert.equal(await page.evaluate(() => localStorage.getItem('neurotune.firmwareReadConsent')), 'false');
      assert.equal(await page.evaluate(() => window.requests.some(item => ['measurement-start', 'diagnose', 'systemone-install', 'scewin-export'].includes(item.command))), false);
      await page.getByRole('button', { name: 'Settings', exact: true }).click();
      await page.getByRole('button', { name: 'Open guided setup', exact: true }).click();
      await guide.getByRole('button', { name: 'Skip without changing permissions', exact: true }).click();
      assert.equal(await page.evaluate(() => localStorage.getItem('neurotune.optionalTelemetryConsent')), 'true');
      await page.getByRole('button', { name: 'Measurements', exact: true }).click();
      await page.getByRole('button', { name: 'Record after measurement', exact: true }).waitFor();
      const board = page.locator('.compare-workbench');
      await board.getByText('100.00').waitFor();
      assert.equal(await board.getByRole('button', { name: 'Calculate comparison', exact: true }).isEnabled(), false);
      await board.getByRole('button', { name: 'Record after measurement', exact: true }).click();
      assert.equal(await page.locator('html').getAttribute('data-motion'), 'quiet');
      await page.waitForFunction(() => window.requests.filter(item => item.command === 'measurement-analyze').length === 1);
      await board.getByText('50.00').waitFor();
      const request = await page.evaluate(() => window.requests.find(item => item.command === 'measurement-start').payload);
      assert.equal(request.conditions, 'Idle'); assert.equal(request.systemWide, true); assert.equal(request.label, 'candidate'); assert.equal(request.durationSeconds, 30);
      assert.equal(request.analysisPreset, 'balanced'); assert.equal(request.optimizationRunId, undefined);
      await board.getByRole('button', { name: 'Calculate comparison', exact: true }).click();
      await board.getByText('Lower observed value', { exact: true }).waitFor();
      assert.equal(await board.locator('td.improvement').count(), 0);
      await page.screenshot({ path: `artifacts/contextual-compare-${theme}.png`, fullPage: true });
      page.once('dialog', dialog => dialog.accept());
      const download = page.waitForEvent('download');
      await board.getByRole('button', { name: 'Save report as JSON', exact: true }).first().click();
      const saved = await download; const content = fs.readFileSync(await saved.path(), 'utf8');
      assert.equal(JSON.parse(content).id, 'saved-before'); assert.equal(JSON.parse(content).conditions, 'Idle');
      await page.reload(); await page.locator('#startup').waitFor({ state: 'detached' });
      assert.equal(await guide.isVisible(), false);
      await page.getByRole('button', { name: 'Measurements', exact: true }).click();
      await page.getByText('2 measurement sessions', { exact: true }).waitFor();
      await board.getByLabel('After measurement').selectOption('saved-after');
      await board.getByRole('button', { name: 'Calculate comparison', exact: true }).click();
      await board.getByText('Lower observed value', { exact: true }).waitFor();
      await page.setViewportSize({ width: 540, height: 573 });
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, 'No horizontal overflow');
      await page.getByRole('button', { name: 'Settings', exact: true }).click();
      await page.getByRole('button', { name: 'Open guided setup', exact: true }).click();
      for (let step = 0; step < 3; step++) await guide.getByRole('button', { name: 'Next', exact: true }).click();
      await guide.getByRole('button', { name: 'Yes', exact: true }).click();
      await guide.getByRole('button', { name: 'Next', exact: true }).click();
      await guide.getByRole('button', { name: 'Save choices and continue', exact: true }).click();
      await page.getByRole('button', { name: 'System One assistant', exact: true }).waitFor();
      await page.getByText('Mock local status only', { exact: true }).waitFor();
      assert.equal(await page.getByRole('button', { name: 'System One assistant', exact: true }).getAttribute('aria-pressed'), 'true');
      await page.getByRole('button', { name: 'BIOS / UEFI inspection', exact: true }).click();
      const firmware = page.locator('.firmware-panel');
      assert.equal(await firmware.getByRole('button', { name: 'Read firmware now', exact: true }).isEnabled(), false);
      await firmware.getByRole('checkbox', { name: /Allow BIOS reading/ }).check();
      await firmware.getByRole('button', { name: 'Read firmware now', exact: true }).click();
      await firmware.getByText('SIMULATED MSI MPG X570 GAMING EDGE WIFI (MS-7C37)', { exact: true }).waitFor();
      await page.screenshot({ path: `artifacts/msi-reader-simulated-${theme}.png`, fullPage: true });
      await firmware.getByRole('button', { name: 'Open setup export / import', exact: true }).click();
      const fileInput = firmware.getByLabel('Or import an existing nvram.txt without running drivers', { exact: true });
      const dump = await page.evaluate(() => window.simulatedBiosText);
      for (const encoding of ['utf8', 'utf16le']) {
        const bytes = encoding === 'utf8' ? Buffer.from(dump, encoding) : Buffer.concat([Buffer.from([0xff, 0xfe]), Buffer.from(dump, encoding)]);
        await fileInput.setInputFiles({ name: 'SIMULATED-MSI-nvram.txt', mimeType: 'text/plain', buffer: bytes });
        await firmware.getByText('Reported current values: 3/5. Missing or conflicting values remain unknown.', { exact: true }).waitFor();
        await firmware.getByText(new RegExp(`Imported file: SIMULATED-MSI-nvram.txt.*${encoding === 'utf8' ? 'utf-8' : 'utf-16le'}`)).waitFor();
        assert.equal(await firmware.getByText('Admin Password', { exact: true }).count(), 0);
        assert.equal(await firmware.getByText('Unknown — no marked current value', { exact: true }).count(), 1);
      }
      await page.screenshot({ path: `artifacts/msi-export-simulated-${theme}.png`, fullPage: true });
      await fileInput.setInputFiles({ name: 'invalid-encoding.txt', mimeType: 'text/plain', buffer: Buffer.from([0xff, 0x00]) });
      await firmware.getByRole('alert').filter({ hasText: 'Convert it to one of those encodings' }).waitFor();
      assert.equal(await firmware.getByText('SIMULATED MSI import — test data, not live BIOS observations', { exact: true }).count(), 0, 'No stale success report after a failed import');
      assert.equal(await page.evaluate(() => window.requests.filter(item => item.command === 'scewin-import').length), 2, 'Bad encoding fails before the local parser call');
      assert.deepEqual(errors, []);
      assert.equal(await page.evaluate(() => window.requests.some(item => /apply|rollback|run-approve|diagnose|scewin-(export|inspect)|system-one-(install|configure|models|analyze)|power-plan-stage/.test(item.command))), false);
      await page.close();
    }
    console.log('PASS: mocked first-launch choices, permission-preserving skip, saved baseline, one owned capture/analysis, diagnostic comparison, JSON export/restart, simulated MSI reading and UTF-8/UTF-16 offline imports in both themes.');
  } catch (error) {
    for (const context of browser.contexts()) for (const page of context.pages()) {
      await page.screenshot({ path: 'artifacts/contextual-browser-failure.png', fullPage: true }).catch(() => {});
      console.error(await page.locator('body').innerText());
    }
    throw error;
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
