# Kinah Ledger data pipeline

How item data gets from NCSOFT's Taiwan item dictionary into the site, what each piece does, how the owner runs it, and what is still undecided. The `tracker/` folder is a separate agent's work and is not part of this pipeline.

## The pieces

```
NCSOFT Taiwan item dictionary (public web, locale=en-US, 11,236 items)
        |  node tools/crawl-tw.js           owner runs it; paced; never run by agents
        v
data/tw-items.json                          raw crawl  {source, fetched, count, items[]}
        |  node tools/build-site-data.js    deterministic, safe to re-run
        v
site/data/items.json        every item, compact keys          \
site/data/materials.json    farm/drop materials + bound pairs  >  site/data-loader.js -> window.KL_DATA -> site/index.html
site/data/categories.json   cat1/cat2 tree, names, counts     /
        |  node tools/fetch-icons.js        optional, owner's decision (see Icons)
        v
site/icons/*.png            local icon mirror for "local" icon mode
```

| Path | Who writes it | What it is |
|---|---|---|
| `tools/crawl-tw.js` | owner | Walks the dictionary's category tree (`/game/item/category?locale=en-US`, which is where `cat1Name`/`cat2Name` come from) and pages each category 50 at a time, one request every 400 ms, resumable through `tools/tw-progress.json`. Writes `tools/tw-items.json` next to itself. |
| `data/tw-items.json` | owner moves it here | The raw crawl, `{source, fetched, count, items[]}`. The build also accepts it in place at `tools/tw-items.json`, so the move is optional. Note `.gitignore` ignores `tools/tw-items.json` but not `data/tw-items.json`; see open questions before committing a multi-megabyte file. |
| `data/tw-items.sample.json` | checked in | 50 real gathering materials from one page of the dictionary (`page=1&size=50`, `Misc_001/GatherResource`), fetched once and reshaped into the crawler's layout; the response's own pagination reports `total: 92` for that category, matching the known count. Used automatically when `tw-items.json` is absent, and only then. |
| `tools/build-site-data.js` | this repo | Turns the crawl into the three site files. No dependencies. |
| `site/data/*.json` | build output | What the browser loads. Built from the sample until the crawl exists; those files carry `"sample": true`. |
| `site/data-loader.js` | this repo | Plain script. Fetches the three files and exposes `window.KL_DATA`. Resolves with empty arrays when files are missing so `index.html` falls back to its embedded sample arrays. |
| `tools/fetch-icons.js` | this repo | Owner-run icon mirror. Paced, resumable, never run by the build. |
| `site/icons/` | fetch-icons output | Local copies of icons, only if the owner chooses to mirror them. |

## Running it

```
node tools/crawl-tw.js            # 1. owner only. Writes tools/tw-items.json (resumable; re-run after an interruption)
node tools/build-site-data.js     # 2. reads data/tw-items.json, else tools/tw-items.json; writes site/data/*.json and prints a summary
node tools/fetch-icons.js         # 3. optional. Read its header first. Try --materials-only or --limit 50 before the full run
```

Then serve `site/` over HTTP (`npx serve site`, or `python -m http.server` from inside `site/`) and open the page. Browsers refuse `fetch()` from `file://`, so opening `index.html` directly always shows the embedded sample data. In the console, `KL_DATA.loaded` should be `true` and `KL_DATA.sample` should be `false` once a real crawl has been built.

What to look at in the build summary:

- `input` names the file used. `[SAMPLE]` means the crawl was not found.
- `iconBase` should be the playnccdn `resources/` folder with `0 outside`. A non-zero `outside` count means some icons live elsewhere; they keep full URLs and still work.
- `pairs`: the number of `"(Bound)"` items and how many found their tradable twin. `ambiguous` is the number left unpaired because two or more tradable items share the name. A handful is normal; hundreds means the name rule needs a look.
- the per-category table shows every cat1/cat2 with its count and whether the material filter includes it. This is the place to decide whether the filter is right (see below).

