"""
Pack the Parser's ability icons into one small atlas the tracker carries (tracker Art/skill-icons.png plus data/skill-icons.txt).

    python -I tools/skill-icons.py <folder of <id>.webp> [--cell 32]

The folder holds MetaBot's skill icons (https://metabot.gg/web/aion2/skills/<id>.webp), one per id in data/skills.json,
fetched into a folder of their own beforehand. Only the ids skills.json names are packed: those are the skills the
Parser knows. Each icon is scaled to one cell (32 px: crisp at the inspect view's 22 px up to 150% scaling), laid out
row by row in id order, and the whole sheet is saved as an 8-bit palette PNG, which keeps the exe's growth small.
skill-icons.txt lists the ids in cell order, one per line, after the cell size and the column count.
An id with no icon is left out; the tracker draws its class-coloured glyph instead.
"""
import json, math, os, sys
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
src = sys.argv[1]
cell = int(sys.argv[sys.argv.index("--cell") + 1]) if "--cell" in sys.argv else 32

skills = json.load(open(os.path.join(ROOT, "data", "skills.json"), encoding="utf-8"))["skills"]
ids = sorted(int(k) for k in skills)
have = [i for i in ids if os.path.exists(os.path.join(src, f"{i}.webp"))]
cols = 16
rows = math.ceil(len(have) / cols)
sheet = Image.new("RGBA", (cols * cell, rows * cell), (0, 0, 0, 0))
for n, i in enumerate(have):
    im = Image.open(os.path.join(src, f"{i}.webp")).convert("RGBA")
    im = im.resize((cell, cell), Image.LANCZOS)
    sheet.paste(im, ((n % cols) * cell, (n // cols) * cell))

art = os.path.join(ROOT, "tracker", "KinahLedger.Tracker", "Art", "skill-icons.png")
# the icons are opaque squares: an adaptive 256-colour palette over the RGB, transparency kept for the empty cells
rgb = sheet.convert("RGB")
pal = rgb.quantize(colors=255, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE)
alpha = sheet.getchannel("A")
pal_img = pal.copy()
# the last palette slot for the empty cells, marked transparent
px = pal_img.load(); ap = alpha.load()
for y in range(sheet.height):
    for x in range(sheet.width):
        if ap[x, y] < 128:
            px[x, y] = 255
pal_img.save(art, optimize=True, transparency=255)
idx = os.path.join(ROOT, "data", "skill-icons.txt")
with open(idx, "w", encoding="utf-8", newline="\n") as f:
    f.write(f"# the ability icons in Art/skill-icons.png (tools/skill-icons.py): cell size, columns, then one skill id per cell in order\n{cell}\n{cols}\n")
    f.write("\n".join(str(i) for i in have) + "\n")
print(f"{len(have)} of {len(ids)} icons, {cols}x{rows} cells of {cell} px, {os.path.getsize(art):,} bytes -> {art}")
missing = [i for i in ids if i not in have]
if missing: print("no icon:", " ".join(map(str, missing)))
