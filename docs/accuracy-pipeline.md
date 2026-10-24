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
- **Rows as a stack** (branch tracker/loot-rows-v2, 2026-10-24, from 10 fps footage in `kinah-bench`): a new row takes
  the bottom slot and pushes every older row up 73 px; nothing moves down; a row is up 2.5 s; long names scroll for the
  row's whole life. `LootRows` matches each read to the live rows in order from the bottom (a small cost alignment:
  shifts up to four slots, never down), by icon hash and name family, not by y. Slices of one name are one row and never
  "the final text"; with an icon, a slice that is part of exactly one item's name names it. A soul row passes as
  `Soul: Kind (Bound)` on its slices too. Read once and gone, or up over 6 s: not a row (`was read 1 time(s) and went`
  only when the read before it showed and the first read it was missing from are under 2.5 s apart and it had no icon,
  since PR "static text in the loot band": a pass can take over a second, so a real row can be read once;
  `has been up 7.0 s ... ignored`, once per text per minute; the cross-check stops waiting for it). Covered by a window
  (`Reset`) and back within 12 s: the same row (`is the row the list showed before it was covered`). trace.log list
  reads now carry `h` (icon hash); the bank has a `cover` read.
- **Chat latency**: the chat line counts on its first read when the loot list shows the same pickup with an icon
  (`ChatVoter.Confirmed`, remote kill switch `chat_fast`); `replay-bank` prints the vote's first-read-to-counted times
  (`--latency` adds the same bank with it off). A chat area kept from an earlier session (`chat not visible at start`)
  is looked for again every 30 s until its input box is found (`chat found on screen at ...`): on 2026-10-24 the kept
  area's bottom sat three lines above the newest line, which was the 2.3 to 9.4 s lag.
- **Anchors, shadow only** (`Ocr/Anchors.cs`): once a minute `anchor <region> dx=.. dy=.. conf=..` (or `none`) and the
  check's cost; `anchor: layout not found` / `found again`. Before any region is switched to its anchored crop, the
  log must show, across several of Steven's sessions and at least two window sizes, that region's dx/dy steady to a few
  pixels at conf 0.7 or more, and "layout not found" only while the game is genuinely hidden. Templates for the loot list,
  the crafting panel, the zone strip and the target bar still have to be cut from real frames into `data/anchors/`.
- **Anchors, phase two for the chat and the Kinah strip** (`AnchoredCrop` in `Ocr/Anchors.cs`, 2026-10-24): once the
  anchor has been seen 3 checks running with every edge within 4 px, in the same game window, at or above the
  confidence floor (strip 0.25, chat 0.4), the Kinah strip is read around its numbers (3 number heights either side,
  half a height above and below, inside the fixed strip: 326x73 of 1296x97 on 5120x2160) and the chat's re-find looks
  around its box (a line up, 2 left, 3 right, 3 down for the input row, inside the quarter: 1074x672 of 2560x1080). The
  chat read itself was already the found box (864x504), not the quarter. Back to the fixed crop at once when a strip read
  shows no numbers, the chat shows no lines for 30 s or isn't found around its anchor, the anchor moves more than 4 px,
  its confidence falls below the lock's, the game window changes or the switch is off. One line each way:
  `anchor balance: reading around the anchor now ...` / `anchor balance: back to the fixed crop (...)`; the minute's
  `anchor:` line says which are anchored. Switches `layout.anchor.chat`, `layout.anchor.balance`, on in code
  (`"layout": {"anchor": {"chat": false}}` turns one off). The shadow dx/dy lines are unchanged.
