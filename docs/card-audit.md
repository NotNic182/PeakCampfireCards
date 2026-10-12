# Card audit (0.6.3) and fixes (0.6.4)

Every card's benefit and drawback was traced from `design/cards.json` through the mod's hooks into PEAK 2.6.b's
own (decompiled) code. All 49 Harmony hooks were confirmed applied at runtime in the 0.6.3 BepInEx log. Verdicts:

- **Works:** the code path is complete and does what the card says.
- **Partial:** it works, but less, more narrowly, or differently than the text says.
- **Broken:** it does not do what the text says.
- **Playtest:** the code is right but depends on game data or physics that code cannot show.

**Totals (111 effects: 64 benefits + 47 drawbacks):** about 86 work, 23 partial, 1 broken, 1 needs a playtest to know.

## Fixed in 0.6.4

Everything in the fix list below was implemented in 0.6.4 (220 unit tests pass; the in-game checks at the end still apply):

- **Rounding carry** (`Core/StatusCarry.cs`): when a card changes a status amount, the part PEAK's 0.025 step would
  throw away is kept and added to the next change, so percentages hold on average. Weight is stored exactly instead of
  snapped to whole units; Petrify fractions carry to the next gain; food and healing amounts are no longer pre-rounded.
  This fixes #2, #3 (drawback), #18, #19, #22, #30, #34, #35, #44, #50, #51 (weight), #61, and makes #26 and #42 exact.
