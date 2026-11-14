# Tracker redesign proposal (2026-11-14)

Every `after-*.png` here comes from the real window. `ui-shot <dir> --proposal [--skin daylight]` (`Ui/MainWindow.Proposal.cs`) fills it with the demo and rearranges it into the proposed layout. Nothing is hand-drawn. The `before-*.png` files are 0.10.84 from the usual sweep. `after-overlays-in-game.png` places three real overlay renders over a blurred game frame; the parser overlay is PR #217's own render.

## What's wrong today (audit of 0.10.84)

- **Nav:** four tabs (Farm, Craft, Runs, Quests). Every feature added since has gone onto one of them, mostly onto Farm. Farm now holds the hero, the session's loot ("The bag"), Inventory, Gear worn, Next up, Just dropped and Where to farm (`before-farm.png`).
- **The world boss timers have one entry point:** Runs › ⋯ › Timers. No tab, shortcut or tray item reaches them.
- **Two different things are called "The bag":** the session's pickups and the character's inventory.
- **Several things show up in more than one place:**
  - Next up: Farm, Craft and Quests.
  - Goals: Farm and Quests.
  - Always on top: the footer, Farm's ⋯ menu and Options.
  - Share: Farm's ⋯ menu and the Runs header.
- **Dead UI:** `TablesCard` (drops from kills, crafting materials) is still filled but always collapsed.
- **Overlay settings are scattered:** the Kinah overlay's look is in Options › In game, and the parser overlay (PR #217) is set from its own page. A timers overlay has no home yet.

## The proposal

**One nav row with seven pages, each feature in exactly one of them** (`after-farm.png`):

| Page | What lives there | Comes from |
|---|---|---|
| **Farm** | The live run: hero (rate, ring, progress to best, Kinah/loot/kills with "here"), Loot this session, Just dropped, then two one-line chips: Next up and the better place to farm | Farm, slimmed; Gathering and Market stay automatic modes of it |
| **Runs** | Expeditions and events, Today/Week, Day wrapped | Runs, unchanged; Timers leaves its ⋯ menu |
| **Craft** | Unchanged | Craft |
| **Character** | Gear worn: all 14 slots in the game's order with +levels, a dashed slot for anything not worn or not yet named, and the average level. Bag: worth, sellable, Kinah held, change since you logged in, every kind by worth, the top 5 with prices. Character bag only, with a Bag / Storage / Both switch | Inventory + Gear worn, moved off Farm |
| **Timers** | Alert settings (minutes before, chime), the event you're in, Belled, Events, then world bosses by map (Verteron / Altgard / Abyss). Every row has a bell. Overlay switch and Pop out in the page head | The Timers window, now a page |
| **Parser** | PR #217's page and its overlay switch, as built | PR #217 |
| **Quests** | Unchanged; the full Next up lives here | Quests |

**One rule for overlays:** each overlay is separate, optional, draggable, and has a right-click menu with its settings.
- The header has an **Overlays** group (Kinah · Timers · Parser) with a lit dot when one is on. Any overlay can be switched from any page in one click.
- **Options › Overlays** (`after-overlays.png`) replaces "In game" and has one block per overlay: look or mode, what it shows, click-through, and "Place them".
- The page that owns an overlay also has an Overlay switch in its page head (Timers, Parser).

**Timers, as Steven specified on 2026-11-14:**
- Belled by default: Soul Ruler Kashapa, Eternal Gartua, High Commander Lagta, and every Abyss world boss. Every other boss is one bell click away.
- **Pop out** opens the full list (every boss and event) as its own resizable window for a second monitor.
- **The in-game timers overlay** (`after-timers-overlay.png`) stays hidden until a belled boss or event is 5 min away. The lead time can be set to 1, 3, 5 or 10 min. It then pops up with a soft chime and a draining bar, shows what comes next, and hides once the boss is up.
- The Timers tab shows the next belled countdown (`4:52`), so the page can't get lost again.

**Look:** the same v2 tokens (Theme.xaml) in Night and Daylight (`after-*-daylight.png`): cards with a 1 px line, gold for money and timing, mint for "on now", Inter. Sections that are lists use two columns of equal rows, so there are no ragged gaps.

## Decisions for Steven

1. **Pages across the top (recommended, `after-farm.png`) or down a left rail (`after-farm-rail.png`)?** The rail adds 70 px of width; the top row adds 44 px of height.
2. **What should the gear and bag page be called?** "Character" (recommended), "Gear", or "Bag & Gear".
3. **On Farm, should Next up and Where to farm stay as two one-line chips (recommended), or leave Farm entirely?** If they leave, Next up moves to Quests and Where to farm to Runs.
4. **Should the bag offer a Storage / Both switch (recommended, default Bag), or show the bag only, with no switch?**
5. **Should the Overlays group sit in the header on every page (recommended), or only in Options › Overlays?**

## Build order once approved (Phase 2, one small PR each)

1. **Shell PR:** nav row, Overlays header group, lean Farm, Ctrl+1..7, options-test layout checks updated, and `TablesCard` dropped. Repeated controls are cut to one place each (Always on top stays in Options and the footer).
2. Timers page: it hosts the world-boss worker's watched set, bells, Pop out, and the timers overlay with its chime.
3. Character page: built after the bag-only and gear-icon fixes land.
4. Options › Overlays.
5. Parser tab: it slots in when PR #217 merges.

Each PR gets before/after sweeps and keeps options-test, parse-test, packet-test, market-test and the bench green.
