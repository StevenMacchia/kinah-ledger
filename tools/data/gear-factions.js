// Writes data/gear-factions.json: the faction of each piece of gear whose icon another item shares, where the game's own
// data says it (2026-10-25: 31 of the 35 "unknown gear" pieces of 2026-10-24 were Elyos/Asmodian twins on one icon, "one
// of Forgotten Helm / Shade Helm", Elder / Relic Collector among them). The tracker settles such a slot by the player's
// faction (TrackerSession, Data/GearFactions.cs). It is built into the exe only (not in the daily data update), so a
// change ships with a release.
//
//   node tools/data/gear-factions.js [path to kinah-ledger-site/data/metabot-monsters.json]
//
// A name gets a faction from one of two things, never from a guess:
//   a. its own name: "Elyos Chanter's Breastplate", "Asmodian Chanter's Breastplate";
//   b. where it drops: the MetaBot.GG monster tables (read from the AION 2 global client) put every monster on its maps.
//      A monster on Elyos maps only (Poeta, Verteron, Eltnen) votes Elyos for each item it drops, one on Asmodian maps
//      only (Ishalgen, Altgard, Morheim) votes Asmodian; Reshanta and maps of both factions vote nothing. A name is a
//      faction's when 10 or more monsters vote for it and it has at least 10 times the other side's votes (Forgotten Helm
//      329 to 1, Shade Helm 270 to 1). The player's own zones (Eastern Cantas Valley, Scarlet Forest, Dawn Legion Base on
//      Siel, Elyos) are all on Verteron.
// Only names in a group of gear sharing one icon and grade are written, and only when every other name of the group has
// the other faction (a group with a name of no known faction, or two of one faction, is left out: the tracker keeps such
// a slot as unknown gear, or settles it by the chat). The ids are checked too: in every pair written, the Elyos item's id is
// the lower one; a pair against that is printed and left out, since the two signals disagree.
const fs = require('fs'), path = require('path');
const root = path.join(__dirname, '..', '..');
const target = path.join(root, 'data', 'gear-factions.json');
const monstersPath = process.argv[2] || path.join(root, '..', 'kinah-ledger-site', 'data', 'metabot-monsters.json');

const ELYOS_MAPS = new Set(['Poeta', 'Verteron', 'Eltnen']), ASMODIAN_MAPS = new Set(['Ishalgen', 'Altgard', 'Morheim']);
const MIN_VOTES = 10, MIN_RATIO = 10;

const items = JSON.parse(fs.readFileSync(path.join(root, 'data', 'tw-items.json'), 'utf8')).items;
const mons = JSON.parse(fs.readFileSync(monstersPath, 'utf8'));
const votes = new Map();
for (const m of Object.values(mons.monsters)) {
  const maps = new Set((m.zones || []).map(z => z.map));
  const e = [...maps].some(x => ELYOS_MAPS.has(x)), a = [...maps].some(x => ASMODIAN_MAPS.has(x));
  if (e === a) continue;
  for (const d of m.drops || []) {
    const v = votes.get(d.name) || { e: 0, a: 0 };
    if (e) v.e++; else v.a++;
    votes.set(d.name, v);
  }
}

function factionOf(name) {
  if (/^Elyos\s/.test(name)) return { f: 'Elyos', why: 'name' };
  if (/^Asmodian\s/.test(name)) return { f: 'Asmodian', why: 'name' };
  const v = votes.get(name);
  if (!v) return null;
  if (v.e >= MIN_VOTES && v.e >= MIN_RATIO * v.a) return { f: 'Elyos', why: `drops ${v.e}:${v.a}` };
  if (v.a >= MIN_VOTES && v.a >= MIN_RATIO * v.e) return { f: 'Asmodian', why: `drops ${v.a}:${v.e}` };
  return null;
}

// gear by icon file and grade, as the tracker groups a reward slot's candidates (TrackerSession.GradedPick)
const keyOf = u => path.basename(String(u).split('?')[0]).replace(/\.[a-z]+$/i, '');
const groups = new Map();
for (const i of items) {
  if (!/^Equip_/.test(i.cat1 || '')) continue;
  const k = keyOf(i.icon) + '|' + i.grade;
  if (!groups.has(k)) groups.set(k, []);
  groups.get(k).push(i);
}
const out = { Elyos: new Set(), Asmodian: new Set() };
let pairs = 0, idAgree = 0, idAgainst = 0, partial = 0, byName = 0;
const against = [];
for (const [k, its] of groups) {
  const names = [...new Set(its.map(i => i.name))];
  if (names.length < 2) continue;
  const f = names.map(factionOf);
  if (f.some(x => x == null)) { if (f.some(x => x != null)) partial++; continue; }
  const ely = names.filter((_, i) => f[i].f === 'Elyos'), asm = names.filter((_, i) => f[i].f === 'Asmodian');
  // one of each, the shape the tracker settles; a group of more (tiers on one icon) is written too, by name, and the
  // tracker settles it only when exactly one of its names is the player's faction
  if (ely.length === 0 || asm.length === 0) continue;
  if (ely.length === 1 && asm.length === 1) {
    pairs++;
    const minId = n => Math.min(...its.filter(i => i.name === n).map(i => i.id));
    if (minId(ely[0]) < minId(asm[0])) idAgree++;
    else { idAgainst++; against.push(`${k}: ${ely[0]} ${minId(ely[0])} / ${asm[0]} ${minId(asm[0])}`); continue; }
    if (f.every(x => x.why === 'name')) byName++;
  }
  for (const n of ely) out.Elyos.add(n);
  for (const n of asm) out.Asmodian.add(n);
}
const both = [...out.Elyos].filter(n => out.Asmodian.has(n));
for (const n of both) { out.Elyos.delete(n); out.Asmodian.delete(n); }

const file = {
  source: 'tools/data/gear-factions.js: data/tw-items.json and the MetaBot.GG monster tables (' + (mons.source_url || 'metabot.gg') + ', fetched ' + (mons.fetched || '?') + ')',
  note: 'Gear sharing one icon with an item of the other faction, by name. Elyos: named so, or dropped on Poeta, Verteron and Eltnen only; Asmodian: named so, or dropped on Ishalgen, Altgard and Morheim only.',
  elyos: [...out.Elyos].sort(),
  asmodian: [...out.Asmodian].sort(),
};
fs.writeFileSync(target, JSON.stringify(file, null, 1) + '\n');
console.log(`wrote data/gear-factions.json: ${file.elyos.length} Elyos and ${file.asmodian.length} Asmodian names`);
console.log(`pairs of one Elyos and one Asmodian name: ${pairs} (${byName} by their names, the rest by where they drop)`);
console.log(`the lower id is the Elyos one in ${idAgree} of ${idAgree + idAgainst} pairs${against.length ? '; left out: ' + against.join('; ') : ''}`);
console.log(`groups with a name of no known faction, left out: ${partial}; names on both sides, left out: ${both.length}`);
