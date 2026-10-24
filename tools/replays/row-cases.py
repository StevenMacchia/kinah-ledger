"""`python tools/replays/row-cases.py tracker/tests/replays` writes the loot-row bank cases rebuilt from the 2026-10-24 10 fps footage (kinah-bench timing.md, kills.md, truth-*.csv).

The list is simulated the way the footage shows it: a new row takes the bottom slot (native y 1195) and pushes every older
row up one slot (73 px); a row is up 2.5 s; nothing moves down when a row expires; the Soul row's name scrolls (only the
slices the reader can parse reach a trace); a full-screen window stops a row's timer. Each pass reads the list (captured
first) then the chat, as a live pass does with the list's picture in before the chat is counted.
"""
import json, os, sys

BANK = sys.argv[1]
BOTTOM, PITCH = 1195, 73
ICON = {"kina": "3c7effffff7e3c18", "soul": "0f1f3f7ffefcf8f0", "leather": "aa55aa55aa55aa55", "stone": "00ff00ff00ff00ff"}
FILLER = ["[Player] chat 1a2b3c", "[Player] chat 4d5e6f", "[Player] chat 778899", "[Player] chat aabbcc", "[Player] chat ddeeff", "[Player] chat 123123", "[Player] chat 456456"]


def soul_slice(age):
    # "Soul: Starturtle (Bound) x1" scrolls left at about six characters a second from 0.2 s; the reader parses only the
    # slices that end with the count (the start cut off), as reads.log shows them
    if age < 0.6: return None
    if age < 0.9: return "l: Starturtle (Bound) x1"
    if age < 1.2: return "Starturtle (Bound) x1"
    if age < 1.6: return "rturtle (Bound) x1"
    if age < 2.0: return "turtle (Bound) x1"
    return "le (Bound) x1"


class Row:
    def __init__(self, at, text, icon, contrast=0.6):
        self.at, self.text, self.icon, self.c = at, text, icon, contrast
        self.hidden = []   # (from, to): covered, its timer stopped

    def age(self, t):
        a = t - self.at
        for f, to in self.hidden:
            if t >= to: a -= (to - f)
            elif t > f: a -= (t - f)
        return a

    def read(self, t):
        a = self.age(t)
        if a < 0.15 or a >= 2.5: return None
        if self.text == "soul": return soul_slice(a)
        return self.text


def case(name, title, source, start, end, period, chat, rows, expect, zone="Kraka Footpath", covers=(), extra=None):
    out = [{"t": iso(start - 1.0), "k": "zone", "l": [zone]}]
    out.append({"t": iso(start - 0.9), "k": "chat", "l": FILLER})   # the first read is history
    for r in rows:
        for f, to in covers: r.hidden.append((f, to))
    t = start
    while t <= end + 1e-9:
        covered = any(f <= t < to for f, to in covers)
        if covered:
            out.append({"t": iso(t), "k": "cover", "l": []})
        else:
            vis = [r for r in rows if r.at <= t and r.age(t) < 2.5]
            lines = []
            for r in vis:
                txt = r.read(t)
                if txt is None: continue
                slot = sum(1 for o in rows if o.at > r.at and o.at <= t)
                lines.append((BOTTOM - PITCH * slot, txt, r))
            for x in (extra or []):
                v = x(t)
                if v: lines.append(v)
            lines.sort(key=lambda x: x[0])
            if lines:
                out.append({"t": iso(t), "k": "list", "l": [l[1] for l in lines], "y": [l[0] for l in lines],
                            "c": [round(l[2].c if isinstance(l[2], Row) else l[2]["c"], 3) for l in lines],
                            "b": [0.2 if isinstance(l[2], Row) else l[2]["b"] for l in lines],
                            "s": [0.13 if isinstance(l[2], Row) else l[2]["s"] for l in lines],
                            "h": [l[2].icon if isinstance(l[2], Row) else l[2]["h"] for l in lines]})
            else:
                out.append({"t": iso(t), "k": "list", "l": [], "y": [], "c": []})
        ct = t + 0.05
        shown = FILLER + [l for at, l in sorted(chat, key=lambda x: x[0]) if at <= ct]
        out.append({"t": iso(ct), "k": "chat", "l": shown[-7:]})
        t = round(t + period, 3)
    d = os.path.join(BANK, name)
    os.makedirs(d, exist_ok=True)
    with open(os.path.join(d, "trace.jsonl"), "w", newline="\n") as f:
        for o in out: f.write(json.dumps(o, separators=(",", ":")) + "\n")
    with open(os.path.join(d, "case.json"), "w", newline="\n") as f:
        json.dump({"pending": False, "needs_ocr": False, "trace": "trace.jsonl", "name": title, "source": source, "expect": expect}, f, indent=2)
        f.write("\n")


