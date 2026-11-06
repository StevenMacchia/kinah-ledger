// The patch-notes watcher (2026-11-05): reads the official Aion 2 global patch notes and lists every name they mention
// that none of the tracker's game-data lists know, so a game patch turns into a short list of additions instead of a
// read-through. The loop that uses it is in tools/patch-watch.md.
//
//   node tools/patch-watch.js                 the articles not seen before (tools/patch-watch-state.json), then remembers them
//   node tools/patch-watch.js --latest 3      the three newest articles, seen or not; nothing is remembered
//   node tools/patch-watch.js --dry           report without remembering
//   node tools/patch-watch.js --out report.md also writes the report to a file
//
// Source: the community board API behind https://aion2.plaync.com/en-us/board/update/list (the page itself is script-
// rendered; the API answers plain requests): the list at .../board/update_en/article, each article's HTML body at
// .../article/<id> under article.content. No login, no key. One request per article, a second apart.
//
// Lists checked (data/): zones.txt, instances.txt, mobs.txt, tw-items.json + extra-items.json (items), recipes.json,
// gathering-items.json, loot-tables.json, skins.json. A name is "known" when its letters and digits match one of them.
// Candidates are quoted names ("Abyssal Forge:Ludra") and runs of capitalised words (Chaotic Outer Reshanta); headings
// and boilerplate are dropped by a stop list, and whatever is left is for a person to judge, with the sentence it came
// from and a guess at which list it belongs in.
const fs = require('fs'), path = require('path');
const root = path.join(__dirname, '..');
const API = 'https://api-global-community.plaync.com/aion2_global/board/update_en';
const UA = 'kinah-ledger patch-watch (https://github.com/StevenMacchia/kinah-ledger)';

const args = process.argv.slice(2);
const opt = (name, dflt) => { const i = args.indexOf(name); return i >= 0 ? (args[i + 1] ?? true) : dflt; };
const dry = args.includes('--dry');
const latest = opt('--latest', null);
const statePath = opt('--state', path.join(__dirname, 'patch-watch-state.json'));
const outPath = opt('--out', null);

// ---- the lists ---------------------------------------------------------------------------------------------------------
const norm = s => s.toLowerCase().replace(/\(bound\)/g, '').replace(/[^a-z0-9]/g, '');
function linesOf(file) {
  const p = path.join(root, 'data', file);
  if (!fs.existsSync(p)) return [];
  return fs.readFileSync(p, 'utf8').split(/\r?\n/).map(l => l.trim()).filter(l => l && !l.startsWith('#')).map(l => l.split('|')[0].trim());
}
function namesIn(file) {
  const p = path.join(root, 'data', file);
  if (!fs.existsSync(p)) return [];
  const out = [];
  const walk = o => {
    if (Array.isArray(o)) o.forEach(walk);
    else if (o && typeof o === 'object') for (const [k, v] of Object.entries(o)) {
      if (typeof v === 'string' && /^(name|n|item|title|result|output|material)$/.test(k)) out.push(v);
      else walk(v);
    }
  };
  try { walk(JSON.parse(fs.readFileSync(p, 'utf8'))); } catch (e) { console.error(`${file}: ${e.message}`); }
  return out;
}
const lists = {
  zones: [...linesOf('zones.txt'), ...linesOf('instances.txt')],
  mobs: linesOf('mobs.txt'),
  items: [...namesIn('tw-items.json'), ...namesIn('extra-items.json'), ...namesIn('skins.json')],
  recipes: namesIn('recipes.json'),
  gathering: namesIn('gathering-items.json'),
  loot: namesIn('loot-tables.json'),
};
const known = new Map();   // normalised name -> which lists
for (const [list, names] of Object.entries(lists)) for (const n of names) { const k = norm(n); if (!k) continue; known.set(k, (known.get(k) || []).concat(list)); }

