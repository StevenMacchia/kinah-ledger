# Accuracy pipeline (tracker 0.8.12, 2026-10-23)

How the tracker learns from every install without reading anything it shouldn't. Backend side: `kinah-ledger-site`,
branch `backend/accuracy`, `backend/README.md` § "Accuracy pipeline" and migration `0013_accuracy.sql`.

| Piece | Tracker | Site |
|---|---|---|
| Reading health | `Session/Accuracy.cs` counts reading time, chat reads, unaligned reads, pickups, guessed / held / snapped / unknown names, loot-list-only pickups, chat-not-found time, crafting panel batches and crafts, rare drops and their second reads. Sent after each shared run (`Upload/AccuracyClient.cs`, `POST /v1/accuracy`) with the app version and install ID; a failed send is kept for the next. | `health_day` per version and day; `GET /admin/health` shows versions side by side. |
| Remote config + staged rollout | `Update/RemoteConfig.cs`: OCR scale, text size, reading pace, the loot list band, notice words, a kill switch per reader, the rare-drop switch, the rollout. Fetched at start and every few hours, kept in `%LocalAppData%\KinahLedger\remote-config.json`, every value clamped; without it the built-in defaults are exactly 0.8.11's. settings.json's own values win. A found update installs on its own only when this install's bucket (SHA-256 of install + version, mod 100) is under the rollout's percent and the rollout isn't held; Check now always shows it. | `GET /v1/config`; `POST /admin/rollout` starts a rollout at 10%; each hourly pass compares the new version's health with the last one's: worse → hold, not worse for a day → 50%, then 100%. |
| Misread fixes | The parser and the dictionary report names snapped, guessed or not found (`Accuracy.Name`, item names only, checked by `SafeName`). `data/aliases.json` is loaded into the dictionary as snapped misreads (`ItemDictionary.LoadAliases`). | `misread`; `GET /v1/aliases.json` promotes a read 3+ installs sent to the same item (80%+), never a rare item. `node tools/data/aliases.js` writes `data/aliases.json` and the manifest; the daily data update carries it. |
| Layout profiles | Where the chat was found (fractions of the game window), keyed by window size and text size (`Session/LayoutProfile.cs`), and whether the loot list and panel showed. A new install whose own search finds no chat tries the site's box for its window size, if a read there shows chat lines. | `layout_vote`; `GET /v1/layout` (boxes from 2+ installs). |
| Replay bank | `replay-bank` (`Session/ReplayBank.cs`) runs `tracker/tests/replays`; Options › Sharing › "Send report pictures" (off by default) sends a miscount report's loot list and crafting panel crops, never the chat. | `POST /v1/frames` → R2 `kinah-ledger-reports`; `tools/replays/pull.js` turns new reports into pending cases. |
| Cross-player checks | — | At ingest (`dropcheck.ts`): a drop nobody else sees from that enemy, or far above everyone's rate, is kept off the public tables when it is Common/Rare and queued for review (still counted) when it is Legend/Unique/Epic or boss gear. |

## Rare drops (Steven's rule)

Boss gear and Legend, Unique and Epic drops always count and are logged right (`Session/RareGate.cs`):

- a guess at one is never held back for lack of a loot table, and a world boss fight never sets one aside;
- each is held for one moment while the picture it was read from is read again (larger, boosted; on the loot list
  also by its icon): an exact second read confirms it or names the right item, otherwise it counts as read and is
  tagged unconfirmed;
