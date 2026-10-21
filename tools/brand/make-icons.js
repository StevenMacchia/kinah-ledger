#!/usr/bin/env node
'use strict';
/*
 * make-icons.js  (Kinah Ledger)  --  OWNER-RUN. NOT PART OF THE BUILD.
 *
 * Builds every icon file from the two logo sources next to it, with headless
 * Chrome doing the drawing:
 *   logo.svg        the K tile (night tile, struck gold K, aether rule), 32px and up
 *   logo-small.svg  the 16 to 24px cut (flat colours, heavier K, whole-pixel stem)
 *
 * Writes
 *   tracker/KinahLedger.Tracker/app.ico   16 to 256: the exe in Explorer, the window, taskbar and tray
 *   tools/brand/logo-512.png, logo-256.png  for the README, Discord and the like
 * and with a siteDir (the site repo's site/ folder):
 *   favicon.ico                16, 32, 48
 *   apple-touch-icon.png       180, full bleed (iOS rounds the corners itself)
 *   icon-192.png, icon-512.png for site.webmanifest
 *
 *   node tools/brand/make-icons.js [siteDir]
 *   CHROME_PATH overrides where Chrome is.
 *
 * Frames up to 128px are 32-bit BMP, the 256px frame is PNG: that is what
 * Windows' own icons use, and what both System.Drawing.Icon and WPF read.
 */
const fs = require('fs');
const path = require('path');
const os = require('os');
const { execFileSync } = require('child_process');

const HERE = __dirname;
const ROOT = path.join(HERE, '..', '..');
const CHROME = process.env.CHROME_PATH || 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const SITE = process.argv[2] ? path.resolve(process.argv[2]) : null;

const big = fs.readFileSync(path.join(HERE, 'logo.svg'), 'utf8');
const svg = {
  b: big,
  s: fs.readFileSync(path.join(HERE, 'logo-small.svg'), 'utf8'),
  // full bleed for the home-screen icon: the tile fills the square, no edge line
  f: big.replace('<rect x="2" y="2" width="96" height="96" rx="23"', '<rect width="100" height="100"').replace(/<rect x="2\.8"[^>]*\/>/, ''),
};

// what to draw: [size, variant]. Small cut below 32px, the full tile from 32px.
const JOBS = [
  [16, 's'], [20, 's'], [24, 's'], [32, 'b'], [40, 'b'], [48, 'b'], [64, 'b'], [96, 'b'], [128, 'b'], [256, 'b'], [512, 'b'],
  [192, 'b'], [180, 'f'],
];

// The page draws each job on a canvas and prints {key: {rgba, png}} as base64.
const page = `<!doctype html><meta charset="utf-8"><pre id="out"></pre><script>
const SVG = ${JSON.stringify(svg)}, JOBS = ${JSON.stringify(JOBS)};
const load = s => new Promise((ok, no) => { const i = new Image(); i.onload = () => ok(i); i.onerror = no; i.src = 'data:image/svg+xml,' + encodeURIComponent(s); });
const b64 = u8 => { let s = ''; for (let i = 0; i < u8.length; i += 32768) s += String.fromCharCode.apply(null, u8.subarray(i, i + 32768)); return btoa(s); };
(async () => {
  const img = {}, out = {};
  for (const k in SVG) img[k] = await load(SVG[k]);
  for (const [n, v] of JOBS) {
    const c = document.createElement('canvas'); c.width = c.height = n; const x = c.getContext('2d');
    x.imageSmoothingQuality = 'high'; x.drawImage(img[v], 0, 0, n, n);
    out[n + v] = { rgba: n <= 128 ? b64(x.getImageData(0, 0, n, n).data) : '', png: c.toDataURL('image/png').split(',')[1] };
  }
  document.getElementById('out').textContent = JSON.stringify(out);
})();
</script>`;

const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'kl-icons-'));
const html = path.join(tmp, 'icons.html');
fs.writeFileSync(html, page);
const dom = execFileSync(CHROME, ['--headless=new', '--disable-gpu', '--virtual-time-budget=5000', '--dump-dom', 'file:///' + html.replace(/\\/g, '/')],
  { encoding: 'utf8', maxBuffer: 64 << 20, stdio: ['ignore', 'pipe', 'ignore'] });
fs.rmSync(tmp, { recursive: true, force: true });
const m = dom.match(/<pre id="out">([^<]+)<\/pre>/);
if (!m) throw new Error('Chrome drew nothing; is CHROME_PATH right?');
const R = JSON.parse(m[1].replace(/&quot;/g, '"').replace(/&amp;/g, '&'));

// one 32-bit BMP icon frame: header, bottom-up BGRA, then the 1-bit AND mask
function bmpFrame(n, rgba) {
  const px = Buffer.from(rgba, 'base64'), maskRow = Math.ceil(n / 32) * 4;
  const b = Buffer.alloc(40 + n * n * 4 + maskRow * n);
  b.writeUInt32LE(40, 0); b.writeInt32LE(n, 4); b.writeInt32LE(n * 2, 8); b.writeUInt16LE(1, 12); b.writeUInt16LE(32, 14);
  b.writeUInt32LE(n * n * 4 + maskRow * n, 20);
  for (let y = 0; y < n; y++) {
    const src = (n - 1 - y) * n * 4;
    for (let x = 0; x < n; x++) {
      const s = src + x * 4, d = 40 + (y * n + x) * 4;
      b[d] = px[s + 2]; b[d + 1] = px[s + 1]; b[d + 2] = px[s]; b[d + 3] = px[s + 3];
      if (px[s + 3] === 0) b[40 + n * n * 4 + y * maskRow + (x >> 3)] |= 0x80 >> (x & 7);
    }
  }
  return b;
}

function ico(keys) {
  const frames = keys.map(k => { const n = parseInt(k, 10); return { n, data: n >= 256 ? Buffer.from(R[k].png, 'base64') : bmpFrame(n, R[k].rgba) }; });
  const head = Buffer.alloc(6 + 16 * frames.length);
  head.writeUInt16LE(1, 2); head.writeUInt16LE(frames.length, 4);
  let off = head.length;
  frames.forEach((f, i) => {
    const e = 6 + 16 * i;
    head[e] = f.n >= 256 ? 0 : f.n; head[e + 1] = f.n >= 256 ? 0 : f.n;
    head.writeUInt16LE(1, e + 4); head.writeUInt16LE(32, e + 6);
    head.writeUInt32LE(f.data.length, e + 8); head.writeUInt32LE(off, e + 12);
    off += f.data.length;
  });
  return Buffer.concat([head, ...frames.map(f => f.data)]);
}

const png = k => Buffer.from(R[k].png, 'base64');
function write(file, buf) { fs.writeFileSync(file, buf); console.log(`${path.relative(process.cwd(), file)}  ${buf.length.toLocaleString()} bytes`); }

write(path.join(ROOT, 'tracker', 'KinahLedger.Tracker', 'app.ico'), ico(['16s', '20s', '24s', '32b', '40b', '48b', '64b', '96b', '128b', '256b']));
write(path.join(HERE, 'logo-512.png'), png('512b'));
write(path.join(HERE, 'logo-256.png'), png('256b'));
if (SITE) {
  write(path.join(SITE, 'favicon.ico'), ico(['16s', '32b', '48b']));
  write(path.join(SITE, 'apple-touch-icon.png'), png('180f'));
  write(path.join(SITE, 'icon-192.png'), png('192b'));
  write(path.join(SITE, 'icon-512.png'), png('512b'));
}
