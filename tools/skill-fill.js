// The skill-fill step (2026-11-14): names every ability the Parser can show, so its inspect view never says a number.
// data/skills.json held only the 279 class skills on MetaBot's skill pages; the chain skills (Templar's Punishing Strike,
// Chanter's Bursting Blow), the Spiritmaster's spirits' own attacks, and the Theostones' strikes were missing, and the
// Parser printed their ids. This adds them from the game client's own English skill table, checks that table against
// every name MetaBot has, and reports what Steven's saved fights still leave unnamed. The loop is in tools/skill-fill.md.
//
//   node tools/skill-fill.js                          add what is missing to data/skills.json, then the coverage report
//   node tools/skill-fill.js --dry-run                the report only, write nothing
//   node tools/skill-fill.js --icons <dir>            also fetch each listed ability's icon into <dir> (<id>.webp / <id>.png),
//                                                     for python -I tools/skill-icons.py <dir>
//   node tools/skill-fill.js --cache <dir>            keep the two source tables in <dir> (and read them from there next time)
//   node tools/skill-fill.js --metabot <file>         the site's data/metabot-classes.json: each MetaBot entry's page as its src
//   node tools/skill-fill.js --verify-items           check each Theostone's name against its MetaBot item page (one request each)
//   node tools/skill-fill.js --records <dir>          where the saved fights are (default %LOCALAPPDATA%\KinahLedger)
//   node tools/skill-fill.js --fights <file> ...      count these records instead (JSON lines, as the tracker keeps them;
//                                                     KinahLedger.exe parser-replay <recording> --save <file> writes them)
//   node tools/skill-fill.js --out report.md          also write the report to a file
//
// Sources, each entry's "src" in skills.json (the "sources" table at its top says where each one is):
//   metabot  MetaBot.GG's skill pages (https://metabot.gg/en/aion-2/skills/<page>), read from the global client: the
//            279 class skills. Kept as they are; never overwritten.
//   client   The global client's English skill table as A2Tools (taengu/A2Tools-DPS-Meter, GPL-3.0) ships it,
//            src/data/i18n/skills/en.json at a pinned commit. It agrees with MetaBot on all 279 names they share, which is
//            the check that it is the global client's text and not a translation. Every new name comes from it.
//   Theostone names are also MetaBot item names ("Theostone: Zikel's Vestige"); --verify-items checks them one by one.
//   Icons ("icon" in an entry: the file's name, or "=<id>" for another entry's): MetaBot's copy for a MetaBot skill
//   (https://metabot.gg/web/aion2/skills/<id>.webp); for the rest the game's own icon file on NCSoft's game-data CDN
//   (https://assets.playnccdn.com/static-aion2-gamedata/resources/<icon>.png), named the way A2Tools names it: a class
//   skill by the first four digits of its id (src/data/skill_icons.json, from the game data), a Theostone by its id
//   (Icon_Item_Usable_Godstone_WP_r_ and the id's 6th-7th digits in hex: Traufnir's Courage 3000117 is _011, as on its
//   MetaBot item page). A spirit's attacks and commands ("Fire Spirit: Flame Explosion") take their spirit's summon icon
//   (Summon: Fire Spirit). Anything else has none, and the tracker draws the class's emblem.
//
// Which ids the Parser can show (it files a class skill's hits under the skill without its rank, the last four digits;
// a spirit's own attack, a Theostone's strike and the like under their own id), so which ids are listed:
//   - each class skill: an id 11xx0000 to 19xx0000 (class 11 Gladiator ... 18 Chanter, 19 Brawler);
//   - each class's own block xx00xxxx (the Spiritmaster's spirit commands: 16001101 "Fire Spirit: Flame Explosion"),
//     except xx000000, the class's "Weapon Equip", which never deals damage;
//   - the spirits' basic attacks (1000xx) and the Theostones (30xxxxx).
// Left out: monsters' skills, test and internal entries ("(DNT)", "Test_"), food and potions.
// Never a guess: an id neither table names stays unnamed, and the report lists it with its damage and who used it.
const fs = require('fs'), path = require('path'), os = require('os');
const root = path.join(__dirname, '..');
const UA = 'kinah-ledger skill-fill (https://github.com/StevenMacchia/kinah-ledger)';
const CLIENT = { repo: 'taengu/A2Tools-DPS-Meter', commit: '537452fbac9fc1db1e46e0b06d42015594fa9f25', file: 'src/data/i18n/skills/en.json', license: 'GPL-3.0' };
const ICONS = { repo: CLIENT.repo, commit: CLIENT.commit, file: 'src/data/skill_icons.json', license: CLIENT.license };
const NC_ICON = 'https://assets.playnccdn.com/static-aion2-gamedata/resources/';
const MB_ICON = 'https://metabot.gg/web/aion2/skills/';
const CLASS_BY_FAMILY = { 11: 'gladiator', 12: 'templar', 13: 'assassin', 14: 'ranger', 15: 'sorcerer', 16: 'spiritmaster', 17: 'cleric', 18: 'chanter', 19: 'brawler' };

