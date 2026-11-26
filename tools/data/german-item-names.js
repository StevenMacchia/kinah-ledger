// Generate the tracker catalog from decoded AION 2 Global client tables.
// Usage: node tools/data/german-item-names.js Item.json l10n_de-DE.json l10n_en-US.json
const fs = require('fs');
const path = require('path');

const [itemFile, germanFile, englishFile] = process.argv.slice(2);
if (!itemFile || !germanFile || !englishFile) {
  console.error('Usage: node tools/data/german-item-names.js Item.json l10n_de-DE.json l10n_en-US.json');
  process.exit(1);
}

const read = file => JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, ''));
const itemTable = read(itemFile);
const german = read(germanFile).Entries;
const english = read(englishFile).Entries;
const rows = itemTable.Properties?.Data;
if (!Array.isArray(rows) || !rows.length || !german || !english || !Number.isInteger(itemTable.Version)) {
  throw new Error('Expected Item.Properties.Data, Item.Version and both localization Entries maps');
}

const grades = new Set(['Common', 'Rare', 'Unique', 'Legend', 'Epic', 'Special']);
const categories = new Set(['Currency', 'Equip', 'Misc', 'Usable']);
const exchangeTypes = new Map([
  ['Exchangeable', true],
  ['SoulBind', true],
  ['SoulBind_Acquire', false],
  ['NonExchangeable', false]
]);
const ids = new Set();
const items = [];
for (const row of rows) {
  const id = row.ID?.Value;
  const key = row.Desc?.Key;
  if (!Number.isSafeInteger(id) || id <= 0 || ids.has(id) || typeof key !== 'string' || !key) {
    throw new Error(`Invalid or duplicate item row: ${id} / ${key}`);
  }
  ids.add(id);
  const textKey = `String_${key}_body`;
  const de = german[textKey]?.trim();
  const n = english[textKey]?.trim();
  if (!de || !n) throw new Error(`Missing localized name for ${id} (${textKey})`);
  const g = row.ItemGrade?.split('::').at(-1);
  const c = row.ItemType?.split('::').at(-1);
  const t = exchangeTypes.get(row.ExchangeType?.split('::').at(-1));
  if (!grades.has(g) || !categories.has(c) || t === undefined) {
    throw new Error(`Unknown item metadata for ${id} (${g}, ${c}, ${row.ExchangeType})`);
  }
  items.push({ id, de, n, g, t, c });
}
items.sort((a, b) => a.id - b.id);
const header = JSON.stringify({ source: 'AION 2 Global client', tableVersion: itemTable.Version });
const output = `${header.slice(0, -1)},"items":[\n${items.map(item => JSON.stringify(item)).join(',\n')}\n]}\n`;
const target = path.join(__dirname, '..', '..', 'data', 'item-names.de-DE.json');
fs.writeFileSync(target, output);
console.log(`Wrote ${items.length} German item names by game ID to ${path.basename(target)}`);
