# Live-log miner

The accuracy bench scores the tracker against four recordings, and it reads them clean now, so it has no targets
left; and footage lags the game, so there will be no more of it. What there is, every day, is the tracker's own logs
from Steven's play: `%LOCALAPPDATA%\KinahLedger\reads.log` holds every chat line it counted or let go and a note for
every place it was unsure or overruled itself (a vote, a rescue, a garble, a line set aside, a repeat, a read that
didn't line up), and `trace.log` beside it holds the raw chat read of every frame those decisions were made on. Each
such place is a candidate parse-test: the frames are its input, the decision is what to confirm or flip. `log-mine`
pairs the two logs by wall time and prints the candidates, each with its frames rendered as a LineTracker block that
pastes into `Ocr/ChatCountTests.cs`.

```
KinahLedger.exe log-mine                              the player's own folder (%LOCALAPPDATA%\KinahLedger): reads.old.log + reads.log, trace.old.log + trace.log
KinahLedger.exe log-mine <folder> --out report.md     a copy of that folder (a miscount report, kinah-bench\logs-<date>); the report to a file as well
KinahLedger.exe log-mine <reads.log> --trace t.log    one reads.log with its trace (several traces comma-separated)
    --kinds vote,rescue,garble,aside,noevent,repeat,align   only these kinds (all by default)
    --max 20 | --all                                  how many spots per kind the report shows (20)
    --from 15:00 --to 15:30                           a stretch of the day
    --at 15:40:00.8                                   the frames around a time, whatever the notes say
    --before 4 --after 2                              seconds of frames before a spot and after it
    --date 2026-11-05                                 the day the newest reads.log's last line belongs to (its last-write date otherwise)
```

The tracker is a windowed exe, so from PowerShell run it with `Start-Process -Wait -NoNewWindow -RedirectStandardOutput`
or use `--out`. A whole day's logs (five million bytes of notes, ninety thousand frames) take about ten seconds. The
command writes nothing into the player's logs; its own notes go to a scratch folder.

## What the report means

The head says which files it read and their span, how many chat rows were counted (`ok`) and how many made no event
(`--`), and the count of spots per kind. Then, per kind, each spot:

- its time and kind, and the note or row that made it (several when the same kind repeats within a second: a burst
  and the settle that followed it, a vote and its row);
- `log:` the reads.log lines within 1.5 s, less the ones that only place the tracker (audit, anchors, scene, zone);
- `loot list reads:` the rows beside the character in the same window, the second witness to a pickup (with the
  nameplates and world text the list reader also sees);
- `chat reads`: the trace's frames from 4 s before to 2 s after, as `lt.NewLines(...)` calls, oldest first, the last one
  as a `Check`. Beside each: `->` what a fresh LineTracker fed only these frames returns as new on that frame, each new
  line's meaning for the vote (`[k:463]` Kina, `[i:odyle x1]` an item, `[-]` nothing countable, `~` an inexact name),
  and `voted out:` what the ChatVoter lets through on that read.

The kinds, and what a miscount looks like in each:

| kind | the spot | the miscount to look for |
|---|---|---|
| vote | the reads of a line disagreed and the vote chose; a line left to the vote by the settle | the winner is not the loot list's row |
| rescue | a line counted by no plain read: a burst, the lines below the overlap, the settle, the unaligned watch, a count settled later | the same line also counted by a plain read (a double); a count that disagrees with the row |
| garble | a line read garbled then taken for a line: fading in, a digit slip, a guessed name | the clean line counted again; a wrong name or amount |
| aside | a line set aside: a reward, the inventory at work, a box, an achievement, a run's. `near` marks a counted pickup of the same item within 10 s | a real drop set aside (a kill or gather just before it, no screen, no storage line); a reward counted |
| noevent | a loot-shaped `--` row, a name no list knows, a loot list row the chat never showed | the player's own loot line with no `ok` row within 5 s and no note for it |
| repeat | a line taken for a repeat and skipped, or a repeat counted as a second pickup | a second copy on screen skipped; one copy counted twice |
| align | a read that couldn't be lined up (both reads follow as `before:` and `now:`), a read lined up on the last full read, a game window over the chat | a loot line new in `now` with no `ok` row in the next 10 s, or with two |

The `->` column is a clue, not the verdict: the live tracker carried state from earlier frames (its anchor, the lines
lost at a read's bottom), so a fresh LineTracker can say "new" where the live one rightly said nothing. The `ok` and
`--` rows are what it did live. A set-aside line's row is `ok` too (a reward is an event), so `near` means a second
row with the same item, not the line's own.

Note formats drift: the classifier's regexes are in `Session/LogMine.cs` (`Notes`, `RowTails`), one per
`CrashLog.Note`/`CrashLog.Read` call on the chat path in `Session/TrackerSession.cs`, `ChatVoter.cs`, `ChatRepeat.cs`
and `Ocr/ChatParser.cs`; a new note there needs a line here and in the parse-test's `LogMine.SelfTest`. A note the
classifier doesn't know is simply not a spot; `--at` still shows the frames around any time.

## From a spot to a test

1. Read the spot's frames and rows together: which lines were the player's own pickups, which were counted (`ok`), once?
   The loot list rows settle most doubts (a row per pickup, with its count). Most spots are the tracker being right the
   hard way; those become no test, only a line in the PR body.
2. A real miscount (counted twice, missed, counted when not a pickup, a wrong amount or name): paste the block into
   `Ocr/ChatCountTests.cs` beside `lt4`/`lt5`, trim it to the frames that matter (the first call is history), and set the
   `Check`'s expected string to what the game showed, not what the build returns. Name the check for the time and the
   rule ("a soul line read garbled while fading, then clean: not new"), and say in a comment which log and time it came
   from. A vote or settle case needs the session: see the `ChatVoter` block under the LineTracker ones in that file.
3. Run `parse-test`: the new check fails. Fix the rule (`Ocr/ChatParser.cs` LineTracker, `Ocr/Reread.cs`,
   `Session/ChatVoter.cs`, `Session/ChatRepeat.cs`, `Session/TrackerSession.cs`) until it passes without breaking the
   rest; the existing LineTracker checks are the regressions to watch.
4. When no spot shows a miscount, pin the two or three hardest right answers as tests of current behaviour, so the next
   change that breaks them says so.

## The pass rule

Every verifier green before the PR opens, with the bench output in the PR body:

- `KinahLedger.exe parse-test` all green (run with `Start-Process -Wait -RedirectStandardOutput`; a failed check prints
  `MISMATCH, expected ...`).
- `KinahLedger.exe replay-bank ..\tests\replays` all green (the 66 miscount-report cases).
- `tracker\tests\bench\run.ps1` with no number above `tracker/tests/bench/baseline.json` (a parser change replays from the
  OCR caches in seconds; a reader change needs `-Fresh`). A run that got better: `-Record` and commit the new baseline.

A fix that changes what the tracker counts is a release: bump `<Version>` in the csproj to the next patch after
`origin/main` (with `sed`, not PowerShell: Get-Content re-encodes the file's ©) and write `release-notes/v<version>.md`
in the style of the last one (a summary line, bold-lead bullets, "Installed copies update by themselves in the
background."). Tests and the miner alone are no release: no bump.

## The loop

Runs after a day of play, as a scheduled session or by hand, and whenever Steven marks a count wrong in the window
(a miscount report under `%LOCALAPPDATA%\KinahLedger\reports\` holds its own reads.log and trace in trace.log's form:
point `log-mine` at that folder).

1. `log-mine --out report.md` on the live folder; `--kinds` and `--from/--to` to narrow it when Steven names a time.
2. Triage every spot by the table above; a reading agent can sort the long report, the verdicts are the loop's.
3. Each miscount: a test from its frames, then the fix; each right answer worth keeping: a test of it.
4. The pass rule, then a PR titled `Tracker <version>: <the rule in one line>` (or `Tracker tests: ...` with no fix),
   its body listing the spots judged and their verdicts, and the bench output. Steven merges.

reads.log holds about two days (it turns over at 5 MB into reads.old.log, trace.log at 40 MB into trace.old.log), so
the miner should run at least every other day. A stretch whose notes are gone but whose trace remains can be replayed
through this build with `KinahLedger.exe replay trace.old.log --from 11:00 --to 12:00 --notes notes.log`, and
`log-mine notes.log --trace trace.old.log --date <day>` mines what this build decided on those frames.