const args = process.argv.slice(2);
const opt = (name, dflt) => { const i = args.indexOf(name); return i >= 0 ? (args[i + 1] ?? true) : dflt; };
const dry = args.includes('--dry-run');
const iconDir = opt('--icons', null);
const cacheDir = opt('--cache', null);
const metabotFile = opt('--metabot', null);
const verifyItems = args.includes('--verify-items');
const recordsDir = opt('--records', path.join(process.env.LOCALAPPDATA || path.join(os.homedir(), 'AppData', 'Local'), 'KinahLedger'));
const outPath = opt('--out', null);
const fightFiles = args.flatMap((a, i) => a === '--fights' && args[i + 1] ? [args[i + 1]] : []);
const skillsPath = path.join(root, 'data', 'skills.json');
const sleep = ms => new Promise(r => setTimeout(r, ms));

async function get(url, binary = false) {
  for (let i = 0; ; i++) {
    try {
      const r = await fetch(url, { headers: { 'user-agent': UA }, redirect: 'follow' });
      if (r.status === 404 || r.status === 403) return null;
      if (!r.ok) throw new Error('HTTP ' + r.status);
      return binary ? Buffer.from(await r.arrayBuffer()) : await r.text();
    } catch (e) { if (i >= 2) throw new Error(url + ': ' + e.message); await sleep(1500 * (i + 1)); }
  }
}

async function table(src) {
  const name = src.repo.replace('/', '_') + '_' + src.commit.slice(0, 7) + '_' + path.basename(src.file);
  if (cacheDir && fs.existsSync(path.join(cacheDir, name))) return JSON.parse(fs.readFileSync(path.join(cacheDir, name), 'utf8'));
  const text = await get(`https://raw.githubusercontent.com/${src.repo}/${src.commit}/${src.file}`);
  if (!text) throw new Error('no ' + src.file + ' at ' + src.repo + '@' + src.commit);
  if (cacheDir) { fs.mkdirSync(cacheDir, { recursive: true }); fs.writeFileSync(path.join(cacheDir, name), text); }
  return JSON.parse(text);
}

/** The Parser's own rule (tracker Parser/Abilities.cs Resolve): the id itself, else a class skill's rank or variant under its base. */
function resolve(skills, id) {
  if (skills[id]) return id;
  if (id >= 10_000_000 && id < 20_000_000 && id % 10_000 && skills[id - id % 10_000]) return id - id % 10_000;
  return null;
}

/** Can the Parser show this id: a class skill (no rank digits), a class's own block, a spirit's attack or a Theostone. */
function showable(id) {
  if (id >= 11_000_000 && id < 20_000_000) {
    if (id % 1_000_000 === 0) return false;                      // the class's Weapon Equip
    if (Math.floor(id / 10_000) % 100 === 0) return true;         // the class's own block: spirit commands and the like
    return id % 10_000 === 0;                                     // a class skill: its ranks come under it
  }
  return (id >= 100_000 && id < 101_000) || (id >= 3_000_000 && id < 3_100_000);
}
const junk = n => !n || /\(DNT\)|^Test|_|\bTEST\b/i.test(n);

