"""Moves the tracker's hard-coded colours from the Quest (plum) palette to the site's v2 look (2026-11-10).

    python -I tools/v2-palette.py [--dry]

Theme.xaml and Ui/Skins.cs hold the named brushes and were moved by hand. About 200 colours more are written out in
Ui/*.cs and the window xaml: the item cards, the HUD panel, the share pictures, the calibration and region windows.
This rewrites them the way the site's tools/seo/v2-palette.js did:
  - the Night palette's own values move to their v2 token (Ink F6F2FF to EDEFF5, Gold FFC857 to F5B942, Card 251E45
    to 111319 and so on: TOKENS below), alpha kept;
  - any other dark surface in the blue-to-purple range (hue 200-300, lightness under 38%, saturation over 17%) becomes
    the neutral of about the same darkness (hue 228, saturation at most 16%, lightness x0.85), alpha kept;
  - a light translucent tint of teal, sky or lilac (alpha under 0.35) becomes a neutral tint at the same alpha;
  - everything else (gold, coral, the grade colours, white and black) is left alone.
Forms it reads: "#RRGGBB" and "#AARRGGBB", Color.FromRgb(r, g, b) and Color.FromArgb([a,] r, g, b) with literal
arguments (hex or decimal), and the HUD's 0xRRGGBB constants (an alpha after them, as in B(0x1A1433, .94), is read).
Run once; the result is committed, so the diff shows every colour that moved. Idempotent: its output never qualifies.
"""
import colorsys
import os
import re
import sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "tracker", "KinahLedger.Tracker", "Ui")
SKIP = {"Skins.cs", "Theme.xaml"}
DRY = "--dry" in sys.argv

# the Night (Quest) palette's values and the v2 token each one becomes (kinah-ledger-site tools/seo/kl-v2.css)
TOKENS = {
    "F6F2FF": "EDEFF5",  # ink
    "E8E2FF": "D9DDE8",  # a common item's ink
    "B3ABD2": "A2A8BA",  # mute
    "7F78A3": "6D7387",  # faint
    "8C86A8": "7A8094",  # a craft's plain outcome
    "5B5485": "4A5066",  # the run dot, off
    "FFC857": "F5B942",  # gold
    "FFD97A": "FFD27A",  # gold-2
    "F8D598": "FFD27A",  # the older pale gold
    "EBAE4C": "F5B942",
    "E9B45A": "F5B942",
    "E59F2B": "D99A24",  # the deep gold of rules and gradients
    "5CE1B5": "3DD68C",  # mint
    "2FB58C": "28A86C",  # mint, the dark end of a bar
    "5ED8CB": "3DD68C",  # the older teal for "yours" and "ready"
    "FF7D6E": "F26D6D",  # coral
    "6EC1FF": "7AB3FF",  # sky
    "B692FF": "B39BFF",  # lilac
    "110D21": "0A0B10",  # bg
    "251E45": "111319",  # surface
    "312960": "171A22",  # surface-2
    "2C2552": "1E2230",  # surface-3, the hover
    "3B3366": "232736",  # line
    "362E5E": "232736",
    "4A4180": "2E3346",  # line-2
    "1C1735": "0F1117",
    "17132B": "0D0F14",  # slot
    "2A2352": "1A1D27",
    "1A1433": "12141B",  # the HUD panel
    "42327A": "1E2230",  # the HUD card's plum glow
}


def move(r, g, b, a):
    """The new (r, g, b), or None to keep."""
    key = "%02X%02X%02X" % (r, g, b)
    if key in TOKENS:
        t = TOKENS[key]
        return tuple(int(t[i:i + 2], 16) for i in (0, 2, 4))
    if (r, g, b) == (190, 198, 220):
        return None
    h, l, s = colorsys.rgb_to_hls(r / 255, g / 255, b / 255)
    h *= 360
    if 215 <= h <= 240 and s < 0.3:
        return None  # already v2 (hue 228; whole RGB values move it a little, most near black)
    if 200 <= h <= 300 and l < 0.38 and s > 0.17:
        nr, ng, nb = colorsys.hls_to_rgb(228 / 360, l * 0.85, min(s, 0.16))
        return tuple(round(v * 255) for v in (nr, ng, nb))
    if a < 0.35 and l > 0.5 and 160 <= h <= 300 and s > 0.3:
        return (190, 198, 220)
    return None


