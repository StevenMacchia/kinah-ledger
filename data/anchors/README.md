# Layout anchors

Templates the tracker looks for to find the game's interface (`tracker/KinahLedger.Tracker/Ocr/Anchors.cs`), built into
the exe. Phase one is shadow only: the anchored crops are written to reads.log beside the fixed-fraction ones
(`anchor <region> dx=.. dy=.. conf=..`), and nothing read changes.

Each anchor is two files:

- `NAME.png`: a small crop of an interface piece that never changes (the Kinah coin icon, a frame corner), cut from a
  full game frame at the size it shows there.
- `NAME.json`: `{"region": "loot", "where": [x, y, w, h], "ref_h": 2160, "search": 0.05}`: the region whose crop follows
  it (balance, chat, loot, craft, zone, target), where the crop sat in that frame as fractions of the game window, the
  frame's height in pixels, and how far around that place to look, as a fraction of the height.

Cut templates only from frames that show nothing but the interface piece (no chat, no names).
