// Capture the current frontend with a demo provider and mocked Agent. No scans, writes or AI calls.
// Run after `cd ui && npm run build && npm run preview`.
// Set PLAYWRIGHT_PATH / CHROME_PATH when using an existing external browser installation.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.PLAYWRIGHT_PATH || 'playwright');

(async () => {
  const output = path.join(__dirname, '../docs/screenshots');
  fs.mkdirSync(output, { recursive: true });
  const browser = await chromium.launch({ headless: true, ...(process.env.CHROME_PATH ? { executablePath: process.env.CHROME_PATH } : {}) });
  try {
    const page = await browser.newPage({ viewport: { width: 1440, height: 960 }, deviceScaleFactor: 1, reducedMotion: 'reduce' });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    // The preview is strictly local; deny any unexpected external asset/request.
    const url = process.env.UI_PREVIEW_URL || 'http://127.0.0.1:4173/';
    assert.ok(['127.0.0.1', 'localhost', '[::1]'].includes(new URL(url).hostname));
    await page.route('**/*', route => new URL(route.request().url()).origin === new URL(url).origin ? route.continue() : route.abort());
    await page.addInitScript(() => {
      localStorage.setItem('neurotune.language', 'en');
      localStorage.setItem('neurotune.theme', 'dark');
      let next = 0;
      window.readmeRequests = [];
      window.__TAURI_EVENT_PLUGIN_INTERNALS__ = { unregisterListener() {} };
      window.__TAURI_INTERNALS__ = {
        transformCallback() { return ++next; },
        async invoke(command, args) {
          if (command.startsWith('plugin:event|')) return ++next;
          if (command !== 'agent') throw Error('Unexpected bridge call: ' + command);
          window.readmeRequests.push(args.command);
          switch (args.command) {
            case 'get-state': return { settings: { provider: 'local', providerName: 'Local model', protocol: 'openAiCompatible', baseUrl: 'http://127.0.0.1:11434/v1', model: 'demo-local-model', requiresApiKey: false }, hasCredential: false, isRecording: false, chatGptAccounts: [] };
            case 'actions': case 'history': case 'run-list': case 'measurement-workloads': case 'measurement-list': return [];
            case 'defender-scan-current': return null;
            default: throw Error('Capture must not perform operations: ' + args.command);
          }
        },
      };
    });
    await page.goto(url);
    await page.locator('#startup').waitFor({ state: 'detached' });
    await page.evaluate(() => document.fonts.ready);
    for (const theme of ['dark', 'light']) {
      await page.evaluate(theme => {
        document.documentElement.dataset.theme = theme;
        document.documentElement.style.colorScheme = theme;
        document.activeElement?.blur();
      }, theme);
      await page.getByRole('heading', { name: 'A visible boundary at every step', exact: true }).waitFor();
      await page.screenshot({ path: path.join(output, `neurotune-${theme}.png`) });
    }
    await page.evaluate(() => { document.documentElement.dataset.theme = 'dark'; document.documentElement.style.colorScheme = 'dark'; });
    await page.getByRole('navigation').getByRole('button', { name: 'Complete diagnosis', exact: true }).click();
    await page.getByRole('group', { name: 'Investigation mode', exact: true }).waitFor();
    await page.screenshot({ path: path.join(output, 'neurotune-diagnosis.png') });
    await page.getByRole('navigation').getByRole('button', { name: 'Privacy & security', exact: true }).click();
    await page.getByRole('heading', { name: 'Privacy & security', exact: true }).first().waitFor();
    await page.screenshot({ path: path.join(output, 'neurotune-security.png') });
    assert.deepEqual(errors, []);
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false);
    const requests = await page.evaluate(() => window.readmeRequests);
    assert.ok(requests.every(command => ['get-state', 'actions', 'history', 'run-list', 'measurement-workloads', 'measurement-list', 'defender-scan-current'].includes(command)));
    for (const name of ['dark', 'light', 'diagnosis', 'security']) {
      const png = fs.readFileSync(path.join(output, `neurotune-${name}.png`));
      assert.equal(png.readUInt32BE(16), 1440);
      assert.equal(png.readUInt32BE(20), 960);
    }
    console.log('PASS: four equal-size current-UI screenshots, demo configuration, no native operations or provider requests.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
