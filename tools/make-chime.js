// The tracker's sounds, made here, no one else's (Steven, 2026-11-04: Windows' notify sound made a blue drop sound
// like an error in the app; and one chime for everything meant a price alert sounded like a drop). Three short
// sounds, each its own shape so the ear tells them apart without looking, written to tracker/KinahLedger.Tracker/
// and embedded in the exe (KinahLedger.Tracker.csproj), played by MainWindow.RareWatch.cs:
//
//   chime.wav   a watched or rare drop: two rising notes, C6 then G6 120 ms later
//   alert.wav   a price alert: the same note twice, A5 then A5 again 180 ms later (a knock, not a rise)
//   ready.wav   an update ready to install: three soft rising notes, E5 G5 C6, slower and quieter
//
//   node tools/make-chime.js            all three
//   node tools/make-chime.js alert      one of them
//
// 44.1 kHz, 16-bit, mono. Each note is a sine with a touch of its octave, a 5 ms rise and an exponential decay,
// the whole peaking at -4 dB (ready at -8 dB).
const fs = require("fs");
const path = require("path");

const sounds = {
  chime: { seconds: 0.75, peakDb: -4, decay: 0.25, notes: [{ hz: 1046.5, at: 0.0 }, { hz: 1568.0, at: 0.12 }] },
  alert: { seconds: 0.7, peakDb: -4, decay: 0.18, notes: [{ hz: 880.0, at: 0.0 }, { hz: 880.0, at: 0.18 }] },
  ready: { seconds: 1.1, peakDb: -8, decay: 0.35, notes: [{ hz: 659.3, at: 0.0 }, { hz: 784.0, at: 0.16 }, { hz: 1046.5, at: 0.32 }] },
};

const rate = 44100;
const want = process.argv.slice(2);
for (const name of want.length ? want : Object.keys(sounds)) {
  const s = sounds[name];
  if (!s) { console.error(`no sound named ${name}; one of ${Object.keys(sounds).join(", ")}`); process.exit(1); }
  const n = Math.round(rate * s.seconds);
  const out = new Float64Array(n);
  for (const { hz, at } of s.notes) {
    const start = Math.round(at * rate);
    for (let i = start; i < n; i++) {
      const t = (i - start) / rate;
      const rise = Math.min(1, t / 0.005);
      const decay = Math.exp(-t / s.decay);
      out[i] += rise * decay * (Math.sin(2 * Math.PI * hz * t) + 0.25 * Math.sin(2 * Math.PI * hz * 2 * t));
    }
  }
  let peak = 0;
  for (const v of out) peak = Math.max(peak, Math.abs(v));
  const gain = Math.pow(10, s.peakDb / 20) / peak;
  const pcm = Buffer.alloc(n * 2);
  for (let i = 0; i < n; i++) pcm.writeInt16LE(Math.round(Math.max(-1, Math.min(1, out[i] * gain)) * 32767), i * 2);

  const header = Buffer.alloc(44);
  header.write("RIFF", 0); header.writeUInt32LE(36 + pcm.length, 4); header.write("WAVE", 8);
  header.write("fmt ", 12); header.writeUInt32LE(16, 16); header.writeUInt16LE(1, 20); header.writeUInt16LE(1, 22);
  header.writeUInt32LE(rate, 24); header.writeUInt32LE(rate * 2, 28); header.writeUInt16LE(2, 32); header.writeUInt16LE(16, 34);
  header.write("data", 36); header.writeUInt32LE(pcm.length, 40);

  const file = path.join(__dirname, "..", "tracker", "KinahLedger.Tracker", `${name}.wav`);
  fs.writeFileSync(file, Buffer.concat([header, pcm]));
  console.log(`wrote ${path.relative(process.cwd(), file)}: ${(44 + pcm.length).toLocaleString()} bytes, ${s.seconds} s`);
}
