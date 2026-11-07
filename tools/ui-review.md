# Window render-and-review loop

The accuracy loops have a verifier that is not the agent's opinion (the bench score, parse-test, the replay bank). This is the window's. The tracker renders its own window to PNG files without a desktop session (`ui-shot`); the loop renders one page in every state a player can put it in, a reviewer applies a fixed rubric to each render, the findings are ranked by how many states and pages they touch, the top one is cut (cut, don't restyle: Steven on the Events page, "so much info you probably don't need"), and the fix is proved two ways: the page changed where the cut was and nowhere else (a pixel diff of the whole sweep before and after), and every number on the page still equals the figure the data gave it (the figures check, written beside each render). The states live in `tracker/tests/ui/states`, one JSON file each; the demo data builder the plain `--demo` render uses reads them.

```
KinahLedger.exe ui-shot <dir> --sweep events        every state on the Events page: events-<state>.png (the whole page), events-<state>.figures.json, sweep.txt
KinahLedger.exe ui-shot <dir> --sweep all           every page: a state with runs of its own is an Events state; the others (demo, scale-80, scale-150, compact, full) go on every page
KinahLedger.exe ui-shot <dir> --events --state many-runs     one state (--state implies --demo and the whole page)
    --skin daylight|abyss                            the other skins (night is the default); the file name carries the skin
    --states-dir <folder>                            when tracker/tests/ui/states isn't found from the working folder or above the exe
python -I tools/ui-diff.py <before> <after> --only events-   which renders changed between two sweeps and where; exit 1 if any file outside the prefix differs
```

`KinahLedger.exe` is a windowed exe: from PowerShell run it with `Start-Process -Wait -NoNewWindow -RedirectStandardOutput log.txt` (or read `sweep.txt` in the output folder). The Events sweep takes about a minute. The exit code is 0 only when every state rendered and every figures check passed. Nothing is tracked, saved or sent: the sample runs go to a scratch folder's `instances.jsonl`, the Market prices come from the state file (never from this PC's price files), and the player's own folder is not read.

## States

`tracker/tests/ui/states/<name>.json`, one per state. The name is the render's name (`events-many-runs.png`). A file without `runs` renders the plain demo's sample (DemoRuns: eleven Urugugu Canyon expeditions, three Shugo Festival events) with the window changed; a file with `runs` is an Events page state with runs of its own.

| Field | What it does |
|---|---|
| `note` | What the state is for, one or two sentences: the reviewer reads it. |
| `runs` | The runs. Absent: the demo's. `[]`: none at all. |
| `live` | The place with a run open (its band shows the step and the clock). Absent: none. |
| `prices` | The Market board by item name. `{}`: no prices at all. Absent: the demo's board. |
| `scale` | The window size slider, 0.8 to 1.5. |
| `view` | `compact` or `full`. |
| `strip` | The TODAY strip's body: `open` (the default) or `closed`. Since 0.10.21 the Today side is its line alone; the body is the Week side's. |

A run line: `zone`, `kind` (`Expedition`, `Event`, `Transcendence`, `World Boss`), `day` (0 today, -1 yesterday), `time` (the first run's start, `HH:mm`), `secs`, `kinah`, `items` (name to count; a name the dictionary doesn't know is made up, with no icon and no price), `gear` (names, bound), `unread` (reward slots not read), `kinahNotRead`, `rewardsNotRead` (the run is flagged and out of the figures), `partly`. One line can stand for many runs: `repeat` runs a day, `step` minutes apart, over `days` days in a row from `day` on, with `vary` (0.1 is ±10%) spreading Kinah and seconds by index, the same every render. The runs are written to `instances.jsonl` and loaded the way the tracker loads its own, so a run that looks like several, or a Kinah not read, is flagged by the same rules as a real one.

The states now: `demo` (the sample as `--demo` has it), `empty` (first day, nothing), `one-run`, `many-runs` (329 runs over three weeks and four places), `long-run` (three-hour runs), `long-names` (a 41-character place, a 62-character item, Traditional Chinese and Korean names; the item dictionary is en-US only, so these are made up), `big-numbers` (billions, stacks of 9,999), `unpriced` (no Market prices), `unread` (every run flagged), `scale-80`, `scale-150`, `compact`, `full`. Add a state when a report names a case none of them shows; a state is cheap, a finding missed is not.

## The figures check

Beside every Events render, `events-<state>.figures.json`: what the page was given (`fixture`: the runs the file asks for, by place; `data`: what loaded, counted, flagged, the Kinah and the value), what it should say (`heading`, `place`: the picked place's runs, counted, an hour, a run, a run's time, best run, its loot by name and count, `today`: the strip's line, sum and loot), and `check`: each expected text looked for among the texts actually drawn (visible text blocks, read from the window after the render), `found`, `missing`, `trimmed` (a figure that got an ellipsis), `labelsTrimmed` (a name or sentence that did; a finding, not a failure), `clippedTexts` (every trimmed text on the page) and `pastWindowEdge` (any text reaching past the window's right edge). The figures come from the data the page is fed (the run log, the price board) through the same arithmetic the page uses, never from the drawing: the check catches the drawing saying something else, a stale box, a figure hidden, trimmed or off the window. A reviewer compares the PNG against this file and never needs the app: every number on the render must be in `found`.

