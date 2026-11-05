# Accuracy bench

Footage of real farming runs, counted again by this version of the tracker and scored against what the footage
shows. Where the replay bank (`../replays`) checks one crop at a time, the bench checks a whole run: the chat and the
loot list read from every frame, the session counting on top, and every pickup in the recording matched to what
landed. It is the verifier for parser and reader changes: a change is merged only when the bench shows no regression.

## What is where

The recordings are not in this repo (about 2 GB of frames per hour of footage). They live in a bench folder beside
the repo's parent, `kinah-bench/`, with one manifest, `bench.json`:

```json
{ "runs": [
  { "name": "131029", "frames": "frames-hd-131029", "start": "2026-10-24 13:10:30.6",
    "truth": "truth-131029.csv", "reads": "run-20261024-131029.reads.log" }
] }
```

- `frames`: the recording cut into JPGs at 2 fps, full recording size
  (`ffmpeg -err_detect ignore_err -i run.mkv -vf fps=2 -q:v 2 frames-hd-<run>/f%04d.jpg`).
- `start`: the wall time (local) of frame 0. The `.start` file ffmpeg wrote is a second or two early; the compare notes
  fix it from the game's clock.
- `truth`: the hand-made list of every event in the footage (`clip_s`, `item`, `qty`, and `source`/`result` or `kind`
  when there are gathers or rewards). Lines starting with `#` are notes. Cards and HUD slots (`kind` card, reward_hud)
  are not read from the chat and are left out of the score.
- `reads`: the live tracker's `reads.log` from that run, for the zone it was in (the replay takes zone and reward
  screen reads from it, as `replay` does).

This repo holds the code (`Session/BenchReplay.cs`, the `bench-replay` command), the runner (`run.ps1`) and the
baseline (`baseline.json`).

## Running it

```powershell
tracker\tests\bench\run.ps1              # every run in the manifest against the baseline
tracker\tests\bench\run.ps1 -Only 131029 # one run
tracker\tests\bench\run.ps1 -Fresh       # read the frames again (after a reader change: ScreenReader, Locators, LootRows, PopupIcons)
tracker\tests\bench\run.ps1 -Record      # keep this build's score as the new baseline (only after it got better)
```

The first pass on a run reads every frame (minutes); the reads are cached beside the frames as `bench-ocr.jsonl`, so a
parser or session change replays in seconds. The cache is keyed on the frame count, the OCR settings and the chat box,
not on the tracker version: a reader change needs `-Fresh`. Each run leaves its replay notes in `bench-reads.log`
beside the frames, the same lines the live tracker writes to `reads.log`: a miss is diagnosed there first.

## The score

For each truth event the replay must land the same thing (the same Kina amount, the same item and count; souls and
gather fails by kind) between 15 s before and 20 s after the footage shows it (before, because the chat settle dates a
line back to when the garbled reads began; after, for the vote and a second witness). Anything that landed and matches no
truth event is an extra: a double when the same thing was matched within 20 s, a phantom otherwise. The run's score
is its `missed` and `extra` counts; latency is reported, not scored.

`baseline.json` keeps each run's last accepted score. A run fails when either count goes above its baseline. A run
that does better passes and says so; `-Record` then writes the new numbers, in the same change as the fix. The loop's
job is to drive every baseline to zero.

## The loop

1. Change the parser, the session or a reader.
2. `dotnet build -c Release` in `tracker/KinahLedger.Tracker`, then `tracker\tests\bench\run.ps1` (`-Fresh` for a
   reader change).
3. A FAIL line names the run; read its `bench-reads.log` around the missed or doubled event's time and fix the rule,
   as `kinah-ledger-live-logs-for-diagnosis` describes for live logs. Write a parse-test from the lines too, so CI
   keeps the case.
4. No tracker PR that touches `Ocr/` or `Session/` merges without the bench output pasted into it, and no baseline
   number goes up.

The bench runs only where the frames are (Windows, this PC): CI runs the replay bank and parse-test, not this.
