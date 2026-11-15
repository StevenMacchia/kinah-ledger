"""
Pack the Parser's ability icons into one small atlas the tracker carries (tracker Art/skill-icons.png plus data/skill-icons.txt).

    python -I tools/skill-icons.py <folder of <id>.webp / <id>.png> [--cell 32]

The folder holds one icon per id in data/skills.json, fetched by `node tools/skill-fill.js --icons <folder>`: MetaBot's
copy for a MetaBot skill (<id>.webp), the game's own file from NCSoft's game-data CDN for the rest (<id>.png). Only the ids
skills.json names are packed: those are the skills the Parser knows. Ids whose pictures are the same (a spirit's attack at
each rank, the Theostones of one grade) share one cell: its line lists them all. Each icon is scaled to one cell (32 px: crisp at the inspect view's 22 px up to 150% scaling), laid out
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
def path_of(i):
    for ext in ("webp", "png"):
        p = os.path.join(src, f"{i}.{ext}")
        if os.path.exists(p): return p
    return None

have = [i for i in ids if path_of(i)]
cells, by_pixels = [], {}   # each cell's ids; a picture's cell by its pixels at cell size
pics = []
for i in have:
    im = Image.open(path_of(i)).convert("RGBA").resize((cell, cell), Image.LANCZOS)
    key = im.tobytes()
    if key in by_pixels: cells[by_pixels[key]].append(i); continue
    by_pixels[key] = len(cells); cells.append([i]); pics.append(im)
cols = 16
rows = math.ceil(len(cells) / cols)
sheet = Image.new("RGBA", (cols * cell, rows * cell), (0, 0, 0, 0))
for n, im in enumerate(pics):
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
    f.write(f"# the ability icons in Art/skill-icons.png (tools/skill-icons.py): cell size, columns, then each cell's skill ids in order (ids on one line share the picture)\n{cell}\n{cols}\n")
    f.write("\n".join(" ".join(str(i) for i in c) for c in cells) + "\n")
print(f"{len(have)} of {len(ids)} ids have an icon, {len(cells)} pictures, {cols}x{rows} cells of {cell} px, {os.path.getsize(art):,} bytes -> {art}")
missing = [i for i in ids if i not in have]
if missing: print("no icon:", " ".join(map(str, missing)))
