# Campfire Cards

A card mod for PEAK. Before the climb starts, and every time your group lights a campfire, each climber picks one
card from a hand of five. Cards change how you climb: tougher skin, faster stamina, higher jumps, softer landings,
better throws, resistance to cold, heat and poison, a slower appetite, or a second wind when you would pass out.
Most good things come with a catch.

The **last climber to reach the fire picks first**, and friends see "X is picking a card..." while you choose.

## You need
- **PEAK** with **BepInEx 5** ([BepInExPack PEAK](https://thunderstore.io/c/peak/p/BepInEx/BepInExPack_PEAK/)).

## Install
Copy the `plugins/CampfireCards` folder from the release zip into `PEAK/BepInEx/plugins/`, then start PEAK.

## Playing together
Co-op for up to 4, exactly like PEAK: host a game and invite friends through Steam. Every player needs the mod.
Card picks are synced over PEAK's own network, and late joiners receive everyone's cards.

## Config
`BepInEx/config/notnic.campfirecards.cfg`: `Sounds`, `PickTimeoutSeconds` (safety timer, default 60).
`Debug.DevKeys` turns on testing keys for the host: F9 lights the current campfire, F10 warps you to it,
F11 shows a sample card fan.

## Building from source
Requirements: .NET SDK 8 or newer, Python 3, and PEAK with BepInEx installed (the project references PEAK's own
assemblies from its install folder; set `PEAK_DIR=<folder>` if PEAK is not in the default Steam location).

```bash
tools/build.sh            # regenerate code from the design sheets, preflight, compile, package
tools/build.sh --install  # same, then copy the plugin into your local PEAK
```

The output is `build/CampfireCards-<version>.zip`.

## How the project is organised
- `design/` holds the JSON design sheets: cards, climb stats, art motifs, sounds, game hooks, network events, UI and
  systems. They are the source of truth; `design/README.md` explains how to add a card.
- `tools/gen.py` turns the sheets into `src/CampfireCards/Generated/*.cs`; `tools/preflight.py` checks that every
  cell is filled and every cross-reference resolves before a build.
- `src/CampfireCards/` is the BepInEx plugin: Harmony hooks into PEAK, the deck, effects, pick sessions, Photon sync,
  the card UI, the animated card art and the synthesised sounds. Art and sounds are generated in code, so the mod
  ships no image or audio files.

## Credits
Design and code: NotNic. PEAK is by Aggro Crab and Landcrab; this is a fan mod and is not affiliated with them.

## License
MIT, see `LICENSE`.
