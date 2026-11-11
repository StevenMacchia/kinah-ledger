# Data fill

The patch-notes watcher (`tools/patch-watch.md`) ends in a table of names no list in `data/` knows. The fill takes that
table, looks each name up on MetaBot, and writes the entry into the list its kind belongs in, so a patch turns into a
commit with a short list of questions instead of a list of names to file by hand.

```
node tools/patch-watch.js --out watch.md          the watcher's report, to a file
node tools/data-fill.js --in watch.md --out fill.md   look the names up, write the entries, validate, report
node tools/data-fill.js "Nightmare Coin" "Argo"      names given by hand (a player's report, a name the watcher missed)
node tools/data-fill.js ... --dry-run                 print the drafts, write nothing
node tools/data-fill.js ... --data some/dir           lists in another folder (a copy, for a proof)
node tools/data-check.js                              the validator alone, after editing any list by hand
```

## Where the data comes from

MetaBot (`https://metabot.gg/en/aion-2`) has the global client's items, monsters and zones days before anything else.
Its search box is client-side (useless without a browser), but every page is server-rendered and answers a plain
request by slug: the name lowercased, apostrophes dropped, every other run of non-letters turned into one hyphen
(`Balaur's Essence` → `/items/balaurs-essence`, `Gear Change Voucher: Ludra (Bound)` → `/items/gear-change-voucher-ludra-bound`).
A missing page is a 404 titled "Page not found". Checked 2026-11-06 from Node with a plain `fetch`: the page carries
the name in the `<h1>` (the `<title>` suffix changes: ": How to Get", " – Drops", none), the item card as text (Grade, Category, Required Level, Trading, Stack size), the icon in
`og:image`, and for a craftable item its Crafting table (skill, mastery, faction, materials, makes, combo chance).
Monster pages (`/monsters/<slug>`) carry Level and Rank, zone pages (`/zones/<slug>`) Map and Type. Nothing needs the
Next.js payload the page also embeds. One request per name, two for an item when the plain slug is missing and the
`-bound` one may exist, 1.5 s apart.

## What the fill writes

The kind comes from the watcher's Guess column (`item`, `skin`, `mob`, `zone/instance`, `?`), and the file from the map
in `tools/patch-watch.md`:

| Kind | Page | File | Entry |
|---|---|---|---|
| item, `?` | `/items/<slug>`, then `-bound` | `data/extra-items.json` | the shape of the entries there: `id` null, name, grade, tradable, category, icon, iconSource, metabotCategory, bound, url. `category` is MetaBot's, except coins and gold → Currency and event coins → Event, as the file already does. Built into the exe: a release ships it. |
| craftable item | the item page's Crafting table | `data/recipes.json` too | one recipe per faction row, in the file's shape; `recipe_id`, `success_rate`, `combo_success_rate` and `success_curve` are not on the item page, so they are null and the entry carries a `note` saying so. The tracker files a null-id recipe under no faction and uses it for either. |
| `X (Skin: Slot)` | `/items/<slug>` | `data/skins.json` | name, set, slot, grade, tradable, url, set_url |
| mob | `/monsters/<slug>` | `data/mobs.txt` | the name, one line |
| zone/instance | `/zones/<slug>` | `data/zones.txt` | the name, one line. An instance needs a Kind no page tells, so a name with no zone page is a question naming the `/dungeons/<slug>` page and the `instances.txt` line to write. |

An entry is never overwritten: a name any list already knows (the same letters and digits, "(Bound)" ignored, like the
watcher's own check) is listed under Skipped with the list it is in. A page whose name is not the name asked for
(MetaBot has "Fresh Leaf Tiara (Skin: Helm)" where the note said "Fresh Leaf Tiara") is a question with the URL, so a
person judges which it is. A name with no page is a question with the URLs tried.

After writing, the fill runs `node tools/data/manifest.js` when a manifest file changed (recipes, skins, zones, mobs:
trackers fetch those within a day) and then the validator, `node tools/data-check.js`; a problem fails the run and
leaves the drafts in the files for `git diff` to show. The validator checks every list the way the tracker reads it:
files parse, ids unique, names unique, the fields the loaders need present and typed, grades, skills and instance
kinds from the known sets, recipe materials items some list knows, loot tables shaped as LootBook reads them, and the
manifest current.

## The loop

Runs after the watcher, weekly after the Tuesday (PST) / Wednesday (CET) patch, and whenever a player reports an
unknown name.

1. `node tools/patch-watch.js --out watch.md`. No new notes: done.
2. `node tools/data-fill.js --in watch.md --out fill.md`. Read the report: the Added table is what was written, the
   Questions are for a person, the Skipped rows were already known.
3. Judge each question from its sentence: file it by hand following the map above (a dungeon with its Kind, a name
   MetaBot spells differently, an item MetaBot does not have yet shaped on a sibling with a `note`), or drop it as
   noise (headings, shop text, event titles, rank names). Then `node tools/data/manifest.js` and `node tools/data-check.js`.
4. Check that the added names look like what the game prints: a zone page's name is MetaBot's, and `zones.txt` wants
   the minimap's wording; a page with the right name but a different grade or category than the note says is worth a
   look. `git diff data/` is the review.
5. Build the tracker (`dotnet build -c Release` in `tracker/KinahLedger.Tracker`) and run `parse-test` (a WinExe: run
   it with `Start-Process -Wait -RedirectStandardOutput`), which has a case per added item that a chat line resolves to.
   Items in `extra-items.json` are built into the exe, so an added item needs a release; the other lists ship through
   the manifest without one.
6. Commit the data files, the manifest, `tools/patch-watch-state.json` and the test together on a branch; open a PR
   titled "Data: <patch date> patch names" whose body is the fill's report (Added, Questions with the verdicts written
   beside them, Skipped) and the validator's and parse-test's last lines.

## Proving it without a patch

With no new note the fill has nothing to do, so prove it on names that already exist: copy `data/` somewhere, remove
three entries from the copy's `extra-items.json`, and run `node tools/data-fill.js --dry-run --data <copy> "<name>" ...`.
The drafts should match the removed entries field for field (2026-11-06: Gear Change Voucher: Ludra (Bound), Abyssal
Forge: Weapon Selection Chest (Bound) and Content Usage Ticket Selection Chest (Bound) came back identical; Artisan's
Asvata Bow's recipe matched on skill, materials, yield, mastery and combo chance, with the four fields the page does
not carry left null).
