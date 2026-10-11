# Clearer cards and the supplied playtest log

Candidate **0.6.1** keeps the 0.6.0 card mechanics and balance. It clarifies 63 descriptions, fixes the spacing in **Mini Tenacity**, and keeps the playful names. Meditation's joke moves to its flavor line so the drawback field accurately shows **No drawback**.

The picker and Current Build now label **Benefit** and **Drawback**, so color is not the only cue. Text names the exact stat, amount, trigger and duration. Manual cards show your configured key. The flavor footer gets its own space when present.

Examples:

| Card | Clearer wording |
|---|---|
| Cool, Calm, and Collected. | Heat builds up 50% slower. / Cold builds up 30% faster. |
| Nic of Time | Press V below 20 stamina to recover 25. Cooldown: 5 min. |
| Discount Cure-All | Press K at 35+ Poison, Spores or Drowsy to remove 20 from the highest. Once per biome. |
| Bing Bong's Baggage | Everything you carry weighs 50% less, including bags and scouts. / Your third pocket cannot accept new items. |

The editing workbook contains the actual new copy. `{key}` is a placeholder replaced by the configured key when the card appears. [The copy review](research/card-copy-review.json) records every changed description before and after. Your original submitted workbook remains unchanged.

## Reading the numbers

- **25 stamina** means 25 points on the ordinary 100-point bar. **25 maximum stamina** increases capacity; it does not instantly refill that amount. Status buildup can still reduce usable capacity.
- **Yellow stamina** is PEAK's temporary extra stamina, separate from ordinary capacity.
- **20 Poison removed** subtracts 20 status points. It does not remove all Poison. A percentage reduction such as **35% slower** changes the rate of new buildup rather than clearing existing status.
- **Injury** is the game's injury status. **Fall Injury** applies to falls; **all Injury** also covers other sources.
- **Heat, Cold, Hunger, Poison, Spores, Drowsy, Thorns, Curse and Petrification** refer to the corresponding game statuses. “Builds up” means gaining a status; “wears off” means recovering naturally. “Warming” clears Cold.
- **Once per biome** refills when the biome changes. **Once per run** does not. **Cooldown: 5 min** counts elapsed game time and is not reset by entering a biome.

The HUD now gives Nic of Time a readable cooldown such as **4:59**. Cure-All shows its minimum status requirement until it can actually activate, and reports how much of which status it removed instead of saying **cleansed**. These messages follow configured thresholds and actual recovery amounts. Delayed fall hits are labeled as queued hits, and active mushroom powers use their native effect names.

## What your log establishes

The supplied log records **Campfire Cards 0.6.0** loading on a PEAK session reporting Photon version **2.6**, with Unity **6000.3.15** and BepInEx **5.4.23.3**. Startup completes, the deck contains **64 cards**, and solo opening/campfire picks are recorded for Piton Fingers, Daddy, ROOTS Survivor and Scoutmaster.

No Campfire Cards exception or failed Harmony patch appears in the supplied log. The logged errors concern the BepInEx Unity log writer, game settings parsing, an authentication attempt and a shader property. Their source tags do not establish that Campfire Cards caused them; the session subsequently reaches hosted play and card picks.

This is useful startup and solo-pick evidence for **0.6.0**. It does not prove every effect, ghost exclusion or multiplayer synchronization. A recorded incapacitation is not enough to establish ghost state. The raw log is kept out of the repository because it contains account, lobby and local-path information.

The new **0.6.1** copy and PNG rendering need a live visual check in PEAK. The build and automated checks here do not run Unity. For the next playtest, check the longest cards, changed key bindings, Cure-All below/at its threshold, and one PNG override alongside a card using default art. Co-op and ghost tests from the existing [card review](card-edit-review.md) remain useful.

See [card image instructions](card-images.md) to add your own art.
