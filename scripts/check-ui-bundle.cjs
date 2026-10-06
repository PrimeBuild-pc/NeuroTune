// Run after `cd ui && npm run build`. Keep Vite's normal 500 kB warning threshold.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const assets = path.join(__dirname, '../ui/dist/assets');
const scripts = fs.readdirSync(assets).filter(name => name.endsWith('.js'));
assert.ok(scripts.some(name => name.startsWith('react-core-')), 'React core has its own chunk');
assert.ok(scripts.some(name => name.startsWith('translations-')), 'Local catalogs have their own chunk');
for (const name of scripts) {
  const bytes = fs.statSync(path.join(assets, name)).size;
  assert.ok(bytes <= 500000, `${name}: ${bytes} bytes exceeds the default 500 kB threshold`);
  console.log(`${name}: ${(bytes / 1000).toFixed(2)} kB`);
}
console.log('PASS: production JavaScript chunks stay below 500 kB without raising the warning threshold.');