- a crop of its chat line or loot list row is kept in `proof\` beside the logs (60 days, 100 MB at most).

The item list grades about a fifth of the loot tables' drops as Legend materials. They count as rare under the rule as
written; `"rare": {"legend_materials": false}` in the site's config keeps Legend for gear only.

## Names, loot list rows and anchors (branch tracker/accuracy-v2, 2026-10-24)

- **Candidate sets** (`Data/NameMatch.cs`, `ItemDictionary.Match`): a misread is settled among the enemies' loot tables
  (target bar, recent targets, nameplates), the gathering materials while gathering, the open recipe and the run's items
  before the whole list, with an OCR-aware distance. In the open world, fighting an enemy whose table is known, a misread
  whose only close item is on none of them and over one edit off is refused. reads.log: `is X, not Y: target-table has
  it`, `which no loot table, gathering or recipe nearby has: unsure, not counted`; the audit line counts both.
- **Loot list rows** (`Session/LootRows.cs`): rows are tracked by slot; text is taken once its contrast settles and voted
  over the last three reads. reads.log: `now reads ... one item, the final text`, `went before its text settled ...:
  unsure, not counted`. trace.log list reads carry `y` and `c`.
- **Anchors, shadow only** (`Ocr/Anchors.cs`): once a minute `anchor <region> dx=.. dy=.. conf=..` (or `none`) and the
  check's cost; `anchor: layout not found` / `found again`. Before any region is switched to its anchored crop, the
  log must show, across several of Steven's sessions and at least two window sizes, that region's dx/dy steady to a few
  pixels at conf 0.7 or more, and "layout not found" only while the game is genuinely hidden. Templates for the loot list,
  the crafting panel, the zone strip and the target bar still have to be cut from real frames into `data/anchors/`.

## Item-name decoder, shadow only (branch tracker/name-decoder, 2026-10-24)

Windows OCR returns a string with no per-letter confidence, so every name fix after it works on a guess.
`Ocr/NameDecoder.cs` runs an open recogniser beside it on the same crops and decodes against the item list itself.

- **Model**: PaddleOCR's en_PP-OCRv4 mobile recognition model (Apache-2.0, ONNX export by CnOCR, 7.7 MB) and its
  95-character list, through ONNX Runtime on one CPU thread. Pinned by URL and SHA-256 in `data/models.json` (also listed
  under `models` in `data/manifest.json`); never committed, never built into the exe, never downloaded by the tracker.
  `node tools/data/models.js` fetches both files into the kept data folder (`%LOCALAPPDATA%\KinahLedger\data`);
  `--out data/models` puts them in the repository folder (gitignored) for parse-test and CI. A file is used only when its
  hash matches the pin.
- **Search**: CTC prefix beam search (32 hypotheses) held to a trie of every item name (`ItemDictionary.Lexicon`,
  normalised by `ItemDictionary.Norm`, plus Kina), followed by what the loot list and chat print after a name:
  ` (Bound)`, ` x12` / `x12` / ` x1,240`, a closing `.`. Characters Norm drops (`[ ] * |` ...) may appear anywhere. The
  answer is an item id, the count, and the log-probability of the in-list reading. **Other** when that reading falls more
  than `CtcLexiconSearch.OtherMargin` (ln 1000, about 6.9 nats) below the model's own best reading: one letter the model
  was sure of that no item has there, a cut-off name, or text that isn't an item name.
- **Where it runs**: each loot list item row (the OCR line's box in the list picture, `TrackerSession.ShadowListNames`,
  skipped where the list isn't read) and each chat pickup line's name words (`ScreenReader.NameSpans`,
  `ShadowChatNames`). Crops go to a queue (16 at most, the same text from the same place once per 3 s) and are decoded on
  the decoder's own below-normal thread; the model loads there on first use. Nothing is counted from it.
- **reads.log**: `decoder off (no model)` once when the files are absent (the tracker then runs exactly as before);
  `decoder on: cold start N ms (files checked, model, lexicon ...)` once; `decoder: N ms per line on average over 20
  lines` once; then per decode `decode Raw Leather p=0.98 vs ocr Raw Leathe x3 agree (list)`, `... disagree (chat)`, or
  `decode other p=0.00 vs ocr ... other (model read "...", nearest in the list ...)`. "vs ocr" compares with what OCR plus
  NameMatch resolved for the same name (`ResolveName`).
- **Tallies**: `decode_agree`, `decode_disagree`, `decode_other` in `Accuracy.Shadows()`: on this PC only, not in
  `Accuracy.Keys`, never sent; the `/v1/accuracy` payload is unchanged.
- **Measured** (parse-test, Ryzen 9 7950X3D with the game running): 30 of 30 drawn crops right (three text heights,
  blur, suffixes), three non-item strings all other; cold start 200 to 280 ms (budget 300); **about 14 ms per line**, of
  which the search is under 1 ms (budget 5 ms: missed; two threads give about 11). Fine for a shadow on its own thread;
  before it counts, an int8 copy of the model or batching a read's rows should be measured.

**Promotion rule** (a second PR switches loot list and chat item names to the decoder) when, on labeled rows (Prompt 2's
table, the report crops), all of these hold: the decoder names the right item more often than OCR plus NameMatch does;
it names a wrong item no more often than OCR plus NameMatch does; and its "other" class catches the garbled and cut-off
rows (most labeled garbled rows come out other, and few clean rows do). Until then OCR plus NameMatch decides and the
decoder only logs.

## Commands

```
KinahLedger.exe accuracy-test                      # this pipeline's own checks
KinahLedger.exe replay-bank tracker/tests/replays  # the bank (CI runs it before every release)
KinahLedger.exe replay-bank <dir> --seed           # write the generated cases
KinahLedger.exe replay-bank <dir> --seed-full      # whole-window crafting pictures for craft-test --glob *.png
KinahLedger.exe accuracy-sample <dir>              # the payloads as sent, for checking against the backend
node tools/data/models.js [--out data/models]      # the name decoder's model files, checked by SHA-256 (--check: only check)
```
