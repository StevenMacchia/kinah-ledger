"""Which renders changed between two ui-shot sweeps, and where (tools/ui-review.md).

    python -I tools/ui-diff.py <before-dir> <after-dir> [--only events-]

Every PNG in either folder is listed: same (identical bytes), only in one folder, or differs with the count of pixels that
changed and the box they lie in (x0,y0-x1,y1, the PNG's pixels). Standard library only: the PNGs the tracker writes are
8-bit RGB or RGBA, not interlaced. With --only, the exit code is 1 when a file whose name doesn't start with the prefix
differs or is missing on one side: a change to one page must leave the other pages' renders byte for byte the same.
"""
import hashlib
import os
import struct
import sys
import zlib


def read_png(path):
    with open(path, "rb") as f:
        data = f.read()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError(f"{path}: not a PNG")
    pos = 8
    idat = []
    width = height = None
    channels = 0
    while pos < len(data):
        length, kind = struct.unpack(">I4s", data[pos:pos + 8])
        body = data[pos + 8:pos + 8 + length]
        pos += 12 + length
        if kind == b"IHDR":
            width, height, depth, ctype, _, _, interlace = struct.unpack(">IIBBBBB", body)
            if depth != 8 or interlace != 0 or ctype not in (2, 6):
                raise ValueError(f"{path}: {depth}-bit colour type {ctype} interlace {interlace} is not handled")
            channels = 3 if ctype == 2 else 4
        elif kind == b"IDAT":
            idat.append(body)
        elif kind == b"IEND":
            break
    raw = zlib.decompress(b"".join(idat))
    stride = width * channels
    rows = []
    prev = bytearray(stride)
    at = 0
    for _ in range(height):
        ftype = raw[at]
        line = bytearray(raw[at + 1:at + 1 + stride])
        at += 1 + stride
        if ftype == 1:
            for i in range(channels, stride):
                line[i] = (line[i] + line[i - channels]) & 0xFF
        elif ftype == 2:
            for i in range(stride):
                line[i] = (line[i] + prev[i]) & 0xFF
        elif ftype == 3:
            for i in range(stride):
                left = line[i - channels] if i >= channels else 0
                line[i] = (line[i] + ((left + prev[i]) >> 1)) & 0xFF
        elif ftype == 4:
            for i in range(stride):
                a = line[i - channels] if i >= channels else 0
                b = prev[i]
                c = prev[i - channels] if i >= channels else 0
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                pred = a if pa <= pb and pa <= pc else b if pb <= pc else c
                line[i] = (line[i] + pred) & 0xFF
        elif ftype != 0:
            raise ValueError(f"{path}: filter {ftype}")
        rows.append(bytes(line))
        prev = line
    return width, height, channels, rows


def pixel_diff(a, b):
    wa, ha, ca, ra = read_png(a)
    wb, hb, cb, rb = read_png(b)
    if (wa, ha, ca) != (wb, hb, cb):
        return f"size {wa}x{ha} vs {wb}x{hb}"
    n = 0
    x0 = y0 = 1 << 30
    x1 = y1 = -1
    for y in range(ha):
        if ra[y] == rb[y]:
            continue
        la, lb = ra[y], rb[y]
        for x in range(wa):
            o = x * ca
            if la[o:o + ca] != lb[o:o + ca]:
                n += 1
                if x < x0: x0 = x
                if x > x1: x1 = x
                if y < y0: y0 = y
                if y > y1: y1 = y
    return f"{n} pixels in {x0},{y0}-{x1},{y1}" if n else "same pixels (bytes differ)"


def sha(path):
    with open(path, "rb") as f:
        return hashlib.sha256(f.read()).hexdigest()


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    before, after = argv[0], argv[1]
    only = argv[argv.index("--only") + 1] if "--only" in argv else None
    names = sorted({f for f in os.listdir(before) if f.endswith(".png")} | {f for f in os.listdir(after) if f.endswith(".png")})
    changed = []
    for name in names:
        pa, pb = os.path.join(before, name), os.path.join(after, name)
        if not os.path.exists(pa):
            line = "only in after"
        elif not os.path.exists(pb):
            line = "only in before"
        elif sha(pa) == sha(pb):
            line = "same"
        else:
            line = "differs: " + pixel_diff(pa, pb)
        if line != "same":
            changed.append(name)
        print(f"{name:40} {line}")
    outside = [n for n in changed if only and not n.startswith(only)]
    print(f"{len(names)} renders, {len(changed)} changed" + (f", {len(outside)} outside {only}*" if only else ""))
    return 1 if outside else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