(async () => {
  const doc = JSON.parse(fs.readFileSync(skillsPath, 'utf8'));
  const skills = doc.skills;
  const report = [];
  const say = s => { report.push(s); console.log(s); };

  const client = await table(CLIENT);
  const iconByPrefix = await table(ICONS);

  // the check that the client table is the global client's English text: it must agree with every MetaBot name
  const metabotIds = Object.keys(skills).filter(k => (skills[k].src ?? 'metabot') === 'metabot');
  const disagree = metabotIds.filter(k => client[k] !== skills[k].name);
  say(`client table: ${Object.keys(client).length} entries; agrees with MetaBot on ${metabotIds.length - disagree.length} of ${metabotIds.length}`);
  for (const k of disagree) say(`  disagrees: ${k} MetaBot "${skills[k].name}", client "${client[k] ?? '(none)'}" (MetaBot kept)`);
  if (disagree.length > metabotIds.length / 20) { console.error('the client table disagrees with MetaBot on more than 5%: not the global client\'s names, stopping'); process.exit(1); }

  // MetaBot entries: their src and page
  const pages = {};
  if (metabotFile) {
    const mb = JSON.parse(fs.readFileSync(metabotFile, 'utf8'));
    for (const s of Object.values(mb.skills)) { const m = /skills\/(\d+)\.webp/.exec(s.icon || ''); if (m) pages[m[1]] = s.url; }
  }
  for (const k of metabotIds) {
    skills[k].src = 'metabot';
    if (pages[k]) skills[k].page = pages[k].replace(/^.*\/skills\//, '');
  }

  // new entries
  let added = 0;
  const summon = {};   // "Fire Spirit" -> the id of "Summon: Fire Spirit"
  for (const [k, name] of Object.entries(client)) {
    const m = /^Summon: (\w+ Spirit)$/.exec(name);
    if (m && k.startsWith('16') && Number(k) % 10_000 === 0 && !summon[m[1]]) summon[m[1]] = k;
  }
  for (const [k, name] of Object.entries(client)) {
    const id = Number(k);
    if (skills[k] || !showable(id) || junk(name)) continue;
    const spirit = /^(\w+ Spirit): /.exec(name)?.[1];
    const cls = id >= 10_000_000 ? CLASS_BY_FAMILY[Math.floor(id / 1_000_000)] ?? '' : spirit ? 'spiritmaster' : '';
    skills[k] = { name, cls, src: 'client' };
    let icon = null;
    if (id >= 3_000_000 && id < 3_100_000) icon = 'Icon_Item_Usable_Godstone_WP_r_' + Number((k + '0').slice(5, 7)).toString(16).padStart(3, '0');
    else if (spirit && summon[spirit] && (id < 1_000_000 || Math.floor(id / 10_000) % 100 === 0)) icon = '=' + summon[spirit];
    else if (id >= 11_000_000 && id % 10_000 === 0) icon = iconByPrefix[k.slice(0, 4)] ?? null;
    if (icon) skills[k].icon = icon;
    added++;
  }
  say(`added ${added} abilities; skills.json lists ${Object.keys(skills).length}`);

  // the Theostones against their MetaBot item pages
  if (verifyItems) {
    let ok = 0; const off = [];
    for (const k of Object.keys(skills).filter(k => /^Theostone: /.test(skills[k].name))) {
      const slug = skills[k].name.toLowerCase().replace(/'/g, '').replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
      const html = await get('https://metabot.gg/en/aion-2/items/' + slug) ?? await get('https://metabot.gg/en/aion-2/items/' + slug + '-bound');
      const t = html && /<title>([^<]*)/.exec(html)?.[1].replace(/&#x27;/g, "'").replace(/&amp;/g, '&');
      if (t && t.startsWith(skills[k].name)) { ok++; skills[k].item = slug; } else off.push(`${k} ${skills[k].name}`);
      await sleep(1200);
    }
    say(`Theostones on MetaBot's item pages: ${ok} found, ${off.length} not: ${off.join('; ') || '-'}`);
  }

  // the icons
  if (iconDir) {
    fs.mkdirSync(iconDir, { recursive: true });
    let got = 0, none = [];
    for (const [k, s] of Object.entries(skills)) {
      if (fs.existsSync(path.join(iconDir, k + '.webp')) || fs.existsSync(path.join(iconDir, k + '.png'))) continue;
      if (s.icon?.startsWith('=')) continue;   // copied from its spirit's summon below
      const url = s.src === 'metabot' ? MB_ICON + k + '.webp' : s.icon ? NC_ICON + s.icon + '.png' : null;
      const buf = url && await get(url, true);
      if (buf) { fs.writeFileSync(path.join(iconDir, k + (s.src === 'metabot' ? '.webp' : '.png')), buf); got++; } else none.push(k);
      await sleep(150);
    }
    for (const [k, s] of Object.entries(skills)) {
      if (!s.icon?.startsWith('=')) continue;
      const from = s.icon.slice(1), ext = ['webp', 'png'].find(e => fs.existsSync(path.join(iconDir, from + '.' + e)));
      if (ext) fs.copyFileSync(path.join(iconDir, from + '.' + ext), path.join(iconDir, k + '.' + ext)); else none.push(k);
    }
    say(`icons: ${got} fetched into ${iconDir}; ${none.length} with none (the class's emblem stands in)`);
  }

  // coverage over the saved fights
  const files = [...fightFiles];
  const chars = path.join(recordsDir, 'characters');
  if (!fightFiles.length && fs.existsSync(chars)) for (const c of fs.readdirSync(chars)) { const f = path.join(chars, c, 'encounters.jsonl'); if (fs.existsSync(f)) files.push(f); }
  if (!fightFiles.length && fs.existsSync(path.join(recordsDir, 'encounters.jsonl'))) files.push(path.join(recordsDir, 'encounters.jsonl'));
  const dmg = new Map(); let total = 0, fights = 0;
  for (const f of files) for (const line of fs.readFileSync(f, 'utf8').split('\n')) {
    if (!line.trim()) continue;
    let e; try { e = JSON.parse(line); } catch { continue; }
    fights++;
    for (const m of e.Roster ?? []) for (const a of m.Abilities ?? []) {
      const d = dmg.get(a.Skill) ?? { dmg: 0, hits: 0, fights: 0, cls: new Set() };
      d.dmg += a.Damage; d.hits += a.Hits; d.fights++; if (m.Class) d.cls.add(m.Class); dmg.set(a.Skill, d); total += a.Damage;
    }
  }
  // ids the tracker logged as unnamed ("ability unnamed: 16000000 ...", Parser/Abilities.cs), from reads.log
  const logged = new Set();
  for (const f of ['reads.log', 'reads.old.log']) {
    const p = path.join(recordsDir, f); if (!fs.existsSync(p)) continue;
    for (const m of fs.readFileSync(p, 'utf8').matchAll(/ability unnamed: (\d+)/g)) logged.add(Number(m[1]));
  }
  if (total > 0) {
    const named = [...dmg].filter(([id]) => id !== 0 && resolve(skills, id) != null).reduce((s, [, d]) => s + d.dmg, 0);
    const over = dmg.get(0)?.dmg ?? 0;
    const pc = x => (100 * x / total).toFixed(2) + '%';
    say(`\nsaved fights: ${fights} in ${files.length} files, ${dmg.size} abilities, ${total.toLocaleString('en-US')} damage`);
    say(`named: ${pc(named)}; damage over time (one row, not an ability): ${pc(over)}; unnamed: ${pc(total - named - over)}`);
    const left = [...dmg].filter(([id]) => id !== 0 && resolve(skills, id) == null).sort((a, b) => b[1].dmg - a[1].dmg);
    if (left.length) {
      say('\n| id | share | hits | fights | used by | client table says |\n|---|---|---|---|---|---|');
      for (const [id, d] of left) say(`| ${id} | ${pc(d.dmg)} | ${d.hits} | ${d.fights} | ${[...d.cls].join(', ')} | ${client[id] ?? '(none)'} |`);
    }
  }
  const loggedLeft = [...logged].filter(id => resolve(skills, id) == null);
  if (loggedLeft.length) say(`\nlogged unnamed by the tracker, still unnamed: ${loggedLeft.join(', ')}`);

  if (!dry) {
    doc.source = 'MetaBot.GG skill pages and the global client\'s English skill table (tools/skill-fill.js)';
    doc.sources = {
      metabot: 'https://metabot.gg/en/aion-2/skills/<page> (via kinahledger.com data/metabot-classes.json, fetched 2026-10-28)',
      client: `https://github.com/${CLIENT.repo}/blob/${CLIENT.commit}/${CLIENT.file} (${CLIENT.license}; agrees with MetaBot on all ${metabotIds.length - disagree.length} shared names)`,
      icon: `${NC_ICON}<icon>.png (named as https://github.com/${ICONS.repo}/blob/${ICONS.commit}/${ICONS.file} and its Theostone rule have it); "=<id>": that entry's icon`,
      item: 'https://metabot.gg/en/aion-2/items/<item> (the Theostones\' names, --verify-items)',
    };
    const sorted = {};
    for (const k of Object.keys(skills).sort((a, b) => a - b)) sorted[k] = skills[k];
    doc.skills = sorted;
    // one skill per line keeps the diff readable
    const body = Object.entries(sorted).map(([k, v]) => `${JSON.stringify(k)}: ${JSON.stringify(v)}`).join(',\n');
    const head = Object.entries(doc).filter(([k]) => k !== 'skills').map(([k, v]) => `${JSON.stringify(k)}: ${JSON.stringify(v, null, 1).replace(/\n\s*/g, ' ')}`).join(',\n');
    fs.writeFileSync(skillsPath, `{\n${head},\n"skills": {\n${body}\n}\n}\n`);
    say(`\nwrote ${path.relative(root, skillsPath)}`);
  }
  if (outPath) fs.writeFileSync(outPath, report.join('\n') + '\n');
})().catch(e => { console.error(e.message); process.exit(1); });
