// Writes data/manifest.json: every game-data file the tracker can update without a release, with its SHA-256 as the
// repository stores it (LF line endings), so trackers download only what changed (tracker/.../Data/DataFiles.cs).
// Run after changing any of these files, and commit the manifest with them:   node tools/data/manifest.js
// `--check` fails when the committed manifest is stale (the build workflow runs it).
const fs = require('fs'), path = require('path'), crypto = require('crypto');
const root = path.join(__dirname, '..', '..');
const NAMES = ['zones.txt', 'instances.txt', 'mobs.txt', 'loot-tables.json', 'skins.json', 'recipes.json', 'mob-images.json', 'gathering-items.json', 'aliases.json', 'vendor-prices.json'];
// raise when a file's format changes in a way older trackers can't read; they then keep their built-in copies
const MIN_TRACKER = '0.8.0';
const files = {};
for (const n of NAMES) {
  const buf = Buffer.from(fs.readFileSync(path.join(root, 'data', n), 'utf8').replace(/\r\n/g, '\n'), 'utf8');
  files[n] = { sha256: crypto.createHash('sha256').update(buf).digest('hex'), size: buf.length };
}
const out = JSON.stringify({ min_tracker: MIN_TRACKER, files }, null, 2) + '\n';
const target = path.join(root, 'data', 'manifest.json');
if (process.argv.includes('--check')) {
  const was = fs.existsSync(target) ? fs.readFileSync(target, 'utf8').replace(/\r\n/g, '\n') : '';
  if (was !== out) { console.error('data/manifest.json is stale: run node tools/data/manifest.js and commit it'); process.exit(1); }
  console.log('data/manifest.json is current');
} else {
  fs.writeFileSync(target, out);
  console.log(`wrote data/manifest.json (${NAMES.length} files)`);
}
