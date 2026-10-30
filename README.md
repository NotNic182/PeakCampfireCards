# Campfire Cards

A roguelike card mod for PEAK. On the beach, and every time your group lights a campfire, each climber is offered
three cards and keeps one. Cards last for the whole run and change how you climb, eat, fall, heal and recover.
About half of the cards have a catch. The deck has 64 cards in five rarities.

By default everyone chooses at the same time, so nobody waits for three other players. The **last climber to reach
the fire still comes first**: they are dealt first (first claim on a Mythic) and head the deal order shown at the top
of the screen. With `PickMode = Turns` the last climber picks first and the others see "X is picking a card" while
they choose.

## You need
- **PEAK** with **BepInEx 5** ([BepInExPack PEAK](https://thunderstore.io/c/peak/p/BepInEx/BepInExPack_PEAK/)).

## Install
Copy the `plugins/CampfireCards` folder from the release zip into `PEAK/BepInEx/plugins/`, then start PEAK.

## How cards work
- Each player has their own collection, and you never own the same card twice.
- An offer is three cards; pick one. Each offered card first rolls a rarity:

| Rarity | Colour | Weight |
|---|---|---|
| Common | gray | 50.0 |
| Uncommon | green | 30.0 |
| Rare | blue | 16.5 |
| Legendary | purple | 3.0 |
| Mythic | gold | 0.5 |

- There are two Mythics. They have no drawback, can go past the normal limits, and each can be owned by only one
  player per run.
- Percentage effects stack by multiplying. Flat maximum-stamina changes add up.
- Since the 0.7.1 rebalance the cards play at full strength on every difficulty (all four tiers default to 100%). The
  tiers remain in the design sheets (cards.json difficultyStrength, the card sheet's Difficulty tab): below 100%
  weakens benefits, above amplifies them, and the card text shows the number in play. Drawbacks and Mythics never
  scale.
  When a card's numbers multiply into one benefit (amount x cooldown), the strength is shared between them.
- Stamina-cost discounts from normal cards have a soft cap at 60%: past it, extra discounts still help but at reduced
  strength (a stack worth 80% off gives about 72%). Normal cards cannot pull maximum stamina below 50.
- Team cards (marked "team" below) only appear when you play with others.
- Cards survive being revived. Everything resets when the run ends.
- Fine Print is the explicit exception to permanent ownership: it can sacrifice an earlier non-Mythic card.
  Sacrifice does not refresh that card's run/biome charges or cooldowns, or erase Clown's permanent penalties.
- Offers remain random by rarity. Earlier card choices do not introduce compatibility exclusions; counteracting
  modifiers, conditional effects and balance-breaking Mythics are intentional parts of stacking.
- Ghosts are not dealt cards. Campfire eligibility is captured when the fire is lit, so being revived afterward
  does not grant a card from that same fire. A living scout who dies before choosing is skipped without receiving
  an automatic card; the other scouts can finish their choices.
- Card-draw cards (Clown Luggage Special, Scout's Dishonor) give their bonus cards right away. Bonus cards are never card-draw cards.
- BingBong Parachute judges a fall by all the Injury it will cost, including the part Piton Payment Plan bills later,
  and cancels all of it.
- Each pick offers `OfferCount` cards (3 by default, up to 10); the picker wraps them into rows and scales to fit.
- The host can turn drawbacks off (`Drawbacks = false`): drawback stats stop applying, cards add no Curse, Drowsy,
  Poison or Injury costs, Fartberry Fitness amplifies no poison hits, and the cards show only their benefit. The host's setting applies to everyone: guests adopt the host's `Drawbacks` and `AllowDuplicates` values when they join.
- Lighter-load cards scale only positive item weight; a Balloon Bunch's lift is never reduced.
- PEAK changes statuses in whole 2.5-point steps. When a card changes an amount, the leftover is kept for the next
  change, so small percentages (a 5-point fall x1.15, 1 Curse per fire) hold on average.
- A card's own penalty (Bugle Injury, a failed Pandora roll, Curse, Drowsy) always lands: Milk or mushroom
  invincibility doesn't block it and a skeleton doesn't take it x8.
- Baggage Claim Escapee treats anything worn in the backpack slot (backpack, fannypack, jetpack, rocketpack) as a backpack.
- Version 0.6.3 fixes interaction and ability-state handling without changing card numbers or rarity weights.
  See [the interaction fixes and playtest checks](docs/card-interactions.md).

## Controls

The picker labels Benefit and Drawback. Numbers without a percent sign use points on the normal 100-point
bar; yellow stamina is temporary extra stamina. [Wording and playtest notes](docs/card-text-and-playtest.md)
explain the status terms and the supplied log.

| Key | What it does |
|---|---|
| V | Nic of Time: recover 30 stamina (five-minute cooldown) |
| K | Discount Cure-All: remove 25 from the highest Poison, Spores or Drowsy at 25+ (once per biome) |
| B | Bootleg Scoutmaster Bugle: gain a full bar of yellow stamina for 15 Injury (once per biome) |
| N | Budget Checkpoint Flag: place your anchor on flat ground (once per biome) |
| Y / U / I | TSA (hold): mark food / unopened luggage / all other items on screen, through terrain, while the key is held |
| F8 | Host only: everyone gets their cards from the saved run back after a crash, a rejoin or a relaunch |
| F9 | Report a problem: pick what went wrong, write a line, optionally attach the mod log (usernames and player names are scrubbed out first), and send it straight to the mod author |
| J | Your deck: your cards fan out as real card faces with ALL CARDS ADDED UP (the combined result: sprint speed, jump height, stamina costs, regeneration, maximum stamina, Injury, item weight and more) beneath them. Click a card to pull it out and see everything it does — benefit, drawback, which conditional effects are active right now, charges and cooldowns — and flip through the deck with < and >. Press J again for TEAM DECKS: every teammate as a pile of card backs — click a deck (or its name) to look through their cards and their combined stats. A third press closes, and Esc always backs out one step without opening the pause menu. Clicking the pile in the corner opens it too |

**With a controller**, move between offered cards with the D-pad or stick and confirm to take one. The ability keys and
the deck key also get a controller button: by default the first of D-pad up, down, left, right, View/Select or a stick
click that PEAK itself does not use. The ability bar and the hints name the button while you play on a controller, and
the log says which buttons were chosen. Set them yourself under `Keys` (`BuildScreenGamepad` and so on, for example
`<Gamepad>/dpad/left`, or `none`). The three TSA reveal keys are **keyboard-only by default** — give them controller
buttons via `ScavengerFoodGamepad` and friends if you play on a pad — and the F8 restore is keyboard-only.

After you take a card, it stays raised and the others dim until the host confirms the pick. If no answer comes within
5 seconds, you can choose again.

Your cards also live on the HUD as a little pile of card backs in the bottom-left corner — newest pick on top, a count
chip, and the deck key named underneath. The combined effects moved into the deck screen (J), where there is room to
show every line instead of a slice.

**Screen size.** The card UI fits any resolution and aspect ratio, from a Steam Deck to 32:9. `General.UiScale` sets its
size: `0` (default) is automatic and a little larger on screens 900 pixels tall or less; `0.75` to `1.5` sets it
yourself (1 = the size at 1080p).

All keys can be changed in the config. Cards with charges or cooldowns show their state at the bottom right. On
the deck screen, a value that depends on a condition or a teammate's aura is marked "on now" or "off now":
it shows what applies now and, next to it, the value that always applies.

## Your own card art

In 0.6.1, save a PNG as `art/cards/<Key>.png` beside `CampfireCards.dll`, then restart PEAK.
Example: `art/cards/GeckoGrip.png` changes Piton Fingers. Use the stable Key, not the display name.
[All 64 filenames](design/editable/card-image-map.csv) and [image instructions](docs/card-images.md) are included.
Recommended: 1024 × 576; maximum: 2048 pixels per dimension and 8 MiB per PNG.
Missing or rejected images use the original animated art. Images are local cosmetics.

## The cards

The deck includes the reviewed mechanics and the clearer 0.6.1 descriptions. [Card review](docs/card-edit-review.md) explains new mechanics and the pending ROOTS Survivor replacement.

### Climbing

| Card | Rarity | Benefit | Drawback |
|---|---|---|---|
| Piton Fingers | Common | Climbing uses 18% less stamina. | Sprinting uses 10% more stamina. |
| Mini Scout Cannon | Common | Jump 30% higher. | Take 5% more fall Injury. |
| To The Bone | Common | Cold builds 30% slower, and another 50% slower while climbing blue ice. | No drawback |
| Pitonless Pit Stop | Legendary | Hang still on a wall to recover 7.5 stamina per second. | Climb 10% slower. |
| Bing Double Boing | Rare | Jump again in mid-air for a double jump. Cooldown: 15s. | No drawback |
| Energy Drink Overdose | Uncommon | Climb 30% faster. | Climbing uses 10% more stamina. |
| Tendiefoot | Common | Take 25% less fall Injury. | Dynamite deals 50% more Injury. |
| Daddy Long Arms | Uncommon | Grab walls from 40% farther away. | All stamina costs are 5% higher. |

### Environment

| Card | Rarity | Benefit | Drawback |
|---|---|---|---|
| Cool, Calm, and Collected. | Common | Heat builds 70% slower, and you get drowsy 30% slower. | Cold builds 10% faster. |
| Just hot. | Common | Cold builds 70% slower, and huddling warms you 30% more. | Heat builds 10% faster. |
| Antidote Enthusiast | Uncommon | Poison builds 70% slower and wears off 50% faster. | Heat and Cold build 10% faster. |
| ROOTS Survivor | Uncommon | Spores build 80% slower, and Poison 45% slower. | No drawback |
| Almost Climbable Cactus | Common | Thorns hurt 80% less, and can be removed 50% faster. | No drawback |
| Aloe | Common | Heat wears off 150% faster. | Heat builds 5% faster. |
| Heat Pack | Common | Cold wears off 150% faster. | Cold builds 5% faster. |
| Fartberry Fitness | Uncommon | Poison that builds up over time (gas, venom, vile food) doesn't affect you. | Instant poison hits hurt 20% more. |

### Inventory

| Card | Rarity | Benefit | Drawback |
|---|---|---|---|
| Bing Bong's Baggage | Rare | All carried weight is 60% lighter, including backpacks and scouts. | Your third pocket cannot hold new items. |
| Carry-On Only | Common | Held items weigh 45% less. | Backpack contents weigh 5% more. |
| Super Idol Hands | Uncommon | Held items weigh 65% less. | No drawback |
| Cook It, Keep It | Uncommon | Cooked food fills you up 40% more, and once per biome your first cooked meal is not consumed. | Uncooked food removes 15% less Hunger. |
| Cooked | Uncommon | Cooked food removes 60% more Hunger. | Eating uncooked food adds 5 Poison. |
| Balright | Rare | Get a mythic item (of your choice) at the next campfire. Once per run. | No drawback |
| Scoutmaster's Care Package | Uncommon | Get a bandage, random food item, and a random item at each new campfire after picking this card. | No drawback |
| Clown Luggage Special | Legendary | Immediately gain 2 random bonus cards right now. | No drawback |

### Mutation

| Card | Rarity | Benefit | Drawback |
|---|---|---|---|
| Scout Cannon Legs | Rare | Sprint 35% faster and jump 25% higher. | Take 25% more fall Injury. |
| Crown of Bad Decisions | Common | Gain a full bar of yellow stamina now, then again at each later campfire. | Gain 3 Curse now, then 1 at each later campfire, up to 5 total. |
| Pandora's HotPocket | Uncommon | Inventory items weigh 25% less, and you gain 37.5 yellow stamina at each new biome. | No drawback |
| Bootleg Scoutmaster Bugle | Rare | Press {key} to gain a full bar of yellow stamina. Once per biome. | Each use adds 15 Injury. |
| Big Lollipop Crash | Common | Sprint for 2s to move 30% faster until you stop. | No drawback |
| Piton Payment Plan | Uncommon | Take 25% of a fall's Injury now and 60% after 20s: 15% less in total. | No drawback |
| Glass Scout Cannon | Rare | Gain 40 maximum stamina. | Take 25% more Injury. |
| Baggage Claim Escapee | Uncommon | Without a backpack: sprint 30% faster and jump 20% higher. | No drawback |

### Mythic

| Card | Rarity | Benefit | Drawback |
|---|---|---|---|
| Scoutmaster | Mythic | Climbing costs no stamina and falls cause no Injury. Sprint 25% faster. | No drawback |
| The Eternal Campfire | Mythic | You and scouts within 15 m: maximum stamina +35; Injury and status buildup -60%. Share 1 auto-revive on death per biome. | No drawback |

### Stamina

| Card | Rarity | Benefit | Drawback |
|---|---|---|---|
| Cookie Addiction | Rare | Gain 30 maximum stamina. | Hunger builds 15% faster. |
| Nic of Time | Common | Press {key} to recover 30 stamina. Cooldown: 5 min. | No drawback |
| Leg Day Skip | Common | Sprinting uses 30% less stamina. | You get hungry 10% faster. |
| HOOOOLLLD | Common | Hanging still on a wall costs almost no stamina, and you slip and slide 40% less. | No drawback |
| Meditation | Uncommon | Crouch and stay still for 3s to gain a full bar of yellow stamina. Once per biome. | No drawback |
| I'm getting Scoutmastered | Uncommon | Sprint 35% faster. | No drawback |
| Endurance Badge | Uncommon | Climb for 2s straight: climbing uses 40% less stamina for the next 3.5s. | No drawback |
| Campfire++ | Common | Each new campfire clears your Hunger. | No drawback |

### Survival

| Card | Rarity | Benefit | Drawback |
|---|---|---|---|
| Bing Boing | Common | Every 5 min alive: a random 20s burst of Infinite Stamina, Invincibility or Low Gravity. | No drawback |
| TechTara | Uncommon | Reduces fall damage by 20% and every bandage in your inventory reduces it by 20% more. (Limit 3) | No drawback |
| Walk It Off | Rare | At 20+ Injury, climbing and sprinting use 35% less stamina. | Take 5% more Injury. |
| Discount Cure-All | Common | Press {key} at 25+ Poison, Spores or Drowsy to remove 25 from the highest. Once per biome. | No drawback |
| BingBong Parachute | Rare | Once per run, cancel a fatal fall and return to safe ground. | No drawback |
| Perma-Cooking | Common | Food removes 25% more Hunger. | No drawback |
| Book of Broken Bones | Uncommon | You receive a Book of Bones right now. | No drawback |
| Mini Tenacity | Rare | Medical and food items also remove a random amount from your highest other negative status. | No drawback |

### Team

| Card | Rarity | Benefit | Drawback |
|---|---|---|---|
| Rule Zero | Rare | You and teammates within 8 m use 20% less climbing stamina. | With no teammate within 8 m, climbing uses 5% more stamina. |
| First Aid Badge | Uncommon | Healing items used on teammates and yourself remove 100% more Injury. | No drawback |
| Carry-On Scout | Common | Carried scouts weigh 75% less, and you pull teammates up from 20% farther away. | No drawback |
| Ascent 5+ | Common | Huddling warms you 75% more, from farther away, and you get cold 20% slower. | No drawback |
| Otto-Matic Clutch | Uncommon | First teammate revive by feeding or statue each biome: both gain a full bar of yellow stamina. | No drawback |
| You're alright, you're alright | Common | Pull teammates up from 40% farther away. | Starting a pull costs a base 5 stamina, once per continuous reach. |
| Take One for the Troop | Rare | Teammates within 8 m take 50% less Injury. | You take a tiny amount of the prevented Injury, up to 5 per hit. |
| Friendship Bugles | Uncommon | You and teammates within 8 m gain 10 maximum stamina. | No drawback |

### Wildcard

| Card | Rarity | Benefit | Drawback |
|---|---|---|---|
| Scout's Dishonor | Rare | Immediately gain 2 random bonus cards, Common to Rare. | Gain 5 Curse. Your next campfire offers only 2 cards. |
| TSA | Legendary | Hold {key} to mark food / luggage / other items within 56 m, even through terrain. | No drawback |
| Medicinal Munchies | Common | At 20+ Injury, eating food heals 7.5 health. | No drawback |
| Budget Checkpoint Flag | Legendary | Press {key} on flat ground: your next pass-out revives you here. Once per biome. | No drawback |
| Petrify Procrastination | Common | Petrification builds 50% slower. | No drawback |
| Fine Print | Rare | Sacrifice one of your cards to gain 35 (MORE) maximum stamina. | Gain 5 Curse when the sacrifice is made. |

## Playing together
Co-op for up to 4, exactly like PEAK: host a game and invite friends through Steam. Every player needs the same
version of the mod with the same card settings. When they differ, both sides get a warning that names what differs
(for example "offer count" or "cards: Piton Fingers"). Sound, key and debug settings are your own and never count.

The host is in charge:
- It deals the offers and checks every pick: a card must come from your own offer, in the current session, once.
- It approves Eternal Campfire revives.
- It places campfire items next to the real fire; Balright is once per run, Supply Scout is once per fire.

A campfire lit while a pick is still running gets its own pick right after it. Late joiners receive everyone's cards.

The mod also remembers the run on disk: everyone's cards are saved by player name as they change
(`BepInEx/config/notnic.campfirecards.run.txt`, with the previous run kept beside it). After a crash, a rejoin or a
relaunch, the **host** presses **F8** (`Keys.RestoreRun`) and every scout in the room gets their saved cards back —
without re-paying any pick-time costs. Restore promptly: the memory holds the current run and the one before it.

## Config
`BepInEx/config/notnic.campfirecards.cfg`:

- `General`:
  - `Sounds`.
  - `CustomFont`: show all Campfire Cards text in the bundled Darumadrop One font (default on); off uses the
    game's own font. Takes effect on the next launch.
  - `PickTimeoutSeconds`: **60 seconds by default**, with the host's value applying to everyone. A positive value
    gives you that long to choose before the host takes a random card of your offer (minimum 5 seconds).
    **Set it to `0` for unlimited time**: no countdown, automatic pick or reading-time cutoff. In Turns mode,
    everyone waits until the current scout chooses; in Simultaneous mode, each scout can choose independently.
    Sacrifice choices follow the same host timer.
  - `PickMode`: `Simultaneous` (default) or `Turns`. The host's choice applies to everyone.
- `Keys`: `SecondWind` (V), `EmergencyPurge` (K), `BloodBattery` (B), `SafetyNet` (N), `BuildScreen` (J),
  `ScavengerFood` (Y), `ScavengerLuggage` (U), `ScavengerMisc` (I) — held, not pressed, and only with TSA —
  `BugReport` (F9), and `RestoreRun` (F8, host only).
- `Reporting`: `Endpoint` (where F9 reports go; empty saves them to `plugins/CampfireCards/reports/` instead) and
  `InstallId` (a random id that groups one install's reports — it carries no account, name or system information).
  Before a report leaves your machine, the attached log is scrubbed: user-folder paths, your OS user name, every
  player name in the room, Steam ids, IP addresses and e-mail addresses are replaced with placeholders. The
  [report server](server/report-server/README.md) stores no addresses either.
- `Cards`: `OfferCount` (3; 1 to 10 cards per pick), `Drawbacks` (false = cards keep only their benefits),
  `AllowDuplicates`, `MythicsEnabled`, and `DisabledCards` (cards that are never offered, comma separated, by the
  name on the card; for the three names that themselves contain a comma, use the card key from
  `design/editable/card-editing.csv` instead, e.g. `LuckyLunch` for Cook It, Keep It). That is the whole card config on purpose: card numbers, rarity weights and the balance
  internals are not configurable — they come from the design sheets compiled into the mod, so every player on the
  same version plays the same, intended cards. Settings written by versions before 0.7.4 (the 64 per-card
  sections, `Rarity Weights`, the balance knobs) are removed from the file once on launch.
- `Debug`: `VerboseLog` (log every card event).

## Known limitations
- ROOTS Survivor retains its spores resistance; your proposed Mandrake/Zombie immunity and next-biome zombie
  spawn need verification against 2.6.b assets. See [the review](docs/card-edit-review.md).
- BingBong's native Low Gravity power floats; it does not provide unrestricted flight.
- Bing Bong's Baggage blocks new items in your third pocket while keeping existing items retrievable.
  Since 0.7.4 a card is only ever off while it is listed in `DisabledCards`.
- New source-specific hooks and native buff behavior still need a multiplayer playtest on PEAK 2.6.b.
- Cook It, Keep It keeps the food in hand. Food that leaves a peel spawns no peel that time.
- TechTara counts the Bandages in your own pockets when the fall lands (up to 3); bandages in a backpack don't count.
- First Aid Badge scales healing items you use on teammates and on yourself. The blowgun dart clears every status and isn't scaled.
- Mini Tenacity triggers on medical items used on you (bandages, first aid kit, antidote, cure-all, medicinal root, aloe,
  heat pack, the Healing Amulet) and on any food you eat; the bonus removal amount is random each time.
- Fartberry Fitness treats any poison that arrives as a steady drip (gas clouds, poison-over-time from stings or vile
  food) as gas and ignores it; discrete hits (the bite itself, blast vines, eating something vile) land 20% harder.
- Balright's mythic choice lists only items this game version can spawn; if an item ever vanishes from PEAK, it simply
  leaves the list.
- BingBong Baggage Scanner marks items with labels drawn over the screen rather than outlines.
- Other players see your card speed boosts with a small delay (their copy of your scout is corrected by the game).
- Continuing a quicksaved run starts with no cards.
- Revives from the Book of Bones and the Scout Effigy do not count for Otto-Matic Clutch.
- Almost Climbable Cactus cuts thorns as they stick to you; thorns already in you when you get the card, or when you leave
  the Eternal Campfire's aura, keep their value. PEAK counts Thorns in steps of one thorn, so the result is rounded
  to the nearest whole thorn: one thorn alone stays one thorn, two become one.
- If the host leaves during a run, the new host does not know which Eternal Campfire revives were already used
  this biome (waiting campfire picks carry over to the new host).
- An ordinary state sync preserves existing local ability state. A fresh client reconnect does not yet restore
  instance-held charges, cooldowns, anchors or queued Injury from the earlier client session.

## Building from source
Requirements: .NET SDK 8 or newer, Python 3, and PEAK with BepInEx installed (the project references PEAK's own
assemblies from its install folder; set `PEAK_DIR=<folder>` if PEAK is not in the default Steam location).

```bash
tools/build.sh            # regenerate code from the design sheets, preflight, compile, package
tools/build.sh --install  # same, then copy the plugin into your local PEAK
dotnet test tests/CampfireCards.Tests   # card logic unit tests
```

The output is `build/CampfireCards-<version>.zip`.

## How the project is organised
- `design/` holds the JSON design sheets: cards, stat channels, conditions, art motifs, sounds, game hooks, network
  events, UI and systems. They are the source of truth; `design/README.md` explains how to add a card.
- `tools/gen.py` turns the sheets into `src/CampfireCards/Generated/*.cs`; `tools/preflight.py` checks that every
  cell is filled and every cross-reference resolves before a build.
- `src/CampfireCards/Core/` is the Unity-free card logic (settings, offers, modifier stacking, timers), covered by
  the tests in `tests/`.
- `src/CampfireCards/` is the BepInEx plugin: Harmony hooks into PEAK, the deck, effects, abilities, pick sessions,
  Photon sync, the card UI, the animated card art and the synthesised sounds.

## Editing the deck

Open [the card sheet](design/editable/card-sheet.xlsx) and edit the blue cells. The **Cards** tab has each card's
name, rarity, benefit, drawback and flavor exactly as shown in game; the **Numbers** tab has every number in plain
units (+40 = 40% more, seconds, stamina points). Keep the **#** column as it is, then send the saved workbook back.

```bash
python tools/card_sheet.py export                      # regenerate the sheet from design/cards.json
python tools/card_sheet.py apply edited.xlsx --check   # show what an edited sheet would change
python tools/card_sheet.py apply edited.xlsx           # write it into design/cards.json, then tools/build.sh
```

Applying validates every value first, warns when a card's text still shows a number that was changed, and raises the
card balance version when a number changes. [Effect feasibility](docs/effect-feasibility.md) explains what is supported
beyond tweaks. Run `python -m unittest discover -s tests -p 'test_*.py'` to check the sheet round trip.

## Credits
Design and code: NotNic. PEAK is by Aggro Crab and Landcrab; this is a fan mod and is not affiliated with them.

Font: Darumadrop One, copyright 2020 The Darumadrop One Project Authors, used under the SIL Open Font License 1.1
(`DarumadropOne-OFL.txt`).

## License
MIT, see `LICENSE`. The bundled font keeps its own license, the SIL Open Font License 1.1.