def iso(s):
    # clip seconds on the 2026-10-24 clock (16:00:00 UTC + s), to the millisecond
    ms = int(round(s * 1000))
    h, rem = divmod(ms, 3600000); m, rem = divmod(rem, 60000); sec, ms = divmod(rem, 1000)
    return f"2026-10-24T{16 + h:02d}:{m:02d}:{sec:02d}.{ms:03d}Z"


SRC = "rebuilt from the 10 fps footage of 2026-10-24 (kinah-bench timing.md, kills.md, truth-%s.csv): rows placed as the footage shows the list (a new row at the bottom pushes the older ones up 73 px; 2.5 s up; nothing moves down), the Soul row's name scrolling (only the parseable slices), chat lines when they print; each pass reads the list, then the chat; icons, contrast, background and spread as LootRows measures real rows"

# 1. a new row pushing the older rows up (110725 10.9 to 12.5: Kina 210, Kina 509, two souls, Raw Leather; five rows up)
case("text-2026-10-24-rows-push-up",
     "five rows in 1.5 s (110725 at 11.0 to 12.5: Kina 210, Kina 509, a Soul, a Soul, Raw Leather): each new row takes the bottom slot and pushes the older ones up (Soul 1 pushes three up at 12.3); one chat line \"Obtained 2 Fera Soul Crystals\" for both souls. 719 Kina, one Raw Leather, two Fera Soul Crystals, each row one object",
     SRC % "110725", 10.6, 15.6, 0.35,
     [(10.9, "Acquired 210 Kina."), (11.4, "Acquired 509 Kina."), (12.4, "Raw Leather acquired."), (12.45, "Obtained 2 Fera Soul Crystals.")],
     [Row(11.0, "Kina x210", ICON["kina"]), Row(11.5, "Kina x509", ICON["kina"]), Row(12.0, "soul", ICON["soul"]), Row(12.3, "soul", ICON["soul"]), Row(12.5, "Raw Leather x1", ICON["leather"])],
     {"items": {"Raw Leather": 1}, "kinah": 719, "pets": 2, "kills": 2, "list_unsure": 0})

# 2. the marquee Soul row across five reads (110231 at 11.3 to 14.5: Kina 273, its soul scrolling)
case("text-2026-10-24-rows-marquee-soul",
     "a Soul row whose name scrolls for its whole life (110231 at 11.8: \"l: Starturtle (Bound) x1\", \"Starturtle (Bound) x1\", \"rturtle (Bound) x1\", \"turtle (Bound) x1\", \"le (Bound) x1\" over five reads) beside its kill's Kina 273: one row, never \"the final text\", one Fera Soul Crystal and 273 Kina",
     SRC % "110231", 11.2, 15.2, 0.4,
     [(11.3, "Acquired 273 Kina."), (11.8, "Obtained 1 Fera Soul Crystals.")],
     [Row(11.5, "Kina x273", ICON["kina"]), Row(12.0, "soul", ICON["soul"])],
     {"items": {}, "kinah": 273, "pets": 1, "kills": 1, "list_unsure": 0})