Input is the first of `--in <file>`, `data/tw-items.json`, `tools/tw-items.json`, `data/tw-items.sample.json` that exists; with none of them it writes valid empty files. Flags: `--in <file>` and `--out <dir>` override the paths, `--dry-run` reports without writing, `--quiet` prints warnings only. Re-running on the same input is a no-op: output is sorted by id with fixed key order and carries the crawl's `fetched` timestamp rather than a build timestamp, and files whose content did not change are not rewritten.

## What the outputs contain

`items.json`

```
{"source": "...", "fetched": "...", "sample": true?, "iconBase": "https://assets.playnccdn.com/static-aion2-gamedata/resources/", "count": N, "items": [
{"id":610610016,"n":"Radiant Odyle","g":"Unique","t":1,"c1":"Misc_001","c2":"GatherResource","i":"Icon_Item_Gather_Od_A_u_001.png"},
...]}
```

One item per line, so diffs stay readable. `t` is tradable as 0/1. `i` is the icon path relative to `iconBase`, normally just the file name; an icon outside the base keeps its full URL; no icon is `""`.

`materials.json` has the same head plus `filter` (the rules that were applied) and the same item shape plus:

- `b: 1` when the name ends in `(Bound)`
- `pair`: id of the twin. On a bound item it points at the tradable one and vice versa; `null` on a bound item with no twin; absent on a tradable item that has no bound twin
- `o: [...]` the crawl's `options` strings, only when non-empty
- `d: "..."` the item description, only when the crawl carries one

`categories.json` is pretty-printed: `{source, fetched, sample?, total, cat1: [{id, name, count, tradable, cat2: [{id, name, count, tradable, material}]}]}`. `name` is the most common `cat1Name` / `cat2Name` seen for that id, falling back to the id itself. `material` tells whether the filter counts that cat2 as a material.

## The material filter

Defined at the top of `tools/build-site-data.js`:

- `EXCLUDE_CAT1 = Equip_Weapon, Equip_Armor, Equip_Accessory, Usable_Skin` are never materials.
- `MATERIAL_CAT1 = Misc_001`: every cat2 under it is a material (GatherResource, CraftResource, ConversionResource, Material, Other, ...).
- `MATERIAL_CAT2 = GatherResource, CraftResource, ConversionResource, Material, MagicStone, Box`: these cat2 values count as materials under any other cat1 (in practice `Usable_001`).

This is a judgment call made before seeing the full category tree. After the first real build, read the per-category table in the summary and adjust the three lists. Things to decide then: whether `Misc_001/Other` is mostly quest junk (then move `Other` out), and which `Usable_001` subcategories besides `MagicStone` and `Box` are farmed and traded (soulstones, odian-type consumables, and so on; use the real cat2 ids the crawl reports).

## Bound and tradable pairs

Bound and tradable versions are separate ids and the bound name ends with ` (Bound)`. The build pairs `X (Bound)` with `X` by name (case-insensitive, whitespace-collapsed). When several tradable items are called `X`, it prefers the one with the same cat2 and grade, then same cat2, then same grade; if a tier still has more than one candidate it leaves the bound item unpaired and counts it as ambiguous rather than guessing. A tradable item is never paired twice.

In the sample page the pairing works out: 50 records, 25 tradable and 25 bound, every bound one paired. Note the dictionary itself is not perfectly consistent: in that page tradable items carry `categoryName: "Gathering Material"` and bound ones `"Gatherable Material"`. The build takes the most common name per category id, so that does not matter, but it is a reminder that text fields are not keys.

## Icons

Three ways to show an item icon, chosen per call or site-wide through `KL_DATA.iconMode`:

- `cdn`: `iconBase + i`, hot-linked from `assets.playnccdn.com`. The CDN sends `Access-Control-Allow-Origin: *`, so `<img>` and canvas use work cross-origin. Nothing is stored on our side. This is the default.
- `local`: `icons/ + i`, served by the site after the owner runs `tools/fetch-icons.js`. Faster and immune to CDN changes, but it is a copy of NCSOFT's artwork hosted by us.
- `none`: empty string; the page draws its own placeholder.

`fetch-icons.js` downloads only the files referenced by `items.json` (or `materials.json` with `--materials-only`), one request every 300 ms, skips files that already exist, writes `.part` files and renames on success, retries 429/5xx with back-off, and logs hard failures to `site/icons/_failed.txt`. Stop it any time; running it again resumes. Its header explains why it is not part of the build: NCSOFT's terms restrict automated collection and commercial use of its content, and the icons are its copyrighted assets. Hot-linking versus mirroring is the owner's decision, not a script default. See `docs/image-sources.md` for the wider artwork question.

