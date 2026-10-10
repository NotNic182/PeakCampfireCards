# Campfire Cards

A roguelike card mod for PEAK. On the beach, and every time your group lights a campfire, each climber is offered
three cards and keeps one. Cards last for the whole run and change how you climb, eat, fall, heal and recover.
Almost every card has a catch. The deck has 64 cards in five rarities.

The **last climber to reach the fire picks first**, and friends see "X is picking a card..." while you choose.

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
- Card-draw cards (Double Deal, Double Down) give their bonus cards right away. Bonus cards are never card-draw cards.

## Controls
| Key | What it does |
|---|---|
| B | Blood Battery: spend it for bonus stamina (once per biome) |
| N | Safety Net: place your anchor on flat ground (once per biome) |

Both keys can be changed in the config. Cards with charges or cooldowns show their state at the bottom right.

## The cards

### Climbing

| Card | Rarity | Benefit | Drawback |
|---|---|---|---|
| Gecko Grip | Common | Climbing stamina cost x0.82. | Sprinting stamina cost x1.20. |
| Surefooted | Common | Slipping and sliding forces x0.50. | Climbing speed x0.90. |
| Ledge Dancer | Common | Wall-grab reach x1.30. | Sprint speed x0.90. |
| Mountain Goat | Uncommon | Jump height x1.25. | Fall Injury received x1.30. |
| Fast Fingers | Uncommon | Climbing speed x1.25. | Climbing stamina cost x1.20. |
| Featherfall | Uncommon | Fall Injury received x0.55. | Maximum stamina -15. |
| Overhang Artist | Rare | Regenerate 6 stamina per second while hanging motionless on a wall. | Maximum stamina -20. |
| Second Spring | Rare | Gain one extra midair jump (45 s cooldown). | Using the extra jump adds 7.5 Drowsy. |

### Environment

| Card | Rarity | Benefit | Drawback |
|---|---|---|---|
| Desert Blood | Common | Heat build-up x0.60. | Cold build-up x1.30. |
| Winter Coat | Common | Cold build-up x0.60. | Heat build-up x1.30. |
| Thornbreaker | Common | Thorns build-up x0.50. | Fall Injury received x1.20. |
| Venom Antibodies | Uncommon | Poison build-up x0.50. | Spores build-up x1.30. |
| Spore Lungs | Uncommon | Spores build-up x0.50. | Drowsy build-up x1.30. |
| Heat Shedding | Uncommon | Heat drains x2.00 faster once you leave the heat. | Poison drains x0.75 as fast. |
| Frostwalker | Uncommon | Cold drains x2.00 faster once you leave the cold. | Hunger build-up x1.25. |
| Toxic Adaptation | Rare | Climbing stamina cost x0.75 while Poison or Spores is at least 20. | Poison and Spores drain x0.70 as fast. |

### Inventory

| Card | Rarity | Benefit | Drawback |
|---|---|---|---|
| One-Handed Scout | Common | Weight of the item in your hand x0.50. | Climbing while holding an item costs x1.20 stamina. |
| Ultralight | Uncommon | Carried item weight x0.65. | Sprint speed x0.90. |
| Lucky Lunch | Uncommon | Your first cooked food in each biome is not used up. | All food removes Hunger x0.75. |
| Camp Chef | Uncommon | Cooked food removes Hunger x1.40. | Eating uncooked food adds 7.5 Poison. |
| Supply Scout | Uncommon | A Bandage appears for you at each new campfire. | Maximum stamina -10. |
| Pack Rat | Rare | One extra inventory slot. | Carried item weight x1.35. |
| Rope Baron | Rare | A Rope Spool appears for you at each new campfire. | Climbing stamina cost x1.12. |
| Double Deal | Legendary | Immediately gain 2 more random cards. | One inventory slot is locked for the rest of the run. |

### Mutation

