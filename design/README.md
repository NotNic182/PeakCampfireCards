# Campfire Cards: design sheets

The sheets in this folder are the source of truth. Code under `src/CampfireCards/Generated/` is produced from them by `tools/gen.py`; hand-written code only fills in handler bodies and ability classes. When something is unclear, fix the sheet first, regenerate, then the code.

| Sheet | One row is | Generates |
|---|---|---|
| `cards.json` | one card: key, name, rarity, category, benefit and drawback text, modifiers, ability, parameters, theme, art (plus offer count, rarity weights and colours, spawn items) | `CardTable.cs` |
| `stats.json` | one stat channel a modifier can change (op mul or add, cap group, where the game applies it) | `ClimbStats.cs` (`Channel`, `ChannelTable`) |
| `conditions.json` | one condition a modifier can require (hanging still, teammate near, ...) | `ClimbStats.cs` (`Condition`) |
| `art.json` | one animated art motif, as a stack of generated shapes | `ArtTable.cs` |
| `sounds.json` | one synthesised UI sound | `SoundTable.cs` |
| `hooks.json` | one place the mod attaches to PEAK (Harmony patch or event) | `HarmonyHooks.cs` |
| `net.json` | one Photon event the lobby exchanges | `NetEvents.cs` |
| `ui.json` | one UI element (plus `canvas`, `timing` and a `palette` of colour roles that rows name as `"@role"`) | `UiLayout.cs` (`Ui_<id>`, `UiTiming`, `UiPalette`) |
| `systems.json` | one C# class with one job | checked by preflight only |

`tools/preflight.py` lays the sheets over each other: every cell must be filled; every modifier must name a real channel, a matching op, a known condition and target; every ability must have an `Ability_<name>` class; every channel and condition must be applied somewhere in the code; the deck must be 64 cards with the specified rarity counts, Mythics without drawbacks and team cards multiplayer-only. Build only when it prints `PREFLIGHT CLEAN`.

## UI conventions
- Every number, colour and player-facing string an overlay needs lives in `ui.json`: a row's `consts`, `timing`, `canvas` or `palette`. Code reads them from `Generated/UiLayout.cs`; a literal in `Ui/*.cs` is a missing sheet value.
- Colours are roles, not swatches: `text`, `textSoft`, `muted`, `faint`, `good`, `bad`, `accent`, `callout`, `surface`, `surfaceStrong`, `backdrop`. The palette's `_note` says which roles pass contrast on which surfaces.
- Rich text opens a colour with `UiKit.ColorTag(color)`. Sizes, line heights and fitting go through `UiKit.Text`, `UiKit.LineHeight`, `UiKit.AutoSize`, `UiKit.FitBacking` and `UiKit.FitBanner`.
- An ability's bar line uses `Ability.BiomeCharge` / `Ability.Line` (ui.json AbilityBar wording). Toasts that only one card ever shows stay next to that card's code.

## Adding or changing a card
1. Add a row to `cards.json` with a new `id` (never reuse one), a PascalCase `key`, a `name`, `rarity`, `category`, the `positive` and `negative` text, a `theme` and an `art` motif.
2. Describe what it does as `mods`: `[channel, op, value, condition, target]`, for example `["climbStamina", "mul", 0.82, "always", "self"]`. Use `team` for an aura on nearby teammates and `teamAndSelf` to include the owner; the aura radius is `params.radius`.
3. For behaviour a modifier cannot express, set `ability` to a name and add a class `Ability_<name>` under `src/CampfireCards/Abilities/`. Its numbers go in `params` and are read with `P("name", fallback)`, so they appear in the config.
4. Run `tools/build.sh`. Preflight tells you exactly which cell is missing or which reference does not resolve.
