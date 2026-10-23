# Code signing policy

Releases are signed with [Microsoft Artifact Signing](https://learn.microsoft.com/azure/artifact-signing/) (formerly
Trusted Signing), under the identity of the project's maintainer, validated by Microsoft.

**Status:** being set up (docs/signing-setup.md). Until it is, releases are unsigned and Windows SmartScreen shows a
"Windows protected your PC" notice the first time the tracker or its installer runs (More info › Run anyway).

## What is signed

`KinahLedger.exe` (the single-file tracker), `KinahLedger-Setup.exe` (the installer) and the program files the
installer puts on the PC, all built from this repository. Every signed file is built from a version tag by
[`.github/workflows/release.yml`](.github/workflows/release.yml), on GitHub-hosted runners, with no steps run anywhere
else. Nothing built outside that workflow, and no third-party program, is ever signed.

## Who can sign

| Role | Who |
|---|---|
| Committers and reviewers: may change the source; review every change from anyone else before it is merged | [@StevenMacchia](https://github.com/StevenMacchia) |
| Release: pushes the version tag that starts a signed build | [@StevenMacchia](https://github.com/StevenMacchia) |

The workflow signs in to Azure through GitHub's OpenID Connect, with no password or key stored in the repository,
and the Azure side accepts that sign-in only from this repository's version tags. Team members use multi-factor
authentication for GitHub and Azure.

## Privacy

The tracker reads only what the game shows on the screen: the chat area, the zone name on the minimap, the target
bar and, when it is open, the Market window. It never reads game memory or files, never captures network traffic
and never sends input to the game. It sends data to these places, and nowhere else:

- **kinahledger.com, only while sharing is on.** The first-run screen asks before anything is read or sent, with
  sharing switched on; Options › Sharing turns it off at any time.
  - When a run ends: your server, what you farmed and where, gear score bracket, run length, kill count, Kinah
    picked up, item counts, the app version and a random install ID used only to stop duplicates and spam
    (`https://kinahledger.com/v1/runs`).
  - While the Market is open: the listings on your screen, item, quantity and unit price, never seller names
    (`https://kinahledger.com/v1/prices`).
  - With each shared run (since 0.8.12): counts of how reading went (reads that didn't line up, names guessed or not
    found, pickups only the loot list saw, crafting panel counts), the item names the tracker couldn't read exactly
    (names only), and where the chat sits as fractions of the game window with the window's size, with the app version
    and the install ID (`https://kinahledger.com/v1/accuracy`). At start and every few hours the tracker reads small
    reading settings and the staged rollout of new versions; only the app version goes with that request
    (`https://kinahledger.com/v1/config`).
  - Only if you turn on Options › Sharing › Send report pictures: a miscount report's pictures of the loot list and
    the crafting panel, never the chat or the whole screen (`https://kinahledger.com/v1/frames`).
  - Never your character name, account, class, level, chat text or screenshots of the chat or the whole screen.
  - If you paste the profile link code into a profile on the site, runs from that PC show there; profiles are
    private unless you make them public.
- **kinahledger.com, for reference data.** The item list (`/data/items.json`) and your server's price board
  (`/prices/<server>.json`) are downloaded so the tracker can name items and value them (the item list only while icons or sharing are on). Only the app version goes
  with the request.
- **Item and enemy pictures.** Item icons come from NCSOFT's image server (`assets.playnccdn.com`) and enemy
  pictures from MetaBot.GG (`metabot.gg`), by file name, the way a browser loads them, and are cached on the PC.
  Nothing about you goes with them. `ui.icons: false` in `settings.json` turns them off.
- **GitHub, for updates.** At start and every six hours the tracker asks
  `api.github.com/repos/StevenMacchia/kinah-ledger/releases/latest` for the newest release and downloads it from
  GitHub when there is one (the installed tracker downloads only what changed). At the same times it checks
  `raw.githubusercontent.com/StevenMacchia/kinah-ledger/main/data/` for newer game data: zones, enemies, loot tables,
  skins, recipes. Only the app version goes with these requests. `update.check: false` in `settings.json` turns both
  off.

Those sites have their own privacy policies:
NCSOFT, [MetaBot.GG](https://metabot.gg/),
[GitHub](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement),
and [Cloudflare](https://www.cloudflare.com/privacypolicy/), which hosts kinahledger.com.

Settings, run history, logs, the downloaded game data and your watched items stay on the PC under
`%LOCALAPPDATA%\KinahLedger`.

The installer puts the program in `%LOCALAPPDATA%\KinahLedgerTracker` for your Windows user only (no administrator
rights), with Start menu and desktop shortcuts and a `kinahledger://` link the website can open. Uninstalling from
Windows Settings › Apps removes the program, the shortcuts, the link and the Start with Windows entry; delete
`%LOCALAPPDATA%\KinahLedger` too to remove your settings and history. The single-file `KinahLedger.exe` installs
nothing: deleting it and that folder removes it completely, apart from an optional Start with Windows entry
(Options › General) that it removes when switched off.
