# Skill fill: every ability the Parser shows, by name

The Parser's inspect view lists each member's damage by ability. Its names come from `data/skills.json` (built into the
tracker), its icons from `tracker/KinahLedger.Tracker/Art/skill-icons.png` with `data/skill-icons.txt`. Steven, 2026-11-14:
"wtf is 'Ability 16050000'… you need to get them fucking right". The view never shows a number: an ability the list
doesn't name reads "<Class> skill" (the class from the skill's id or the member) or "Other damage", and the tracker notes
it once in reads.log as `ability unnamed: <id> (<class>)`.

## Sources (each entry's `src`)

- `metabot`: MetaBot.GG's skill pages, the 279 class skills (`page` is the slug under `https://metabot.gg/en/aion-2/skills/`).
  Never overwritten.
- `client`: the global client's English skill table as A2Tools ships it (`taengu/A2Tools-DPS-Meter`,
  `src/data/i18n/skills/en.json`, GPL-3.0), at the commit pinned in `tools/skill-fill.js`. The tool stops if it disagrees
  with more than 5% of MetaBot's names; on 2026-11-14 it agreed on all 279. Theostones also carry `item`, their MetaBot
  item page, when `--verify-items` found one (51 of 90; the other 39 are not on MetaBot yet).
- Icons (`icon`: a file name, or `=<id>` for another entry's): MetaBot's copy for a MetaBot skill; for the rest the game's
  own icon on NCSoft's game-data CDN (`https://assets.playnccdn.com/static-aion2-gamedata/resources/<icon>.png`), named
  as A2Tools names it: a class skill by its id's first four digits (`src/data/skill_icons.json`), a Theostone by its id
  (`Icon_Item_Usable_Godstone_WP_r_` and the id's 6th-7th digits in hex; MetaBot's item pages show the same icons). A
  spirit's attacks and commands take their spirit's summon icon. Anything else has none: the class's emblem stands in.

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
2. To take a newer client table, change the pinned commit in `tools/skill-fill.js` (check the agreement line).
3. `python tools/skill-icons.py <scratch>/icons` repacks the icon sheet (ids with the same picture share a cell).
4. `dotnet build`, then `KinahLedger.exe parser-test` through `tracker/tests/hidden-run.ps1`.
5. Commit `data/skills.json`, `data/skill-icons.txt` and the sheet on a branch; the names ship with the next release.
6. An id still unnamed: read its row in the report (share, hits, who used it) and look for it on MetaBot or another
   public Aion 2 database; record the source in its entry. Never name it from a guess.