| Card | Rarity | Benefit | Drawback |
|---|---|---|---|
| Loaded Dice | Uncommon | Each biome: 60% chance of +25 bonus stamina. Item weight always x0.90. | Otherwise you take 20 Injury. |
| Momentum Fiend | Uncommon | After 2 s of sprinting, sprint speed x1.25 until you stop. | Stopping after the boost adds 7.5 Drowsy (30 s cooldown). |
| Packless Nomad | Uncommon | Without a backpack: sprint speed x1.25, jump height x1.20. | With a backpack: sprint speed x0.85. |
| Rocket Ankles | Rare | Sprint speed x1.30 and jump height x1.20. | Fall Injury received x1.80. |
| Cursed Crown | Rare | +30 bonus stamina now and +15 at every later campfire. | +10 Curse now and +5 at every later campfire. |
| Delayed Consequences | Rare | 75% of fall Injury is delayed 20 s, then only 70% of it lands. | Maximum stamina -10. |
| Blood Battery | Legendary | Activate for 50 bonus stamina (once per biome). | Activating it adds 25 Injury. |
| Glass Heart | Legendary | Maximum stamina +35. | All Injury received x1.75. |

### Mythic

| Card | Rarity | Benefit | Drawback |
|---|---|---|---|
| The Mountain's Heir | Mythic | Climbing costs no stamina. No fall Injury. Sprint speed x1.25. | None |
| The Eternal Campfire | Mythic | Within 15 m: +35 max stamina, x0.40 Injury and status build-up. Once per biome, revives the first scout to go down. | None |

### Stamina

| Card | Rarity | Benefit | Drawback |
|---|---|---|---|
| Marathon Legs | Common | Sprint stamina cost x0.75. | Carried item weight x1.20. |
| Quick Recovery | Common | Stamina starts regenerating 0.5 s sooner. | Stamina regeneration x0.80. |
| Half Tank | Common | +20 bonus stamina whenever a new campfire is lit. | Hunger build-up x1.20. |
| Deep Lungs | Uncommon | Maximum stamina +20. | Hunger build-up x1.35. |
| Quiet Breathing | Uncommon | Stamina regeneration x1.50 while crouched and still. | Sprint speed x0.90. |
| Cardio Tax | Uncommon | Sprint speed x1.20. | Maximum stamina -12.5. |
| Second Wind | Rare | Restore 30 stamina when it falls below 20 (once per biome). | Triggering it adds 10 Drowsy. |
| Pulse Climber | Rare | After 3 s of climbing, climbing stamina cost x0.50 for 1 s. | Stamina regeneration on the ground x0.80. |

### Survival

| Card | Rarity | Benefit | Drawback |
|---|---|---|---|
| Bone Plating | Common | Non-fall Injury received x0.70. | Injury healed by items x0.75. |
| Iron Stomach | Common | Food removes Hunger x1.30. | Every food you eat adds 5 Drowsy. |
| Efficient Medic | Uncommon | Injury healed by items x1.40. | Poison build-up x1.25. |
| Pain Engine | Uncommon | Climbing and sprinting stamina cost x0.80 while Injury is at least 20. | All Injury received x1.20. |
| Cleansing Ritual | Uncommon | Curative items also remove 10 from your worst other status. | Each curative use adds 5 Hunger. |
| Emergency Purge | Rare | Remove 25 Poison, Spores or Drowsy when that status reaches 35 (once per biome). | Maximum stamina -10. |
| Curse Bargain | Rare | Curse build-up x0.40. | All Injury received x1.25. |
| Phoenix Clause | Legendary | Survive one fatal fall: return to a recent safe foothold, revived (once per run). | Return with 40 Injury and skip your next card pick. |

### Team

