# Campfire Cards: design sheets

The sheets in this folder are the source of truth. Code under `src/CampfireCards/Generated/` is produced from them by `tools/gen.py`; hand-written code only fills in handler bodies. When something is unclear, fix the sheet first, regenerate, then the code.

| Sheet | One row is | Generates |
|---|---|---|
| `cards.json` | one card: name, flavour, rarity, colour theme, art motif, effects (plus the theme palette and draw weights) | `CardTable.cs` (one `CardDef` per row, themes) |
| `stats.json` | one climb stat a card can push (toughness, throw force, stamina regen, ...) with its formula, clamps and PEAK target | `ClimbStats.cs` (enum + clamp table + display strings) |
| `art.json` | one animated art motif, as a stack of generated shapes | `ArtTable.cs` (one `ArtMotif` per row) |
| `sounds.json` | one synthesised UI sound | `SoundTable.cs` (one `SoundDef` per row) |
| `hooks.json` | one place the mod attaches to PEAK (Harmony patch or event) | `HarmonyHooks.cs` (one patch class per Harmony row) |
| `net.json` | one Photon event the lobby exchanges | `NetEvents.cs` (one struct with Write/Read per row) |
| `ui.json` | one UI element of the card picker | `UiLayout.cs` (constants per row) |
| `systems.json` | one C# class with one job | checked by preflight only |

`tools/preflight.py` lays the sheets over each other: every cell must be filled and every reference (card effect to stat id, card to theme and art motif, art layer to shape/colour/animation, stat hook to hooks id, UI sound to sounds id, system class to source file) must resolve. Build only when it prints `PREFLIGHT CLEAN`.

## Adding or changing a card
1. Add a row to `cards.json` with a new `id` (never reuse one), a `name`, a `flavor` line, a `rarity`, a `theme` and an `art` motif.
2. List its `effects` using stat ids from `stats.json`; values must sit inside each stat's `min`/`max`.
3. Run `tools/build.sh`. Preflight tells you exactly which cell is missing or out of range.

The card's effect lines are written automatically from the `display` strings in `stats.json`.