- **Pass time per region** (`Session/PassTimes.cs`): the audit line's pass part reads `57 passes, 1016 ms each (per pass:
  chat 310 · list 180 · target · balance · craft · grid · nameplates · header · anchors · scene · decoder ms)`, each the
  region's time over the minute divided by the passes. The list runs beside the chat, so they add up to more than the
  pass; balance (the watcher's 10 s timer) and decoder (its own thread) are off the pass. The chat read skips recognition
  when its boosted pixels are exactly the last read's (`ocr.chat_skip_same`, on in code): the audit ends `chat read at
  2x, N of M reads unchanged and not recognized again`.
- **OCR sizing at high resolution** (2026-10-24, `bench --sizing`, offline, drawn 5120x2160 pictures; `--chat dir` and
  `--frames dir` add real crops and frames). `ocr.target_sized` (on in code): the target band (1740x237 on 5120x2160)
  is read around its own text, the gold pixels for the name-colour read and the dark pixels of the boosted band for the
  brightness read, with an eighth of the band's height as margin (`TargetReader.InkBox`; no such pixels: no recognition),
  at 3x until five words are measured and for text under 18 px, else 2x (`TextScale` with `Least = 2`); 48 drawn bands
  read every enemy right at 1.5x and 2x and missed one at 1.25x. Bench: about 275 ms to about 100 ms a read, same enemy.
  Log: `target bar: its text is N px tall on this screen: now read at 2x (was 3x)`. `ocr.plates_half` (on in code):
  the nameplate bars are found on every second pixel of every second row of the capture's own 32-bit pixels (no 24-bit
  copy), then each place is scanned again at full size in a window that grows until no red run crosses it, so the bars
  are the full scan's own (parse-test: 2 drawn frames and 40 random frames at four sizes, 1,673 bars, all identical);
  about 20 ms to 3 ms. `ocr.chat_scale_to_target` (OFF in code): the chat aimed at 36 px with the floor only under
  18 px (1.25x for 29 px text). On 72 drawn chats at 29 px it read 193 of 240 lines exactly against 218 at 2x, so the
  chat keeps the 2x floor; `"ocr": {"chat_scale_to_target": true}` turns it on once real crops show it holds.

## Lines in the loot list's band that aren't rows (branch tracker/loot-crop, 2026-10-24)

The band sits beside the character, so the world's own text lands in it. Steven's 5120x2160 reads.log, 35 minutes:
157 lines refused only as "went before its text settled" (27x "Vangi", 21x "Vang", 11x "Vangt": the nameplate
"Vanguard of Atreia" next to the character; "Kraka Footpath hmpsite": the campsite's name; "Withered Fairy"), and in
trace.log many more that settled (899 reads of "Vangi" at a median contrast of 0.54, a real row's level; the campsite's
name settled and "changed in place" over and over). `Session/LootNotRow.cs` drops them in `ProcessList`, after the
gathering panel's cut and before `LootRows.Feed`; the trace keeps them, so a replay drops them the same way.

- **(a) a name on screen**: by NameMatch's OCR-aware distance, the text is a nameplate (`Engaged`) or the target bar's
  name (an enemy on the list, or the last unlisted read of 8+ letters) seen in the last 10 s, also as a prefix (cut by
  the band's edge: "Vangt"); the zone, or one read in the last 2 min, also with more after it ("Kraka Footpath
  Campsite" in Kraka Footpath); or any zone on the list read whole.
- **(b) no icon**: the whole place an icon could be (3.2 to 0.15 line heights left of the text, 2.4 tall; it covers
  every size and gap PopupIcons tries, since the icon's exact place isn't measured on real frames) is flat: spread
  (standard deviation of a 12x8 grid of cell means) at most 0.05 and under 0.15 of the text's contrast, so a row
  fading in, whose icon fades with it, keeps its icon. The 8x8 icon hash being 1 is the same verdict on a smaller square
  and isn't used on its own.
- **(c) not on the dark strip**: background (the darker 30% of the line's box, what is behind the letters) at least
  0.22 above the strip's and 1.8 times it. The strip's is what sure rows in the same read sit on (Kinah, "(Bound)", a
  count of 2+), else the same over the last 30 s, else only a background of 0.6 or more counts. Only for text at
  contrast 0.35 or more: a row fading in fades with its strip.
- **Never dropped**: Kinah, soul and shop rows, "(Bound)" (or "(B" read badly), anything opening with "<", a count of
  2+, an item's own name, and a line the picture vouches for (spread 0.08+ on a background within 0.08 of the strip's):
  an enemy's soul row cut at the left reads as the enemy's name.
- **reads.log**: `loot list: "Vangi" is a nameplate|a zone|not on the list: ignored (<why>)`, once per text per
  minute; each such line adds one to `list_notrow` (Accuracy, on this PC; not in `Accuracy.Keys`, the payload is
  unchanged). trace.log list reads now also carry `b` (background) and `s` (spread) per row.
- **Measured on Steven's trace** (14:36 to 15:22 UTC, text only, nameplates from reads.log's `fighting:` lines): 93
  logged drops, every one a nameplate or the campsite's name, and unsure rows 62 to 55. "Vangi" itself isn't caught by
  rule (a) that session (the target bar never read the Vanguard, and `Engaged` holds only enemies with a health bar);
  rules (b) and (c) need the picture's measures, which trace.log only keeps from this version on. The thresholds are
  set on drawn frames: check them against Steven's next trace.log (`b`, `s` beside each `Vangi` and each real row).
- **The band at 5120x2160** (y from trace.log, cy = 1080): rows sit in four slots at y 979, 1050, 1123, 1197 (73 px,
  0.034h, apart), so the list runs from about cy-0.047h to cy+0.072h; the band is cy-0.06h to cy+0.30h (950 to 1728),
  over three times that, and its lower part holds the gathering panel (1512 to 1693), which the band's reader needs for
  its cut. The nameplate (950 to 997) overlaps the top slot and the campsite's name (1096 to 1116) sits between slots
  two and three, so no crop separates them from rows; a fifth slot would be at about 906, above the band, and wasn't
  seen. Left and right can't be checked from the logs (no x). Geometry is the anchor work's, not changed here.

### Static text and the title (branch tracker/title-drop, 2026-10-24)

On main eee36b2 (13:10 to 13:15) the player's own title, "Vanguard of Atreia", the green line of the player's nameplate
at screen centre (x about 2437 to 2667, y 955 to 991 at 5120x2160: the fourth slot's height, 520 px right of where
rows' text starts), was read as "Vangi", "Vangt", "Vqng(", "Yang(": 31 one-read objects went unsure in 4.5 minutes.
(a) needs its name seen; (b) and (c) need the measures, and over the world they vary. From trace.log 12:47 to 13:25
(1,209 title reads, 131 real Kina, Sapphire Gemstone, Orichalcum Ore and soul rows):

| | y | contrast c | background b | spread s | s/c |
|---|---|---|---|---|---|
| title | 950 to 1002, median 974 | 0.01 to 0.66, median 0.54 | 0.01 to 0.75, median 0.10 | 0.001 to 0.32, median 0.028, p90 0.105 | median 0.064 |
| real item rows | 1093 to 1197 | 0.28 to 0.69 | 0.05 to 0.25 | 0.112 to 0.199 | 0.23 or more |
| Kina rows | 1053 to 1209 | 0.03 to 0.70 | 0.03 to 0.28 | 0.197 to 0.294 | 0.38 or more |

The icon hash `h` doesn't separate them (the title's hashes are as busy as icons; 45 of 1,209 are 0 or 1).

- **(b) and (c) tuned**: (b) at spread 0.05 and 0.15 of the contrast takes half the title's reads and no real row
  (margins 2.2x over the least real spread, 1.5x on the ratio); (c) at 0.22 above the strip (about 0.34) stays 0.05 over
  the most background a real row showed with its text at 0.35 or more; the vouch moves to 0.08, above (b)'s bound.
- **(d) static text**, no thresholds: text with no icon of its own (hash 0 or 1, or spread under 0.08) or whose name
  the item list doesn't know, read at one y (within half a slot pitch) three times or more over more than 6 s,
  whatever its text, makes that y a static band (held while such reads keep coming within 30 s, forgotten after 60 s).
  Later lines there are dropped at once, after (a) to (c), which keep their own verdicts and log lines;
  `loot list: static text at y 974: ignored (...)` once a minute per band, one `list_notrow` each. Guards: a line
  that was a slot or more lower in the read before with the same text has moved up into the band (a row); where real
  rows sat in the last 2 min (the list's slots), only a line with no icon of its own goes. The pitch is the smallest
  gap between two real rows in one read (73 px here), never the title-to-row gap.
- **(e) position**: text starting more than 5.5 slot pitches right of the rows' text column (the median of where sure
  rows' text starts) is not a row: the title starts about 7 pitches right. trace.log list reads now carry `x` (each
  line's left) so a replay applies it; older traces don't, and (e) waits for a column.
- **Gathering panel**: its parse leaves out lines already known not to be rows (static text, a nameplate), and its
  cut uses the panel's own spacing (`LootPopup.PanelCut`: the smallest gap from the line above its first label down,
  walking up while lines stay within 1.6 of it) instead of the read's median gap, which with the title and a row
  above it (223 to 424 px) reached up over the list's rows: "Vangt | Sapphire Gemstone | P rofes | Sapphire Gemstone
  | Success" cut the real row at 1197 with the panel (12 real row reads cut that way in the trace, 0 with the new cut).
- **A row's icon hash may move**: the same item read again in its own slot within 2.5 s is the same row even when its
  hash is 20+ bits off (a fading read): "Sapphire Gemstone x1" then "•Sapphire Gemstohé x1" 1.3 s later (33 bits) had
  been split into two one-read objects, one refused as read once, one as unsure.
- **Measured** (the trace replayed, chat replaced by filler, no `x`): unsure 455 to 149, of them title reads 236 to 26
  (each after a gap of over 30 s, until three reads over 6 s again), `read 1 time(s)` lines 88 to 0, and no
  title line in a panel note. Bank case `text-2026-10-24-title-static-band`.

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

**Live tally, 2026-10-24 (Steven's reads.log, build main eee36b2, chat lines):** across the day the decoder agreed with
OCR plus NameMatch 97 times and named a wrong item 0 times. It disagreed 7 times in the 13:10 to 13:15 farming log
(run-20261024-131029), every one a garbled "Kina" word ("Kinå.æe", "Kipa-", "King.", "Kirfy—", "Kin%.", "Kinta+tt;$t.ds",
the loot list's "Kinæx382"), and the footage shows it was right all 7 times. In the 13:07 reward burst it settled
Forgotten Gloves over "Forgotteh GCoves" and Riverside Pauldrons over "RiveTSi4e" with OCR, and its two "disagree" lines
there were OCR carrying a count ("Refining Stonevx6.") or garbage (p 0.07). So on chat item names the first two
conditions hold on what has been labeled (more right than OCR plus NameMatch, never a wrong item). **What is left before
chat item names switch to the decoder:** the third condition, measured rather than assumed: a labeled set of garbled and
cut-off chat lines (Prompt 2's table and the report crops hold loot list rows, not chat lines) showing that "other"
catches them and few clean lines; and the per-line time on Steven's PC under the 5 ms budget (14 ms measured) or the
decode kept off the counting path. Kina words are the first candidates: 7 of 7 right where OCR garbled them. Since this
branch the decoder also reads the count after a chat line's name (the crop runs to the line's end, past it when OCR read
no count; the search's count accepts the digit slips below) and logs `qty xN vs ocr xM agree|disagree` (tallies
`decode_qty_agree` / `decode_qty_disagree`, this PC only); its counts wait for the same rule.

## Chat counts, repeated lines and the Kinah counter as Kina's witness (branch tracker/chat-qty, 2026-10-24)

Scored against footage (kinah-bench `compare-130149.md`, `compare-131029.md`): the 13:07 Monster Cube rewards filed
"Acquired Odyle x7." as x1 and lost the second of two "Riverside Pauldrons acquired." lines; the 13:10 farming run missed
four Kina lines (382, 665, 293, 508) and counted three twice (408, 189, 344) while the Kinah counter rose by exactly the
footage's 15,660.

- **Counts** (`ChatParser.QuantityTail`, `Parse`): a count the recognizer garbled is read through its digit slips (O/o 0,
  l/I/| 1, S/s 5, b 6, T/t 7; "Refining StonevxT" x7, "Kinæx382" x382); letters alone must follow the x directly, so a
  numeral after a space ("Field Boss I", "Treasure Box II") is never a count. A verb-first line whose name is settled but
  whose count is gone ("Acquired Odyle") takes the count of the loot list's row for the same item read with its icon
  within 3 s (`TrackerSession.ListCount`, tally `qty_from_list`); else it counts one for now and is watched
  (`Reread.CountWatch`): its place in the next three reads (moved up by new lines, one either side), and when the same
  item shows a count there the difference is added (`chat · count read again`, `qty_recovered`). Nothing found: reads.log
  `"Acquired Odyle": quantity unsure, 1 assumed (...)` and `qty_assumed`. A name-first line ("Odyle acquired.") is one.
  The three tallies stay on this PC (not in `Accuracy.Keys`).
- **Glued and garbled verbs** (`CleanSystemLine`): "Pauldröns:acquired." (a colon or semicolon before the verb) and
  "acquited" (t for r) are the verb. The settle (`Reread.Settled`) takes a glued verb ("Pauldtonsacquired.") as whole
  once the line is put back in shape. Burst lines that still parse to nothing are looked at in place again (`Reread.Take`).
- **Repeats are by place, not by text.** Two identical lines are a repeat only where the alignment puts them on the same
  screen line: `LineTracker.Align` lining up on a run of one line repeated counts the copies past the last read's run as
  new (a stray line between copies is no new copy); `Reread.Consume` lets each counted line stand for one line on screen
  in the settle (one counted Pauldrons line no longer hides a second); a loot line a read lost at its bottom and the next
  read shows in the same place is the same line (`LineTracker.Back`: 189 and 344); below the overlap of a read that didn't
  line up, the last read's own line read differently ("cquihed 408 Kina") is still that line, and a line counted in the
  last 10 s is not counted again there (`RecountSeconds`). A fragment read while its line faded in ("3 Kina.", "d 382
  Kina.", "508 Kina.") takes the whole line that then shows in its place (`ChatVoter.Fragments`). A loot line first shown
  by a read that couldn't be lined up and read the same in three reads, which nothing counted, counts (`chat · unaligned`:
  the 665 line). "chat repeats skipped" (the loot list's Kina rule) is unchanged.
- **The Kinah counter decides Kina** (`Session/KinaWitness.cs`). While the counter is read and trusted (a reading at
  most 15 s old; remote switch `kina_witness`), a Kina chat line waits for the counter: it counts when the counter rose by
  its amount, or by the sum of a burst; once two readings after it show no room for it, it is refused (a re-read, a line
  pushed up by a scroll, a misread amount such as "2367" for 236: `<line>: the Kinah counter didn't rise by N for it ...`).
  A suspected repeat (its amount confirmed in the last 15 s) counts only on a rise of exactly its amount. A rise no line
  claims, quiet for 8 s, counts: named after the uncounted Kina lines the chat showed when their amounts make it up
  (`chat · unaligned · counter`, "never counted (garbled, or in a read that didn't line up) ... counted (unaligned)"),
  else as Kina from the counter (`counter`, or `loot list · counter` when the list showed that amount once), at most
  5,000 at a time (more is a sale, mail or a claim: logged only). A late line for a rise already counted that way is that
  rise's line, once. Bound and payment Kina explain their own rise. A fall (Kinah spent) can't be judged: waiting lines
  count as read and the pool starts again; a counter gone quiet does the same. Without a trusted counter nothing changes.
