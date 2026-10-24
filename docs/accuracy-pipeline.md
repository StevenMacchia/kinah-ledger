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

## Commands

```
KinahLedger.exe accuracy-test                      # this pipeline's own checks
KinahLedger.exe replay-bank tracker/tests/replays  # the bank (CI runs it before every release)
KinahLedger.exe replay-bank <dir> --seed           # write the generated cases
KinahLedger.exe replay-bank <dir> --seed-full      # whole-window crafting pictures for craft-test --glob *.png
KinahLedger.exe accuracy-sample <dir>              # the payloads as sent, for checking against the backend
```
