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

**Screen reading.** The game prints every pickup in chat ("Acquired 2,790 Kina.",
"Acquired Drakan hide ×2."). The tracker reads those lines off the screen with the text recognition
built into Windows, counts what's new, and shows Kinah and loot per hour in a small always-on-top
panel. Setup is dragging a box around the chat once.

1. Play in borderless or windowed mode with the chat open at the bottom-left. A chat tab showing only System Notifications reads best; the normal tab works too.
2. Double-click `KinahLedger.exe` and press **Start tracking**. The tracker finds the game window, locates the chat above the "Enter your message" box, and reads the zone name from the minimap every few seconds. If it can't find the chat, **Choose the chat** lets you drag a box yourself (the zone may not be detected in that case).
3. **Test read** shows what it sees; loot lines count in the tables.
4. Pick Open world, Gathering or Pets. Open world shows Kinah per hour and per kill, kills per hour, drops per hour, and every drop with its drop rate and per-hour rate, with item names in the game's tier colours. Gathering splits each skill (mining, logging, essence extraction, herbs, cooking ingredients) into its own section with its own nodes, Plentiful rate and per-node rates. Pets counts Soul Crystals by zone with drops and per hour only, no drop %, so the best pet-farming spot stands out; Soul Points and Soul Crystals never count as drops. Ctrl+Shift+K starts and stops from inside the game; minimizing keeps it running in the tray. Runs are saved per user with minutes, kills, Kinah and crystals per zone. The window also shows a profile link code (a short form of the random install ID); pasting it into a profile on the site attaches this PC's runs to that profile. Without it, runs stay anonymous.

The same steps exist as commands for scripting (`pick`, `ocr-test`, `watch`), listed below.

Limits: it only sees what the chat shows, it can miss a line if many arrive in the same instant, and
the first read counts nothing so old lines on screen aren't mistaken for new loot.

**Market prices (0.6.0).** While you track, the tracker also watches for the game's Market window. When it is open,
the listings on your screen (item, quantity, unit price) are read about once a second, the same way the chat is, and
queued as anonymous asks so the site can show per-server prices the way Universalis does for FFXIV. Coverage follows
what players look at: nothing is clicked or typed in the game, and no account ever reads the Market on its own.

- The Market is found by its title and the column headers of the listing table ("Item", "Quantity", "Unit Price",
  "Total Price"); the headers also give the column positions, so each cell is read on its own. If it can't be found,
  **Choose the Market** lets you drag a box around the rows; each row is then read as one line and split by its
  numbers from the right (quantity, unit price, total). Quantity × unit price has to equal the total, so a misread
  digit drops the row instead of sharing a wrong price. Digits use the chat parser's corrections (O for 0, l for 1,
  thousands separators).
- Type your server once (**Server**, e.g. `Siel (Elyos)`). It travels with the prices as `region-server`
  (`na_e-siel-elyos`) so they land on the right board; without it, asks are read but not sent.
- **Share market prices while the Market is open** is on by default and remembered; off means the Market is never
  read, and the rest of the tracker works as before. The card shows what the reader is doing: asks read, items,
  asks shared and when it last read. The same ask (item, stack, price) isn't sent twice within 15 minutes; batches
  go out every 10 seconds or 100 asks, and once more when the run stops.
- Seller names are never captured. With a chosen area, anything after the numbers on a row is dropped, never kept.
- The live preview outlines the Market table in mint next to the chat (blue) and the zone (gold).

The locator was written from the game's known Market layout, not from a screenshot; `KinahLedger market-test --image
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
KinahLedger watch --activity "name"      live tracker, chat and Market (--server "Siel (Elyos)", --no-market)
KinahLedger parse-test                   self-test of the chat-line parser
KinahLedger market-test                  self-test of the Market row parser
KinahLedger market-test --image shot.png find the Market in a screenshot and read its rows
KinahLedger ui-shot captures/ui --demo   render the window to PNG (--pets, --gathering, --market, --compact)
KinahLedger market-shot captures/ui      the window with the Market card in its tracking state
KinahLedger reset-id                     new random install ID
```

### What a shared run contains

Region, activity, gear score bracket, minutes, kills, Kinah (tradeable and bound), item counts by ID,
app version, and a random install ID used only to drop duplicates and spam. Never the character name,
server, account, class, level, chat text or screenshots. Sharing is on by default and off with one
setting; the live readout works either way.

### What a shared price contains

A price batch is `{app, server_key, install, asks: [{item, unit_price, qty, seen_at}]}`: the server you typed
(as `region-server`), each ask's item name, stack size, unit price and the time it was seen, the app version and
the install ID. Never seller names, the character or screenshots. Batches post to the `/v1/prices` route:
`upload.pricesEndpoint` in `settings.json`, or derived from `upload.endpoint` when that ends in `/v1/runs`. Both
are empty by default, so nothing leaves the PC until the site can receive them.

## Data

`tools/crawl-tw.js` reads NCSoft Taiwan's public item dictionary (English names, grades, categories,
icon URLs). NCSoft's EULA restricts automated access to its services, so running it is the owner's
decision. See `docs/` for the pipeline and image-source notes.
