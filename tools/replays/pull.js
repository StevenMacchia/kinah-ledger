// Brings players' miscount picture reports into the replay test bank (tracker/tests/replays). Each report a player sent
// with "Send report pictures" on (loot list and crafting panel crops only, never the chat) becomes a pending case:
// the pictures, where they sat in the game window, and what that tracker counted then. A pending case runs with the
// bank but never fails a release until someone checks it:
//
//   KL_ADMIN_KEY=... node tools/replays/pull.js          # pull every new report, mark it pulled on the site
//   KinahLedger.exe replay-bank tracker/tests/replays    # see what this version counts from each
//   # then, for a case whose right count is known: edit its "expect" by hand (and set "pending": false), or, when this
//   # version's count is the right one, KinahLedger.exe replay-bank tracker/tests/replays --record <case folder>
//
// Options: --base https://kinahledger.com (or KL_BASE), --dry-run (list only), --keep-new (don't mark them pulled).
// Pictures are players' screens: look at each case before committing it, and drop any that shows more than loot rows
// or the crafting panel.
const fs = require('fs'), path = require('path');
const root = path.join(__dirname, '..', '..');
const bank = path.join(root, 'tracker', 'tests', 'replays');
const args = process.argv.slice(2);
const opt = (k, d) => { const i = args.indexOf(k); return i >= 0 ? args[i + 1] : d; };
const base = (opt('--base', process.env.KL_BASE || 'https://kinahledger.com')).replace(/\/+$/, '');
const key = process.env.KL_ADMIN_KEY;

async function get(p, asBuffer) {
  const r = await fetch(base + p, { headers: { 'x-admin-key': key, 'user-agent': 'kinah-ledger-replays/1' } });
  if (!r.ok) throw new Error(`${p}: HTTP ${r.status} ${(await r.text()).slice(0, 200)}`);
  return asBuffer ? Buffer.from(await r.arrayBuffer()) : r.json();
}

/** A case folder name from the report: its day and the start of its id. */
function caseName(rep) { return `report-${String(rep.received_at || '').slice(0, 10)}-${String(rep.id).slice(0, 8)}`; }

/** The bank's case.json for a report (pending until someone says what the right count is). */
function caseFor(rep) {
  return {
    name: rep.why || 'miscount report',
    source: `miscount report ${rep.id} from tracker ${rep.app}, received ${rep.received_at}`,
    game: { w: rep.game?.w || 1920, h: rep.game?.h || 1080 },
    pending: true,
    needs_ocr: (rep.frames || []).some((f) => f.k === 'loot'),
    counted_then: rep.counted || [],
    frames: (rep.frames || []).filter((f) => f.k === 'loot' || f.k === 'craft').map((f) => ({ t: f.t, k: f.k, f: 'frames/' + f.f, x: f.x, y: f.y })),
    expect: {},
  };
}

async function main() {
  if (!key) { console.error('set KL_ADMIN_KEY (the backend\'s ADMIN_KEY secret)'); process.exit(1); }
  const list = await get('/admin/frames?status=new');
  console.log(`${list.reports.length} new report(s) on ${base}`);
  for (const row of list.reports) {
    const rep = await get(`/admin/frames/${row.id}`);
    const dir = path.join(bank, caseName(rep));
    console.log(`  ${row.id}  ${rep.app}  ${(rep.frames || []).length} picture(s)  "${rep.why}" -> ${path.relative(root, dir)}`);
    if (args.includes('--dry-run')) continue;
    fs.mkdirSync(path.join(dir, 'frames'), { recursive: true });
    for (const f of rep.frames || []) {
      if (!/^(loot|craft)-\d{3}\.png$/.test(f.f)) { console.log(`    skipped ${f.f}: not a loot list or panel picture`); continue; }
      fs.writeFileSync(path.join(dir, 'frames', f.f), await get(`/admin/frames/${row.id}/${f.f}`, true));
    }
    fs.writeFileSync(path.join(dir, 'case.json'), JSON.stringify(caseFor(rep), null, 2) + '\n');
    if (!args.includes('--keep-new')) {
      const r = await fetch(`${base}/admin/frames/${row.id}`, { method: 'POST', headers: { 'x-admin-key': key, 'content-type': 'application/json' }, body: JSON.stringify({ status: 'pulled' }) });
      if (!r.ok) console.log(`    couldn't mark it pulled: HTTP ${r.status}`);
    }
  }
}

module.exports = { caseFor, caseName };
if (require.main === module) main().catch((e) => { console.error(e.message || e); process.exit(1); });
