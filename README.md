# Campfire Cards

A roguelike card mod for PEAK. On the beach, and every time your group lights a campfire, each climber is offered
three cards and keeps one. Cards last for the whole run and change how you climb, eat, fall, heal and recover.
About half of the cards have a catch. The deck has 64 cards in five rarities.

By default everyone chooses at the same time, so nobody waits for three other players. The **last climber to reach
the fire still comes first**: they are dealt first (first claim on a Mythic) and head the pick order shown at the top
of the screen. With `PickMode = Turns` the last climber picks first and the others see "X is picking a card..." while
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
| Common | gray | 48.0 |
| Uncommon | green | 32.0 |
| Rare | blue | 16.5 |
| Legendary | purple | 3.0 |
| Mythic | gold | 0.5 |

- There are two Mythics. They have no drawback, can go past the normal limits, and each can be owned by only one
  player per run.
- Percentage effects stack by multiplying. Flat maximum-stamina changes add up.
- Normal cards together can cut a stamina cost by at most 60%, and cannot pull maximum stamina below 50.
- Team cards (marked "team" below) only appear when you play with others.
- Cards survive being revived. Everything resets when the run ends.
- Ghosts are not dealt cards. Campfire eligibility is captured when the fire is lit, so being revived afterward
  does not grant a card from that same fire. A living scout who dies before choosing is skipped without receiving
  an automatic card; the other scouts can finish their choices.
