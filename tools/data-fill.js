// The data-fill step (2026-11-06): takes the names the patch-notes watcher (tools/patch-watch.js) could not find in any
// list, looks each one up on MetaBot, and drafts its entry in the data/ file its kind belongs in, so the watcher's
// report turns into a commit instead of a to-do list. Names MetaBot does not have, or has under another name, become
// questions for a person. The loop that uses it is in tools/data-fill.md.
//
//   node tools/data-fill.js --in report.md            the names in a watcher report (node tools/patch-watch.js --out report.md)
//   node tools/data-fill.js "Nightmare Coin" "Argo"   names given by hand (kind guessed from the name alone)
//   node tools/data-fill.js ... --dry-run             print the drafts, write nothing
//   node tools/data-fill.js ... --out fill.md         also write the report (added, questions, skipped) to a file
//   node tools/data-fill.js ... --data some/dir       read and write the lists in another folder (a copy, for a proof)
//   node tools/data-fill.js ... --pause 1500          milliseconds between requests (default 1500)
//   node tools/data-fill.js ... --no-check            skip the validator after writing (tools/data-check.js)
//
// Source: MetaBot (https://metabot.gg/en/aion-2), which has the global client's items, monsters and zones days before
// anything else. Its search is client-side, but its pages are server-rendered and answer a plain request by slug
// (the name lowercased, apostrophes dropped, every other run of non-letters a hyphen): /items/<slug>,
// /monsters/<slug>, /zones/<slug>; a missing page is a 404 titled "Page not found". The fields are read from the
// rendered page: the name from <title>, grade/category/trading from the item card, the icon from og:image, and a
// craftable item's recipe rows (skill, mastery, faction, materials, makes, combo chance) from its Crafting table.
// One request per name, two for an item when the plain name is missing and the "(Bound)" one may exist.
//
// Where a name goes (the file map in tools/patch-watch.md): items → data/extra-items.json (built into the exe: a
// release ships it), "X (Skin: Slot)" → data/skins.json, a craftable item's recipe → data/recipes.json (recipe_id,
// success rate and curve are not on the item page: null, with a note), enemies → data/mobs.txt, zones →
// data/zones.txt. Instances need a Kind a page cannot tell, so a dungeon page becomes a question with the line to add.
// An entry is never overwritten: a name any list already knows is skipped and said so.
const fs = require('fs'), path = require('path');
const { spawnSync } = require('child_process');
const root = path.join(__dirname, '..');
const MB = 'https://metabot.gg/en/aion-2';
const UA = 'kinah-ledger data-fill (https://github.com/StevenMacchia/kinah-ledger)';

const args = process.argv.slice(2);
const opt = (name, dflt) => { const i = args.indexOf(name); return i >= 0 ? (args[i + 1] ?? true) : dflt; };
const dry = args.includes('--dry-run');
const noCheck = args.includes('--no-check');
const dataDir = path.resolve(opt('--data', path.join(root, 'data')));
const outPath = opt('--out', null);
const pause = Number(opt('--pause', 1500)) || 0;
const inputs = []; const given = [];
for (let i = 0; i < args.length; i++) {
  if (args[i] === '--in') inputs.push(args[++i]);
  else if (['--data', '--out', '--pause'].includes(args[i])) i++;
  else if (!args[i].startsWith('--')) given.push(args[i]);
}
const today = new Date().toISOString().slice(0, 10);

// ---- the lists (the same reading as the watcher's, so "known" means the same thing in both) ---------------------------
const norm = s => String(s).toLowerCase().replace(/\(bound\)/g, '').replace(/[^a-z0-9]/g, '');
const readText = file => { const p = path.join(dataDir, file); return fs.existsSync(p) ? fs.readFileSync(p, 'utf8') : null; };
function linesOf(file) {
  const raw = readText(file); if (raw == null) return [];
  return raw.split(/\r?\n/).map(l => l.trim()).filter(l => l && !l.startsWith('#')).map(l => l.split('|')[0].trim());
}
function namesIn(file) {
  const raw = readText(file); if (raw == null) return [];
  const out = [];
  const walk = o => {
    if (Array.isArray(o)) o.forEach(walk);
    else if (o && typeof o === 'object') for (const [k, v] of Object.entries(o)) {
      if (typeof v === 'string' && /^(name|n|item|title|result|output|material)$/.test(k)) out.push(v);
      else walk(v);
    }
  };
  try { walk(JSON.parse(raw)); } catch (e) { console.error(`${file}: ${e.message}`); }
  return out;
}
const lists = {
  zones: linesOf('zones.txt'), instances: linesOf('instances.txt'), mobs: linesOf('mobs.txt'),
  items: [...namesIn('tw-items.json'), ...namesIn('extra-items.json')], skins: namesIn('skins.json'),
  recipes: namesIn('recipes.json'), gathering: namesIn('gathering-items.json'), loot: namesIn('loot-tables.json'),
};
const known = new Map();   // normalised name -> which lists
for (const [list, names] of Object.entries(lists)) for (const n of names) { const k = norm(n); if (!k) continue; known.set(k, (known.get(k) || []).concat(list)); }
const knownAs = name => known.get(norm(name)) || known.get(norm(name).replace(/s$/, '')) || known.get(norm(name) + 's');

