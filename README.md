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

1. In the game's chat settings, add a tab (call it Loot) that shows only System Notifications, and make it a few lines tall. Play in borderless or windowed mode.
2. Double-click `KinahLedger.exe`. Press **Pick chat area** and drag a box around that tab's text.
3. **Test read** shows what it sees; loot lines get a tick.
4. Choose what you're farming and press **Start tracking**. A small panel sits over the game; the window shows each loot line as it's read.

The same steps exist as commands for scripting (`pick`, `ocr-test`, `watch`), listed below.

Limits: it only sees what the chat shows, it can miss a line if many arrive in the same instant, and
the first read counts nothing so old lines on screen aren't mistaken for new loot.

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
KinahLedger pick                         drag a box around the chat's loot lines (remembered)
KinahLedger ocr-test [--save]            read the box once and show what was recognized
KinahLedger watch --activity "name"      live tracker with on-screen panel
KinahLedger parse-test                   self-test of the chat-line parser

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

## Data

`tools/crawl-tw.js` reads NCSoft Taiwan's public item dictionary (English names, grades, categories,
icon URLs). NCSoft's EULA restricts automated access to its services, so running it is the owner's
decision. See `docs/` for the pipeline and image-source notes.
