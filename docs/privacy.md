# What leaves the PC, setting by setting

The tracker reads the game's screen and nothing else. This page lists what it sends under each setting in Options ›
Sharing, and how long the site keeps it. The full statement is the Privacy section of
[CODE_SIGNING_POLICY.md](../CODE_SIGNING_POLICY.md#privacy).

| Setting | Default | What leaves the PC | Kept on the site |
|---|---|---|---|
| Share runs and Market prices anonymously | on | Run totals (`/v1/runs`), the Market listings on screen without seller names (`/v1/prices`), reading-health counts and names it couldn't read exactly (`/v1/accuracy`). | Folded into hourly totals; nothing per run is stored. |
| Help improve counting | on (only while sharing is on) | For each pickup two of the tracker's own checks agreed on: what it read, what it settled on (item and quantity), which checks agreed, the app version, the install ID, the window size and the text height (`/v1/labels`). Small pictures of that item's name words, icon or reward slot, at most 60 an hour (`/v1/label-crops`). | Pairs 180 days, pictures 90 days, then deleted. Stored under a salted hash of the install ID, never the link code. |
| Send report pictures | off (the row is not shown in Options yet) | With a miscount report you send: the loot list and crafting panel pictures of the last minute, never the chat (`/v1/frames`). Unchanged by Help improve counting. | Kept for turning into replay tests. |

## Help improve counting, exactly

Steven's rule (2026-10-24): the tracker learns from everyone's play without asking anyone to do anything, but never
secretly. So the setting is on by default, named plainly in Options, said once on first run with "Keep on" and "Turn
off", and honoured everywhere.

- **When a pair is made.** Only when a second witness confirmed a count: a chat Kina line and the Kinah counter's rise;
  a chat pickup and the loot list's row; a gathering material's row and the gathering bar's Success; a rare row and a
  second read of its picture; an event card slot's icon and its backing grade. A pickup only one reader saw makes no
  pair.
- **What a pair holds.** The text as read (one line, at most 96 characters; a timestamp in front dropped), the icon
  fingerprint if any, the name decoder's read if it ran, the item id, name and quantity the witnesses settled on, the
  witnesses' names, the app version, the game window's size and the chat's text height, and the time. Text with a
  sender bracket, a "Name:" chat form, or another player's loot line is refused before it is written.
- **What a picture can be.** A loot list row's name words, its icon square, a chat pickup line's item words, or one
  reward slot. `Session/Crops.cs` refuses any crop larger than the item's own bounds plus 4 pixels, any chat box or
  chat line as a whole, and any whole frame; parse-test checks it. The site refuses pictures larger than one line of
  text or one slot.
- **On this PC.** Pairs wait in `%LOCALAPPDATA%\KinahLedger\labels\pending.jsonl` (2 MB at most) and pictures in
  `labels\crops\` (8 MB at most) until the next send, every ten minutes while the window is open. A sent batch is deleted.
- **Off.** Turning the setting off (or sharing off) stops writing and sending at once and deletes the `labels` folder.
  The remote switch `labels` in the site's config stops it for everyone.
- **Whole frames** are never sent under this setting. They stay under "Send report pictures", off until you turn it on,
  exactly as before.
