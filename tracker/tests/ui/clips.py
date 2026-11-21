"""Turn `ui-shot DIR --parser themes` frames made with KL_MOMENT_SEQ=N into one animated GIF per look and moment.

    KL_THEME_LOOK=prism KL_MOMENT_SEQ=24 hidden-run.ps1 KinahLedger.exe ui-shot OUT --parser themes
    python clips.py OUT [--seconds 1.6] [--scale 2]

Writes OUT/clips/LOOK-MOMENT.gif (played at the moment's length, looped with a beat of rest) and OUT/clips/LOOK-MOMENT-strip.png
(every 4th frame side by side, for a reviewer that reads images). Judge motion from these, never from one still.
"""
import glob, os, re, sys
from PIL import Image

def main():
    out = sys.argv[1]
    secs = float(sys.argv[sys.argv.index('--seconds') + 1]) if '--seconds' in sys.argv else 1.6
    scale = float(sys.argv[sys.argv.index('--scale') + 1]) if '--scale' in sys.argv else 1
    groups = {}
    for f in glob.glob(os.path.join(out, 'ui-theme-*-s[0-9][0-9].png')):
        m = re.match(r'ui-theme-(.+)-s(\d\d)\.png$', os.path.basename(f))
        groups.setdefault(m.group(1), []).append((int(m.group(2)), f))
    os.makedirs(os.path.join(out, 'clips'), exist_ok=True)
    for key, fs in sorted(groups.items()):
        fs.sort()
        ims = [Image.open(f).convert('RGBA') for _, f in fs]
        w = max(i.width for i in ims); h = max(i.height for i in ims)
        frames = []
        for i in ims:
            c = Image.new('RGBA', (w, h), (40, 44, 52, 255)); c.alpha_composite(i)
            if scale != 1: c = c.resize((int(w * scale), int(h * scale)), Image.LANCZOS)
            frames.append(c.convert('RGB'))
        step = max(1, round(secs * 1000 / max(1, len(frames) - 1)))
        dur = [step] * len(frames); dur[-1] = 700
        frames[0].save(os.path.join(out, 'clips', key + '.gif'), save_all=True, append_images=frames[1:], duration=dur, loop=0, optimize=False)
        pick = frames[::4] + ([frames[-1]] if (len(frames) - 1) % 4 else [])
        strip = Image.new('RGB', (sum(p.width for p in pick) + 4 * len(pick), pick[0].height), (20, 20, 20))
        x = 0
        for p in pick: strip.paste(p, (x, 0)); x += p.width + 4
        strip.save(os.path.join(out, 'clips', key + '-strip.png'))
    print(f'{len(groups)} clips in {os.path.join(out, "clips")}')

if __name__ == '__main__':
    main()
