#!/usr/bin/env node
'use strict';
/*
 * build-site-data.js  (Kinah Ledger)
 *
 * Reads the Taiwan item-dictionary crawl (data/tw-items.json, written by
 * tools/crawl-tw.js) and writes the compact files the site loads at runtime:
 *
 *   site/data/items.json       every item: {id, n, g, t, c1, c2, i} + iconBase
 *   site/data/materials.json   farmable / droppable non-equipment items, with
 *                              bound <-> tradable twins linked through `pair`
 *   site/data/categories.json  cat1 -> cat2 tree with English names and counts
 *
 * Usage
 *   node tools/build-site-data.js              data/tw-items.json -> site/data/
 *   node tools/build-site-data.js --in x.json --out some/dir
 *   node tools/build-site-data.js --dry-run    compute and report, write nothing
 *   node tools/build-site-data.js --quiet      warnings only
 *
 * Input resolution, first match wins:
 *   --in <file>  >  data/tw-items.json  >  tools/tw-items.json (where crawl-tw.js
 *   writes it)  >  data/tw-items.sample.json  >  (empty)
 * Falling back to the sample is loud and the outputs then carry "sample":true,
 * so nobody ships sample data by accident. A missing or empty input writes
 * valid empty files and exits 0; a file that is not valid JSON exits 1.
 *
 * Idempotent: items are sorted by id, key order is fixed and no run timestamp
 * is added (the crawl's own `fetched` value is carried through instead), so the
 * same input always gives byte-identical output, and unchanged files are not
 * rewritten.
 *
 * Output shapes
 *   items.json      {source, fetched, sample?, iconBase, count, items:[...]}
 *                   item = {id, n:name, g:grade, t:tradable 0|1, c1, c2, i}
 *                   `i` is the icon path relative to iconBase (normally just
 *                   the file name). An icon outside iconBase keeps its full
 *                   URL; no icon -> "".
 *   materials.json  same head + `filter`; item = items.json item, plus
 *                   b:1 when the name ends in "(Bound)",
 *                   pair: id of the twin (bound <-> tradable, matched by
 *                   name), null for a bound item with no tradable twin,
 *                   absent for a tradable item with no bound twin,
 *                   o:[options] only when non-empty,
 *                   d:description only when the crawl carries one.
 *   categories.json {source, fetched, sample?, total, cat1:[{id, name, count,
 *                   tradable, cat2:[{id, name, count, tradable, material}]}]}
 *
 * No dependencies. Node 18+.
 */

const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '..');
const DEFAULT_IN = path.join(ROOT, 'data', 'tw-items.json');
const CRAWL_IN = path.join(ROOT, 'tools', 'tw-items.json');   // crawl-tw.js writes here
const SAMPLE_IN = path.join(ROOT, 'data', 'tw-items.sample.json');
const DEFAULT_OUT = path.join(ROOT, 'site', 'data');

// ---- material filter ------------------------------------------------------
// Tune these after looking at real data. The summary prints one line per
// cat1/cat2 with the verdict, so you can see exactly what each rule caught.
const EXCLUDE_CAT1 = ['Equip_Weapon', 'Equip_Armor', 'Equip_Accessory', 'Usable_Skin'];
const MATERIAL_CAT1 = ['Misc_001'];                       // every cat2 under these
const MATERIAL_CAT2 = ['GatherResource', 'CraftResource', 'ConversionResource',
  'Material', 'MagicStone', 'Box'];                       // these cat2 under any other cat1
const BOUND_SUFFIX = /\s*\(bound\)\s*$/i;

function isMaterialCat(cat1, cat2) {
  if (EXCLUDE_CAT1.includes(cat1)) return false;
  if (MATERIAL_CAT1.includes(cat1)) return true;
  return MATERIAL_CAT2.includes(cat2);
}

// ---- helpers --------------------------------------------------------------
const str = v => (typeof v === 'string' ? v.trim() : v == null ? '' : String(v).trim());
const truthy = v => v === true || v === 1 || v === '1' || (typeof v === 'string' && v.toLowerCase() === 'true');
const rel = p => (p ? (path.relative(ROOT, p).split(path.sep).join('/') || p) : '(none)');
const fmt = n => n.toLocaleString('en-US');
const byId = (a, b) => (a.id < b.id ? -1 : a.id > b.id ? 1 : 0);
const nameKey = s => s.toLowerCase().replace(/\s+/g, ' ').trim();

