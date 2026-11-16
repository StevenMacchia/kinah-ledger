// The specialty-fill step (2026-11-16): the specializations of each class skill, from MetaBot's skill pages.
// In the game a specialization has no name, only "Specialization N" and an effect text; each needs a skill level.
//
//   node tools/specialty-fill.js              fetch every page in data/skills.json once, write data/specialties.json
//   node tools/specialty-fill.js --dry-run    fetch and report, write nothing
//
// Source: https://metabot.gg/en/aion-2/skills/<page>, <section id="specializations"> (server-rendered, plain HTTP).
// Icon id = skill id + N*10 (stigma/<id>.webp). A page shared by several skill ids is written under each id.
// Polite: sequential requests with a short delay.
const fs = require('fs'), path = require('path');
const SKILLS = path.join(__dirname, '..', 'data', 'skills.json');
const OUT = path.join(__dirname, '..', 'data', 'specialties.json');
const dry = process.argv.includes('--dry-run');
const sleep = ms => new Promise(r => setTimeout(r, ms));

const unescape = s => s
  .replace(/&#x([0-9a-f]+);/gi, (_, h) => String.fromCodePoint(parseInt(h, 16)))
  .replace(/&#(\d+);/g, (_, d) => String.fromCodePoint(+d))
  .replace(/&quot;/g, '"').replace(/&apos;/g, "'").replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&amp;/g, '&');
const clean = s => unescape(s.replace(/<!--.*?-->/g, '').replace(/<[^>]+>/g, '')).replace(/\s+/g, ' ').trim();

function parse(html) {
  const m = html.match(/<section id="specializations"[\s\S]*?<\/section>/);
  if (!m) return [];
  const specs = [];
  for (const li of m[0].split('<li>').slice(1)) {
    const head = li.match(/class="[^"]*specHead">([\s\S]*?)<\/div>/);
    const text = li.match(/class="[^"]*specText">([\s\S]*?)<\/p>/);
    const img = li.match(/<img src="([^"]+)"/);
    const n = head && clean(head[1]).match(/Specialization\s+(\d+)/);
    if (!n) continue;
    const lvl = head[1].match(/requires skill level (\d+)/);
    let icon = null;
    if (img) {
      const src = unescape(img[1]).match(/[?&]src=([^&]+)/);
      icon = src ? decodeURIComponent(src[1]) : null;
    }
    const spec = { n: +n[1], text: text ? clean(text[1]) : '' };
    if (lvl) spec.level = +lvl[1];
    if (icon) spec.icon = icon;
    specs.push(spec);
  }
  return specs;
}

async function get(page) {
  for (let i = 0; i < 3; i++) {
    try {
      const r = await fetch('https://metabot.gg/en/aion-2/skills/' + page, { headers: { 'User-Agent': 'Mozilla/5.0' } });
      if (r.ok) return await r.text();
      if (r.status === 404) return null;
    } catch (e) { /* retry */ }
    await sleep(1500 * (i + 1));
  }
  throw new Error('failed: ' + page);
}

(async () => {
  const skills = JSON.parse(fs.readFileSync(SKILLS, 'utf8')).skills;
  const byPage = {};
  for (const [id, s] of Object.entries(skills)) if (s.page) (byPage[s.page] = byPage[s.page] || []).push(id);
  const pages = Object.keys(byPage);
  const out = {}, none = [], failed = [], empty = [];
  let done = 0;
  for (const page of pages) {
    let specs = [];
    try {
      const html = await get(page);
      specs = html ? parse(html) : [];
    } catch (e) { failed.push(page); }
    if (!specs.length) none.push(page);
    for (const s of specs) if (!s.text) empty.push(page + ' #' + s.n);
    if (specs.length) for (const id of byPage[page]) out[id] = { page, specs };
    if (++done % 50 === 0) console.log(done + '/' + pages.length);
    await sleep(250);
  }
  const sorted = Object.fromEntries(Object.keys(out).sort().map(k => [k, out[k]]));
  const doc = { source: 'https://metabot.gg/en/aion-2/skills/<page>, section "specializations" (tools/specialty-fill.js)', fetched: new Date().toISOString().slice(0, 10), skills: sorted };
  if (!dry) fs.writeFileSync(OUT, JSON.stringify(doc, null, 1).replace(/\n\s+("n"|"text"|"level"|"icon")/g, ' $1').replace(/\n\s+\}/g, ' }') + '\n');
  console.log(`pages ${pages.length}, skills with specs ${Object.keys(sorted).length}, pages without ${none.length}, failed ${failed.length}, empty text ${empty.length}`);
  if (none.length) console.log('no specs:', none.join(', '));
  if (failed.length) console.log('failed:', failed.join(', '));
  if (empty.length) console.log('empty text:', empty.join(', '));
})();
