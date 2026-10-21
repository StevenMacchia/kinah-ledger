#!/usr/bin/env node
'use strict';
/*
 * release-manifest.js  (Kinah Ledger)
 *
 * Writes latest.json for a tracker release: the version (from the csproj), the size and SHA-256 of the
 * published exe, and where the site serves it. Upload it to the GitHub release next to KinahLedger.exe;
 * kinahledger.com/download/latest.json serves it to the tracker's update check, and the tracker installs a
 * download only when its size and checksum match this file.
 *
 * Usage
 *   node tools/release-manifest.js                       finds the published exe, writes latest.json beside it
 *   node tools/release-manifest.js path/to/KinahLedger.exe [--out dir]
 *
 * Then, for version X.Y.Z:
 *   gh release create vX.Y.Z <dir>/KinahLedger.exe <dir>/latest.json --title "Kinah Ledger tracker X.Y.Z" --notes-file notes.md
 */
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const root = path.resolve(__dirname, '..');
const csproj = path.join(root, 'tracker', 'KinahLedger.Tracker', 'KinahLedger.Tracker.csproj');
const REPO = 'https://github.com/StevenMacchia/kinah-ledger';
const SITE = 'https://kinahledger.com';

const args = process.argv.slice(2);
const outIdx = args.indexOf('--out');
const outDir = outIdx >= 0 ? path.resolve(args[outIdx + 1] || '.') : null;
const given = args.filter((a, i) => !a.startsWith('--') && (outIdx < 0 || i !== outIdx + 1))[0];

const candidates = given ? [path.resolve(given)] : [
  path.join(root, 'dist', 'KinahLedger.exe'),
  path.join(root, 'tracker', 'KinahLedger.Tracker', 'dist', 'KinahLedger.exe'),
  path.join(root, 'tracker', 'KinahLedger.Tracker', 'bin', 'Release', 'net8.0-windows10.0.19041.0', 'win-x64', 'publish', 'KinahLedger.exe'),
];
const exe = candidates.find((p) => fs.existsSync(p));
if (!exe) {
  console.error('No published KinahLedger.exe found. Run `dotnet publish -c Release` first, or pass its path. Looked in:\n  ' + candidates.join('\n  '));
  process.exit(1);
}

const m = /<Version>\s*(\d+\.\d+\.\d+)\s*<\/Version>/.exec(fs.readFileSync(csproj, 'utf8'));
if (!m) { console.error('No <Version>x.y.z</Version> in ' + csproj); process.exit(1); }
const version = m[1];

const bytes = fs.readFileSync(exe);
if (bytes.length < 1e6) { console.error(`${exe} is only ${bytes.length} bytes; is that the self-contained publish?`); process.exit(1); }
const sha256 = crypto.createHash('sha256').update(bytes).digest('hex');

const manifest = {
  version,
  url: `${SITE}/download/v${version}/KinahLedger.exe`,
  sha256,
  size: bytes.length,
  notes_url: `${REPO}/releases/tag/v${version}`,
  published_at: new Date().toISOString().replace(/\.\d{3}Z$/, 'Z'),
};

const dest = path.join(outDir || path.dirname(exe), 'latest.json');
fs.mkdirSync(path.dirname(dest), { recursive: true });
fs.writeFileSync(dest, JSON.stringify(manifest, null, 2) + '\n');
console.log(`Wrote ${dest}\n  version ${version}\n  size    ${bytes.length.toLocaleString('en-US')} bytes\n  sha256  ${sha256}`);
console.log(`\nNext: gh release create v${version} "${exe}" "${dest}" --title "Kinah Ledger tracker ${version}" --notes-file notes.md`);