## The rubric

The reviewer answers every item for every render, yes or no, with what it saw and where. A yes is a finding.

1. **The same information drawn twice on one screen.** The same figure, the same label or the same row of icons in two places (a sum in a head and again in the row under it; the same loot in two boxes; the same sort links twice).
2. **A value or label cut off or overflowing.** An ellipsis, a word ending at a box's edge, a pill or a control pushed out, text on top of text. `labelsTrimmed`, `clippedTexts` and `pastWindowEdge` in the figures file say where.
3. **A column or row that is all "none", "0" or empty.** A figure of 0 with a word, a group with nothing in it, a line of dashes.
4. **An empty state with no sentence saying what will appear and when.** A blank box, a heading over nothing, a place that says "no" without saying what to do.
5. **A number with no unit or no label.** A figure alone, or one whose word could mean two things ("a run" for Kinah and for time on the same line).
6. **Text under the contrast of the rest.** Faint text that carries something the player needs (a figure, a warning), or faint on faint.
7. **A control with no visible purpose.** A switch, a chevron, a link or a dropdown that does nothing in this state (a scope switch with one scope, a sort with one slot, a dropdown with one choice).
8. **Anything a first-time player could not name.** A label or an abbreviation that needs the code to explain it, a figure whose source is not on the page.
9. **A panel whose height is spent on chrome rather than content.** Headings, bands, padding and separators taller than what they frame.
10. **Dark skin and the other skins.** On `--skin daylight` and `--skin abyss`: anything that reads on one skin and not on another (a frame lost, text the colour of its box).
11. **Nothing wider than the window at 80%.** On `scale-80`: every row and pill inside the window; `pastWindowEdge` empty.
12. **The figures check.** `check.pass` true, and every number read off the PNG is in `found`. A number on the page that the file doesn't know is a finding too.

## The review step

A reviewer that is not the agent making the change: a Sonnet subagent given this file, the folder of renders and their figures files. It writes `ui-review-<date>.md` in the sweep's folder:

```
# Window review <date> · <sweep folder>
<one line: N renders, N states, which pages and skins>

## Findings, ranked
1. [events] [12 of 13 states] item 1 · the TODAY strip's row and the place box's LOOT draw the same icons with the same counts · crop 24,120-684,210 and 24,440-684,520 in events-one-run.png
2. ...

## Per render
events-empty.png: 7 yes (control with no purpose: ...), others no
...
```

One line per finding: page, how many states (and pages, skins) it touches, the rubric item, what it saw in plain words, and a crop (x0,y0-x1,y1 in the PNG's pixels) so the next reader can look without reading the whole render. Ranked by how many states and pages the finding touches, then by rubric item number. The agent then reads the PNGs itself at the crops and keeps what is real: real, not real (and why: a demo artefact, a wrong reading), or later (real, but not this PR's cut). The ranked list with the verdicts goes in the PR body.

## The loop

1. Build (`dotnet build -c Release` in `tracker/KinahLedger.Tracker`) and sweep into a `before` folder: `ui-shot before --sweep all`. Every state must render and every figures check pass before anything is changed; a failure here is the first finding.
2. Review: the reviewer scores the Events renders (the page under review) against the rubric and writes the report. Judge its findings at the crops.
3. Pick the top real finding. Cut it with the smallest change that removes it. No restyle rides along.
4. Build and sweep into an `after` folder. `python -I tools/ui-diff.py before after --only events-`: the files that differ must all be the page under review; the diff lists each with the pixel box that changed, which must be where the cut was. The demo feed carries the clock, so the Farming, Crafting and Market renders drift between any two sweeps (a few hundred pixels in the feed's time column, 75,201-106,318 at 100%, and a feed line now and then on Gathering and Market). Sweep the `after` build a second time into `after2` and diff `after` against it: a file that differs between before and after in the same box it differs in between after and after2 is the clock's, not the change's. Anything else outside the page under review is a finding against the change.
5. Verify: `options-test` (three failures are known: two "panel look: the Strip", "LOOT, This zone"; nothing new may fail), `parse-test` green, the sweep's exit code 0, nothing under `Ocr/` or the chat-reading parts of `Session/` touched.
6. Before and after renders of the affected states into `docs/ui/` as `before-<page>-<state>.png` and `after-<page>-<state>.png`; the PR body carries them, the ranked findings with the verdicts, the diff's list and the verifiers' lines.
7. The next iteration starts from the new `after` as its `before`. A finding judged "later" stays in the last report until a PR takes it.

## The pass rule

A sweep passes when every state renders, every `check.pass` is true and `pastWindowEdge` is empty everywhere. A PR from this loop passes when the sweep passes before and after, the diff touches only the page under review, the verifiers above hold, and the finding it set out to cut is gone from the renders it named.
