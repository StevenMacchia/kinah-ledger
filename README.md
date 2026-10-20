# Kinah Ledger

A fan project for Aion 2 (global): Kinah per hour, material yields, drop rates and market prices,
built from runs players share through a free tracker. Made by players for players; not affiliated
with NCSOFT. MIT licensed.

This is a side project, separate from the author's professional work.

```
site/      the website (single-file prototype in site/index.html)
tracker/   the Windows tracker, C# / .NET 8
tools/     data tooling (item dictionary crawler, site data build)
data/      data drops (tw-items.json goes here)
docs/      notes on data sources and the pipeline
```

## Tracker status

The tracker has two modes. Both run only on the player's PC and nothing touches the game process.

**Screen reading (works today).** The game prints every pickup in chat ("Acquired 2,790 Kina.",
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

**Packet capture (built, not yet decoding).** The same passive method the community damage meters use:
Npcap captures the game's traffic, nothing is hidden from anyone. The capture, stream rebuild and
recorder work; the Aion 2 message layouts (`opcodes.json`) are not mapped, so `run` shows nothing yet.
What's built:

- finds the game's server connections from Windows' TCP table and captures only those flows
- rebuilds each direction's byte stream (retransmits, out-of-order segments, gap skipping)
- a data-driven decoder: message layouts live in `opcodes.json`, so a game patch means editing a file
- a session engine with Kinah / bound Kinah / item / kill totals and per-hour rates
- `record` mode with typed notes, and `dump` to read recordings back, for mapping the protocol
- the anonymous upload payload (see below), sent only when `upload.endpoint` is set

What's not done: the Aion 2 message layouts themselves. `opcodes.json` is empty, so `run` shows nothing
yet. Mapping them needs recordings made while playing (see "Helping map the protocol").

### Build

Needs the .NET 8 SDK once (`winget install Microsoft.DotNet.SDK.8`), then:

```
cd tracker/KinahLedger.Tracker
dotnet build -c Release
dotnet publish -c Release
```

`dotnet publish` writes one self-contained `KinahLedger.exe` to `dist/` with the .NET runtime inside, so players
install nothing. It isn't code-signed yet, so Windows SmartScreen shows a warning the first time it runs; a
signing certificate removes that. Packet mode also needs [Npcap](https://npcap.com) (defaults are fine).

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

KinahLedger devices                      capture adapters
KinahLedger connections                  the game's live connections
KinahLedger record --out run1.klrec      capture to a file; type notes + Enter
KinahLedger run                          live tracker
KinahLedger dump run1.klrec --around 4   traffic around each note
KinahLedger reset-id                     new random install ID
```

### Helping map the protocol

1. Start the game, log in, stand somewhere quiet.
2. `KinahLedger record --out loot.klrec`
3. Do one clear thing at a time and type a note right after each:
   pick up a single stack of a known material → `looted 3 Iron Ore`;
   pick up Kinah → `kinah 1,240`; kill one mob → `killed Flame Sprite`;
   enter a dungeon → `entered Fire Temple`.
4. Ctrl+C, then `KinahLedger dump loot.klrec` shows what the server sent around each note.

Recordings are raw server traffic and can include character names. They're never uploaded; share them by choice.

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