- **Cold and healing coverage:** Cold removed by any warming (body heat, Heat Pack, warm zones and fires, Heat
  cancelling Cold) now uses the cold-decay cards (#3, #23); medical items stay exact. Tara (#26) now also boosts the
  Healing Amulet, Remedy Fungus puff and mushroom heals.
- **The Eternal Campfire (#64):** revives its owner after an instant death, and waits 1.5 s (like the Checkpoint Flag)
  so the game's pass-out can't undo the revive.
- **Penalties can't be dodged:** card penalties (Bugle, Pandora, Crown, Dishonor, Fine Print, Cooking Badge, Bing BOING,
  Lollipop, Take One for the Troop's share) ignore invincibility and the skeleton x8. Payment Plan debt (#54) lands when
  the card is sacrificed instead of being forgiven.
- **BingBong (#25):** Low Gravity uses strength 3 like the real mushroom (balance version 4 refreshes saved card values
  once; Enabled choices are kept), and the ability bar shows the active power.
- **Take One for the Troop (#47):** small shares add up and are sent once they reach 2.5 Injury.
- **Tenderfoot Insurance (#7):** dynamite exploding in your own hand is tripled too.
- **Pitonless Pit Stop (#4):** hanging still refunds the game's grip cost, so it really recovers 4 per second.
- **Mini Tenacity (#32):** an explicit medical-item list (no more Coconut Half); the Heat Pack and Healing Amulet count.
- **Baggage Claim Escapee (#56):** a Fannypack, Jetpack or Rocketpack counts as a backpack.
- **BingBong Baggage Scanner (#58):** skips opened luggage and scout statues on every player.
- **Pandora's Pocket Gamble (#51):** a scout who was dead at the biome change rolls once revived.

Not changed (design or text choices): Nic of Time's cooldown, Discount Cure-All and poison over time, Glass Scout
Cannon not scaling card penalties, Scout's Dishonor with a Parachute skip, the revive cards dropping your gear (the
text doesn't mention it), and the Parachute's "fatal fall" meaning a knockout.

## The main cause: the game's 2.5-point rounding

Most partial results share one cause. PEAK only changes a status in whole 2.5-point steps (0.025), and throws
away the leftover each time:

- `CharacterAfflictions.AddStatus` (decompiled `:824-835`) and `SubtractStatus` (`:909-919`) round **down** to a
  2.5-point step and reset the leftover to zero.
- `SetStatus` (used for Weight) snaps to the **nearest** step (`RoundStatus`, `:626-629`), i.e. one weight unit.
- Petrify is an integer (`AddPetrify(int)`), so the mod's `RoundToInt` rounded each gain.

A card that multiplies a single chunky tick loses its effect when the result lands in the same step. For example,
the minimum fall Injury is exactly 5 (`CharacterMovement.cs:837`): ×1.3 = 6.5, which drops back to 5. Body heat
removes 5 Cold per half-second (`CharacterHeatEmission.cs:7`): ×0.909 = 4.5, which drops to 2.5. Small
per-frame sources (fog, night cold, poison over time, natural decay) are not affected, because they accumulate.

**Fix (one change, many cards):** keep a per-status remainder in the mod's AddStatus / SubtractStatus / weight /
petrify / hunger hooks (like the existing thorns ledger), so the fractions add up over time instead of being lost.

## Every card

| # | Card | Benefit | Drawback | Why |
|---|---|---|---|---|
| 1 | Piton Fingers | Works | Works | Every climbing cost (wall, rope, vine, lunge, grab) is scaled; sprint cost field is used by the game. |
| 2 | Mini Scout Cannon | Works | Partial | Jump force ×1.3 works; real height gain needs a playtest. Fall Injury ×1.3 is lost to rounding on small (5-point) falls. |
| 3 | To The Bone | Playtest | **Broken** | Benefit needs blue ice to carry a Cold surface (game data) and rounding makes it ~50%, not 35%. Drawback: Cold never decays on its own; only body heat warms, and 5 × 0.909 drops to 2.5, so warming takes **twice as long**, not 10% longer. |
| 4 | Pitonless Pit Stop | Partial | Works | Gives 4/s, but hanging on a steep wall still costs up to 2/s and blocks normal regen: net about +2/s. |
| 5 | Bing BOING | Works | Works | Walking off a ledge with the charge ready also gives a free normal midair jump first. |
| 6 | Energy Drink Overdose | Works | Works | Also speeds slides and the climb lunge. |
| 7 | Tenderfoot Insurance | Works | Partial | Blast Injury from dynamite is tripled, but the separate 25 Injury when dynamite explodes **in your hand** is not. |
| 8 | Daddy | Works | Works | Grab ray ×1.2; every stamina cost ×1.1. |
| 9 | Cookie Addiction | Works | Works | Max-stamina hook is what the game clamps to. Teammates may see your bar drawn wrong (cosmetic). |
| 10 | Nic of Time | Works | — | Cooldown is spent in full even if statuses limit the recovery below 25. |
| 11 | Hasty Badge Training | Works | Works | Sprint-jump's extra cost isn't discounted (text only says sprinting). |
| 12 | HOLD | Works | Works | |
| 13 | Meditation | Works | — | |
| 14 | I'm getting Scoutmastered | Works | Works | |
| 15 | Endurance Badge | Works | — | |
| 16 | Marshmallow Morale | Works | Works | No leftover cooked-food ban code. |
| 17 | Cool, Calm, and Collected. | Works | Works | Halved Heat also halves how much heat cancels Cold (hidden extra cost). |
| 18 | Just hot. | Works | Partial | Heat +30% is lost to rounding on chunky ticks (likely the Mesa sun); works on lava and per-frame heat. |
| 19 | Antidote Enthusiast | Works | Partial | Cold half works; Heat half is lost to rounding like #18. Poison resistance is approximate on chunky hits. |
| 20 | ROOTS Survivor | Works | — | All spore sources go through the scaled path. |
| 21 | Cactus? What Cactus? | Works | Works | 1 thorn stays 1, 2 become 1, as the text says. |
| 22 | Aloe | Works | Partial | Heat +15% is lost to rounding like #18. Doesn't affect the Aloe Vera item. |
| 23 | Heat Pack | Partial | Works | Cold has no natural decay, so only body heat is sped up (~+50% after rounding). Heat Pack item, campfires and heat cancelling Cold are not. |
| 24 | Fartberry Fitness | Works | Works | |
| 25 | BingBong | Works | — | Low Gravity uses strength 1; the real mushroom uses 3, so it is a third as strong. Code bug: the game stores a *copy* of the buff, so the ability bar never shows the active buff and sacrifice can't remove it early. |
| 26 | Tara | Partial | — | Bandages, First Aid Kit, Cure-All, roots, berries are boosted; Healing Amulet, Remedy Fungus and mushroom heals are not. |
| 27 | Walk It Off | Works | Works | |
| 28 | Discount Cure-All | Works | — | Poison-over-time keeps ticking after the 20 is removed; the charge is spent even if nothing could be removed. |
| 29 | BingBong Parachute | Works | Works | "Fatal fall" really means a fall that would knock you out. |
| 30 | Gourmand's Gut | Partial | — | Rounding: 5-point foods get nothing; others get +17% to +33% instead of +20%. |
| 31 | Book of Broken Bones | Works | Works | Covers every revive that adds Curse. |
| 32 | Mini Tenacity | Partial | — | "Medical item" is guessed from item tags: misses Heat Pack, Healing Amulet, Remedy Fungus, Blowgun; wrongly fires on Coconut Half. |
| 33 | Bing Bong's Baggage | Works | Works | Weight snaps to whole units (3 → 2, 5 → 2). Pocket lock never deletes items. |
| 34 | Carry-On Only | Partial | Partial | Weight snaps to whole units: weight-1 held items get nothing; backpack penalty starts only at 5+ weight. |
| 35 | Idol Hands | Partial | — | Same rounding: weight-1 held items get nothing. |
| 36 | Cook It, Keep It | Works | Works | Burnt food counts as cooked; packaged snacks count as uncooked. |
| 37 | Cooking Badge Deluxe | Works | Works | Milk invincibility blocks the raw-food Poison. |
| 38 | Balright | Works | Works | Balloon spawns on the ground within 7 m of the fire (playtest placement). |
| 39 | Scoutmaster's Care Package | Works | — | A ghost at the fire loses that fire's bandage. |
| 40 | Clown Luggage Special | Works | Works | Four penalties, 25% each; kept after sacrifice; a second pick adds another. |
| 41 | Rule Zero | Works | Works | Two owners together each get ~28% off. |
| 42 | First Aid Badge | Works | Works | Rounding makes it about +58% / −33%. |
| 43 | Human Carry-On | Works | Works | The 0.6.2 hook fix is correct: the death timer is advanced in `UpdateVariablesFixed`. |
| 44 | Happy Camper Huddle | Partial | Works | "Farther away" works (1 m → 1.35 m). "35% more" is erased: 5 × 1.35 = 6.75 drops to 5. |
| 45 | Otto-Matic Clutch | Works | — | Yellow stamina caps at one bar. |
| 46 | You're alright, you're alright | Works | Works | |
| 47 | Take One for the Troop | Works | Partial | Shares under 1.25 are never sent, so hits under ~8 Injury cost you nothing. |
| 48 | Friendship Bugle Choir | Works | Works | |
| 49 | Scout Cannon Legs | Works | Works | Sprint works; "jump 25% higher" needs a playtest to measure. |
| 50 | Crown of Bad Decisions | Works | Partial | 2 Curse is below the 2.5 step: averages ~1.25 per campfire, sometimes 0. |
| 51 | Pandora's Pocket Gamble | Partial | Partial | 10% lighter does nothing under 5 weight units. Failed-roll Injury is blocked while invincible (Milk) and ×8 as a skeleton. A scout dead at the biome change loses that roll. |
| 52 | Bootleg Scoutmaster Bugle | Works | Partial | Used while invincible, you get the 60 stamina with no Injury. |
| 53 | Big Lollipop Crash | Works | Works | |
| 54 | Piton Payment Plan | Works | — | Exploit: sacrificing the card to Fine Print forgives pending debt. |
| 55 | Glass Scout Cannon | Works | Works | Penalty Injury from other cards isn't scaled ×1.5 (minor). |
| 56 | Baggage Claim Escapee | Works | Partial | Only a real Backpack counts: a Fannypack, Jetpack or Rocketpack gets the bonus with no penalty. Jump needs a playtest. |
| 57 | Scout's Dishonor | Works | Works | If a Parachute skip lands on the same fire, the one-card penalty is used up for free. |
| 58 | BingBong Baggage Scanner | Partial | — | On non-host players, opened luggage keeps being marked; scout statues are labelled "Luggage". |
| 59 | Medicinal Munchies | Works | — | |
| 60 | Budget Checkpoint Flag | Works | Works | Revive drops all items and the backpack and clears statuses (not in the text). |
| 61 | Petrify Procrastination | Partial | Works | Petrify gains are rounded each time: +1 gains (amulets) aren't reduced at all. |
| 62 | Fine Print | Works | Works | The +35 applies before you choose the sacrifice. |
| 63 | Scoutmaster | Works | — | Zero climbing cost and zero fall Injury on every path. |
| 64 | The Eternal Campfire | Partial | — | Aura works. Revive bug: the owner's **own instant death** (petrify, kill plane) is never revived, because dead owners are skipped. The revive may also re-collapse you for ~3 s (playtest). |

## Fix list, most important first

1. **Rounding carry** (fixes #2, #3, #18, #19, #22, #23 partly, #30, #34, #35, #44, #50, #51 weight, #61; improves #26 and #42).
2. **The Eternal Campfire:** let the owner's own instant death request a revive; wait until fully passed out (like the Checkpoint Flag's 1.5 s) before reviving.
3. **Dodgeable penalties:** apply card penalty Injury with `ignoreInvincibility` (Bugle, Pandora); apply Payment Plan debt immediately when the card is removed instead of forgiving it.
4. **BingBong:** track the game's copy of the buff; consider strength 3 for Low Gravity.
5. **Narrow coverage:** Heat Pack (other warming sources), Tara (amulet and fungus heals), Mini Tenacity (explicit medical item list), Tenderfoot (in-hand dynamite), Take One for the Troop (send small shares), Baggage Claim Escapee (decide what other packs count as), Scanner (skip opened luggage and statues).
6. **Text only:** Pitonless Pit Stop (net +2/s), Parachute ("would knock you out"), Checkpoint Flag and Eternal Campfire revives drop your gear.

## Needs a playtest

To The Bone's blue ice surface; the real jump heights of #2, #49, #56; Low Gravity strength; the Mesa sun's tick size
(decides how much #18, #19 and #22 lose); The Eternal Campfire's revive timing; Balright's balloon placement.