// ---- the input ---------------------------------------------------------------------------------------------------------
// a watcher report: "## title" then rows "| Name | Seen | Guess | Where |"
function rowsOf(reportPath) {
  const rows = []; let note = '';
  for (const line of fs.readFileSync(reportPath, 'utf8').split(/\r?\n/)) {
    if (line.startsWith('## ')) { note = line.slice(3).trim(); continue; }
    const m = line.match(/^\|\s*(.+?)\s*\|\s*(\d+)\s*\|\s*(.+?)\s*\|\s*(.*?)\s*\|$/);
    if (!m || m[1] === 'Not in any list') continue;
    rows.push({ name: m[1], seen: Number(m[2]), guess: m[3], where: m[4], note });
  }
  return rows;
}
const SKIN = /\(Skin: /;
const guessOf = name => SKIN.test(name) ? 'skin' : '?';

// ---- MetaBot -------------------------------------------------------------------------------------------------------------
const slugOf = name => name.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(/['’]/g, '').replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');
const decode = s => s.replace(/&amp;/g, '&').replace(/&#x27;|&#39;|&apos;/g, "'").replace(/&quot;|&#34;/g, '"').replace(/&#x2013;|&ndash;/g, '–').replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&nbsp;|&#160;/g, ' ');
const sleep = ms => new Promise(r => setTimeout(r, ms));
let requests = 0;
async function page(url) {
  if (requests > 0 && pause) await sleep(pause);
  requests++;
  const r = await fetch(url, { headers: { 'User-Agent': UA, Accept: 'text/html' }, redirect: 'follow' });
  if (r.status === 404) return null;
  if (!r.ok) throw new Error(`${r.status} ${url}`);
  const html = await r.text();
  const title = decode((html.match(/<title>([^<]*)<\/title>/) || [])[1] || '');
  if (/^Page not found/.test(title)) return null;
  // "Kina – Drops, Sources & Uses | AION 2 | MetaBot.GG", "Nightmare Coin (Bound) | AION 2 | MetaBot.GG"
  const name = title.split(' | ')[0].replace(/ – [^–]*$/, '').trim();
  const body = html.slice(html.indexOf('<body'));
  const text = decode(body.replace(/<script[\s\S]*?<\/script>/g, ' ').replace(/<style[\s\S]*?<\/style>/g, ' ').replace(/<[^>]+>/g, '|')).replace(/\|\s*\|/g, '|').replace(/\|+/g, '|');
  const icons = [...html.matchAll(/property="og:image" content="([^"]+)"/g)].map(m => decode(m[1])).filter(u => !/\/brand\//.test(u));
  const field = label => { const m = text.match(new RegExp(`\\|${label}\\|([^|]+)\\|`)); return m ? m[1].trim() : null; };
  return { url, name, text, icon: icons[0] || null, field };
}

// the Crafting table of a craftable item's page: one row per faction
function recipesOn(p) {
  const out = [];
  const re = /\|([A-Za-z]+) · Lv\. (\d+) · (Elyos|Asmodian)\|((?:\d+\|× \|[^|]+\|(?:\+ \|)?)+)×(\d+)\|(\d+)%\|/g;
  for (const m of p.text.matchAll(re)) {
    const materials = [...m[4].matchAll(/(\d+)\|× \|([^|]+)\|/g)].map(x => ({ item: x[2].trim(), qty: Number(x[1]) }));
    out.push({ skill: m[1], mastery: Number(m[2]), faction: m[3], materials, makes: Number(m[5]), combo: Number(m[6]) / 100 });
  }
  return out;
}
// extra-items.json keeps MetaBot's category and a coarser one of its own: coins and gold are Currency, event coins Event.
// Its "bound" is the trading rule (a plain-named currency that cannot be traded is bound too), not the name's suffix.
const coarse = c => !c ? 'Currency' : /^Coin Event/.test(c) ? 'Event' : /^(Coin|Gold|Abyss Point)/.test(c) ? 'Currency' : c;

// ---- the drafts ------------------------------------------------------------------------------------------------------------
const added = [], questions = [], skipped = [];
const drafts = { 'extra-items.json': [], 'skins.json': [], 'recipes.json': [], 'mobs.txt': [], 'zones.txt': [] };
const sameName = (a, b) => norm(a) === norm(b);

async function resolve(row) {
  const name = row.name.replace(/\s+/g, ' ').trim();
  const have = knownAs(name);
  if (have) { skipped.push({ ...row, why: `already in ${[...new Set(have)].join(', ')}` }); return; }
  const guess = row.guess || guessOf(name);
  const slug = slugOf(name);
  const tried = [];
  const get = async url => { tried.push(url); return page(url); };
  const ask = why => questions.push({ ...row, why, tried: tried.slice() });

  if (guess === 'mob') {
    const p = await get(`${MB}/monsters/${slug}`);
    if (!p) return ask('no MetaBot monster page');
    if (!sameName(p.name, name)) return ask(`MetaBot's monster page is named "${p.name}"`);
    if (knownAs(p.name)) return skipped.push({ ...row, why: `already in ${knownAs(p.name).join(', ')} as "${p.name}"` });
    drafts['mobs.txt'].push(p.name);
    return added.push({ name: p.name, file: 'mobs.txt', kind: `enemy, level ${p.field('Level') || '?'} ${p.field('Rank') || ''}`.trim(), url: p.url });
  }
  if (guess === 'zone/instance') {
    const p = await get(`${MB}/zones/${slug}`);
    if (!p) return ask(`no MetaBot zone page; an instance would be ${MB}/dungeons/${slug} and needs a Kind in instances.txt`);
    if (!sameName(p.name, name)) return ask(`MetaBot's zone page is named "${p.name}"`);
    if (knownAs(p.name)) return skipped.push({ ...row, why: `already in ${knownAs(p.name).join(', ')} as "${p.name}"` });
    drafts['zones.txt'].push(p.name);
    return added.push({ name: p.name, file: 'zones.txt', kind: `${p.field('Type') || 'zone'} in ${p.field('Map') || '?'}`, url: p.url });
  }
  // an item, a skin, or no idea: the item page, then the bound one
  let p = await get(`${MB}/items/${slug}`);
  if (!p && !/-bound$/.test(slug)) p = await get(`${MB}/items/${slug}-bound`);
  if (!p) return ask('no MetaBot item page');
  if (!sameName(p.name, name)) return ask(`MetaBot's item page is named "${p.name}"`);
  if (knownAs(p.name)) return skipped.push({ ...row, why: `already in ${knownAs(p.name).join(', ')} as "${p.name}"` });
  const grade = p.field('Grade'), category = p.field('Category'), trading = p.field('Trading');
  if (!grade || !category) return ask(`the item page has no Grade/Category card (title "${p.name}")`);
  const skin = p.name.match(/^(.+?) \(Skin: ([^)]+)\)( \(Bound\))?$/);
  if (skin) {
    drafts['skins.json'].push({ id: null, name: p.name, set: skin[1], slot: skin[2], grade, tradable: trading !== 'Not tradable', url: p.url, set_url: `${MB}/skins/sets/${slugOf(skin[1])}` });
    return added.push({ name: p.name, file: 'skins.json', kind: `${grade} skin, ${skin[1]} / ${skin[2]}`, url: p.url });
  }
  drafts['extra-items.json'].push({ id: null, name: p.name, grade, tradable: trading !== 'Not tradable', category: coarse(category), icon: p.icon,
    iconSource: p.icon ? 'MetaBot' : null, metabotCategory: category, bound: trading === 'Not tradable', url: p.url });
  added.push({ name: p.name, file: 'extra-items.json', kind: `${grade} ${category}${trading === 'Not tradable' ? ', not tradable' : ''}`, url: p.url });
  for (const r of recipesOn(p)) {
    if (knownAs(p.name) && knownAs(p.name).includes('recipes')) break;
    drafts['recipes.json'].push({ name: p.name, skill: r.skill, output: p.name, output_qty: r.makes, materials: r.materials, kinah: 0, success_rate: null, great: [],
      recipe_id: null, mastery: r.mastery, combo_chance: r.combo, combo_success_rate: null, success_curve: null, url: p.url,
      note: `${r.faction} recipe drafted from the item page by tools/data-fill.js on ${today}: recipe_id, success_rate and the curve are not on the page` });
    added.push({ name: p.name, file: 'recipes.json', kind: `${r.faction} ${r.skill} recipe, mastery ${r.mastery}, ${r.materials.length} materials`, url: p.url });
  }
}

// ---- writing ------------------------------------------------------------------------------------------------------------
function writeJson(file, key, entries) {
  const p = path.join(dataDir, file);
  const raw = fs.readFileSync(p, 'utf8'), eol = raw.includes('\r\n') ? '\r\n' : '\n';
  const obj = JSON.parse(raw);
  obj[key].push(...entries);
  fs.writeFileSync(p, (JSON.stringify(obj, null, 1) + '\n').replace(/\n/g, eol));
}
function appendLines(file, names) {
  const p = path.join(dataDir, file);
  const raw = fs.readFileSync(p, 'utf8'), eol = raw.includes('\r\n') ? '\r\n' : '\n';
  fs.writeFileSync(p, raw + (raw.endsWith('\n') ? '' : eol) + names.map(n => n + eol).join(''));
}
function run(script, extra = []) {
  const r = spawnSync(process.execPath, [path.join(root, 'tools', script), ...extra], { stdio: 'inherit' });
  return r.status === 0;
}

async function main() {
  const rows = [...inputs.flatMap(rowsOf), ...given.map(name => ({ name, seen: 0, guess: guessOf(name), where: '', note: 'given' }))];
  const seen = new Set(); const todo = rows.filter(r => { const k = norm(r.name); if (!k || seen.has(k)) return false; seen.add(k); return true; });
  if (!todo.length) { console.log('No names to fill.'); return 0; }
  console.error(`${todo.length} name(s) from ${inputs.length} report(s) and ${given.length} given; lists read from ${dataDir}`);
  for (const row of todo) {
    try { await resolve(row); }
    catch (e) { questions.push({ ...row, why: `fetch failed: ${e.message}`, tried: [] }); }
    console.error(`  ${row.name}: ${added.find(a => sameName(a.name, row.name)) ? 'added' : questions.find(q => q.name === row.name) ? 'question' : 'skipped'}`);
  }
  const files = Object.entries(drafts).filter(([, v]) => v.length);
  if (!dry) {
    for (const [file, entries] of files) {
      if (file === 'extra-items.json' || file === 'skins.json') writeJson(file, 'items', entries);
      else if (file === 'recipes.json') writeJson(file, 'recipes', entries);
      else appendLines(file, entries);
    }
  }
  // the report
  const rep = [`# Data fill ${today}`, '',
    `${todo.length} name(s) looked up, ${requests} request(s) to MetaBot: ${added.length} entr${added.length === 1 ? 'y' : 'ies'} ${dry ? 'drafted (dry run, nothing written)' : 'written'}, ${questions.length} question(s), ${skipped.length} skipped (already in a list).`, ''];
  if (added.length) {
    rep.push('## Added', '', '| Name | File | What | Source |', '|---|---|---|---|');
    for (const a of added) rep.push(`| ${a.name} | ${a.file} | ${a.kind} | ${a.url} |`);
    rep.push('');
  }
  if (questions.length) {
    rep.push('## Questions', '', 'Names no page answered for, or that MetaBot has under another name. Each needs a person: file it by hand, or drop it as noise.', '');
    for (const q of questions) rep.push(`- **${q.name}** (${q.guess}${q.note && q.note !== 'given' ? `, ${q.note}` : ''}): ${q.why}.${q.tried.length ? ` Tried ${q.tried.join(' , ')}.` : ''}${q.where ? ` Seen in: "${q.where}"` : ''}`);
    rep.push('');
  }
  if (skipped.length) {
    rep.push('## Skipped', '');
    for (const s of skipped) rep.push(`- ${s.name}: ${s.why}`);
    rep.push('');
  }
  if (dry && files.length) {
    rep.push('## Drafts', '');
    for (const [file, entries] of files) rep.push(`### ${file}`, '', '```json', JSON.stringify(entries, null, 1), '```', '');
  }
  const out = rep.join('\n');
  console.log(out);
  if (outPath) fs.writeFileSync(outPath, out + '\n');
  if (dry || !files.length) return 0;
  // the manifest for the files trackers fetch, then the validator: a bad draft fails the run, and git shows what it wrote
  const manifestFiles = files.filter(([f]) => f !== 'extra-items.json').length;
  if (dataDir === path.join(root, 'data') && manifestFiles && !run(path.join('data', 'manifest.js'))) { console.error('data-fill: tools/data/manifest.js failed'); return 1; }
  if (!noCheck && !run('data-check.js', dataDir === path.join(root, 'data') ? [] : ['--data', dataDir])) { console.error('data-fill: the validator failed; the drafts are in the files above, fix or revert them'); return 1; }
  return 0;
}
main().then(c => process.exit(c)).catch(e => { console.error(e.stack || e.message); process.exit(2); });
