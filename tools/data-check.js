// The game-data validator (2026-11-06): checks every list in data/ the way the tracker will read it, so a bad entry
// fails here instead of being skipped silently by the tracker's loaders (ItemDictionary, RecipeBook, LootBook,
// ZoneLocator, TargetReader) or shipping to installed trackers through the manifest. tools/data-fill.js runs it after
// writing its drafts; run it by hand after editing any list:
//
//   node tools/data-check.js              every file; exits 1 on a problem, prints each one
//   node tools/data-check.js --data dir   another folder of lists (a copy)
//   node tools/data-check.js --no-manifest   skip the manifest staleness check (a copy has no manifest to keep)
//
// What it checks: every JSON file parses and the .txt lists read; tw-items ids are integers, unique, every item named;
// extra-items names are unique, not already in tw-items (the tracker ignores such an entry), with the fields the
// tracker reads (category, tradable, bound, icon, url) present and of the right type, a bound one not tradable; skins
// have set, slot and grade;
// gathering ids are unique; grades and instance kinds are from the known sets; recipes have a known skill, an output,
// a positive output_qty, materials that are items some list knows, unique recipe ids (null for a drafted recipe is
// allowed and reported); loot tables are shaped as LootBook reads them; zones.txt and mobs.txt have no repeated line;
// and data/manifest.json is current (tools/data/manifest.js --check). Warnings never fail the run.
const fs = require('fs'), path = require('path');
const { spawnSync } = require('child_process');
const root = path.join(__dirname, '..');
const args = process.argv.slice(2);
const at = args.indexOf('--data');
const dataDir = path.resolve(at >= 0 && args[at + 1] ? args[at + 1] : path.join(root, 'data'));
const checkManifest = !args.includes('--no-manifest') && dataDir === path.join(root, 'data');