function printHelp() {
  console.log('usage: node tools/build-site-data.js [--in <file>] [--out <dir>] [--dry-run] [--quiet]');
}

function parseArgs(argv) {
  const o = { in: null, out: DEFAULT_OUT, dryRun: false, quiet: false };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--in') o.in = path.resolve(argv[++i] || '');
    else if (a === '--out') o.out = path.resolve(argv[++i] || '');
    else if (a === '--dry-run') o.dryRun = true;
    else if (a === '--quiet') o.quiet = true;
    else if (a === '-h' || a === '--help') { printHelp(); process.exit(0); }
    else { console.error('unknown argument: ' + a); printHelp(); process.exit(2); }
  }
  return o;
}

// ---- input ----------------------------------------------------------------
function loadInput(opt) {
  const candidates = opt.in ? [[opt.in, 'main']] : [[DEFAULT_IN, 'main'], [CRAWL_IN, 'main'], [SAMPLE_IN, 'sample']];
  for (const [p, kind] of candidates) {
    if (!fs.existsSync(p)) continue;
    let raw;
    try { raw = JSON.parse(fs.readFileSync(p, 'utf8')); }
    catch (e) { throw new Error(`cannot parse ${rel(p)}: ${e.message}`); }
    const items = Array.isArray(raw) ? raw : (raw && Array.isArray(raw.items) ? raw.items : null);
    if (!items) throw new Error(`${rel(p)}: expected an array, or an object with an "items" array`);
    return { path: p, kind, items, meta: Array.isArray(raw) ? {} : raw };
  }
  if (opt.in) throw new Error(`input not found: ${rel(opt.in)}`);
  return { path: null, kind: 'none', items: [], meta: {} };
}

function normalizeItems(rawItems) {
  const out = [];
  const seen = new Set();
  let invalid = 0, dup = 0;
  for (const r of rawItems) {
    if (!r || typeof r !== 'object') { invalid++; continue; }
    const id = Number(r.id);
    const name = str(r.name).replace(/\s+/g, ' ');
    if (!Number.isInteger(id) || !name) { invalid++; continue; }
    if (seen.has(id)) { dup++; continue; }
    seen.add(id);
    out.push({
      id,
      name,
      grade: str(r.grade),
      tradable: truthy(r.tradable) ? 1 : 0,
      cat1: str(r.cat1),
      cat1Name: str(r.cat1Name),
      cat2: str(r.cat2),
      cat2Name: str(r.cat2Name),
      icon: str(r.icon),
      description: str(r.description),
      options: Array.isArray(r.options)
        ? r.options.filter(s => typeof s === 'string').map(s => s.trim()).filter(Boolean)
        : [],
    });
  }
  out.sort(byId);
  return { items: out, invalid, dup };
}

// ---- icons ----------------------------------------------------------------
// Pick the directory prefix that saves the most bytes overall: every ".../"
// prefix of every icon URL is scored by (number of icons under it x its
// length). One stray icon on another host therefore cannot collapse the base
// to "https://"; it simply keeps its full URL.
function computeIconBase(items) {
  const score = new Map();
  for (const it of items) {
    const u = it.icon;
    const m = /^https?:\/\/[^/]+\//.exec(u || '');
    if (!m) continue;
    for (let i = m[0].length - 1; i !== -1; i = u.indexOf('/', i + 1)) {
      const pre = u.slice(0, i + 1);
      score.set(pre, (score.get(pre) || 0) + pre.length);
    }
  }
  let best = '', bestScore = 0;
  for (const [pre, s] of score) {
    if (s > bestScore || (s === bestScore && pre < best)) { best = pre; bestScore = s; }
  }
  return best;
}

function compactItem(it, iconBase) {
  let i = '';
  if (it.icon) i = iconBase && it.icon.startsWith(iconBase) ? it.icon.slice(iconBase.length) : it.icon;
  return { id: it.id, n: it.name, g: it.grade, t: it.tradable, c1: it.cat1, c2: it.cat2, i };
}

