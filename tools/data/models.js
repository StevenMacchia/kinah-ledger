// Fetches the name decoder's model files (tracker Ocr/NameDecoder.cs) from the pinned URLs in data/models.json,
// checks each against its SHA-256 and writes it where the tracker looks for it. The files are never committed and
// never built into the exe; a tracker without them runs as before and logs "decoder off (no model)".
//
//   node tools/data/models.js                  # into %LOCALAPPDATA%\KinahLedger\data (the tracker's kept data folder)
//   node tools/data/models.js --out data/models  # into a folder of your choice (CI and parse-test use data/models)
//   node tools/data/models.js --check [--out d]  # only check what is there; exits 1 when a file is missing or wrong
//
// A file already present with the right hash is not downloaded again. A download whose hash differs is thrown away.
const fs = require('fs'), path = require('path'), crypto = require('crypto');
const root = path.join(__dirname, '..', '..');
const pins = JSON.parse(fs.readFileSync(path.join(root, 'data', 'models.json'), 'utf8')).files;
const args = process.argv.slice(2);
const at = args.indexOf('--out');
const out = at >= 0 && args[at + 1] ? path.resolve(args[at + 1])
  : path.join(process.env.LOCALAPPDATA || path.join(require('os').homedir(), 'AppData', 'Local'), 'KinahLedger', 'data');
const check = args.includes('--check');
const sha = buf => crypto.createHash('sha256').update(buf).digest('hex');

(async () => {
  let bad = 0;
  if (!check) fs.mkdirSync(out, { recursive: true });
  for (const [name, pin] of Object.entries(pins)) {
    const target = path.join(out, name);
    if (fs.existsSync(target) && sha(fs.readFileSync(target)) === pin.sha256) { console.log(`ok       ${name} (already there)`); continue; }
    if (check) { console.log(`missing  ${name}`); bad++; continue; }
    const t0 = Date.now();
    const res = await fetch(pin.url, { redirect: 'follow' });
    if (!res.ok) { console.error(`failed   ${name}: HTTP ${res.status} from ${pin.url}`); bad++; continue; }
    const buf = Buffer.from(await res.arrayBuffer());
    const got = sha(buf);
    if (got !== pin.sha256) { console.error(`failed   ${name}: SHA-256 ${got}, expected ${pin.sha256}; not written`); bad++; continue; }
    fs.writeFileSync(target + '.part', buf);
    fs.renameSync(target + '.part', target);
    console.log(`fetched  ${name} (${buf.length.toLocaleString()} bytes, ${Date.now() - t0} ms)`);
  }
  console.log(bad ? `${bad} file(s) not in place in ${out}` : `model files in place in ${out}`);
  process.exit(bad ? 1 : 0);
})().catch(e => { console.error(e.message); process.exit(1); });