- Card-draw cards (Clown Luggage Special, Scout's Dishonor) give their bonus cards right away. Bonus cards are never card-draw cards.

## Controls

The picker labels Benefit and Drawback. Numbers without a percent sign use points on the normal 100-point
bar; yellow stamina is temporary extra stamina. [Wording and playtest notes](docs/card-text-and-playtest.md)
explain the status terms and the supplied log.

| Key | What it does |
|---|---|
| V | Nic of Time: recover 25 stamina below 20 (five-minute cooldown) |
| K | Discount Cure-All: remove 20 from the highest Poison, Spores or Drowsy at 35+ (once per biome) |
| B | Bootleg Scoutmaster Bugle: gain 60 yellow stamina for 15 Injury (once per biome) |
| N | Budget Checkpoint Flag: place your anchor on flat ground (once per biome) |
| J | Current Build: every card you own, its benefit and drawback, which conditional effects are active right now, charges and cooldowns, and the combined result (sprint speed, jump height, stamina costs, regeneration, maximum stamina, Injury, item weight and more) |

All keys can be changed in the config. Cards with charges or cooldowns show their state at the bottom right. On
the Current Build screen, a value that depends on a condition or a teammate's aura shows what applies right now and,
next to it, the value that always applies.

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
| Piton Fingers | Common | Climbing uses 12% less stamina. | Sprinting uses 12% more stamina. |
| Mini Scout Cannon | Uncommon | Jump 30% higher. | Take 30% more fall Injury. |
| To The Bone | Common | Cold builds 35% slower while climbing blue ice. | Warming up takes 10% longer. |
| Pitonless Pit Stop | Rare | Hang still on a wall to recover 4 stamina per second. | Climb 30% slower. |
| Bing BOING | Rare | Jump again in mid-air for a double jump. Cooldown: 35s. | Each double jump adds 7.5 Drowsy. |
| Energy Drink Overdose | Uncommon | Climb 30% faster. | Climbing uses 20% more stamina. |
| Tenderfoot Insurance | Uncommon | Take 25% less fall Injury. | Dynamite deals 3 times as much Injury. |
| Daddy | Common | Grab walls from 20% farther away. | All stamina costs are 10% higher. |

### Environment

| Card | Rarity | Benefit | Drawback |
|---|---|---|---|
| Cool, Calm, and Collected. | Common | Heat builds 50% slower. | Cold builds 30% faster. |
| Just hot. | Common | Cold builds 50% slower. | Heat builds 30% faster. |
| Antidote Enthusiast | Uncommon | Poison builds 35% slower. | Heat and Cold build 25% faster. |
| ROOTS Survivor | Uncommon | Spores build 35% slower. | No drawback |
| Cactus? What Cactus? | Common | New Thorns add 40% less status, rounded up to whole thorns. | Removing Thorns takes twice as long. |
| Aloe | Uncommon | Heat wears off 60% faster. | Heat builds 15% faster. |
| Heat Pack | Uncommon | Cold wears off 60% faster. | Cold builds 15% faster. |
| Fartberry Fitness | Rare | Above 20 Poison, climbing uses 40% less stamina. | Poison wears off 30% slower. |

### Inventory

| Card | Rarity | Benefit | Drawback |
|---|---|---|---|
| Bing Bong's Baggage | Rare | All carried weight is 50% lighter, including backpacks and scouts. | Your third pocket cannot hold new items. |
| Carry-On Only | Uncommon | Held items weigh 25% less. | Backpack contents weigh 10% more. |
| Idol Hands | Common | Held items weigh 35% less. | No drawback |
| Cook It, Keep It | Uncommon | Once per biome, your first cooked meal is not consumed. | Uncooked food removes 50% less Hunger. |
| Cooking Badge Deluxe | Uncommon | Cooked food removes 50% more Hunger. | Eating cookable food raw adds 7.5 Poison. |
| Balright | Rare | Get one Balloon Bunch at the next campfire. Once per run. | All stamina costs are 10% higher. |
| Scoutmaster's Care Package | Uncommon | Get a Bandage at each new campfire after picking this card. | No drawback |
| Clown Luggage Special | Legendary | Immediately gain 2 random bonus cards. | One permanent random penalty: climbing or sprinting cost +15%, Injury +20%, or Hunger buildup +20%. |

### Mutation

| Card | Rarity | Benefit | Drawback |
|---|---|---|---|
| Scout Cannon Legs | Rare | Sprint 35% faster and jump 25% higher. | Take 40% more fall Injury. |
| Crown of Bad Decisions | Rare | Gain 30 yellow stamina now, then 20 at each later campfire. | Gain 10 Curse now, then 2 at each later campfire. |
| Pandora's Pocket Gamble | Uncommon | Inventory items weigh 10% less. Each biome: 65% chance of 25 yellow stamina. | A failed roll adds 20 Injury instead. |
| Bootleg Scoutmaster Bugle | Legendary | Press B to gain 60 yellow stamina. Once per biome. | Each use adds 15 Injury. |
| Big Lollipop Crash | Uncommon | Sprint for 2s to move 30% faster until you stop. | Stopping after a boost adds 7.5 Drowsy, at most once every 30s. |
| Piton Payment Plan | Rare | Take 25% of a fall's Injury now and 60% after 20s: 15% less in total. | No drawback |
| Glass Scout Cannon | Legendary | Gain 40 maximum stamina. | Take 50% more Injury. |
| Baggage Claim Escapee | Uncommon | Without a backpack: sprint 30% faster and jump 20% higher. | With a backpack: sprint 15% slower. |

### Mythic

| Card | Rarity | Benefit | Drawback |
|---|---|---|---|
| Scoutmaster | Mythic | Climbing costs no stamina and falls cause no Injury. Sprint 25% faster. | No drawback |
| The Eternal Campfire | Mythic | You and scouts within 15 m: maximum stamina +35; Injury and status buildup -60%. Share 1 auto-revive per biome. | No drawback |

### Stamina

| Card | Rarity | Benefit | Drawback |
|---|---|---|---|
| Cookie Addiction | Uncommon | Gain 25 maximum stamina. | Hunger builds 35% faster. |
| Nic of Time | Rare | Press V below 20 stamina to recover 25. Cooldown: 5 min. | No drawback |
| Hasty Badge Training | Common | Sprinting uses 15% less stamina. | Climbing uses 15% more stamina. |
| HOLD | Common | Stamina regeneration starts 0.3s sooner. | Lose 15 maximum stamina. |
| Meditation | Uncommon | Crouch and stay still to recover stamina 35% faster. | No drawback |
| I'm getting Scoutmastered | Uncommon | Sprint 25% faster. | Lose 12.5 maximum stamina. |
| Endurance Badge | Rare | Climb for 3s straight: climbing uses 35% less stamina for the next 1s. | No drawback |
| Marshmallow Morale | Common | Each new campfire clears your Hunger. | You cannot eat cooked food, including food fed to you. |

### Survival

| Card | Rarity | Benefit | Drawback |
|---|---|---|---|
| BingBong | Common | Every 10 min alive: a random 20s burst of Infinite Stamina, Invincibility or Low Gravity. | No drawback |
| Tara | Uncommon | Healing items remove 25% more Injury from you. | No drawback |
| Walk It Off | Uncommon | At 20+ Injury, climbing and sprinting use 25% less stamina. | Take 10% more Injury. |
| Discount Cure-All | Rare | Press K at 35+ Poison, Spores or Drowsy to remove 20 from the highest. Once per biome. | No drawback |
| BingBong Parachute | Legendary | Once per run, cancel a fatal fall and return to safe ground. | Using it skips your next card pick. |
| Gourmand's Gut | Common | Food removes 20% more Hunger. | No drawback |
| Book of Broken Bones | Rare | Reviving does not add Curse. | Take 25% more Injury. |
| Mini Tenacity | Uncommon | Medical items also remove 7.5 from your highest other status. | No drawback |

### Team

| Card | Rarity | Benefit | Drawback |
|---|---|---|---|
| Rule Zero | Uncommon | You and teammates within 6 m use 15% less climbing stamina. | With no teammate within 6 m, climbing uses 15% more stamina. |
| First Aid Badge | Uncommon | Healing items used on teammates remove 60% more Injury. | Healing items used on yourself remove 35% less Injury. |
| Human Carry-On | Common | Carried scouts weigh 35% less. | Their paused death timer drains at 15% of normal speed. Already-active timers drain 15% faster. |
| Happy Camper Huddle | Common | Huddling warms you 35% more, from 35% farther away. | With no teammate within 6 m, Cold builds 20% faster. |
| Otto-Matic Clutch | Rare | First teammate revive by feeding or statue each biome: both gain 100 yellow stamina. | No drawback |
| You're alright, you're alright | Common | Pull teammates up from 30% farther away. | Starting a pull costs 10 stamina, once per continuous reach. |
| Take One for the Troop | Rare | Teammates within 6 m take 30% less Injury. | You take half the prevented Injury, up to 10 per hit. |
| Friendship Bugle Choir | Uncommon | Teammates within 6 m recover stamina 12% faster. | With no teammate within 6 m, all stamina costs are 10% higher. |

### Wildcard

| Card | Rarity | Benefit | Drawback |
|---|---|---|---|
| Scout's Dishonor | Legendary | Immediately gain 1 random bonus card, Common to Rare. | Gain 10 Curse. Your next campfire offers only 1 card. |
| BingBong Baggage Scanner | Rare | Mark food, items and luggage within 12 m, even through terrain. | No drawback |
| Medicinal Munchies | Uncommon | At 20+ Injury, eating food removes 7.5 Injury. | No drawback |
| Budget Checkpoint Flag | Legendary | Press N on flat ground: your next pass-out revives you here. Once per biome. | Lose 15 maximum stamina. The revival leaves 40 Injury. |
| Petrify Procrastination | Rare | Petrification builds 35% slower. | Take 15% more fall Injury. |
| Fine Print | Legendary | Sacrifice one of your cards to gain 35 maximum stamina. | Gain 5 Curse at each later campfire. |

## Playing together
Co-op for up to 4, exactly like PEAK: host a game and invite friends through Steam. Every player needs the same
version of the mod with the same card settings. When they differ, both sides get a warning that names what differs
(for example "rarity weights" or "cards: Piton Fingers"). Sound, key and debug settings are your own and never count.

The host is in charge:
- It deals the offers and checks every pick: a card must come from your own offer, in the current session, once.
- It approves Eternal Campfire revives.
- It places campfire items next to the real fire; Balright is once per run, Supply Scout is once per fire.

A campfire lit while a pick is still running gets its own pick right after it. Late joiners receive everyone's cards.

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
    Version 0.5.1 upgrades an existing 30-second default to 60 once, preserving other durations. To deliberately
    use 30 seconds, set it after that first launch. Sacrifice choices follow the same host timer.
  - `PickMode`: `Simultaneous` (default) or `Turns`. The host's choice applies to everyone.
- `Keys`: `SecondWind` (V), `EmergencyPurge` (K), `BloodBattery` (B), `SafetyNet` (N), `BuildScreen` (J).
- `Cards`: `OfferCount`, `AllowDuplicates`, `MythicsEnabled`, `StaminaCostReductionCapPercent` (60), `MaxStaminaFloor` (50).
- `Rarity Weights`: one weight per rarity.
- `Card - <Key>`: `Enabled`, every modifier value, and every parameter (thresholds, amounts, cooldowns, radii).
  Statuses and stamina use the game's units, where 0.1 is 10.
- `Debug`: `VerboseLog`, and `DevKeys` for testing (F9 lights the current campfire, F10 warps you to it,
  F11 shows a sample offer, F8 lets the host grant the card named in `GrantCard`).

## Known limitations
- ROOTS Survivor retains its spores resistance; your proposed Mandrake/Zombie immunity and next-biome zombie
  spawn need verification against 2.6.b assets. See [the review](docs/card-edit-review.md).
- BingBong's native Low Gravity power floats; it does not provide unrestricted flight.
- Bing Bong's Baggage blocks new items in your third pocket while keeping existing items retrievable.
  Fresh configs enable the card; previous `Enabled=false` choices remain. Set `[Card - PackRat] Enabled = true`
  to re-enable it after upgrading. Balance version 3 refreshes numerical card defaults once.
- New source-specific hooks and native buff behavior still need a multiplayer playtest on PEAK 2.6.b.
- Cook It, Keep It keeps the food in hand. Food that leaves a peel spawns no peel that time.
- Tara, BingBong Safety Briefing and Medkit Merit Badge scale the Injury that items heal. Healing from the Healing Amulet
  and dart is not scaled.
- Mini Tenacity counts medical items (bandages, first aid, antidote, cure-all, roots, aloe) used on you;
  berries and packaged snacks are not curative.
- BingBong Baggage Scanner marks items with labels drawn over the screen rather than outlines.
- Other players see your card speed boosts with a small delay (their copy of your scout is corrected by the game).
- Continuing a quicksaved run starts with no cards.
- Revives from the Book of Bones and the Scout Effigy do not count for Otto-Matic Clutch.
- Cactus? What Cactus? cuts thorns as they stick to you; thorns already in you when you get the card, or when you leave
  the Eternal Campfire's aura, keep their value. PEAK counts Thorns in steps of one thorn, so an odd thorn is
  rounded up: one thorn alone is never removed.
- If the host leaves during a run, the new host does not know which Eternal Campfire revives were already used
  this biome (waiting campfire picks carry over to the new host).

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

Open [the card workbook](design/editable/card-editing.xlsx) and edit the blue columns on **Cards**: Name, Benefit,
Drawback, Flavor and Requested effects. All 64 cards are included, with current numbers and handlers on the
**Current code** tab. The **New ideas** tab is for new concepts. Keep ID and Key unchanged, then send the saved
workbook back to have the changes implemented. A [CSV copy](design/editable/card-editing.csv) is also available.

Changing a description alone does not change an effect. Describe gameplay changes in Requested effects with
the trigger, amount, duration and drawback. [Effect feasibility](docs/effect-feasibility.md) explains what is
already supported, where new hooks are needed, and what still requires a PEAK 2.6.b playtest.

To regenerate the editable files or make a text-only review copy:

```bash
python tools/card_catalog.py export --xlsx   # XLSX requires openpyxl; omit --xlsx for CSV only
python tools/card_catalog.py import-text design/editable/card-editing.xlsx --output build/cards-reviewed.json
```

The import writes a separate review copy, keeps mechanics unchanged, and reports gameplay requests for
implementation. Run `python -m unittest discover -s tests -p 'test_*.py'` to check the catalog round trip.

## Credits
Design and code: NotNic. PEAK is by Aggro Crab and Landcrab; this is a fan mod and is not affiliated with them.

Font: Darumadrop One, copyright 2020 The Darumadrop One Project Authors, used under the SIL Open Font License 1.1
(`DarumadropOne-OFL.txt`).

## License
MIT, see `LICENSE`. The bundled font keeps its own license, the SIL Open Font License 1.1.