// ---- bound <-> tradable pairs ---------------------------------------------
// "X (Bound)" is paired with "X". When several items are called "X", prefer
// the one in the same cat2 and grade, then same cat2, then same grade; if a
// tier still has more than one candidate the bound item is left unpaired and
// counted as ambiguous rather than guessed.
function buildPairs(items) {
  const byBase = new Map();
  const bound = [];
  for (const it of items) {
    if (BOUND_SUFFIX.test(it.name)) { bound.push(it); continue; }
    const k = nameKey(it.name);
    if (!byBase.has(k)) byBase.set(k, []);
    byBase.get(k).push(it);
  }
  const pair = new Map();
  let matched = 0, unmatched = 0, ambiguous = 0;
  for (const b of bound) {
    const cands = (byBase.get(nameKey(b.name.replace(BOUND_SUFFIX, ''))) || []).filter(x => !pair.has(x.id));
    const tiers = [
      cands.filter(x => x.cat2 === b.cat2 && x.grade === b.grade),
      cands.filter(x => x.cat2 === b.cat2),
      cands.filter(x => x.grade === b.grade),
      cands,
    ];
    let hit = null, amb = false;
    for (const t of tiers) {
      if (t.length === 1) { hit = t[0]; break; }
      if (t.length > 1) { amb = true; break; }
    }
    if (hit) { pair.set(b.id, hit.id); pair.set(hit.id, b.id); matched++; }
    else if (amb) ambiguous++;
    else unmatched++;
  }
  return { pair, matched, unmatched, ambiguous, boundTotal: bound.length };
}

// ---- categories -----------------------------------------------------------
function buildCategories(items) {
  const c1 = new Map();
  const bump = (m, k) => { if (k) m.set(k, (m.get(k) || 0) + 1); };
  const pick = (m, fallback) => {
    let best = fallback, bs = 0;
    for (const [n, c] of m) if (c > bs) { best = n; bs = c; }
    return best;
  };
  for (const it of items) {
    const k1 = it.cat1 || '_none';
    if (!c1.has(k1)) c1.set(k1, { id: k1, names: new Map(), count: 0, tradable: 0, cat2: new Map() });
    const n1 = c1.get(k1);
    n1.count++; n1.tradable += it.tradable; bump(n1.names, it.cat1Name);
    const k2 = it.cat2 || '_none';
    if (!n1.cat2.has(k2)) n1.cat2.set(k2, { id: k2, names: new Map(), count: 0, tradable: 0 });
    const n2 = n1.cat2.get(k2);
    n2.count++; n2.tradable += it.tradable; bump(n2.names, it.cat2Name);
  }
  return [...c1.values()].sort(byId).map(n1 => ({
    id: n1.id,
    name: pick(n1.names, n1.id),
    count: n1.count,
    tradable: n1.tradable,
    cat2: [...n1.cat2.values()].sort(byId).map(n2 => ({
      id: n2.id,
      name: pick(n2.names, n2.id),
      count: n2.count,
      tradable: n2.tradable,
      material: isMaterialCat(n1.id, n2.id),
    })),
  }));
}

// ---- output ---------------------------------------------------------------
// One item per line: still compact, but git diffs stay readable.
function serializeList(head, items) {
  const h = JSON.stringify(head);
  const open = h.length > 2 ? h.slice(0, -1) + ',' : '{';
  if (!items.length) return open + '"items":[]}\n';
  return open + '"items":[\n' + items.map(x => JSON.stringify(x)).join(',\n') + '\n]}\n';
}

function writeIfChanged(p, text, dryRun) {
  let prev = null;
  try { prev = fs.readFileSync(p, 'utf8'); } catch (e) { /* new file */ }
  const changed = prev !== text;
  if (changed && !dryRun) {
    fs.mkdirSync(path.dirname(p), { recursive: true });
    fs.writeFileSync(p, text, 'utf8');
  }
  return { changed, bytes: Buffer.byteLength(text, 'utf8') };
}

