// Fetches the crowd-learned misread fixes from the site and writes data/aliases.json, which the tracker's daily data
// update carries (tracker/.../Data/DataFiles.cs, ItemDictionary.LoadAliases). Run it with the daily game-data refresh:
//
//   node tools/data/aliases.js            # fetch https://kinahledger.com/v1/aliases.json, write data/aliases.json, rewrite the manifest
//   node tools/data/aliases.js --in f     # use a saved answer instead (tests, or a copy from the admin)
//   node tools/data/aliases.js --check    # only check the committed file (shape, ids, no rare items); exits 1 on a problem
//
// The site promotes an alias only when 3+ trackers read the same misread as the same item (see the backend README,
// "Accuracy pipeline"). This script checks it again here, against this repository's own lists, before anything ships:
// the item must be in data/tw-items.json, must not be Legend/Unique/Epic (rare reads never become exact by crowd), and
// the read must not be any item's own name. Anything that fails is left out and printed.
const fs = require('fs'), path = require('path'), https = require('https');
const root = path.join(__dirname, '..', '..');
const target = path.join(root, 'data', 'aliases.json');
const RARE = new Set(['legend', 'legendary', 'unique', 'epic']);

function norm(s) {
  return String(s).normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(/’/g, "'")
    .replace(/[^a-z0-9'():+.,&\- ]/g, ' ').split(' ').filter(Boolean).join(' ');
}

function loadItems() {
  const raw = JSON.parse(fs.readFileSync(path.join(root, 'data', 'tw-items.json'), 'utf8'));
  const list = raw.items || raw;
  const byId = new Map(), names = new Set();
  for (const i of list) { byId.set(Number(i.id), i); names.add(norm(i.name)); }
  return { byId, names };
}

/** The aliases that pass this repository's own checks, sorted, and the ones left out with why. */
function vet(aliases, items) {
  const kept = {}, dropped = [];
  for (const [read, id] of Object.entries(aliases || {})) {
    const key = norm(read), n = Number(id), item = items.byId.get(n);
    if (key.length < 3 || key.length > 48) { dropped.push([read, id, 'length']); continue; }
    if (!Number.isInteger(n) || n <= 0 || !item) { dropped.push([read, id, 'not an item in data/tw-items.json']); continue; }
    if (RARE.has(String(item.grade || '').toLowerCase())) { dropped.push([read, id, `a ${item.grade} item`]); continue; }
    if (items.names.has(key)) { dropped.push([read, id, "the read is an item's own name"]); continue; }
    kept[key] = n;
  }
  const sorted = {};
  for (const k of Object.keys(kept).sort()) sorted[k] = kept[k];
  return { kept: sorted, dropped };
}

function fetchJson(url) {
  return new Promise((resolve, reject) => {
    https.get(url, { headers: { 'user-agent': 'kinah-ledger-data/1 (+https://kinahledger.com)', accept: 'application/json' } }, (res) => {
      let body = '';
      res.on('data', (c) => (body += c));
      res.on('end', () => {
        if (res.statusCode !== 200) return reject(new Error(`HTTP ${res.statusCode} from ${url}`));
        try { resolve(JSON.parse(body)); } catch (e) { reject(e); }
      });
    }).on('error', reject);
  });
}

async function main() {
  const args = process.argv.slice(2);
  const items = loadItems();
  if (args.includes('--check')) {
    const cur = JSON.parse(fs.readFileSync(target, 'utf8'));
    const { kept, dropped } = vet(cur.aliases, items);
    if (dropped.length || Object.keys(kept).length !== Object.keys(cur.aliases || {}).length) {
      for (const d of dropped) console.error('bad alias:', JSON.stringify(d));
      process.exit(1);
    }
    console.log(`data/aliases.json is fine (${Object.keys(kept).length} aliases)`);
    return;
  }
  const i = args.indexOf('--in');
  const answer = i >= 0 ? JSON.parse(fs.readFileSync(args[i + 1], 'utf8')) : await fetchJson(process.env.ALIASES_URL || 'https://kinahledger.com/v1/aliases.json');
  const { kept, dropped } = vet(answer.aliases, items);
  for (const d of dropped) console.log('left out:', JSON.stringify(d));
  const out = {
    source: 'kinahledger.com/v1/aliases.json: misreads several trackers snapped or guessed to the same item (tools/data/aliases.js)',
    generated: answer.generated || null,
    aliases: kept,
  };
  const text = JSON.stringify(out, null, 2) + '\n';
  const was = fs.existsSync(target) ? fs.readFileSync(target, 'utf8').replace(/\r\n/g, '\n') : '';
  // only the date changed: nothing to ship
  const same = was && JSON.stringify(JSON.parse(was).aliases) === JSON.stringify(kept);
  if (same) { console.log(`data/aliases.json unchanged (${Object.keys(kept).length} aliases)`); return; }
  fs.writeFileSync(target, text);
  console.log(`wrote data/aliases.json (${Object.keys(kept).length} aliases, ${dropped.length} left out)`);
  require('child_process').execFileSync(process.execPath, [path.join(__dirname, 'manifest.js')], { stdio: 'inherit' });
}

module.exports = { vet, norm };
if (require.main === module) main().catch((e) => { console.error(e.message || e); process.exit(1); });