# 3. the player's title in the band for 60 s while a fight's rows come and go below
TITLE = ["Vang(", "Nan go", "Vangi", "Vangt", "Vangi", "Vanc", "Vangu", "Vangi", "Vang(", "Vangt"]
TC = [0.31, 0.19, 0.25, 0.16, 0.32, 0.36, 0.30, 0.26, 0.31, 0.32]
def title(t):
    k = int(round(t * 10))
    if k % 10 in (3, 7): return None   # not read every pass
    i = (k // 10) % len(TITLE)
    return (975 - (k % 3 == 0), TITLE[i], {"c": TC[i], "b": 0.35, "s": 0.05, "h": "5a5a5a5a00000000"})
kills = [(5.0, 524), (15.0, 239), (26.0, 198), (37.0, 273), (48.0, 384), (58.0, 159)]
chat3 = [(t, f"Acquired {a} Kina.") for t, a in kills] + [(37.4, "Spiritstone acquired.")]
rows3 = [Row(t + 0.15, f"Kina x{a}", ICON["kina"]) for t, a in kills] + [Row(37.6, "Spiritstone x1", ICON["stone"])]
case("text-2026-10-24-rows-title-60s",
     "the player's own title \"Vanguard of Atreia\" stands right of the top slot for a minute, read as \"Vang(\", \"Vangi\", \"Vangt\", \"Nan go\" at y 974 to 975 (contrast 0.16 to 0.36, the world behind it, so LootNotRow keeps it), while six kills' Kina rows and a Spiritstone come and go below: one object up over 6 s, not a row, logged once; never unsure every second (0.8.26 logged one unsure line a second for it). 1,777 Kina, one Spiritstone",
     SRC % "110231" + "; the title's reads and contrast from reads.log 11:02:06 to 11:02:47", 2.0, 62.0, 1.0,
     chat3, rows3, {"items": {"Spiritstone": 1}, "kinah": 1777, "kills": 6, "list_unsure": 0}, extra=[title])

# 4. two souls 0.4 s apart, one chat line for both (110725 at 23.9 to 24.8)
case("text-2026-10-24-rows-obtained-2",
     "two Soul rows 0.4 s apart (110725 at 24.4 and 24.8, two kills' souls picked off the ground) and one chat line \"Obtained 2 Fera Soul Crystals\": one pickup of two, not two of one and not three. 162 Kina, two Fera Soul Crystals",
     SRC % "110725", 23.6, 28.0, 0.5,
     [(23.9, "Acquired 162 Kina."), (24.8, "Obtained 2 Fera Soul Crystals.")],
     [Row(24.0, "Kina x162", ICON["kina"]), Row(24.4, "soul", ICON["soul"]), Row(24.8, "soul", ICON["soul"])],
     {"items": {}, "kinah": 162, "pets": 2})

# 5. the Kina row 0.2 s after the kill's chat line (110725 at 36.1, 10 fps)
case("text-2026-10-24-rows-kina-after-kill",
     "the kill's \"Acquired 343 Kina.\" prints at 36.1 and its row's first pixels come at 36.3 (110725, 10 fps): the chat line counts once, on the read the row agrees with it, and the row adds nothing. 343 Kina",
     SRC % "110725", 35.6, 40.1, 0.5,
     [(36.1, "Acquired 343 Kina.")],
     [Row(36.3, "Kina x343", ICON["kina"])],
     {"items": {}, "kinah": 343, "kills": 1})

# 6. a soul picked 4.5 s after its kill, after the next kill had started (110725 at 36.1 to 41.0)
case("text-2026-10-24-rows-item-late",
     "the kill at 36.1 (Kina 343) drops a soul picked off the ground at 40.6, 4.5 s later, its own chat line before the next kill's Kina 447 (40.9) and that kill's soul (41.0): two Fera Soul Crystals, 790 Kina",
     SRC % "110725", 35.6, 44.6, 0.5,
     [(36.1, "Acquired 343 Kina."), (40.6, "Obtained 1 Fera Soul Crystals."), (40.9, "Acquired 447 Kina."), (41.0, "Obtained 1 Fera Soul Crystals.")],
     [Row(36.3, "Kina x343", ICON["kina"]), Row(40.6, "soul", ICON["soul"]), Row(41.0, "Kina x447", ICON["kina"]), Row(41.1, "soul", ICON["soul"])],
     {"items": {}, "kinah": 790, "pets": 2, "kills": 2})

# 7. a row hidden behind the Shugo Festival window, back 6 s after it came (110231 at 13.8 to 20.5)
case("text-2026-10-24-rows-hidden-window",
     "Kina x384 comes at 14.3 (110231), the Shugo Festival window covers the screen 17.0 to 20.3 and the same row is back at 20.5, its timer stopped behind the window: the same row, one pickup, not two. Added to the clip: a Spiritstone row in the same stretch, its chat line once, so a tracked item row is covered too. The list is taken as covered from 16.3, so the row has the 0.5 s left that it showed after the window (20.3 to about 20.8). 384 Kina, one Spiritstone",
     SRC % "110231", 13.6, 23.6, 0.5,
     [(13.8, "Acquired 384 Kina."), (14.6, "Spiritstone acquired.")],
     [Row(14.3, "Kina x384", ICON["kina"]), Row(14.8, "Spiritstone x1", ICON["stone"])],
     {"items": {"Spiritstone": 1}, "kinah": 384, "kills": 1}, covers=[(16.3, 20.3)])
print("ok")
