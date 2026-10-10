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
- Card-draw cards (Double Deal, Double Down) give their bonus cards right away. Bonus cards are never card-draw cards.

## Controls
| Key | What it does |
|---|---|
| B | Blood Battery: spend it for bonus stamina (once per biome) |
| N | Safety Net: place your anchor on flat ground (once per biome) |
| J | Current Build: every card you own, its benefit and drawback, which conditional effects are active right now, charges and cooldowns, and the combined result (sprint speed, jump height, stamina costs, regeneration, maximum stamina, Injury, item weight and more) |

All keys can be changed in the config. Cards with charges or cooldowns show their state at the bottom right. On
the Current Build screen, a value that depends on a condition or a teammate's aura shows what applies right now and,
next to it, the value that always applies.

## The cards

About half of the cards have a catch (a drawback); the rest are smaller, pure upgrades.

### Climbing

| Card | Rarity | Benefit | Drawback |
|---|---|---|---|
| Sticky Fingers | Common | Climbing tires you 12% less. | None |
| Mountain Goat | Uncommon | Jump 30% higher. | 30% more fall damage. |
| Surefooted | Common | You slip and slide 35% less. | None |
| Parkourist | Rare | Hang still on a wall to get 4 stamina back every second. | None |
| Bing BOING | Rare | Double jump: one extra jump in mid-air, every 35 seconds. | Each double jump makes you a bit drowsy (+7.5 Drowsy). |
| Diet Energy Drink | Uncommon | You climb 30% faster. | Climbing tires you 20% more. |
| Lite Tenderfoot | Uncommon | Falls hurt 25% less. | None |
| Scout Master Reach | Common | Grab walls from 20% farther away. | None |

### Environment

| Card | Rarity | Benefit | Drawback |
|---|---|---|---|
| Mesa Blood | Common | You heat up 50% slower. | You get cold 30% faster. |
| Alpine Coat | Common | You get cold 50% slower. | You heat up 30% faster. |
| Anti Antibodies | Uncommon | Poison builds up 35% slower. | None |
| Spore Lungs | Uncommon | Spores build up 35% slower. | None |
| Thornbreaker | Common | New thorns count 40% less. | None |
| Heat Shedding | Uncommon | Once you're out of the heat, it wears off 60% faster. | None |
| Frostwalker | Uncommon | Once you're out of the cold, it wears off 60% faster. | None |
| Toxic Adaptation | Rare | While you have 20+ Poison or Spores, climbing tires you 30% less. | Poison and Spores wear off 30% slower. |

### Inventory

| Card | Rarity | Benefit | Drawback |
|---|---|---|---|
| Pack Rat | Rare | One extra pocket. | Everything you carry is 35% heavier. |
| Ultralight | Uncommon | Everything you carry is 20% lighter. | None |
| One-Saber Scout | Common | The item in your hands weighs 35% less. | None |
| Uncooked? | Uncommon | Once per biome, the first cooked food you eat isn't used up. | None |
| Scout Chef | Uncommon | Cooked food fills you up 50% more. | Eating uncooked food poisons you a little (+7.5 Poison). |
| Rope Baron | Rare | A free rope spool appears for you at each new campfire. | Climbing tires you 8% more. |
| Supply Scout | Uncommon | A free bandage appears for you at each new campfire. | None |
| Double Deal | Legendary | Get 2 more random cards right now. | Your third pocket is locked for the rest of the run. |

### Mutation

| Card | Rarity | Benefit | Drawback |
|---|---|---|---|
| Rocket Ankles | Rare | Sprint 35% faster and jump 25% higher. | Falls hurt 80% more. |
| Cursed Crown | Rare | Get 30 extra (yellow) stamina now, and 20 more at every campfire after. | Get 10 Curse now, and 5 more at every campfire after. |
| Loaded Dice | Uncommon | Everything you carry is 10% lighter. In each biome, a 65% chance of 25 extra (yellow) stamina. | Otherwise, you take 20 Injury. |
| Blood Battery | Legendary | Once per biome, press B for 60 extra (yellow) stamina. | Using it costs you 25 Injury. |
| Momentum Fiend | Uncommon | Sprint for 2 seconds and you speed up 30% until you stop. | Stopping after the speed-up makes you a bit drowsy (+7.5 Drowsy, at most every 30 seconds). |
| Delayed Consequences | Rare | Falls hurt 15% less, and most of the injury only arrives 20 seconds later. | None |
| Glass Heart | Legendary | Your stamina bar is 40 longer. | Every injury hurts 75% more. |
| Packless Nomad | Uncommon | Without a backpack, sprint 30% faster and jump 20% higher. | With a backpack, you sprint 15% slower. |

### Mythic

| Card | Rarity | Benefit | Drawback |
|---|---|---|---|
| Scoutmaster | Mythic | Climbing never tires you, falls never hurt, and you sprint 25% faster. | None |
| The Eternal Campfire | Mythic | Everyone within 15 m of you (you too) gets a stamina bar 35 longer and takes 60% less injury and statuses. Once per biome, the first scout nearby who passes out is revived. | None |

### Stamina

