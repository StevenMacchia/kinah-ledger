#!/usr/bin/env node
'use strict';
/*
 * fetch-icons.js  (Kinah Ledger)  --  OWNER-RUN. NOT PART OF THE BUILD.
 *
 * Downloads the item icons referenced by site/data/items.json from NCSOFT's
 * CDN (assets.playnccdn.com) into site/icons/, so the site can serve them
 * itself ("local" mode in site/data-loader.js) instead of hot-linking them.
 *
 * READ THIS BEFORE RUNNING IT
 *   The icons are NCSOFT's copyrighted artwork. NCSOFT's terms restrict
 *   automated collection of its content and any commercial use of it, and a
 *   fan site hosting its own copies of ~11,000 icons is a different thing
 *   from a visitor's browser loading them one at a time from NCSOFT's CDN.
 *   Hot-linking ("cdn" mode) leaves the files on NCSOFT's servers and is the
 *   lighter-touch option; a local mirror is faster and survives CDN changes
 *   but is a redistribution of their assets. Whether that is acceptable for
 *   this site is the owner's decision, which is why this script exists but
 *   is never run by the build. If in doubt, run it with --materials-only (a
 *   few hundred files rather than eleven thousand), or do not run it at all.
 *   That the CDN answers with Access-Control-Allow-Origin: * is a fact about
 *   CORS, not a licence.
 *
 * Behaviour
 *   - one request at a time, DELAY_MS (300 ms) between requests, no parallelism
 *   - skips files that already exist with size > 0, so it is resumable: stop
 *     it with Ctrl-C at any time and run it again later
 *   - writes <name>.part and renames on success, so an interruption never
 *     leaves a truncated icon behind
 *   - 429 / 5xx / network errors: waits and retries up to 3 times with
 *     growing delays; 403 / 404 / other 4xx: recorded in site/icons/_failed.txt
 *     and skipped (delete that file to forget them; they are retried anyway
 *     on the next run because the icon file does not exist)
 *   - sends a plain, honest User-Agent (edit UA below if you want a contact)
 *
 * Usage
 *   node tools/fetch-icons.js                   every icon in items.json
 *   node tools/fetch-icons.js --materials-only  only icons in materials.json
 *   node tools/fetch-icons.js --limit 50        stop after 50 downloads (trial)
 *   node tools/fetch-icons.js --dry-run         list what would be fetched
 *   node tools/fetch-icons.js --delay 1000      slower (ms between requests)
 *
 * Needs Node 18+ (global fetch). No dependencies. Run tools/build-site-data.js first.
 */

const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '..');
const ITEMS = path.join(ROOT, 'site', 'data', 'items.json');
const MATERIALS = path.join(ROOT, 'site', 'data', 'materials.json');
const OUT = path.join(ROOT, 'site', 'icons');
const FAILED_LOG = path.join(OUT, '_failed.txt');

const DELAY_MS = 300;
const TIMEOUT_MS = 20000;
const MAX_RETRIES = 3;
const UA = 'kinah-ledger/fetch-icons (unofficial Aion 2 fan site; one request per 300 ms)';

function parseArgs(argv) {
  const o = { materialsOnly: false, limit: Infinity, dryRun: false, delay: DELAY_MS };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--materials-only') o.materialsOnly = true;
    else if (a === '--limit') o.limit = Math.max(0, parseInt(argv[++i], 10) || 0);
    else if (a === '--dry-run') o.dryRun = true;
    else if (a === '--delay') o.delay = Math.max(0, parseInt(argv[++i], 10) || DELAY_MS);
    else if (a === '-h' || a === '--help') {
      console.log('usage: node tools/fetch-icons.js [--materials-only] [--limit N] [--dry-run] [--delay ms]');
      process.exit(0);
    } else { console.error('unknown argument: ' + a); process.exit(2); }
  }
  return o;
}

const sleep = ms => new Promise(r => setTimeout(r, ms));
const fmt = n => n.toLocaleString('en-US');
const rel = p => path.relative(ROOT, p).split(path.sep).join('/');

// Build the de-duplicated list of {url, dest} from the data file.
function planDownloads(dataFile) {
  if (!fs.existsSync(dataFile)) {
    throw new Error(`${rel(dataFile)} not found. Run "node tools/build-site-data.js" first.`);
  }
  const data = JSON.parse(fs.readFileSync(dataFile, 'utf8'));
  const base = typeof data.iconBase === 'string' ? data.iconBase : '';
  const items = Array.isArray(data.items) ? data.items : [];
  const seen = new Set();
  const plan = [];
  let unsafe = 0;
  for (const it of items) {
    const i = it && typeof it.i === 'string' ? it.i : '';
    if (!i || seen.has(i)) continue;
    seen.add(i);
    let url, relPath;
    if (/^https?:\/\//i.test(i)) {           // icon outside iconBase: keep only the file name
      url = i;
      try { relPath = path.posix.basename(new URL(i).pathname); } catch (e) { unsafe++; continue; }
    } else {
      if (!base) { unsafe++; continue; }
      url = base + i;
      relPath = i;
    }
    // never write outside site/icons
    const norm = path.posix.normalize(relPath);
    if (!norm || norm.startsWith('..') || path.posix.isAbsolute(norm) || /^[a-z]:/i.test(norm)) { unsafe++; continue; }
    plan.push({ url, dest: path.join(OUT, ...norm.split('/')) });
  }
  return { plan, unsafe, base, total: items.length };
}

