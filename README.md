# Kinah Ledger

A fan project for Aion 2 (global): Kinah per hour, material yields, drop rates and market prices,
built from runs players share through a free tracker. Made by players for players; not affiliated
with NCSOFT. MIT licensed.

This is a side project, separate from the author's professional work.

```
(website)  lives in a separate private repo; its download button points at this repo's releases
tracker/   the Windows tracker, C# / .NET 8
tools/     data tooling (item dictionary crawler, site data build)
data/      data drops (tw-items.json goes here)
docs/      notes on data sources and the pipeline
```

## Tracker status

The tracker only ever reads the screen. It never captures network traffic, reads game memory, modifies
the client or sends input to the game; everything runs on the player's PC, and what leaves it is listed
below. (An earlier packet-capture experiment was removed in 0.6.0 so that this stays true in full.)

Since 0.6.2 it reads only the parts of the game that nothing covers. While another window sits over the chat
(a browser, a chat app, the tracker's own window) or the game is minimized, reading pauses, the window says
which program is in the way, and that time doesn't count toward any per-hour rate. `KinahLedger.exe in-view`
shows what the tracker can see right now. Since 0.6.4 a zone name it has never seen (not in its list and without
a place word such as Valley or Ruins) has to be read twice in a row before it counts, so a button or tooltip that
drifts over the minimap can't become a zone.

**Screen reading.** The game prints every pickup in chat ("Acquired 2,790 Kina.",
"Acquired Drakan hide ×2."). The tracker reads those lines off the screen with the text recognition
built into Windows, counts what's new whenever the game is open, and times farming runs on top of that
count with Kinah and loot per hour in a small always-on-top panel. Setup is picking your server once.

1. Play in borderless or windowed mode with the chat open at the bottom-left. A chat tab showing only System Notifications reads best; the normal tab works too, and so does the frameless chat with no input box.
2. Double-click `KinahLedger.exe`. On first run pick your region, faction and server, and a look (Night, Abyss or Daylight); everything shared is filed per server. From then on the tracker finds the game window, the chat (above the "Enter your message" box, or from its own lines) and the zone on the minimap by itself, and **counts everything the chat shows while the game is open**: drops, gathers, Soul Crystals, and Market prices whenever the Market is open. That count is shared anonymously in 15-minute chunks and never kept here.
3. **Start farming** times a run on top of that count: the window shows the run (four tiles, then Drops / Gathered / Pets tabs), and Stop saves it to your history and shares it. Open world, Gathering or Pets is the run's label. Farming time is noticed from the chat itself: every loot, kill or gather line keeps the farming clock running, a minute with none stops it until the next, so every per-hour rate (here and on the site) divides by time actually spent farming, while counts and drop rates keep counting regardless. Open world shows Kinah per hour and per kill, kills per hour, drops per hour, and every drop with its drop rate and per-hour rate, with item names in the game's tier colours. Gathering splits each skill (mining, logging, essence extraction, herbs, cooking ingredients) into its own section with its own attempts, Plentiful rate and per-attempt rates. Pets counts Soul Crystals by zone with drops and per hour only, no drop %, so the best pet-farming spot stands out. Soul Points and Soul Crystals never count as drops. The game's item list is built into the tracker and is the whitelist for drops: a name that isn't on it (a misread, a nameplate behind the chat) is never counted and never sent. Zones work the same way against the tracker's zone list; anything else counts under "Unknown zone". Ctrl+Shift+K starts and stops a run from inside the game; minimizing keeps everything running in the tray.

   **Zones.** The tables start over when you change zone, so they show the zone you are in. Tick **All zones** under the activity picker to keep them counting across zones. Either way, what goes to the site is split by zone: each zone sends its own time, Kinah, kills and items, so one zone's drops are never filed under another.

   **Quest rewards** are left out of every farming number. Kinah marked (Bound), the Kinah after a "total payment … completed" line, a Kinah line that names a quest or reward, and any single Kinah line far above what the mobs pay (more than 25 times the median of your last pickups) are counted as skipped rewards, shown in the summary line, never as income or kills.

   **Kinah by enemy (a best guess).** The tracker reads the target bar at the top-centre of the game every 2 seconds. Kinah, drops and Soul Crystals that land within 8 seconds of it naming an enemy are credited to that enemy, in the zone you fought it in. With several enemies on you the bar shows only one of them, so these figures are a guess, and the window and the site say so. Only enemy names on the tracker's enemy list (`data/mobs.txt`) count. If the panel over the game sits on the target bar, the window asks you to drag it aside.

   **Crafting** is its own part of the window: press **Crafting** at the top. Each recipe shows how many times you tried it, how often it worked, what came out (a great success included), what one try costs and what the result sells for, with the profit or loss. Costs come from the recipe's materials at the lowest Market prices on your server; a failed try uses them too. Bound vendor materials can't be priced and are left out, and the row says so. The recipes come from [MetaBot.GG](https://metabot.gg/en/aion-2/crafting). Crafted items are never drops, crafting is never farming time, and none of it goes into a zone's numbers. Market settlement payouts ("A total payment of … completed") are never farming Kinah either.
4. **Settings** holds the server, the one **Share** switch (runs and Market prices together), the panel over the game, the profile link code (a short form of the random install ID: pasting it into a profile on the site attaches this PC's runs to that profile, otherwise runs stay anonymous), chat detection with **Detect now**, **Choose the chat** and **Test read**, the Market area, and the look.

The same steps exist as commands for scripting (`pick`, `ocr-test`, `watch`), listed below.

Limits: it only sees what the chat shows, it can miss a line if many arrive in the same instant, and
the first read counts nothing so old lines on screen aren't mistaken for new loot.

**Market prices (0.6.0).** Whenever the tracker window and the game are open, the tracker watches for the game's
Market window; no farming run is needed. When the Market is open, the listings on your screen (item and lowest sale
price on the main board; item, quantity and unit price on a listing table) are read about once a second, the same way the chat is, and queued as anonymous asks so
the site can show per-server prices the way Universalis does for FFXIV. Coverage follows what players look at:
nothing is clicked or typed in the game, and no account ever reads the Market on its own.

- The Market is found by its title and the column headers: the main board's ("Grade", "Item", "Sales List",
  "Lowest Sale Price", where the "Item Level N" line under each name is folded into its row) or a listing table's
  ("Item", "Quantity", "Unit Price", "Total Price"); the headers also give the column positions, so each cell is read on its own. If it can't be found,
  **Choose the Market** lets you drag a box around the rows; each row is then read as one line and split by its
  numbers from the right (quantity, unit price, total). Quantity × unit price has to equal the total, so a misread
  digit drops the row instead of sharing a wrong price. Digits use the chat parser's corrections (O for 0, l for 1,
  thousands separators).
- Your server, chosen on first run, travels with the prices as `region-server` (`na_e-siel-elyos`) so they land on
  the right board; without it, asks are read but not sent.
- Market prices follow the one **Share** switch in Settings: with sharing off the Market is never read, and the rest
  of the tracker works as before. Settings shows what the reader is doing: waiting for the game,
  watching for the Market, or reading it, with asks read, items, asks shared and when it last read. The same ask
  (item, stack, price) isn't sent twice within 15 minutes; batches go out every 10 seconds or 100 asks, and once
  more when the window closes.
- Seller names are never captured. With a chosen area, anything after the numbers on a row is dropped, never kept.
- The live preview outlines the Market table in mint next to the chat (blue) and the zone (gold).

The main-board layout comes from a real screenshot of the Market; the listing-table layout from the game's known layout. `KinahLedger market-test --image
shot.png` runs it over a screenshot of the Market and prints what it finds, which is how to check it against the real
window (`--region x,y,w,h` reads an area the way a chosen one is read).

### Build

Needs the .NET 8 SDK once (`winget install Microsoft.DotNet.SDK.8`), then:

```
cd tracker/KinahLedger.Tracker
dotnet build -c Release
dotnet publish -c Release
```

`dotnet publish` writes one self-contained `KinahLedger.exe` to `dist/` with the .NET runtime inside, so players
install nothing. It isn't code-signed yet, so Windows SmartScreen shows a warning the first time it runs; a
signing certificate removes that. There are no drivers or other prerequisites.

### Commands

```
KinahLedger locate                       find the game, the chat, the zone and the Market; saves an annotated frame
KinahLedger pick                         drag a box around the chat's loot lines (remembered)
KinahLedger ocr-test [--save]            read the box once and show what was recognized
KinahLedger watch --activity "name"      a farming run in the console, Market reader beside it (--server "Siel (Elyos)", --no-market)
KinahLedger market-watch                 only the Market reader, as the window runs it in the background
KinahLedger parse-test                   self-test of the chat-line parser
KinahLedger market-test                  self-test of the Market row parser
KinahLedger market-test --image shot.png find the Market in a screenshot and read its rows
KinahLedger ui-shot captures/ui --demo   render the window to PNG (--pets, --gathering, --market, --compact, --welcome, --skin night|abyss|daylight)
KinahLedger market-shot captures/ui      the window with the Market reader in its tracking state
KinahLedger reset-id                     new random install ID
```

### What a shared run contains

Server (region and world, as chosen in the window, so drops can be kept per server), activity, gear score
bracket, minutes, kills, Kinah (tradeable and bound), item counts by ID, app version, and a random install ID
used only to drop duplicates and spam. Never the character name, account, class, level, chat text or
screenshots. Sharing is on by default and off with one setting; the live readout works either way.

The totals post once, when the run stops, to `https://kinahledger.com/v1/runs` (`upload.endpoint` in
`settings.json`, set by default; blank it and nothing is sent). Runs under 5 minutes or with no loot are
never sent. If the site can't be reached, the run simply stays on this PC: it is still saved to your
history and is not sent later.

### What a shared price contains

A price batch is `{app, server_key, install, asks: [{item, unit_price, qty, seen_at}]}`: the server you chose
(as `region-server`), each ask's item name, stack size, unit price and the time it was seen, the app version and
the install ID. Never seller names, the character or screenshots. Batches post whenever the window and the game
are open, run or no run, to `https://kinahledger.com/v1/prices`, derived from `upload.endpoint`;
`upload.pricesEndpoint` in `settings.json` overrides it. If the site can't be reached, the batch is retried a few
seconds later; whatever is still unsent when the window closes is dropped, and prices are never stored on this PC.

## Data

`tools/crawl-tw.js` reads NCSoft Taiwan's public item dictionary (English names, grades, categories,
icon URLs). NCSoft's EULA restricts automated access to its services, so running it is the owner's
decision. See `docs/` for the pipeline and image-source notes.

The tracker ships no icons. When a row's item has a known icon (the 46 gathering materials always; anything else
once `data/tw-items.json` sits beside the exe), the file is fetched once from NCSOFT's image server by name, the
way a browser does on their site, and kept under `%LocalAppData%\KinahLedger\icons`; a session touches a few dozen
files at most. `"ui": { "icons": false }` in `settings.json` turns this off, and rows show a lettered tile instead.
