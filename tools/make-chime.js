// The tracker's chime: two rising notes, made here, no one else's sound (Steven, 2026-11-04: Windows' notify sound
// made a blue drop sound like an error in the app). Writes tracker/KinahLedger.Tracker/chime.wav, embedded in the exe
// (KinahLedger.Tracker.csproj) and played for a watched drop, a price alert and Options' Play (MainWindow.RareWatch.cs).
//
//   node tools/make-chime.js
//
// 44.1 kHz, 16-bit, mono, about 0.75 s: C6 then G6 120 ms later, each a sine with a touch of its octave, a 5 ms rise
// and a quarter-second decay, peaking at -4 dB.
const fs = require("fs");
const path = require("path");

const rate = 44100, seconds = 0.75;
const notes = [{ hz: 1046.5, at: 0.0 }, { hz: 1568.0, at: 0.12 }];
const n = Math.round(rate * seconds);
const out = new Float64Array(n);
for (const { hz, at } of notes) {
  const start = Math.round(at * rate);
  for (let i = start; i < n; i++) {
    const t = (i - start) / rate;
    const rise = Math.min(1, t / 0.005);
    const decay = Math.exp(-t / 0.25);
    out[i] += rise * decay * (Math.sin(2 * Math.PI * hz * t) + 0.25 * Math.sin(2 * Math.PI * hz * 2 * t));
  }
}
let peak = 0;
for (const v of out) peak = Math.max(peak, Math.abs(v));
const gain = Math.pow(10, -4 / 20) / peak;
const pcm = Buffer.alloc(n * 2);
for (let i = 0; i < n; i++) pcm.writeInt16LE(Math.round(Math.max(-1, Math.min(1, out[i] * gain)) * 32767), i * 2);

const header = Buffer.alloc(44);
header.write("RIFF", 0); header.writeUInt32LE(36 + pcm.length, 4); header.write("WAVE", 8);
header.write("fmt ", 12); header.writeUInt32LE(16, 16); header.writeUInt16LE(1, 20); header.writeUInt16LE(1, 22);
header.writeUInt32LE(rate, 24); header.writeUInt32LE(rate * 2, 28); header.writeUInt16LE(2, 32); header.writeUInt16LE(16, 34);
header.write("data", 36); header.writeUInt32LE(pcm.length, 40);

const file = path.join(__dirname, "..", "tracker", "KinahLedger.Tracker", "chime.wav");
fs.writeFileSync(file, Buffer.concat([header, pcm]));
console.log(`wrote ${path.relative(process.cwd(), file)}: ${(44 + pcm.length).toLocaleString()} bytes, ${seconds} s`);