| Card | Rarity | Benefit | Drawback |
|---|---|---|---|
| Rescue Harness (team) | Common | Carrying a scout weighs x0.50. | Sprint stamina cost x1.15. |
| Heat Huddle (team) | Common | Huddling radius and cold removal x1.50. | Cold build-up x1.30 while away from every teammate. |
| Long Arm (team) | Common | Helping-hand reach x1.40. | Your climbing speed x0.90. |
| Belay Buddy (team) | Uncommon | You and teammates within 6 m spend x0.88 stamina climbing. | Alone, your climbing stamina cost is x1.15. |
| Field Medic (team) | Uncommon | Healing items you use on teammates heal x1.50. | Healing items you use on yourself heal x0.65. |
| Camp Choir (team) | Uncommon | Teammates within 6 m regenerate stamina x1.15. | Alone, you regenerate stamina x0.85. |
| Hero's Arrival (team) | Rare | First revive of a teammate each biome: you both gain 25 bonus stamina. | Maximum stamina -10. |
| Shared Burden (team) | Rare | Teammates within 6 m take x0.75 Injury. | You absorb half of what it prevents (max 10 per hit). |

### Wildcard

| Card | Rarity | Benefit | Drawback |
|---|---|---|---|
| Hungry Hands | Uncommon | Eating with at least 20 Injury removes 10 Injury. | All food removes Hunger x0.70. |
| Scavenger's Instinct | Rare | See food, items and luggage within 15 m through terrain. | Hunger build-up x1.30. |
| Stoneblood | Rare | Petrify build-up x0.50. | Bonus stamina received x0.75. |
| Double Down | Legendary | Immediately gain 1 more random Common, Uncommon or Rare card. | +10 Curse now and only 1 card to choose at the next campfire. |
| Safety Net | Legendary | Once per biome, place an anchor: if you go down, you are revived there. | Maximum stamina -20. |
| Mountain's Bargain | Legendary | Sacrifice one of your cards for +35 maximum stamina. | +5 Curse at every later campfire. |

## Playing together
Co-op for up to 4, exactly like PEAK: host a game and invite friends through Steam. Every player needs the mod,
with the same card settings (you get a warning when they differ). The host deals the offers, approves Eternal
Campfire revives and spawns campfire items. Late joiners receive everyone's cards.

## Config
`BepInEx/config/notnic.campfirecards.cfg`:

- `General`: `Sounds`, `PickTimeoutSeconds` (a random card is taken after this many seconds, default 60).
- `Keys`: `BloodBattery`, `SafetyNet`.
- `Cards`: `OfferCount`, `AllowDuplicates`, `MythicsEnabled`, `StaminaCostReductionCapPercent` (60), `MaxStaminaFloor` (50).
- `Rarity Weights`: one weight per rarity.
- `Card - <Name>`: `Enabled`, every modifier value, and every parameter (thresholds, amounts, cooldowns, radii).
  Statuses and stamina use the game's units, where 0.1 is 10.
- `Debug`: `VerboseLog`, and `DevKeys` for testing (F9 lights the current campfire, F10 warps you to it,
  F11 shows a sample offer, F8 lets the host grant the card named in `GrantCard`).

## Known limitations
- **Pack Rat's extra inventory slot is not built.** PEAK reserves slot 3 for the backpack throughout its
  inventory, hotbar, HUD and network code, so a fourth pocket would break every player's inventory. Pack Rat is
  therefore off by default; if you turn it on, only its weight drawback applies.
- Double Deal's lock never deletes anything: an item already in your third pocket stays there.
- Lucky Lunch keeps the food in hand. Food that leaves a peel spawns no peel that time.
- Field Medic, Bone Plating and Efficient Medic scale the Injury that items heal. Healing from the Healing Amulet
  and dart is not scaled.
- Cleansing Ritual counts medical items (bandages, first aid, antidote, cure-all, roots, aloe) used on you;
  berries and packaged snacks are not curative.
- Scavenger's Instinct marks items with labels drawn over the screen rather than outlines.
- With more than 100 maximum stamina, PEAK's stamina bar draws past its frame. This is only cosmetic.
- Other players see your card speed boosts with a small delay (their copy of your scout is corrected by the game).
- Continuing a quicksaved run starts with no cards.
- Revives from the Book of Bones and the Scout Effigy do not count for Hero's Arrival.

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

## Credits
Design and code: NotNic. PEAK is by Aggro Crab and Landcrab; this is a fan mod and is not affiliated with them.

## License
MIT, see `LICENSE`.
