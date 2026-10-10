# PEAK x ROUNDS: Campfire Cards — design sheets

The sheets in this folder are the source of truth. Code under `src/PeakRounds/Generated/` is produced from them by `tools/gen.py`; hand-written code only fills in handler bodies. When something is unclear, fix the sheet first, regenerate, then the code.

| Sheet | One row is | Generates |
|---|---|---|
| `stats.json` | one climb stat a card can push (toughness, throw force, stamina regen, ...) with its formula, clamps and PEAK target | `ClimbStats.cs` (enum + clamp table + display strings) |
| `cards.json` | one ROUNDS card prefab and its climb effects | `CardTable.cs` (one `CardDef` per row) |
| `hooks.json` | one place the mod attaches to PEAK (Harmony patch or event) | `HarmonyHooks.cs` (one patch class per Harmony row) |
| `net.json` | one Photon event the lobby exchanges | `NetEvents.cs` (one struct with Write/Read per row) |
| `ui.json` | one UI element of the card picker | `UiLayout.cs` (constants per row) |
| `rounds_assets.json` | one file or asset read from the player's ROUNDS install | `RoundsAssets.cs` (names and formats) |
| `systems.json` | one C# class with one job | checked by preflight only |
| `rounds_pool.json` | reference copy of what the installed ROUNDS actually contains (from research) | checked by preflight only |

`tools/preflight.py` lays the sheets over each other: every cell must be filled and every reference (card effect -> stat id, stat hook -> hooks id, audio key -> asset row, card -> ROUNDS pool name, system class -> source file) must resolve. Build only when it prints `PREFLIGHT CLEAN`.

The games' own content is never copied into the build: PEAK is the host (Melty installs BepInEx and this plugin into it) and the real ROUNDS cards, icons, colours and sounds are read from the player's ROUNDS folder when PEAK starts.
