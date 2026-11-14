# Tracker redesign proposal (2026-11-14, revised)

Steven loved the main page as it was before Inventory and Gear worn were added to it, so it stays exactly that. This proposal only decides **where everything else lives**, how a player finds it, and the overlays.

The `after-*.png` files come from the real window. `ui-shot <dir> --proposal [--skin daylight]` (`Ui/MainWindow.Proposal.cs`) fills it with the demo and changes only what is listed below. The `before-*.png` files are 0.10.84. `after-overlays-in-game.png` places real overlay renders over a blurred game frame; the parser's is PR #217's own render.

## Where each thing lives

| Feature | Home | How a player gets there |
|---|---|---|
| Live farming (hero, The bag, Next up, Just dropped, Where to farm) | **Farm**, as 0.10.80 drew it (`after-farm.png`) | the Farm pill |
| Crafting | **Craft**, unchanged | the Craft pill |
| Expeditions, events, Today/Week, Day wrapped | **Runs**, unchanged except Timers leaves its ⋯ menu | the Runs pill |
| Event and world boss timers (incl. the Abyss) | **Timers** page (`after-timers.png`) | the **Timers** pill, which shows the next belled countdown (`4:52`) so it can't be missed |
| Boss parser | **Parser** page (PR #217) | the Parser pill, shown while the parser is on |
| Quests | Quests page, unchanged | Next up's header on Farm (it links there already); no pill of its own |
| Inventory | its own window (0.10.85 stopgap). It opens on **this session's drops**, and "The bag" (bag only, no storage) is a toggle the player must pick | Farm's ⋯ menu |
| Gear worn | in no page: "<Name>'s gear" (icons, +levels) at the top of the character switcher | click your name in the header |
| The site | a footer link beside Discord | footer |

The header keeps its look: the name chip on the left, the page pills, then the round buttons. Five pills fit beside the name: Farm · Craft · Runs · Timers · Parser. To make room, the globe button moved to the footer as "kinahledger.com". Without the parser there are four pills.

## Overlays: three, each separate and optional

- **One round Overlays button** in the header (`after-overlays-menu.png`). It shows a green dot while any overlay is on and opens one switch per overlay. Each switch says where that overlay is set up:
  - **Kinah:** its look stays in Options › In game.
  - **Timers:** set up on the Timers page.
  - **Parser:** set up on the Parser page (parser.json).
- **Timers overlay** (`after-timers-overlay.png`): one minimal panel for events and world bosses, parked out of the way. It stays hidden until a belled one is 5 min away (1/3/5/10), then chimes softly and shows the countdown, with what comes next under it. Belled by default: Soul Ruler Kashapa, Eternal Gartua, High Commander Lagta, and every Abyss world boss. Any other boss or event is one bell click away on the Timers page.
- **Pop out** on the Timers page opens the full list (every boss and event) as its own window for a second monitor.
- Each overlay can be dragged, remembers its place, and has a right-click menu (hide, click-through).

## Decisions for Steven

1. **Timers as a header pill with its countdown (recommended), or a round icon button?** The round button is today's stopgap.
2. **Quests without a pill, reached through Next up (recommended), or keep its pill?** A sixth pill doesn't fit beside the name.
3. **The site link in the footer (recommended), or keep the globe button and shorten the server name in the header?**
4. **One Overlays button in the header (recommended), or overlay switches only on each overlay's own page?**

## Build order once approved

1. **Header PR:** Timers pill with its countdown, Parser pill shown while the parser is on, Quests pill removed, the Overlays button and menu, the site link in the footer, and the stopgap Timers button dropped. options-test layout checks are updated.
2. **Timers page:** hosts the world-boss worker's watched set, bells, Pop out and overlay settings.
3. **Parser pill:** when PR #217 merges.

Each PR comes with before/after sweeps and keeps options-test, parse-test, packet-test, market-test and the bench green.