async function download(url, dest, opt) {
  for (let attempt = 0; ; attempt++) {
    const ctrl = new AbortController();
    const timer = setTimeout(() => ctrl.abort(), TIMEOUT_MS);
    let res;
    try {
      res = await fetch(url, { headers: { 'User-Agent': UA, Accept: 'image/*,*/*;q=0.8' }, signal: ctrl.signal, redirect: 'follow' });
    } catch (e) {
      clearTimeout(timer);
      if (attempt < MAX_RETRIES) { await sleep(opt.delay * 4 * (attempt + 1)); continue; }
      return { ok: false, status: 'network: ' + (e && e.name === 'AbortError' ? 'timeout' : (e && e.message) || e) };
    }
    clearTimeout(timer);
    if (res.ok) {
      const buf = Buffer.from(await res.arrayBuffer());
      if (!buf.length) return { ok: false, status: 'empty body' };
      fs.mkdirSync(path.dirname(dest), { recursive: true });
      const part = dest + '.part';
      fs.writeFileSync(part, buf);
      fs.renameSync(part, dest);
      return { ok: true, bytes: buf.length };
    }
    if ((res.status === 429 || res.status >= 500) && attempt < MAX_RETRIES) {
      const ra = Number(res.headers.get('retry-after'));
      await sleep(ra > 0 ? ra * 1000 : opt.delay * 10 * (attempt + 1));
      continue;
    }
    return { ok: false, status: 'HTTP ' + res.status };
  }
}

async function main() {
  const opt = parseArgs(process.argv.slice(2));
  if (typeof fetch !== 'function') throw new Error('global fetch is missing; use Node 18 or newer');

  const src = opt.materialsOnly ? MATERIALS : ITEMS;
  const { plan, unsafe, base, total } = planDownloads(src);
  const existing = plan.filter(p => { try { return fs.statSync(p.dest).size > 0; } catch (e) { return false; } });
  const todo = plan.filter(p => !existing.includes(p));

  console.log('fetch-icons');
  console.log(`  source   ${rel(src)}  (${fmt(total)} items, ${fmt(plan.length)} distinct icons${unsafe ? `, ${fmt(unsafe)} skipped as unsafe/unresolvable` : ''})`);
  console.log(`  iconBase ${base || '(none)'}`);
  console.log(`  target   ${rel(OUT)}/  (${fmt(existing.length)} already present, ${fmt(todo.length)} to fetch` +
              (Number.isFinite(opt.limit) ? `, limit ${fmt(opt.limit)}` : '') + `, ${opt.delay} ms apart)`);
  if (opt.dryRun) {
    const planned = todo.slice(0, Math.min(todo.length, opt.limit));
    const shown = planned.slice(0, 20);
    for (const p of shown) console.log('    ' + p.url + '  ->  ' + rel(p.dest));
    if (planned.length > shown.length) console.log(`    ... and ${fmt(planned.length - shown.length)} more`);
    console.log(`  (dry run: ${fmt(planned.length)} would be fetched, nothing fetched)`);
    return;
  }
  if (!todo.length) { console.log('  nothing to do'); return; }

  const eta = Math.round(Math.min(todo.length, opt.limit) * opt.delay / 1000);
  console.log(`  estimated ${eta >= 90 ? Math.round(eta / 60) + ' min' : eta + ' s'} at the configured pace. Ctrl-C to stop; re-run to resume.`);

  let stop = false;
  process.on('SIGINT', () => { stop = true; console.log('\n  stopping after the current file ...'); });

  let done = 0, failed = 0, bytes = 0;
  const started = Date.now();
  const failedLines = [];
  for (const p of todo) {
    if (stop || done + failed >= opt.limit) break;
    const r = await download(p.url, p.dest, opt);
    if (r.ok) { done++; bytes += r.bytes; }
    else { failed++; failedLines.push(`${r.status}\t${p.url}`); console.log(`  FAIL ${r.status}  ${p.url}`); }
    if ((done + failed) % 50 === 0) {
      console.log(`  ${fmt(done + failed)} / ${fmt(Math.min(todo.length, opt.limit))}  (${fmt(failed)} failed, ${fmt(Math.round(bytes / 1024))} KB)`);
    }
    await sleep(opt.delay);
  }
  if (failedLines.length) {
    fs.mkdirSync(OUT, { recursive: true });
    fs.appendFileSync(FAILED_LOG, failedLines.join('\n') + '\n', 'utf8');
  }
  const secs = Math.round((Date.now() - started) / 1000);
  console.log(`  finished: ${fmt(done)} downloaded (${fmt(Math.round(bytes / 1024))} KB), ${fmt(failed)} failed, ` +
              `${fmt(todo.length - done - failed)} remaining, ${secs} s` +
              (failedLines.length ? `\n  failures appended to ${rel(FAILED_LOG)}` : ''));
}

main().catch(e => { console.error('ERROR ' + (e && e.message ? e.message : e)); process.exit(1); });