| Card | Rarity | Benefit | Drawback |
|---|---|---|---|
| Heart of the Cards | Uncommon | Your stamina bar is 25 longer. | You get hungry 35% faster. |
| Second Wind | Rare | Once per biome, when your stamina drops below 20, you get 25 back. | None |
| Runner | Common | Sprinting tires you 15% less. | None |
| Quick Recovery | Common | Stamina starts coming back 0.3 seconds sooner. | None |
| Quiet Breathing | Uncommon | Crouch and stay still to get stamina back 35% faster. | None |
| Cardio Tax | Uncommon | You sprint 25% faster. | Your stamina bar is 12.5 shorter. |
| Pulse Climber | Rare | Climb for 3 seconds straight, and the next second of climbing costs 35% less stamina. | None |
| Half Tank | Common | Every time a campfire is lit, get 15 extra (yellow) stamina. | None |

### Survival

| Card | Rarity | Benefit | Drawback |
|---|---|---|---|
| Bone Plating | Common | Hits that aren't falls hurt 20% less. | None |
| Efficient Medic | Uncommon | Bandages and medkits heal you 25% more. | None |
| Pain Engine | Uncommon | While you have 20+ Injury, climbing and sprinting tire you 25% less. | Every injury hurts 20% more. |
| Emergency Purge | Rare | Once per biome, when Poison, Spores or Drowsy reaches 35, 20 of it clears at once. | None |
| Phoenix Clause | Legendary | Once per run, a fall that would knock you out puts you back on safe ground instead. | You come back with 40 Injury and skip your next card pick. |
| Iron Stomach | Common | Food fills you up 20% more. | None |
| Curse Bargain | Rare | Curse builds up 70% slower. | Every injury hurts 25% more. |
| Cleansing Ritual | Uncommon | Medical items also clear 7.5 of your worst other status. | None |

### Team

| Card | Rarity | Benefit | Drawback |
|---|---|---|---|
| Belay Buddy (team) | Uncommon | Near a teammate (6 m), climbing tires you both 15% less. | Climbing alone tires you 15% more. |
| Tara (team) | Uncommon | Healing items you use on teammates heal 60% more. | Healing items you use on yourself heal 35% less. |
| Rescue Harness (team) | Common | Carrying a scout feels 35% lighter. | None |
| Heat Huddle (team) | Common | Huddling warms you 35% more, from 35% farther away. | None |
| Otto (team) | Rare | The first time you revive a teammate in each biome, you both get 20 extra (yellow) stamina. | None |
| Long Arm (team) | Common | Pull teammates up from 30% farther away. | None |
| Shared Burden (team) | Rare | Teammates near you (6 m) take 30% less injury. | You take half of what you saved them (at most 10 per hit). |
| Camp Choir (team) | Uncommon | Teammates near you (6 m) get stamina back 12% faster. | None |

### Wildcard

| Card | Rarity | Benefit | Drawback |
|---|---|---|---|
| Double Down | Legendary | Get 1 more random card right now (Common to Rare). | Get 10 Curse now, and the next campfire offers you only 1 card. |
| Scavenger's Initiative | Rare | See food, items and luggage within 12 m, even through rock. | None |
| Hungry Hands | Uncommon | Eating while you have 20+ Injury heals 7.5 Injury. | None |
| Safety Net | Legendary | Once per biome, press N to set an anchor. If you pass out, you wake up there. | Your stamina bar is 15 shorter. |
| Stoneblood | Rare | You turn to stone 35% slower. | None |
| Mountain's Bargain | Legendary | Give up one of your cards: your stamina bar becomes 35 longer. | Get 5 Curse at every campfire after. |

## Playing together
Co-op for up to 4, exactly like PEAK: host a game and invite friends through Steam. Every player needs the same
version of the mod with the same card settings. When they differ, both sides get a warning that names what differs
(for example "rarity weights" or "cards: Sticky Fingers"). Sound, key and debug settings are your own and never count.

The host is in charge:
- It deals the offers and checks every pick: a card must come from your own offer, in the current session, once.
- It approves Eternal Campfire revives.
- It places campfire items (once per card per campfire, next to that fire).

A campfire lit while a pick is still running gets its own pick right after it. Late joiners receive everyone's cards.

## Config
`BepInEx/config/notnic.campfirecards.cfg`:

- `General`:
  - `Sounds`.
  - `PickTimeoutSeconds`: a random card of your offer is taken after this many seconds. The default is 30, and the
    host's value applies to everyone. A config file written by version 0.3.0 keeps its old value of 60 until you
    change it.
  - `PickMode`: `Simultaneous` (default) or `Turns`. The host's choice applies to everyone.
- `Keys`: `BloodBattery`, `SafetyNet`, `BuildScreen`.
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
- Uncooked? keeps the food in hand. Food that leaves a peel spawns no peel that time.
- Tara, Bone Plating and Efficient Medic scale the Injury that items heal. Healing from the Healing Amulet
  and dart is not scaled.
- Cleansing Ritual counts medical items (bandages, first aid, antidote, cure-all, roots, aloe) used on you;
  berries and packaged snacks are not curative.
- Scavenger's Initiative marks items with labels drawn over the screen rather than outlines.
- Other players see your card speed boosts with a small delay (their copy of your scout is corrected by the game).
- Continuing a quicksaved run starts with no cards.
- Revives from the Book of Bones and the Scout Effigy do not count for Otto.
- Thornbreaker halves thorns as they stick to you; thorns already in you when you get the card, or when you leave
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

## Credits
Design and code: NotNic. PEAK is by Aggro Crab and Landcrab; this is a fan mod and is not affiliated with them.

## License
MIT, see `LICENSE`.