// ---- the notes -----------------------------------------------------------------------------------------------------------
async function getJson(url) {
  const r = await fetch(url, { headers: { 'User-Agent': UA, Accept: 'application/json' } });
  if (!r.ok) throw new Error(`${r.status} ${url}`);
  return r.json();
}
const sleep = ms => new Promise(r => setTimeout(r, ms));
function textOf(html) {
  return html
    .replace(/<br\s*\/?>|<\/(p|div|li|h[1-6]|tr)>/gi, '\n')
    .replace(/<[^>]+>/g, ' ')
    .replace(/&nbsp;|&#160;/g, ' ').replace(/&quot;|&#34;/g, '"').replace(/&#39;|&apos;/g, "'").replace(/&amp;/g, '&').replace(/&lt;/g, '<').replace(/&gt;/g, '>')
    .replace(/[​‌﻿]/g, '')
    .replace(/[ \t]+/g, ' ').replace(/\n\s*\n+/g, '\n').trim();
}

// words that start headings and boilerplate, never a game name on their own
const STOP = new Set(['Patch Notes', 'Hotfix', 'Notice', 'Improvements', 'Bug Fixes', 'Other Improvements', 'Content & Systems', 'Content And Systems',
  'Normal', 'Hard', 'Easy', 'PST', 'PDT', 'CET', 'CEST', 'UTC', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday',
  'Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec', 'January', 'February', 'March', 'April', 'June', 'July', 'August', 'September', 'October', 'November', 'December',
  'Retrieval And Restoration', 'Known Issues', 'Fixed', 'Added', 'Changed', 'Removed', 'Update', 'Updates', 'Maintenance', 'Thank You', 'Dear Daevas', 'Daevas',
  'NC', 'NCSOFT', 'AION', 'Aion', 'Aion 2', 'AION 2', 'Discord', 'Steam', 'Windows', 'PC', 'HP', 'MP', 'DP', 'XP', 'UI', 'HUD', 'PvP', 'PvE', 'NPC', 'NPCs', 'AA', 'Lv', 'Level']);
const SMALL = /^(of|the|and|to|in|on|for|a|an|at|by|from|with|or)$/i;
// words that make a run of capitals a heading, a date, a menu path or a price line, never a game name
const GENERIC = new Set(['mode', 'details', 'schedule', 'gear', 'improvements', 'players', 'event', 'events', 'menu', 'store', 'shop', 'product', 'content', 'contents',
  'purchase', 'limit', 'sales', 'period', 'deletion', 'date', 'regions', 'region', 'europe', 'na', 'east', 'west', 'south', 'america', 'asia', 'am', 'pm', 'pst', 'pdt', 'cet', 'cest',
  'normal', 'hard', 'easy', 'all', 'both', 'some', 'any', 'new', 'other', 'bound', 'unbound', 'item', 'items', 'level', 'entry', 'minimum', 'maximum', 'skin', 'skins', 'set', 'sets',
  'weapon', 'armor', 'wings', 'outfit', 'pet', 'pets', 'name', 'source', 'ui', 'color', 'palette', 'code', 'font', 'chat', 'settings', 'hey', 'dear', 'daevas', 'top', 'ranking', 'top-ranking',
  'forces', 'force', 'hall', 'fame', 'first', 'season', 'missions', 'mission', 'eligible', 'attendance', 'daily', 'weekly', 'dungeon', 'dungeons', 'expedition', 'expeditions', 'boss', 'bosses',
  'field', 'artifact', 'siege', 'battle', 'crafted', 'crafting', 'recipe', 'recipes', 'transfer', 'server', 'servers', 'character', 'progression', 'in-game', 'retrieval', 'restoration', 'success', 'great', 'available', 'rewards', 'reward']);
const MONTH = /\b(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\b|\b(AM|PM|PST|PDT|CET|CEST|UTC)\b|\b(Mon|Tues|Wednes|Thurs|Fri|Satur|Sun)days?\b|\d{1,2}:\d{2}/;
const PLACE = /\b(Valley|Highlands?|Canyon|Hills?|Sanctum|Ruins|Garden|Forest|Plains?|Caves?|Cavern|Fortress|Village|Lake|Coast|Shore|Pass|Temple|Swamp|Marsh|Desert|Island|Isle|Abyss|Outpost|Camp|Fields?|Ridge|Peak|Mountains?|Basin|Plateau|Woods|Grove|Gorge|Hollow|Ravine|Cliffs?|Falls|Springs?|River|Tower|Citadel|Stronghold|Fort|Shrine|Sanctuary|Crypt|Tomb|Mines?|Quarry|Den|Nest|Lair|Harbor|Oasis|Wasteland|Crater|Dunes|Lowlands|Vale|Meadow|Base|Reshanta|Forge|Dungeon|Arena|Trial|Expedition|Raid)\b/;
const ITEMISH = /\b(Stone|Shard|Fragment|Crystal|Ore|Ingot|Leather|Essence|Potion|Scroll|Box|Chest|Pouch|Ticket|Token|Coin|Key|Codex|Odyle|Kina|Sword|Dagger|Bow|Staff|Orb|Mace|Spear|Armor|Helm|Gloves|Boots|Pauldrons|Greaves|Cloak|Ring|Earring|Necklace|Belt|Bracelet|Skin|Outfit|Pet|Mount|Candy)\b/;
const MOBISH = /\b(Boss|Monster|Elite|Named|Lord|King|Queen|Guardian|Keeper|Warden|Captain|Commander|Chief|Raider|Scout|Soldier|Drakan|Kobold|Kerubim|Kerubar|Krall|Mau|Balaur|Shugo|Golem|Spirit|Wraith|Beast)\b/;

function candidates(text) {
  const found = new Map();   // name -> {count, where}
  const add = (name, sentence) => {
    name = name.replace(/[“”]/g, '"').replace(/[’]/g, "'").replace(/\s+/g, ' ').replace(/^[\s"':,.-]+|[\s"':,.-]+$/g, '');
    if (name.length < 3 || name.length > 60 || /^\d/.test(name)) return;
    if (STOP.has(name) || STOP.has(name.replace(/\s+/g, ' '))) return;
    if (!/[A-Za-z]{3}/.test(name)) return;
    if (MONTH.test(name)) return;
    name = name.replace(/^(The|A|An)\s+/, '');
    // "Black Crow Mask (Skin: Helm)", "Pet: Azure Flame Kumiho": the tag is the kind, the rest is the name
    if (/^Skin:/.test(name)) return;
    name = name.replace(/^Pet:\s*/, '');
    // a heading or a menu path: every word is a generic one
    const words = name.split(/[\s:]+/).filter(w => w && !SMALL.test(w));
    if (words.length === 0 || words.every(w => GENERIC.has(w.toLowerCase()))) return;
    // "Fire Temple and Ferocious Horn Den": two names, each judged on its own
    if (/\s(and|\/)\s/.test(name)) { for (const part of name.split(/\s(?:and|\/)\s/)) if (part.trim().split(/\s+/).length >= 2) add(part, sentence); return; }
    const e = found.get(name) || { count: 0, where: sentence.trim().slice(0, 160) };
    e.count++; found.set(name, e);
  };
  for (const line of text.split('\n')) {
    for (const m of line.matchAll(/["“]([^"”\n]{3,60})["”]/g)) add(m[1], line);
    // runs of capitalised words, small words allowed inside, ":" allowed ("Abyssal Forge:Ludra")
    for (const m of line.matchAll(/\b([A-Z][\w'’-]*(?:(?:\s+(?:of|the|and|de)\s+|\s*:\s*|\s+)[A-Z][\w'’-]*){1,5})\b/g)) {
      const words = m[1].split(/\s+/).filter(w => !SMALL.test(w));
      if (words.length >= 2) add(m[1], line);
    }
  }
  return found;
}
const SKIN_TAG = /\((?:Skin|Armor Skin|Weapon Skin|Wings Skin)/i;
const guess = (name, where) => (where.includes(name + ' (Skin') || where.includes(name + '(Skin') || (where.startsWith('(') && SKIN_TAG.test(where))) ? 'skin'
  : PLACE.test(name) ? 'zone/instance' : ITEMISH.test(name) ? 'item' : MOBISH.test(name) ? 'mob' : '?';

async function main() {
  const state = fs.existsSync(statePath) ? JSON.parse(fs.readFileSync(statePath, 'utf8')) : { seen: {} };
  const list = (await getJson(`${API}/article`)).contentList || [];
  let pick = latest ? list.slice(0, Number(latest)) : list.filter(a => !state.seen[a.id]);
  if (pick.length === 0) { console.log(`No new patch notes (${list.length} on the board, last seen: ${Object.values(state.seen)[0] || 'none'}).`); return 0; }
  const report = [];
  let unknownTotal = 0;
  for (const a of pick) {
    await sleep(1000);
    const art = await getJson(`${API}/article/${a.id}`);
    const body = art.article?.content; const text = textOf(typeof body === 'string' ? body : body?.content || '');
    const when = a.timestamps?.publishedAt || a.timestamps?.createdAt || '';
    const url = `https://aion2.plaync.com/en-us/board/update/view?articleId=${a.id}`;
    const cands = candidates(text);
    const lookup = name => known.get(norm(name)) || known.get(norm(name).replace(/s$/, '')) || known.get(norm(name) + 's');
    const rows = [...cands.entries()].map(([name, e]) => ({ name, ...e, known: lookup(name) }));
    const unknown = rows.filter(r => !r.known).sort((x, y) => y.count - x.count || x.name.localeCompare(y.name));
    const seen = rows.filter(r => r.known);
    unknownTotal += unknown.length;
    report.push(`## ${a.title}`, `${url}${when ? `  (${String(when).slice(0, 10)})` : ''}`, '',
      `${text.length} characters, ${rows.length} names found: ${seen.length} known, ${unknown.length} not in any list.`, '');
    if (unknown.length) {
      report.push('| Not in any list | Seen | Guess | Where |', '|---|---|---|---|');
      for (const r of unknown) report.push(`| ${r.name} | ${r.count} | ${guess(r.name, r.where)} | ${r.where.replace(/\|/g, '/')} |`);
      report.push('');
    }
    if (seen.length) report.push(`Known: ${seen.map(r => `${r.name} (${[...new Set(r.known)].join(', ')})`).join(' · ')}`, '');
    if (!dry && !latest) state.seen[a.id] = a.title;
  }
  const out = report.join('\n');
  console.log(out);
  if (outPath) fs.writeFileSync(outPath, out + '\n');
  if (!dry && !latest) fs.writeFileSync(statePath, JSON.stringify({ seen: state.seen, last_run: new Date().toISOString() }, null, 2) + '\n');
  console.log(`\n${pick.length} article(s), ${unknownTotal} name(s) not in any list.`);
  return 0;
}
main().then(c => process.exit(c)).catch(e => { console.error(e.message); process.exit(2); });