- **Why the counter never compared** (every audit said "agreed 0×"): the strip's 2nd and 3rd figures both rise with every
  pickup, so the counter finder (`BalanceTracker`) never had a single winner and never locked. A tie at three votes or
  more now takes the rightmost, the one that doesn't fall on a spend (kinah-bench `kinah-counter.md`); a value read the
  same twice running is trusted (the count lags the counter while lines wait); the strip is read every 3 s instead of 10
  (`GameWatcher.BalanceEverySec`); each reading reaches the check at its own time, so a stale one never passes for a new
  one. Kill evidence now joins either side within 3 s (`SessionEngine.KillEvidence`), since a Kina line can be fed after
  a later kill's Soul Points line.
- **Bank:** `text-2026-10-24-farm-kina-counter` (the 131029 trace, player names shifted, with the counter's readings
  written from the footage every 2.5 s: 15,660 Kina, 52 kills, the 9 gathers and 3 items right; main eee36b2 counts
  17,120 in the same replay, and live it filed 15,121, 14,753 after its 368 skip), `text-2026-10-24-chat-repeat-two-pickups`
  (two Pauldrons, two; main one) and `-reread` (the same screen garbled, one). Without the counter's readings the same trace
  now counts 18,027: every line-level miss and double above is fixed, and what is left is the misread "2367" that only the
  counter can refuse. The counter's live behaviour (the lock, the 3 s reads, readings during busy farming) has not yet
  been checked on Steven's PC: read the next farming log's `the Kinah counter ...` lines against the footage.

