import assert from 'node:assert/strict';
import { copyFileSync, existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { builtinModules, createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

if (process.platform !== 'win32' || process.arch !== 'x64') {
  throw new Error('Pi packaging requires Windows x64 Node.js; cross-platform runtime downloads are not supported.');
}
const [major, minor] = process.versions.node.split('.').map(Number);
if (major < 22 || (major === 22 && minor < 19)) {
  throw new Error(`Pi packaging requires Node.js >=22.19.0; found ${process.version}.`);
}

const root = fileURLToPath(new URL('../', import.meta.url));
const ui = join(root, 'ui');
const require = createRequire(join(ui, 'package.json'));
const { build } = require('esbuild');
const destination = join(ui, 'src-tauri', 'agent', 'pi');
const runtimeDirectory = dirname(process.execPath);
const nodeLicense = process.env.NEUROTUNE_NODE_LICENSE || ['LICENSE', 'LICENSE.txt', 'LICENSE.md']
  .map(name => join(runtimeDirectory, name)).find(existsSync);
if (!nodeLicense) {
  throw new Error(`Node.js LICENSE is missing beside ${process.execPath}. Use a complete Windows x64 Node distribution (such as setup-node in CI), or set NEUROTUNE_NODE_LICENSE to its matching official LICENSE file; no runtime or license is downloaded.`);
}
const nodeLicenseText = readFileSync(nodeLicense, 'utf8');
assert.match(nodeLicenseText, /Node\.js/iu, 'The runtime LICENSE must contain the Node.js notices.');
for (const name of ['pi-agent-core', 'pi-ai']) {
  const metadata = JSON.parse(readFileSync(join(ui, 'node_modules', '@earendil-works', name, 'package.json'), 'utf8'));
  assert.equal(metadata.version, '1.1.0', `${name} must be pinned to 1.1.0.`);
  assert.equal(metadata.license, 'MIT', `${name} license changed; review packaging notices.`);
}

// npm's Pi 1.1.0 tarballs omit LICENSE; preserved from https://github.com/earendil-works/pi/blob/v1.1.0/LICENSE.
const piLicense = `MIT License

Copyright (c) 2025 Mario Zechner

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
`;

const result = await build({
  absWorkingDir: ui,
  entryPoints: [join(root, 'scripts', 'pi-agent.mjs')],
  nodePaths: [join(ui, 'node_modules')],
  outfile: join(destination, 'agent.mjs'),
  bundle: true,
  platform: 'node',
  format: 'esm',
  target: 'node22.19',
  legalComments: 'external',
  metafile: true,
  write: false,
});
const builtins = new Set([...builtinModules, ...builtinModules.map(name => `node:${name}`)]);
for (const output of Object.values(result.metafile.outputs)) {
  for (const imported of output.imports) {
    assert.ok(!imported.external || builtins.has(imported.path), `Pi bundle still requires an external dependency: ${imported.path}`);
  }
}

rmSync(destination, { recursive: true, force: true });
mkdirSync(destination, { recursive: true });
for (const output of result.outputFiles) writeFileSync(output.path, output.contents);
const legalFile = join(destination, 'agent.mjs.LEGAL.txt');
const legalComments = existsSync(legalFile) ? readFileSync(legalFile, 'utf8') : '';
// These dependencies have no legal comments in their JS, so esbuild cannot retain their MIT notices.
const dependencyLicenses = ['typebox/license', 'partial-json/LICENSE']
  .map(path => readFileSync(join(ui, 'node_modules', path), 'utf8')).join('\n\n');
writeFileSync(legalFile, `${dependencyLicenses}\n\nThird-party notices retained by esbuild (if any):\n${legalComments}`);
copyFileSync(process.execPath, join(destination, 'node.exe'));
writeFileSync(join(destination, 'LICENSE-node.txt'), nodeLicenseText);
writeFileSync(join(destination, 'LICENSE-pi.txt'), piLicense);

// Self-check only syntax and the copied runtime; never start the bridge or contact a provider.
for (const args of [['--version'], ['--check', join(destination, 'agent.mjs')]]) {
  const check = spawnSync(join(destination, 'node.exe'), args, { encoding: 'utf8', windowsHide: true });
  assert.equal(check.error, undefined, 'The packaged Node runtime could not start.');
  assert.equal(check.status, 0, check.stderr || 'Packaged runtime/bridge self-check failed.');
  if (args[0] === '--version') assert.equal(check.stdout.trim(), process.version);
}
console.log(`Pi bridge and Node ${process.version} packaged in ${destination}`);
