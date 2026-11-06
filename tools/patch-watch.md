# Patch-notes watcher

Every game patch adds names the tracker has to know: zones and instances the minimap prints, enemies on the target
bar, items in the chat and the loot list, recipes. The lists live in `data/` and ship to installed trackers through
`data/manifest.json` without a release. The watcher reads each new patch note from the official board and prints the
names it mentions that no list knows, so a patch becomes a short list of additions to judge instead of a read-through.

```
node tools/patch-watch.js              new notes since the last run (tools/patch-watch-state.json), then remembers them
node tools/patch-watch.js --latest 3   the three newest notes, remembering nothing
node tools/patch-watch.js --dry        report without remembering
node tools/patch-watch.js --out r.md   also writes the report to a file
```

The board's API answers plain requests (no browser, no login): `https://api-global-community.plaync.com/aion2_global/board/update_en/article`
lists the notes, and `.../article/<id>` holds one note's HTML body. One request per note, a second apart.

## What the report means

Each note gets a table of names not in any list, with how often the note says it, a guess at the kind (`zone/instance`
by a place word, `item`, `mob`, `skin` from a "(Skin: …)" tag, `?`) and the sentence it came from. The guess is a hint;
the sentence decides. Headings, dates, menu paths and shop boilerplate are dropped, but some noise stays ("Epic Gear",
"Substance Morph" the system): skip those. A name the lists already hold is listed under "Known" with the list it is in.

## The loop

Runs weekly after the Tuesday (PST) / Wednesday (CET) patch, as a scheduled session, and whenever a player reports an
unknown zone or name.

1. `node tools/patch-watch.js`. No new notes: done.
2. For each name in the table, decide from its sentence what it is, then follow the file map in the memory note
   `aion2-patch-notes-sources`: zones and instances → `data/zones.txt` / `data/instances.txt`; enemies → `data/mobs.txt`;
   items → `data/extra-items.json` (a "(Bound)" entry also covers the plain name; items need a release); recipes →
   `data/recipes.json`. A name the note only describes (an event title, a rank name, a UI label) goes nowhere.
3. Check the spelling against MetaBot (`https://metabot.gg/en/aion-2/items/<name-lowercased-hyphenated>`) when the
   note's wording could differ from the game's.
4. `node tools/data/manifest.js`, then commit the data files, the manifest and `tools/patch-watch-state.json` together,
   on a branch, and open a PR titled "Data: <patch date> patch names". The PR body is the watcher's table with each row's
   verdict written beside it.
5. Items or chat wording changed: a tracker release is needed too (`Ocr/ChatParser.cs`, `data/extra-items.json` are built in).

The state file is committed so the next run, on any checkout, starts after the last note that was judged.