// ---- main -----------------------------------------------------------------
function main() {
  const opt = parseArgs(process.argv.slice(2));
  const log = opt.quiet ? () => {} : (...a) => console.log(...a);

  const input = loadInput(opt);
  if (input.kind === 'none') {
    console.warn('WARN  no input found (looked for data/tw-items.json, tools/tw-items.json, then data/tw-items.sample.json); writing empty outputs');
  } else if (input.kind === 'sample') {
    console.warn('WARN  no crawl found (data/tw-items.json or tools/tw-items.json); USING SAMPLE data/tw-items.sample.json. Outputs are marked "sample":true. Do not ship them.');
  }

  const { items, invalid, dup } = normalizeItems(input.items);
  const iconBase = computeIconBase(items);
  const pairs = buildPairs(items);
  const categories = buildCategories(items);

  const head = {};
  if (str(input.meta.source)) head.source = str(input.meta.source);
  if (str(input.meta.fetched)) head.fetched = str(input.meta.fetched);
  if (input.kind === 'sample') head.sample = true;

  const compact = items.map(it => compactItem(it, iconBase));
  const materials = items.filter(it => isMaterialCat(it.cat1, it.cat2)).map(it => {
    const m = compactItem(it, iconBase);
    if (BOUND_SUFFIX.test(it.name)) { m.b = 1; m.pair = pairs.pair.has(it.id) ? pairs.pair.get(it.id) : null; }
    else if (pairs.pair.has(it.id)) m.pair = pairs.pair.get(it.id);
    if (it.options.length) m.o = it.options;
    if (it.description) m.d = it.description;
    return m;
  });

  const iconStats = { under: 0, outside: 0, missing: 0 };
  for (const it of items) {
    if (!it.icon) iconStats.missing++;
    else if (iconBase && it.icon.startsWith(iconBase)) iconStats.under++;
    else iconStats.outside++;
  }
  const matBound = materials.filter(m => m.b).length;
  const matPaired = materials.filter(m => m.b && m.pair != null).length;

  const outItems = path.join(opt.out, 'items.json');
  const outMats = path.join(opt.out, 'materials.json');
  const outCats = path.join(opt.out, 'categories.json');

  const r1 = writeIfChanged(outItems, serializeList({ ...head, iconBase, count: compact.length }, compact), opt.dryRun);
  const r2 = writeIfChanged(outMats, serializeList({
    ...head, iconBase, count: materials.length,
    filter: { cat1: MATERIAL_CAT1, cat2: MATERIAL_CAT2, excludeCat1: EXCLUDE_CAT1 },
  }, materials), opt.dryRun);
  const r3 = writeIfChanged(outCats, JSON.stringify({ ...head, total: items.length, cat1: categories }, null, 2) + '\n', opt.dryRun);

  const state = r => (opt.dryRun ? (r.changed ? 'would write' : 'unchanged') : (r.changed ? 'written' : 'unchanged'));
  log('build-site-data');
  log(`  input          ${rel(input.path)}${input.kind === 'sample' ? '  [SAMPLE]' : ''}  ${fmt(input.items.length)} raw items` +
      (head.fetched ? `, fetched ${head.fetched}` : ''));
  if (invalid || dup) log(`  skipped        ${fmt(invalid)} without a numeric id and name, ${fmt(dup)} duplicate ids`);
  log(`  iconBase       ${iconBase || '(none)'}  (${fmt(iconStats.under)} under base, ${fmt(iconStats.outside)} outside, ${fmt(iconStats.missing)} missing)`);
  log(`  pairs          ${fmt(pairs.boundTotal)} "(Bound)" items overall: ${fmt(pairs.matched)} paired, ${fmt(pairs.unmatched)} no twin, ${fmt(pairs.ambiguous)} ambiguous (left unpaired)`);
  log(`  ${rel(outItems).padEnd(28)} ${fmt(compact.length).padStart(6)} items  ${fmt(r1.bytes).padStart(10)} bytes  ${state(r1)}`);
  log(`  ${rel(outMats).padEnd(28)} ${fmt(materials.length).padStart(6)} items  ${fmt(r2.bytes).padStart(10)} bytes  ${state(r2)}  (${fmt(matBound)} bound, ${fmt(matPaired)} of them paired)`);
  log(`  ${rel(outCats).padEnd(28)} ${String(categories.length + ' cat1 / ' + categories.reduce((n, c) => n + c.cat2.length, 0) + ' cat2').padStart(12)}  ${fmt(r3.bytes).padStart(10)} bytes  ${state(r3)}`);
  if (categories.length) {
    log('  material filter by category:');
    for (const c of categories) {
      for (const s of c.cat2) {
        log(`    ${(c.id + '/' + s.id).padEnd(36)} ${fmt(s.count).padStart(6)}  ${s.material ? 'material' : '-'}  ${c.name}${s.name !== s.id ? ' > ' + s.name : ''}`);
      }
    }
  }
  if (opt.dryRun) log('  (dry run: nothing written)');
}

try {
  main();
} catch (e) {
  console.error('ERROR ' + (e && e.message ? e.message : e));
  process.exit(1);
}
