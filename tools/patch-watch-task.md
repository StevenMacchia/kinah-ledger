# Scheduled task for the patch-notes watcher (paste into a scheduled task; Steven creates it)

Schedule: every Wednesday 09:00 local (cron `0 9 * * 3`), after the Tuesday PST / Wednesday CET patch.

Prompt:

You are running the weekly patch-notes loop for the Kinah Ledger tracker (repo C:\Users\swoly_uygf8xn\github-portfolio\kinah-ledger). Read the loop's rules first: `tools/patch-watch.md` in that repo (on origin/main). The main checkout is stale and parked on an old branch: never edit it. Work in a fresh git worktree under `.claude/worktrees/patch-<date>` on a branch `data/patch-<date>` cut from `origin/main` (run `git fetch origin` first).

Steps:
1. In the worktree run `node tools/patch-watch.js`. If it prints "No new patch notes", stop and report that in one line.
2. Otherwise judge every row of its table from the sentence it quotes, following `tools/patch-watch.md` and the file map there: zones and instances the minimap prints → `data/zones.txt` / `data/instances.txt` (read each file's header comment for its format), enemies → `data/mobs.txt`, items → `data/extra-items.json`, recipes → `data/recipes.json`. Event titles, rank names, UI labels, shop boilerplate go nowhere. When unsure of a spelling, check the MetaBot item page `https://metabot.gg/en/aion-2/items/<name-lowercased-hyphenated>` with WebFetch.
3. Run `node tools/data/manifest.js`, then commit the changed data files, `data/manifest.json` and `tools/patch-watch-state.json` together with `git -c user.name="Steven Macchia" -c user.email="64022630+StevenMacchia@users.noreply.github.com" commit`. Do not change `tools/patch-watch.js` itself.
4. Push the branch and open a pull request against main with `"C:\Program Files\GitHub CLI\gh.exe" pr create`, titled "Data: <patch date> patch names", whose body is the watcher's table with your verdict written beside each row. Do not merge it. End the PR body with the line: 🤖 Generated with [Claude Code](https://claude.com/claude-code)
5. If a row needs a tracker release (new items go in `data/extra-items.json`, which is built into the exe; chat wording changes live in `Ocr/ChatParser.cs`), say so in the PR body and in your final report; do not bump the version or release.

Final report: two or three sentences, the PR link, and the rows you could not place.

## The fill (2026-11-06)

Step 2 is no longer done by hand first: right after the watcher, run the fill. Write the watcher's report to a file (`node tools/patch-watch.js --out watch.md`) and run `node tools/data-fill.js --in watch.md --out fill.md` in the same worktree. It looks every name up on MetaBot without a browser, writes the entries it can draft into the right `data/` file (items into `data/extra-items.json`, skins into `data/skins.json`, a craftable item's recipe into `data/recipes.json`, enemies into `data/mobs.txt`, zones into `data/zones.txt`), rewrites the manifest and runs the validator `node tools/data-check.js`; a validator failure fails the run and the drafts stay in the files for `git diff` to show. Its report has three parts: Added (written, review `git diff data/`), Questions (no MetaBot page, or a page under another name: judge each from its sentence as step 2 says, file it by hand or drop it as noise), Skipped (already known). Then step 3 onward as before, with `node tools/data-check.js` green before the commit, and one parse-test case per added item (see `tools/data-fill.md`, step 5). The PR body is the fill's report with your verdict written beside each question, and the final report lists every question with its verdict. The procedure is `tools/data-fill.md`.
