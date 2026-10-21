# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

**Status:** the application to SignPath Foundation is pending. Until it is approved, releases are unsigned and Windows
shows an "unknown publisher" notice the first time the tracker runs. Once approved, Windows names the publisher as
SignPath Foundation.

## What is signed

Only `KinahLedger.exe`, the Windows tracker built from this repository. Every signed file is built from a version
tag by [`.github/workflows/release.yml`](.github/workflows/release.yml), on GitHub-hosted runners, with no
steps run anywhere else. Nothing built outside that workflow, and no third-party program, is ever signed.

## Team roles

| Role | Who |
|---|---|
| Committers and reviewers: may change the source; review every change from anyone else before it is merged | [@StevenMacchia](https://github.com/StevenMacchia) |
| Approvers: approve each signing request | [@StevenMacchia](https://github.com/StevenMacchia) |

Every signing request is approved by hand before it is signed. Team members use multi-factor authentication for
GitHub and SignPath.

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
  - Never your character name, account, class, level, chat text or screenshots.
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
  GitHub when there is one; only the app version goes with the request. `update.check: false` in `settings.json`
  turns this off.

Those sites have their own privacy policies:
NCSOFT, [MetaBot.GG](https://metabot.gg/),
[GitHub](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement),
and [Cloudflare](https://www.cloudflare.com/privacypolicy/), which hosts kinahledger.com.

Settings, run history and logs stay on the PC under `%LOCALAPPDATA%\KinahLedger`. Deleting that folder and the exe
removes the tracker completely; it installs nothing else, apart from an optional Start with Windows entry
(Options › General) that it removes when switched off.
