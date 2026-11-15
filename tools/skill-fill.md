# Skill fill: every ability the Parser shows, by name

The Parser's inspect view lists each member's damage by ability. Its names come from `data/skills.json` (built into the
tracker), its icons from `tracker/KinahLedger.Tracker/Art/skill-icons.png` with `data/skill-icons.txt`. Steven, 2026-11-14:
"wtf is 'Ability 16050000'… you need to get them fucking right". The view never shows a number: an ability the list
doesn't name reads "<Class> skill" (the class from the skill's id or the member) or "Unknown skill", and the tracker notes
it once in reads.log as `ability unnamed: <id> (<class>)`.

## Sources (each entry's `src`)

- `metabot`: MetaBot.GG's skill pages, the 279 class skills (`page` is the slug under `https://metabot.gg/en/aion-2/skills/`).
  Never overwritten.
- `acompanion`: Swellshinider/AcompanION `public/gamedata.json` (MIT), names by id with the game's icon file name.
- `pingmaker`: gunba/pingmaker `skills.json` (MIT), ids by name.
- Both are pinned to a commit in `tools/skill-fill.js`. Every name they give is checked against the game client's English
  table as A2Tools carries it (taengu/A2Tools-DPS-Meter, GPL-3.0; it agrees with MetaBot on 279 of 279). That table is a
  check only: nothing from it ships. A name it contradicts is left out; where the two MIT tables differ, the one it
  confirms ships.
- Names only the A2Tools table has (228 on 2026-11-14) are left out until a non-GPL source names them; they read
  "<Class> skill". The report lists the ones the saved fights use, with their share.
- Theostones carry `item`, their MetaBot item page, when `--verify-items` found one (18 of 18 on 2026-11-14).
- Icons (`icon`): `nc:<file>` is the game's own file on NCSoft's game-data CDN
  (`https://assets.playnccdn.com/static-aion2-gamedata/resources/<file>.png`) as AcompanION names it; `item:<file>` a
  Theostone's MetaBot item icon; `waffle` Waffle-ens/waffle_meter's per-id copy (MIT); none on a MetaBot skill is
  MetaBot's copy. Anything else has none: the class's emblem stands in.

No name is ever made up: an id neither source names stays unnamed and is listed in the report.

## Which ids the Parser can show

A class skill's hits come under the skill without its rank (the last four digits): 12060003 is Punishing Strike
(12060000). The tracker does the same when it looks a name up (`Parser/Abilities.cs` `Resolve`), so a rank the list
lacks still gets its skill's name and icon. A class's own block (`xx00xxxx`, the Spiritmaster's spirit commands such as
16001101 Fire Spirit: Flame Explosion) stays whole; the spirits' basic attacks (`1000xx`) and the Theostones (`30xxxxx`)
come under their own ids. Ids that name the same ability (a spirit's attack at each rank) are one row in the view
(`Abilities.Merged`).

Fights saved before this change (2026-11-14) have the spirit commands under one id, 16000000; those rows read
"Spiritmaster skill".

## The loop

Weekly with the patch watcher (`tools/patch-watch.md`, step 6), and after any patch that adds skills.

1. `node tools/skill-fill.js --cache <scratch>/cache --icons <scratch>/icons --verify-items --metabot <site>/data/metabot-classes.json --out fill.md`
   (`--dry-run` for the report only). It adds what the sources name and reports coverage over the saved fights in
   `%LOCALAPPDATA%\KinahLedger\characters\*\encounters.jsonl`, plus any id the tracker noted as unnamed.
2. To take newer tables, change the pinned commits in `tools/skill-fill.js` (check the agreement lines). Look for a
   non-GPL source of the "names only the client table has" before each run; never ship one from the A2Tools table.
3. `python tools/skill-icons.py <scratch>/icons` repacks the icon sheet (ids with the same picture share a cell).
4. `dotnet build`, then `KinahLedger.exe parser-test` through `tracker/tests/hidden-run.ps1`.
5. Commit `data/skills.json`, `data/skill-icons.txt` and the sheet on a branch; the names ship with the next release.
6. An id still unnamed: read its row in the report (share, hits, who used it) and look for it on MetaBot or another
   public Aion 2 database; record the source in its entry. Never name it from a guess.
