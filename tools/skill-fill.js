// The skill-fill step (2026-11-14): names every ability the Parser can show, so its inspect view never says a number.
// data/skills.json held only the 279 class skills on MetaBot's skill pages; the chain skills (Templar's Punishing Strike,
// Chanter's Bursting Blow), the Spiritmaster's spirits' own attacks and the Theostones' strikes were missing, and the
// Parser printed their ids. This adds the ones public MIT-licensed tables name, checks each name against the game
// client's own English table, and reports what Steven's saved fights still leave unnamed. The loop is in tools/skill-fill.md.
//
//   node tools/skill-fill.js                          rebuild the non-MetaBot entries of data/skills.json, then the report
//   node tools/skill-fill.js --dry-run                the report only, write nothing
//   node tools/skill-fill.js --icons <dir>            also fetch each listed ability's icon into <dir> (<id>.webp / <id>.png),
//                                                     for python tools/skill-icons.py <dir>
//   node tools/skill-fill.js --cache <dir>            keep the source tables in <dir> (and read them from there next time)
//   node tools/skill-fill.js --metabot <file>         the site's data/metabot-classes.json: each MetaBot entry's page
//   node tools/skill-fill.js --verify-items           check each Theostone against its MetaBot item page (name and icon)
//   node tools/skill-fill.js --records <dir>          where the saved fights are (default %LOCALAPPDATA%\KinahLedger)
//   node tools/skill-fill.js --fights <file> ...      count these records instead (JSON lines, as the tracker keeps them;
//                                                     KinahLedger.exe parser-replay <recording> --save <file> writes them)
//   node tools/skill-fill.js --out report.md          also write the report to a file
//
// What ships ("src" of each entry; the "sources" table at the top of skills.json says where each one is):
//   metabot     MetaBot.GG's skill pages (https://metabot.gg/en/aion-2/skills/<page>): the 279 class skills. Never touched.
//   acompanion  Swellshinider/AcompanION public/gamedata.json (MIT): names by id, with the game's icon file name.
//   pingmaker   gunba/pingmaker skills.json (MIT): ids by name.
//   Both pinned to a commit. Where they differ, the one the check table confirms ships; with no check entry, they must agree.
// The check (never shipped): the global client's English skill table as A2Tools carries it (taengu/A2Tools-DPS-Meter,
// GPL-3.0). It agrees with MetaBot on all 279 shared names, so a shipped name must match it too; one that doesn't is
// left out and reported. Names only A2Tools has are left out as well (they read "<Class> skill" in the tracker) and
// the report lists the ones Steven's fights use, with their share of the damage, until a non-GPL source names them.
// Icons: MetaBot's copy for a MetaBot skill; the icon AcompanION names, from NCSoft's game-data CDN
// (https://assets.playnccdn.com/static-aion2-gamedata/resources/<icon>.png); else Waffle-ens/waffle_meter's per-id copy
// (MIT, dotnet/Assets/SkillIcons/<id>.png); a Theostone's from its MetaBot item page (--verify-items). Else none: the
// tracker draws the class's emblem.
//
// Which ids the Parser can show (it files a class skill's hits under the skill without its rank, the last four digits;
// a spirit's own attack, a Theostone's strike and the like under their own id), so which ids are listed:
//   - each class skill: an id 11xx0000 to 19xx0000 (class 11 Gladiator ... 18 Chanter, 19 Brawler);
//   - each class's own block xx00xxxx (the Spiritmaster's spirit commands: 16001101 "Fire Spirit: Flame Explosion"),
//     except xx000000, the class's "Weapon Equip", which never deals damage;
//   - the spirits' basic attacks (1000xx) and the Theostones (30xxxxx).
// Never a guess: an id no shipped source names stays unnamed, and the report lists it.
const fs = require('fs'), path = require('path'), os = require('os');
const root = path.join(__dirname, '..');
const UA = 'kinah-ledger skill-fill (https://github.com/StevenMacchia/kinah-ledger)';
const ACOMP = { repo: 'Swellshinider/AcompanION', commit: 'beb75aef160d74f37300701603490e9c2f41df13', file: 'public/gamedata.json', license: 'MIT' };
const PING = { repo: 'gunba/pingmaker', commit: '09687ad3ddc19f5189562eb8e17375f7c45e02c9', file: 'skills.json', license: 'MIT' };
const WAFFLE = { repo: 'Waffle-ens/waffle_meter', commit: '2bef5591d27f06f9731a862f6bfab3cf94b8f3d9', dir: 'dotnet/Assets/SkillIcons', license: 'MIT' };
const CHECK = { repo: 'taengu/A2Tools-DPS-Meter', commit: '537452fbac9fc1db1e46e0b06d42015594fa9f25', file: 'src/data/i18n/skills/en.json', license: 'GPL-3.0' };
const NC_ICON = 'https://assets.playnccdn.com/static-aion2-gamedata/resources/';
const MB_ICON = 'https://metabot.gg/web/aion2/skills/';
const MB_ITEM_ICON = 'https://metabot.gg/web/aion2/items/';
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
const raw = (src, file) => `https://raw.githubusercontent.com/${src.repo}/${src.commit}/${file}`;

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
  const text = await get(raw(src, src.file));
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
const slugOf = n => n.toLowerCase().replace(/'/g, '').replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');