def num(tok):
    return int(tok, 16) if tok.lower().startswith("0x") else int(tok)


def fmt(v, like):
    return "0x%02X" % v if like.lower().startswith("0x") else str(v)


N = r"(0x[0-9A-Fa-f]{1,2}|\d{1,3})"


def migrate(text):
    n = 0

    def hexstr(m):
        nonlocal n
        x = m.group(1)
        a = int(x[:2], 16) / 255 if len(x) == 8 else 1
        rgb = x[-6:]
        out = move(int(rgb[0:2], 16), int(rgb[2:4], 16), int(rgb[4:6], 16), a)
        if out is None:
            return m.group(0)
        n += 1
        return "#" + (x[:2] if len(x) == 8 else "") + "%02X%02X%02X" % out

    text = re.sub(r"#([0-9A-Fa-f]{8}|[0-9A-Fa-f]{6})(?![0-9A-Za-z_])", hexstr, text)

    def from_rgb(m):
        nonlocal n
        args = [m.group(i) for i in (2, 3, 4, 5) if m.group(i) is not None]
        vals = [num(v) for v in args]
        if any(v > 255 for v in vals):
            return m.group(0)
        if len(vals) == 4:
            a, (r, g, b) = vals[0] / 255, vals[1:]
        else:
            a, (r, g, b) = 1, vals
        out = move(r, g, b, a)
        if out is None:
            return m.group(0)
        n += 1
        new = ([args[0]] if len(vals) == 4 else []) + [fmt(v, like) for v, like in zip(out, args[-3:])]
        return m.group(1) + "(" + ", ".join(new) + ")"

    text = re.sub(r"(Color\.From(?:Argb|Rgb))\(\s*" + N + r"\s*,\s*" + N + r"\s*,\s*" + N + r"\s*(?:,\s*" + N + r"\s*)?\)", from_rgb, text)

    # Color.FromArgb(a, r, g, b) with the alpha worked out at run time (the HUD panel's opacity): judged as solid
    def var_alpha(m):
        nonlocal n
        args = [m.group(i) for i in (3, 4, 5)]
        out = move(*[num(v) for v in args], 1)
        if out is None:
            return m.group(0)
        n += 1
        return m.group(1) + "(" + m.group(2) + ", " + ", ".join(fmt(v, like) for v, like in zip(out, args)) + ")"

    text = re.sub(r"(Color\.FromArgb)\(\s*([A-Za-z_][^,()]*)\s*,\s*" + N + r"\s*,\s*" + N + r"\s*,\s*" + N + r"\s*\)", var_alpha, text)

    def uint(m):
        nonlocal n
        x, alpha = m.group(1), m.group(2)
        a = float(alpha) if alpha else 1
        out = move(int(x[0:2], 16), int(x[2:4], 16), int(x[4:6], 16), a)
        if out is None:
            return m.group(0)
        n += 1
        return "0x%02X%02X%02X" % out + m.group(0)[8:]

    text = re.sub(r"\b0x([0-9A-Fa-f]{6})\b(?:\s*,\s*(\d?\.\d+))?", uint, text)
    return text, n


def main():
    total = 0
    for f in sorted(os.listdir(ROOT)):
        if not f.endswith((".cs", ".xaml")) or f in SKIP:
            continue
        p = os.path.join(ROOT, f)
        with open(p, "rb") as fh:
            raw = fh.read()
        src = raw.decode("utf-8-sig")
        out, k = migrate(src)
        if k:
            total += k
            print(f"v2-palette: {f} {k}")
            if not DRY:
                with open(p, "wb") as fh:
                    fh.write((b"\xef\xbb\xbf" if raw.startswith(b"\xef\xbb\xbf") else b"") + out.encode("utf-8"))
    print(f"v2-palette: {total} colours moved" + (" (dry run)" if DRY else ""))


if __name__ == "__main__":
    main()
