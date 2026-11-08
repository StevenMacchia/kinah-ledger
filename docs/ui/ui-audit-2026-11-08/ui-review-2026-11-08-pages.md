# Window review 2026-11-08 (pages) · sweep folder
64 renders, 8 pages, states demo/compact/full/scale-80/scale-150/daylight/abyss (+13 Events states); all figures checks pass, no trimmed labels except the known long-names place. Quests and Craft renders are one window tall (Quests list is cut at the window bottom; the rest of the list, any pins/streaks lower down were not visible in any render).

## Findings, ranked
1. [runs] [all demo-like states, all skins] item 1 · the same Kinah total is drawn four to five times: TODAY strip, "N runs · X made" heading, hero "a run", the Runs row, "Paid out here" (one-run: 676.2K five times; demo: 6.37M in heading and Paid out here) · crop 24,66-684,170 and 24,820-684,860 in events-demo.png; 24,66-684,170 and 24,500-684,545 in events-one-run.png
2. [runs] [every run state] item 1 · the run clock is drawn three times in the hero: "Paid · 5:48 in" pill, the ring "5:48 RUN 11", and "run 11 open since" + list row "Run 11 · open" · crop 470,200-670,290 in events-demo.png
3. [farm, gathering, market] [all 7 states each] item 1 · bag head value (894K / 548.9K) repeats the stat tiles above; the Kinah coin tile (632.3K) repeats "632.3K Kinah picked up" · crop 24,215-684,360 in farming-demo.png (gathering-demo.png 548.9K twice)
4. [farm, gathering, market] [all] item 1/5 · three drawings of one comparison: "131.4K an hour to your best here", the flag 1.4M and the 91% ring; the sentence reads as a gap but has no verb · crop 24,64-684,200 in farming-demo.png
5. [quests] [all 7] item 3/4 · "Your score" is four tiles of "0 pts" while quests are visibly progressing, and "Active" says nothing yet; the top third of the page is empty-state · crop 24,125-684,300 in quests-demo.png
6. [quests] [all 7] item 8 · progress bars appear on some rows (Kukuru, Orichalcum Ore, gathers) and not others (Fine Orichalcum, Cypri, Inina, 100M) with no visible rule; row heights jump · crop 24,455-684,775 in quests-demo.png
7. [quests, craft] [all] item 8 · "Two are on Next up; pin one to put it there" but Farm and Runs show no Next up; only Craft has one (a craft goal). Which two, and where, can't be told · crop 24,92-440,112 in quests-demo.png and 24,260-684,385 in crafting-demo.png
8. [quests] [all 7] item 1 · same progress shown twice: "300 gathers 211" daily and "1,000 gathers 211" all time; "2,865 so far" for both 500K today and 100M all time · crop 24,330-684,770 in quests-demo.png
9. [all pages] [all states] item 7/8 · header: Farm shows four icon buttons (unlabelled mode toggle, globe, share, settings), other pages two; the tab group slides ~75px between Farm and the rest · crop 0,0-706,60 in farming-demo.png vs events-demo.png
10. [craft, quests] [all] item 8 · the trophy icon means Next up, Best crafts today, Your score and the Quests tab; "Back to even" (red glowing bar) frames a loss as a goal · crop 24,260-684,385 in crafting-upgrades-demo.png
11. [craft] [demo, compact, full, scales, skins] item 7/9 · "Best crafts today" is a closed card with a chevron and no content, last on the page; empty Craft is four stacked headed cards for one sentence each · crop 24,812-684,855 in crafting-demo.png; 24,60-684,375 in crafting-empty-demo.png
12. [runs] [unpriced, one-run, unread] item 7/3 · a full gold bar with "at the average" when there is one average; list rows end in "–" · crop 40,300-580,320 in events-unpriced.png; 490,440-670,520 in events-unread.png

## Broader UX
- Headers inconsistent since restyle: Quests has a title + subtitle, Runs a TODAY strip above a title, Farm and Craft no title, only a hero. Pick one pattern (hero or title, not both kinds).
- Totals sit in four places: hero (Farm/Craft), card head right (Runs strip, Paid out here, bag), heading subtitle (Runs), tiles (Farm). Same for scope switches: Runs has Today/Week and This session/All time stacked.
- One thing per page at a glance: Farm = an hour (works); Craft = profit (works); Runs = an hour per place but the page's first thing is the TODAY strip; Quests = nothing (score is 0, Active empty), so the answer to "what do I do next" is below the fold.
- Density: Runs is eight cards tall in demo; Farm "Just dropped" repeats the bag; cut candidates are the TODAY strip when the hero is the same place, and Recipes this session vs Just crafted on Craft.
- Quests gamification: point totals (and "sent to your profile") are noise until points exist and are explained; per-item 5-pt farm quests (Cypri, Inina) are a long list of near-identical rows; keep daily goals and the one nearest to done, collapse the rest.
- Navigation: Quests tab does not tell which page a quest's "Next up" lives on; chevrons on every quest row go to an unknown place.

## Known leftovers status
- "pts" unexplained: still there
- lime/lilac/mint bar colours, no legend; chevrons alike: still there (Quests also mixes lime, mint, lilac)
- "Paid · 5:48 in" unnamed: still there
- "a run" for Kinah and time: still there (events-demo, one-run)
- long place names trim, lose chevron: still there (events-long-names)
- "no prices yet" without when: still there (events-unpriced)
- Daylight Faint tone and solid loot tiles: still there (farming-daylight, events-daylight)
- "..." button unnamed: still there (Farm, Gathering, Market, Runs)
- "42 min" twice on Farming: not visible in the demo render (once in the hero); not confirmed
- Crafting profit twice: still there (header +156.5K and Recipes sum)
- Splendent sub-rows' empty columns: not shown in any of these renders; not confirmed