(async () => {
  const doc = JSON.parse(fs.readFileSync(skillsPath, 'utf8'));
  const report = [];
  const say = s => { report.push(s); console.log(s); };

  // start from the MetaBot entries; every other entry is rebuilt from the sources on each run
  const skills = {};
  for (const [k, v] of Object.entries(doc.skills)) if ((v.src ?? 'metabot') === 'metabot') skills[k] = { ...v, src: 'metabot' };
  const metabotIds = Object.keys(skills);
  if (metabotFile) {
    const mb = JSON.parse(fs.readFileSync(metabotFile, 'utf8'));
    for (const s of Object.values(mb.skills)) { const m = /skills\/(\d+)\.webp/.exec(s.icon || ''); if (m && skills[m[1]]) skills[m[1]].page = s.url.replace(/^.*\/skills\//, ''); }
  }

  const check = await table(CHECK);
  const acomp = {};
  for (const [k, v] of Object.entries((await table(ACOMP)).skills ?? {})) acomp[k] = { name: typeof v.name === 'object' ? v.name.en : v.name, icon: v.icon };
  const ping = {};
  for (const [name, ids] of Object.entries(await table(PING))) for (const id of ids) if (!(id in ping)) ping[id] = name;

  // each source's agreement with MetaBot, and the check table's
  const agree = t => metabotIds.filter(k => t(k) != null).map(k => t(k) === skills[k].name);
  for (const [label, t] of [['client table (check)', k => check[k]], ['AcompanION', k => acomp[k]?.name], ['pingmaker', k => ping[k]]]) {
    const a = agree(t); say(`${label}: agrees with MetaBot on ${a.filter(Boolean).length} of the ${a.length} MetaBot skills it names`);
  }
  const checkAgree = agree(k => check[k]);
  if (checkAgree.filter(x => !x).length > metabotIds.length / 20) { console.error('the check table disagrees with MetaBot on more than 5%: stopping'); process.exit(1); }

  // new entries: a name an MIT source gives, the sources agreeing with each other and with the check table
  const conflicts = [], unchecked = [];
  let added = 0;
  const ids = new Set([...Object.keys(acomp), ...Object.keys(ping)].filter(k => !skills[k] && showable(Number(k))));
  for (const k of [...ids].sort((a, b) => a - b)) {
    const a = acomp[k]?.name, p = ping[k];
    // the name a source gives that the check table confirms; with no check entry, the sources must agree
    const name = check[k] ? [a, p].find(n => n === check[k]) : a && p && a !== p ? undefined : a ?? p;
    if (junk(name ?? a ?? p)) continue;
    if (!name) { conflicts.push(`${k}: ${[a && `AcompanION "${a}"`, p && `pingmaker "${p}"`].filter(Boolean).join(', ')}${check[k] ? `, the client table "${check[k]}"` : ''}`); continue; }
    if (!check[k]) unchecked.push(`${k} ${name}`);
    const id = Number(k);
    const spirit = /^(\w+ Spirit): /.exec(name)?.[1];
    const cls = id >= 10_000_000 ? CLASS_BY_FAMILY[Math.floor(id / 1_000_000)] ?? '' : spirit ? 'spiritmaster' : '';
    skills[k] = { name, cls, src: [a === name && 'acompanion', p === name && 'pingmaker'].filter(Boolean).join(',') };
    if (a === name && acomp[k]?.icon) skills[k].icon = 'nc:' + acomp[k].icon;
    added++;
  }
  say(`added ${added} abilities from the MIT tables; skills.json lists ${Object.keys(skills).length}`);
  say(`left out, the sources disagreeing (${conflicts.length}): ${conflicts.join('; ') || '-'}`);
  if (unchecked.length) say(`shipped though the client table lacks them (${unchecked.length}): ${unchecked.join('; ')}`);

  // the names only the GPL client table has: left out
  const checkOnly = Object.entries(check).filter(([k, n]) => !skills[k] && showable(Number(k)) && !junk(n)).map(([k]) => k);
  say(`names only the client table has (left out, "<Class> skill" in the tracker): ${checkOnly.length}`);

  // the Theostones against their MetaBot item pages: the name, and the icon
  if (verifyItems) {
    let ok = 0; const off = [];
    for (const k of Object.keys(skills).filter(k => /^Theostone: /.test(skills[k].name))) {
      const slug = slugOf(skills[k].name);
      let html = await get('https://metabot.gg/en/aion-2/items/' + slug), page = slug;
      if (!html) { html = await get('https://metabot.gg/en/aion-2/items/' + slug + '-bound'); page = slug + '-bound'; }
      const t = html && /<title>([^<]*)/.exec(html)?.[1].replace(/&#x27;/g, "'").replace(/&amp;/g, '&');
      if (t && t.startsWith(skills[k].name)) {
        ok++; skills[k].item = page;
        const og = /og:image" content="https:\/\/metabot\.gg\/web\/aion2\/items\/([a-z0-9_]+\.webp)"/.exec(html);
        if (og && !skills[k].icon) skills[k].icon = 'item:' + og[1];
      } else off.push(`${k} ${skills[k].name}`);
      await sleep(1200);
    }
    say(`Theostones on MetaBot's item pages: ${ok} found, ${off.length} not${off.length ? ': ' + off.join('; ') : ''}`);
  }

  // the icons
  if (iconDir) {
    fs.mkdirSync(iconDir, { recursive: true });
    for (const f of fs.readdirSync(iconDir)) if (!skills[f.replace(/\.\w+$/, '')]) fs.unlinkSync(path.join(iconDir, f));   // ids no longer listed
    let got = 0; const none = [];
    for (const [k, s] of Object.entries(skills)) {
      if (['webp', 'png'].some(e => fs.existsSync(path.join(iconDir, k + '.' + e)))) continue;
      const tries = s.src === 'metabot' ? [[MB_ICON + k + '.webp', 'webp']]
        : s.icon?.startsWith('nc:') ? [[NC_ICON + s.icon.slice(3) + '.png', 'png']]
        : s.icon?.startsWith('item:') ? [[MB_ITEM_ICON + s.icon.slice(5), 'webp']]
        : [[raw(WAFFLE, `${WAFFLE.dir}/${k}.png`), 'png']];
      let buf = null, ext = null;
      for (const [u, e] of tries) { buf = await get(u, true); if (buf) { ext = e; if (!s.icon && s.src !== 'metabot') s.icon = 'waffle'; break; } }
      if (buf) { fs.writeFileSync(path.join(iconDir, k + '.' + ext), buf); got++; } else none.push(k);
      await sleep(120);
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
    say(`\nfights: ${fights} in ${files.length} files, ${dmg.size} abilities, ${total.toLocaleString('en-US')} damage`);
    say(`named: ${pc(named)}; damage over time (one row, not an ability): ${pc(over)}; unnamed: ${pc(total - named - over)}`);
    const left = [...dmg].filter(([id]) => id !== 0 && resolve(skills, id) == null).sort((a, b) => b[1].dmg - a[1].dmg);
    if (left.length) {
      say('\n| id | share | hits | fights | used by | client table (not shipped) |\n|---|---|---|---|---|---|');
      for (const [id, d] of left) say(`| ${id} | ${pc(d.dmg)} | ${d.hits} | ${d.fights} | ${[...d.cls].join(', ')} | ${check[id] ?? '(none)'} |`);
    }
  }
  const loggedLeft = [...logged].filter(id => resolve(skills, id) == null);
  if (loggedLeft.length) say(`\nlogged unnamed by the tracker, still unnamed: ${loggedLeft.join(', ')}`);

  if (!dry) {
    doc.source = 'MetaBot.GG skill pages and public MIT-licensed skill tables, each name checked against the game client\'s (tools/skill-fill.js)';
    doc.sources = {
      metabot: 'https://metabot.gg/en/aion-2/skills/<page> (via kinahledger.com data/metabot-classes.json, fetched 2026-10-28)',
      acompanion: `https://github.com/${ACOMP.repo}/blob/${ACOMP.commit}/${ACOMP.file} (${ACOMP.license})`,
      pingmaker: `https://github.com/${PING.repo}/blob/${PING.commit}/${PING.file} (${PING.license})`,
      item: 'https://metabot.gg/en/aion-2/items/<item> (a Theostone\'s name and icon, --verify-items)',
      icon: `"nc:<file>": ${NC_ICON}<file>.png; "item:<file>": ${MB_ITEM_ICON}<file>; "waffle": https://github.com/${WAFFLE.repo}/blob/${WAFFLE.commit}/${WAFFLE.dir}/<id>.png (${WAFFLE.license}); none on a MetaBot skill: ${MB_ICON}<id>.webp`,
    };
    const sorted = {};
    for (const k of Object.keys(skills).sort((a, b) => a - b)) sorted[k] = skills[k];
    // one skill per line keeps the diff readable
    const body = Object.entries(sorted).map(([k, v]) => `${JSON.stringify(k)}: ${JSON.stringify(v)}`).join(',\n');
    const head = Object.entries(doc).filter(([k]) => k !== 'skills').map(([k, v]) => `${JSON.stringify(k)}: ${JSON.stringify(v, null, 1).replace(/\n\s*/g, ' ')}`).join(',\n');
    fs.writeFileSync(skillsPath, `{\n${head},\n"skills": {\n${body}\n}\n}\n`);
    say(`\nwrote ${path.relative(root, skillsPath)}`);
  }
  if (outPath) fs.writeFileSync(outPath, report.join('\n') + '\n');
})().catch(e => { console.error(e.message); process.exit(1); });