const GRADES = new Set(['Common', 'Rare', 'Heroic', 'Epic', 'Unique', 'Legend', 'Special']);
const SKILLS = new Set(['Handicrafting', 'Cooking', 'Armorsmithing', 'Blacksmithing', 'Alchemy']);
const KINDS = new Set(['Expedition', 'Arena', 'Legion', 'Event', 'Ignored']);
// ItemDictionary.Norm: the key two names collide under
const norm = s => String(s).normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(/’/g, "'")
  .replace(/[^a-z0-9'():+.,&\- ]/g, ' ').split(' ').filter(Boolean).join(' ');

const problems = [], warnings = [];
const bad = (file, what) => problems.push(`${file}: ${what}`);
const warn = (file, what) => warnings.push(`${file}: ${what}`);
const json = file => {
  const p = path.join(dataDir, file);
  if (!fs.existsSync(p)) { bad(file, 'missing'); return null; }
  try { return JSON.parse(fs.readFileSync(p, 'utf8')); } catch (e) { bad(file, `does not parse: ${e.message}`); return null; }
};
const lines = file => {
  const p = path.join(dataDir, file);
  if (!fs.existsSync(p)) { bad(file, 'missing'); return []; }
  return fs.readFileSync(p, 'utf8').split(/\r?\n/).map(l => l.trim()).filter(l => l && !l.startsWith('#'));
};
const isStr = v => typeof v === 'string' && v.length > 0;
const unique = (file, what, keys) => { const seen = new Set(); for (const k of keys) { if (seen.has(k)) bad(file, `${what} repeated: ${k}`); seen.add(k); } };

// ---- items ----------------------------------------------------------------------------------------------------------------
const names = new Set();            // every item name any list knows, normalised
const tw = json('tw-items.json');
if (tw) {
  const items = tw.items || [];
  if (!Array.isArray(items) || !items.length) bad('tw-items.json', 'no items');
  const ids = new Set();
  items.forEach((it, i) => {
    if (!Number.isInteger(it.id) || it.id <= 0) bad('tw-items.json', `item ${i} (${it.name}): id must be a positive integer`);
    else if (ids.has(it.id)) bad('tw-items.json', `id repeated: ${it.id}`); else ids.add(it.id);
    if (!isStr(it.name) && !isStr(it.n)) bad('tw-items.json', `item ${i} (id ${it.id}): no name`);
    else names.add(norm(it.name || it.n));
    if (it.grade != null && !GRADES.has(it.grade)) bad('tw-items.json', `${it.name}: unknown grade ${it.grade}`);
  });
}
const twNames = new Set(names);
const extra = json('extra-items.json');
if (extra) {
  const items = extra.items || [];
  if (!Array.isArray(items)) bad('extra-items.json', 'items is not a list');
  unique('extra-items.json', 'name', items.map(it => norm(it.name || '')));
  items.forEach((it, i) => {
    const who = `item ${i} (${it.name})`;
    if (!isStr(it.name)) return bad('extra-items.json', `${who}: no name`);
    if (twNames.has(norm(it.name))) bad('extra-items.json', `${who}: already in tw-items.json, the tracker ignores it here`);
    if (it.id != null && !Number.isInteger(it.id)) bad('extra-items.json', `${who}: id must be null or an integer`);
    if (!isStr(it.category)) bad('extra-items.json', `${who}: category must be a non-empty string`);
    if (typeof it.tradable !== 'boolean') bad('extra-items.json', `${who}: tradable must be true or false`);
    if (typeof it.bound !== 'boolean') bad('extra-items.json', `${who}: bound must be true or false`);
    if (it.bound && it.tradable) bad('extra-items.json', `${who}: a bound item is not tradable`);
    if (it.icon && !/^https?:\/\//.test(it.icon)) bad('extra-items.json', `${who}: icon must be a URL, empty or null`);
    if (it.url && !/^https?:\/\//.test(it.url)) bad('extra-items.json', `${who}: url must be a URL, empty or null`);
    if (it.grade == null || it.grade === '') warn('extra-items.json', `${who}: no grade`);
    else if (!GRADES.has(it.grade)) bad('extra-items.json', `${who}: unknown grade ${it.grade}`);
    if (!it.icon) warn('extra-items.json', `${who}: no icon`);
    names.add(norm(it.name));
  });
}
const skins = json('skins.json');
if (skins) {
  const items = skins.items || [];
  unique('skins.json', 'name', items.map(it => norm(it.name || '')));
  items.forEach((it, i) => {
    const who = `item ${i} (${it.name})`;
    if (!isStr(it.name)) return bad('skins.json', `${who}: no name`);
    if (!isStr(it.set) || !isStr(it.slot)) bad('skins.json', `${who}: set and slot are required`);
    else if (!it.name.includes(`(Skin: ${it.slot})`)) bad('skins.json', `${who}: name does not carry "(Skin: ${it.slot})"`);
    if (it.grade != null && !GRADES.has(it.grade)) bad('skins.json', `${who}: unknown grade ${it.grade}`);
    if (it.id != null && !Number.isInteger(it.id)) bad('skins.json', `${who}: id must be null or an integer`);
    names.add(norm(it.name));
  });
}
const gathering = json('gathering-items.json');
if (gathering) {
  const items = gathering.items || [];
  const ids = [];
  items.forEach((it, i) => {
    const who = `item ${i} (${it.name})`;
    if (!isStr(it.name)) return bad('gathering-items.json', `${who}: no name`);
    if (!isStr(it.icon)) bad('gathering-items.json', `${who}: no icon`);
    if (!Array.isArray(it.ids) || !it.ids.every(n => Number.isInteger(n) && n > 0)) bad('gathering-items.json', `${who}: ids must be positive integers`);
    else ids.push(...it.ids);
    names.add(norm(it.name));
  });
  unique('gathering-items.json', 'id', ids);
}

// ---- recipes ----------------------------------------------------------------------------------------------------------------
const recipes = json('recipes.json');
if (recipes) {
  const list = recipes.recipes || [];
  if (!Array.isArray(list) || !list.length) bad('recipes.json', 'no recipes');
  const outputs = new Set(list.map(r => norm(r.output || r.name || '')));
  const itemKnown = n => names.has(norm(n)) || outputs.has(norm(n));
  unique('recipes.json', 'recipe_id', list.map(r => r.recipe_id).filter(id => id != null));
  list.forEach((r, i) => {
    const who = `recipe ${i} (${r.name || r.output})`;
    if (!isStr(r.output) && !isStr(r.name)) return bad('recipes.json', `${who}: no output or name`);
    if (!SKILLS.has(r.skill)) bad('recipes.json', `${who}: unknown skill ${r.skill}`);
    if (!Number.isInteger(r.output_qty) || r.output_qty < 1) bad('recipes.json', `${who}: output_qty must be a positive integer`);
    if (!Array.isArray(r.materials) || !r.materials.length) bad('recipes.json', `${who}: no materials`);
    else for (const m of r.materials) {
      if (!isStr(m.item) || !Number.isInteger(m.qty) || m.qty < 1) bad('recipes.json', `${who}: material must be {item, qty}: ${JSON.stringify(m)}`);
      else if (!itemKnown(m.item)) bad('recipes.json', `${who}: material "${m.item}" is not an item any list knows`);
    }
    for (const g of r.great || []) if (!isStr(g.item) || !itemKnown(g.item)) bad('recipes.json', `${who}: great outcome "${g && g.item}" is not an item any list knows`);
    if (r.recipe_id == null) warn('recipes.json', `${who}: recipe_id is null (a draft: the tracker files it under no faction)`);
    else if (!Number.isInteger(r.recipe_id)) bad('recipes.json', `${who}: recipe_id must be an integer or null`);
    if (r.success_rate != null && !(r.success_rate >= 0 && r.success_rate <= 1)) bad('recipes.json', `${who}: success_rate must be 0..1 or null`);
    if (r.combo_chance != null && !(r.combo_chance >= 0 && r.combo_chance <= 1)) bad('recipes.json', `${who}: combo_chance must be 0..1 or null`);
    if (!Number.isInteger(r.mastery) || r.mastery < 0) bad('recipes.json', `${who}: mastery must be an integer`);
    if (!itemKnown(r.output || r.name)) warn('recipes.json', `${who}: output is not in any item list`);
  });
}

// ---- loot tables ----------------------------------------------------------------------------------------------------------
const loot = json('loot-tables.json');
if (loot) {
  const tables = loot.tables;
  if (!tables || typeof tables !== 'object' || !Object.keys(tables).length) bad('loot-tables.json', 'no tables');
  else {
    let unknown = 0;
    for (const [enemy, t] of Object.entries(tables)) {
      if (!Array.isArray(t.drops)) { bad('loot-tables.json', `${enemy}: drops is not a list`); continue; }
      for (const d of t.drops) {
        if (!isStr(d.item) || d.item.length < 2) bad('loot-tables.json', `${enemy}: a drop with no item name`);
        else if (!names.has(norm(d.item))) unknown++;
        if (d.id != null && !Number.isInteger(d.id)) bad('loot-tables.json', `${enemy}: ${d.item}: id must be null or an integer`);
        if (d.chance != null && !(d.chance >= 0 && d.chance <= 1)) bad('loot-tables.json', `${enemy}: ${d.item}: chance must be 0..1`);
      }
    }
    if (unknown) warn('loot-tables.json', `${unknown} drop(s) name an item no list knows (souls, event items): the tracker counts them by name`);
  }
}

// ---- the text lists ----------------------------------------------------------------------------------------------------------
const zones = lines('zones.txt'); unique('zones.txt', 'zone', zones);
const mobs = lines('mobs.txt'); unique('mobs.txt', 'enemy', mobs);
if (!zones.length) bad('zones.txt', 'empty'); if (!mobs.length) bad('mobs.txt', 'empty');
const instances = lines('instances.txt'); unique('instances.txt', 'instance', instances.map(l => l.split('|')[0].trim()));
for (const l of instances) {
  const parts = l.split('|').map(s => s.trim());
  // a fourth field "chat" (Name|Kind||chat, instances.txt's header, 0.10.14): an area whose loot prints in the chat kill by kill
  const chat = parts.length === 4 && parts[3] === 'chat';
  if (parts.length < 2 || (parts.length > 3 && !chat) || !parts[0]) bad('instances.txt', `not "Name|Kind", "Area|Kind|Expedition" or "Name|Kind||chat": ${l}`);
  else if (!KINDS.has(parts[1])) bad('instances.txt', `${parts[0]}: unknown kind ${parts[1]}`);
}
for (const f of ['aliases.json', 'vendor-prices.json', 'mob-images.json', 'gear-factions.json', 'models.json']) json(f);

// ---- the manifest ----------------------------------------------------------------------------------------------------------
if (checkManifest) {
  const r = spawnSync(process.execPath, [path.join(root, 'tools', 'data', 'manifest.js'), '--check'], { encoding: 'utf8' });
  if (r.status !== 0) bad('manifest.json', (r.stderr || r.stdout || '').trim() || 'stale');
}

for (const w of warnings) console.log(`warning  ${w}`);
for (const p of problems) console.log(`PROBLEM  ${p}`);
const counts = `${tw ? tw.items.length : 0} items, ${extra ? extra.items.length : 0} extra, ${skins ? skins.items.length : 0} skins, ${recipes ? recipes.recipes.length : 0} recipes, ${loot && loot.tables ? Object.keys(loot.tables).length : 0} loot tables, ${zones.length} zones, ${instances.length} instances, ${mobs.length} enemies`;
console.log(problems.length ? `data-check: ${problems.length} problem(s), ${warnings.length} warning(s) (${counts})` : `data-check: ok, ${warnings.length} warning(s) (${counts})`);
process.exit(problems.length ? 1 : 0);