## What has been verified

- `build-site-data.js` with no input at all: writes valid empty files, exits 0, second run reports all three unchanged.
- With `data/tw-items.sample.json`: 50 items, 25 pairs, correct `iconBase`, idempotent.
- With a synthetic fixture (fake names, not in the repo): pairing tiers, ambiguity handling, icon on another host, missing icon, invalid and duplicate rows, whitespace in names, string booleans, the material filter across cat1/cat2, dry run, invalid JSON exit code, bare-array input.
- `data-loader.js` under Node with stubbed browser globals: missing files, rejected fetch, HTML served instead of JSON, and the real sample-built files; `iconUrl` in all three modes.

Nothing has been run against a full crawl yet, because none exists in this checkout.

## Open questions

1. **Taiwan versus Global item set.** The TW dictionary has 11,236 items. Global launched later (Steam early access 30 Sep 2026, full launch 5 Oct 2026 per press coverage) and will lag TW's content, and some TW items may never appear (event and regional items). Options, none verified yet: (a) check whether the Global service exposes the same dictionary under another host and crawl that instead; at least one fan site says it reads an "official NCSOFT Aion 2 API" for Global; (b) gate by id range once the pattern between TW-only and shared items is understood; (c) a hand-kept `data/global-exclude.json` of ids or name patterns, applied in the build; (d) show everything and let the tracker data (what actually drops on Global) decide what is surfaced. The build has no filter for this today.
2. **Name drift between localisations.** TW's `en-US` strings may differ from Global's English client for the same item. Matching by id is safe only if ids are shared across regions, which is unknown. Keep ids as the key and treat names as display text until proven otherwise.
3. **`description` and `options`.** The per-item API record carries `description` (for example "Used as a material for Blacksmithing and Armorsmithing."), which a farming site can use, but `crawl-tw.js` currently drops it: its row mapper copies `id, name, grade, tradable, categoryName, image` and `options`. Adding `description: r.description` to that mapper is a one-line change, after which the build passes it through to `materials.json` as `d` with no other work. Conversely the record seen has no `options` field at all, so `o` will stay empty unless another endpoint supplies options.
4. **Category names.** The per-item record carries only `categoryName`; `crawl-tw.js` fills `cat1Name`/`cat2Name` from the category endpoint, so a real crawl has them. The sample, built from one item page, has an empty `cat1Name`, which is why `categories.json` built from it shows `Misc_001` as its own name. Items in a cat1 with no subcategory arrive with `cat2: null`; the build stores `""` and groups them under `_none`.
5. **`(Bound)` suffix stability.** The pairing assumes that exact English suffix. If the Global client phrases it differently, change `BOUND_SUFFIX` in the build (one regex).
6. **Materials filter.** See above; decide after the first full build.
7. **Icon hosting.** `cdn` or `local`; owner decision; documented in `fetch-icons.js` and `docs/image-sources.md`.
8. **Committing the crawl.** `data/tw-items.json` will be several megabytes. Committing it makes builds reproducible without re-crawling; ignoring it keeps the repo small. The build also works from `site/data/*.json` being committed, which is what the site actually needs.
9. **Size budget.** Measured on the sample, `items.json` costs about 150 bytes per item, so a full crawl of 11,236 items is roughly 1.7 MB before compression; `materials.json` costs about 250 bytes per item with descriptions, so a few hundred KB at most. GitHub Pages serves gzip, so this is fine, but if it grows, `items.json` can be split per cat1 and loaded on demand.
10. **Prices, farming rates and drop rates are not in this dictionary at all.** This pipeline only supplies the item catalogue. Where market prices and drop observations come from is the tracker's problem and still open.
11. **Refresh cadence and crawl etiquette.** Patches add items. A monthly re-crawl is probably enough; the crawler should keep its pacing and identify itself. Whether NCSOFT objects to the crawl at all is a question for the owner to settle against the terms quoted in `docs/image-sources.md`.