## Scene state: every reader gated by what is on screen (branch tracker/scene-state, 2026-10-24)

`Session/SceneState.cs` decides once per pass what the game window shows, before any reader runs, and
`TrackerSession.Loop` asks it before each reader. Every reader's own exclusion (loot list off while crafting, reward
screens, the crafting panel and window) stays in place: the gate only ever adds to them.

**Inputs, all cheap.** The readers' own signs (`SceneSignals`): the crafting panel's locator (its Success and Fail
labels, and its title saying crafting rather than a gathering bar), the crafting window's tabs or "acquired designs"
box in the last 5 s, the Market reader's table, a reward window's title, "Reward collected." over a reward table, an
event's reward screen, and the two free anchors (the Kinah counter's numbers in the last 25 s, the chat box showing
lines in the last 30 s). Plus one 256 px thumbnail of the game window (`Ocr/FrameThumb.cs`: sampled straight out of
window capture's newest frame, else shrunk from the screen by GDI only while the game is in front) and its measures
(`FrameStats`): brightness, near-black share, red share, black bands top and bottom, edge density where the counter,
minimap and skill bar sit, flatness of the right side and the upper left.

**States and rules, first match wins.**

| State | Decided by | Gates by default |
|---|---|---|
| EventCards | the event reward screen's title (RewardGridDue) | yes |
| Reward | "Reward collected." over a reward table, or a pass, shop or reward window's title | yes |
| Market | the Market reader has its table | yes |
| Crafting | the crafting panel's labels with a crafting title, or the crafting window's signs | yes |
| Cutscene | top and bottom bands 97%+ near-black, the middle lit | no, log-only |
| Loading | 85%+ of the frame near-black and nothing where the counter and minimap sit | no, log-only |
| Death | half the frame clearly red, the frame dark | no, log-only |
| ChatExpanded | the HUD up, and a flat dark panel with text rows above the usual chat box, darker than the frame | no, log-only |
| Bag | the HUD up, and a flat dark panel on the right, darker than the frame | no, log-only |
| World | the HUD up (an anchor, or HUD edges in 2 of 3 places); also any frame before the HUD has been seen once this session | always (it only keeps the craft panel reader off) |
| Unknown | none of the above, after the HUD has been seen | only right after a gating state (below) |

A reader's sign comes before any picture rule, so a dark crafting window is Crafting, never Loading. `SceneState.Model`
is the hook where a small image classifier can replace the picture rules (taken at 0.9 confidence or more, never over a
reader's sign); null today.

**Which states gate (`scene.gate.states`).** Only states read from a locator or a reader's own sign gate by default:
Crafting, Market, Reward, EventCards (and World). The picture-only states (Bag, Loading, Death, Cutscene,
ChatExpanded) are still classified, explained and counted in the minute line, but gate nothing and hold nothing:
every reader runs in them as it does without the scene, and entering one logs `scene <state> (log-only) since <t>`.
Turn-on rule: a picture-only state may be added to the list only after Steven's frames show no false positives for it
over two sessions (read its `(log-only)` lines against what was on screen). The site adds it with
`"scene": {"gate": {"states": ["Crafting", "Market", "Reward", "EventCards", "Loading"]}}`; unknown names are ignored.

**Hysteresis.** A state takes over only when seen on two passes in a row; a one-pass flicker never changes it. The gate
uses the stricter of the settled state and this pass's own verdict, so entering a stricter scene gates at once and
leaving it waits for the new one to settle. `Explain()` says which features decided.

**Per-reader validity** (`SceneState.Table`; `+` runs, `-` skipped, `h` read and held). A column applies only while its
state gates; a log-only state runs every reader:

| Reader | World | Bag | Crafting | Market | Reward | EventCards | Loading | Death | Cutscene | ChatExpanded | Unknown |
|---|---|---|---|---|---|---|---|---|---|---|---|
| chat | + | + | + | + | + | + | - | + | - | + | h |
| loot_list | + | + | - | - | - | - | - | - | - | - | h |
| craft_panel (crafts from the panel's race) | - | - | + | - | - | - | - | - | - | - | - |
| gather_bar (the same bars on a node) | + | + | - | - | - | - | - | - | - | + | + |
| nameplates | + | + | - | - | - | - | - | - | - | + | - |
| reward_grid | + | + | - | - | + | + | + | + | + | + | + |
| header (event header) | + | + | - | - | - | + | - | - | - | + | + |
| target (a detector) | + | + | + | + | + | + | + | + | + | + | + |
| zone (a detector) | + | + | + | + | + | + | + | + | + | + | + |

Not gated: the crafting panel's locator (it is a sign), anchors, reward pictures, and in the watcher the Market reader
and the Kinah counter (themselves the Market's and the HUD's witnesses). The reward grid stays on everywhere but Crafting
and Market (which a run never shows) because its own "Reward collected." title is its evidence: a table frame a
picture rule misjudges (dark, letterboxed, red) must never lose a run's rewards.

**Unknown.** Only when the settled state before it was a gating state (Crafting, Market, Reward, EventCards or one
added to the list; not World, not a log-only state), chat and loot list reads taken while the scene is Unknown are held (lines only, never pictures; at most
20 s and 60 reads). When the scene settles, each is counted at the time it was read if its reader is valid in the
settled state, else dropped; one log line either way. Otherwise Unknown gates nothing. A rare drop released from hold counts without its second read
(tagged unconfirmed).

**reads.log.** `scene <state> since <t>; skipped <reader,...>; held <...> (<why>)` once per change into a gating state,
`scene <state> (log-only) since <t>; gates nothing (<why>)` into any other;
`scene <state>: N read(s) held while Unknown released (...); M dropped (...)`; once a minute
`scene: N passes, classifier X ms each, picture Y ms each, K one-pass flicker(s) ignored; <state> since <t>`.

**Switch.** Remote config `"scene": {"gate": false}` (or `{"gate": {"on": false}}`) turns all gating off; the state is still worked out and logged (the
change line then says what it would skip).

**Parity.** Until a first frame is seen nothing is gated (`SceneState.HasFrame`). The replay bank's cases never give
a whole frame (text traces, and crops of the loot list, the panel or the table), so their reads go through the gate
(`ChatGated`, `ListGated`, `LootGated`) and all run, exactly as before: every case (18 at the 0.8.26 merge), the same JSON, gate on or off.

**Not yet seen on real frames.** Cutscene, Death, Loading, EventCards (picture side), Bag and ChatExpanded are drawn
synthetically in parse-test only, which is why the picture-only ones are log-only until the turn-on rule above is
met. A wrong rule is fixed in `SceneState.Classify`; all gating can be switched off for everyone with `scene.gate`.

## Event reward cards, read from the picture (branch tracker/event-cards, 2026-10-24)

Steven's rule: event loot comes from the game's reward picture, never the chat. Until now the Shugo Festival's card
screen was only kept as pictures and the run's rewards stayed "not read". `Ocr/EventCards.cs` reads it.

- **The screen** (Hidden Lugi, 17 pictures at 5120x2160 and the 10 fps footage in kinah-bench): five tall cards side by
  side, centred. Received: bright blue frame, a card icon and a cyan "1" top right. Not received: dim gold frame, neither.
  Turned face up ("Rewards have been sent to your cube."), each card is a list of reward slots, three to a row, the
  expedition table's kind of slot (icon on its grade's backing, count lower right, none for one piece of gear), in a
  window 4.8 rows tall that the player can scroll. The cards print no item names. The pick before it ("Obtain rewards by
  clicking the Selection Complete button.", cards face down) is never read.
- **Geometry** at 2160 high, x from the screen's centre: card 0's left -1355.25, pitch 556.5, width 484, top 616; slots
  133x135 at 32 px in, 144 apart across and 146 down; the window from y 782 to 1482, the first row's top at 787
  unscrolled. Each frame's sides and top are found within 12 px of where they belong.
- **Received or not**: the top edge's colour (near white, or gold with R-B of 30 or more) and the cyan "1" (about 270 px,
  or none) must agree on every card, or the read is not recognised.
- **Rows**: the first row's top is where the slots' top and bottom edges line up across their whole width (lower quartile
  along each line; the mean found lines inside the icons). A slot is there when its sides stand out from the card (lower
  quartile 12 or more; slots 20 to 108, empty places 0 to 3). A slot the window cuts is kept with no count and no icons,
  never read from half a picture. The list's end shows when an empty place after the last slot is the card's own blue
  backing; a grey place (a tooltip over the list) or a slot after a gap marks the read covered.
- **Each slot** is read by `RewardGrid.ReadSlotAsync` (the expedition table's per-slot reader, moved out unchanged):
  grade, count, closest icons, named by `GradedPick` with the sure distance (70). The second witness is the slot's
  backing grade (the icon's items must hold that grade); the cards have no name text to read. Four plain figures or more
  is a misread (the cards write 1,000 and up as "1.1K"): the Abyss Points coin's ring made "440" read "3440", so the
  figures' own width on the count's lower half keeps the last three; otherwise the count isn't read. A sure icon that
  holds only gear is one piece (figures read on gear art, "x11", are dropped).
- **Votes**: read once a second while the screen shows (a cache keyed on each slot's exact pixels skips text recognition
  for a slot already read). Each card's read is placed in its list: unscrolled at the top; scrolled, at the one whole-row
  offset where at least two sure slots agree and none clashes, else it is left out. Each place is voted like the table
  (sure reads only). Filed through `ApplyGrid` once the last two reads agree and every list was seen to its end, else
  when the screen has been gone 2.5 s, the event is left, after 12 reads or after 20 s. Once per screen.
- **Filing**: every received card's list in order, as the run's rewards (`Instances.RewardTable`, so they upload and
  show on the profile: Abyss Points, tokens, consumables, bound items alike). A slot not named surely (a gear icon two
  items share, a count not read, a slot only ever seen cut) is an unknown item ("Common x? · picture") and its crop goes
  to `proof\` (`...-event-cardN-slotM-Grade.png`). A card whose list may run past what was seen (partly read) adds
  `card N: partly read, M slot(s) seen, more below what the screen showed` and leaves the run's Kinah unknown. A card not received is logged, not
  counted. The pictures are kept as before.
- **Keyed on the frames too**: inside an event, every 2 s, a quick look (about 200 pixels) for the five frames, then
  `Locate`; found, the screen counts as up even when its title isn't read (a Daeva Pass banner over the title held it
  back 20 s on 2026-10-24), and face-up cards are read. reads.log: `event cards: found by their frames (the screen's
  title wasn't read)`.
- **Fallback**: no five cards where they belong, a frame neither blue nor gold, or a frame and its icon that disagree:
  that read is left out; with no read at all, `event cards: layout not recognised, pictures kept (...)`, and the run's
  rewards stay not read, exactly as before.
- **reads.log**: `event cards read N: card 1 12 slots + 2 cut, end shows | ... | card 4 not received`, then per card
  `event cards: card 1 received, 14 slot(s), 2 only ever seen cut ...: Common x440 Icon_abysspoint 63 -> Abyss Points |
  ...`, `event cards: filed after 3 read(s) (...)`, and ApplyGrid's `event cards read: 45 slots, ...`.
- **Measured** (replay bank `event-2026-10-24-hidden-lugi-1306` and `-1006`, seven pictures): every received card's slot
  count right (14, 12, 19 = 45, the footage's count; 13, 20, 13), every count right, Abyss Points 440 + 480 + 800 + 360
  = 2,080 (the counter's rise in the footage), consumables named (Soul Codex, Refining Stone, Odyle, Fine Odyle,
  Resurrection Spiritstone, Centuryroot Token, Enhance Stone), gear left as unknown items (each gear icon names two items
  at one grade), card 1's last row at 13:06 only ever seen cut (two unknown items). parse-test draws the screen for
  received vs not, four cards, a missing "1", face down, the scroll merge and the unknown-item path.
- **Event types seen: Hidden Lugi only.** The other Shugo Festival games (Jump Jump, Wraith Evasion, Mysterious Track,
  Odyle Flight Frenzy, Goldrin's Treasure, Up! Up! Up!, Not This Tile?, Nyerk Shooter, Shugo's Dilemma) need one picture
  each of their card screen before their layout is trusted; until then a different layout falls back as above. Only
  5120x2160 has been seen; the geometry scales with the window's height and is centred, as the expedition table's does,
  unchecked here.

## Commands

```
KinahLedger.exe accuracy-test                      # this pipeline's own checks
KinahLedger.exe replay-bank tracker/tests/replays  # the bank (CI runs it before every release)
KinahLedger.exe replay-bank <dir> --seed           # write the generated cases
KinahLedger.exe replay-bank <dir> --seed-full      # whole-window crafting pictures for craft-test --glob *.png
KinahLedger.exe accuracy-sample <dir>              # the payloads as sent, for checking against the backend
node tools/data/models.js [--out data/models]      # the name decoder's model files, checked by SHA-256 (--check: only check)
```
