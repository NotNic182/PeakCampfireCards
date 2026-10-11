# Review of your card edits — candidate 0.6.0

Target: **PEAK 2.6.b**. Your two uploaded workbooks are identical. There are **50 edited cards** among the 64 stable card IDs. The supported changes are implemented in this candidate; the paired ROOTS Survivor replacement is held back. The original submitted workbook is preserved alongside the reviewed workbook.

**This is compiled and unit-tested code, not an in-game verification on 2.6.b.** Available game references are stripped/publicized **2.5.a** APIs. They establish member signatures, not behavior, prefab presence or multiplayer correctness. No PEAK game or Unity physics ran here.

## Download and edit

- [Updated editing workbook](../design/editable/card-editing.xlsx): the actual shipped names, effects and numbers; edit its blue columns for another pass.
- [Review workbook](../design/editable/card-edit-review.xlsx): each submitted edit, the shipped effect, status and implementation notes.
- [Original submitted workbook](../design/reviews/card-editing-submitted.xlsx): unchanged backup of your upload.
- [Machine-readable review](research/card-edit-review.json): exact before/requested values and final definitions for all 50 edited cards.

The 60-second pick default, unlimited-time option (`PickTimeoutSeconds = 0`) and ghost pick exclusions remain included. Every player in a lobby needs **0.6.0** with matching card settings because the synced card state now includes the random penalty and one-time Balloon Bunch reward.

## Supported mechanics and interpretations

| Request | Candidate behavior |
|---|---|
| Nic of Time | **V** restores 25 stamina below 20, capped by usable capacity; five-minute cooldown, including across biome changes |
| Discount Cure-All | **K** removes 20 from the worst Poison/Spores/Drowsy status at 35+, once per biome |
| Bootleg Scoutmaster Bugle | **B** grants 60 yellow stamina for 15 Injury, once per biome |
| Budget Checkpoint Flag | **N** places the existing anchor; all keys are configurable |
| Tenderfoot Insurance | Approved **3× dynamite Injury**; ordinary impacts keep their usual rules |
| Clown Luggage Special | Two bonus cards and one approved permanent penalty: +15% climbing cost, +15% sprint cost, +20% Injury, or +20% Hunger buildup. The choice is synced and cannot reroll through a repeated message |
| BingBong mushroom cycle | Random native Infinite Stamina, Invincibility or **Low Gravity**, 20 seconds every ten minutes alive. Ghost time pauses the clock; a pending reward waits while unconscious |
| Marshmallow Morale | Clears Hunger when a new fire is lit while you own the card; blocks cooked food, including recipient-side teammate feeding |
| Bing Bong's Baggage | Halves total carried weight, including a scout and backpack; blocks new items in pocket three while retaining access to items already there |
| Carry-On Only | Held items weigh 25% less; backpack contents weigh 10% more. The bag shell follows held/carried rules |
| Balright | Native Balloon Bunch at the next fire after acquisition, once per run. Failed/missed rewards can retry at a later fire; only a successful spawn marks the reward used |
| Book of Broken Bones | Stops additional Curse from the native revive-status routine; keeps existing Curse and the unchanged +25% Injury drawback |
| BingBong Parachute | Prevents the fatal fall without adding the old 40 Injury; retains the next-pick penalty |
| To The Bone | Cold reduction while climbing a cold-emitting wall; ordinary warming takes 10% longer. Medical cleanses remain exact |
| Longer teammate pull | Uses 10 normal stamina units once when a valid pull starts during one continuous reach; other cost cards can scale it |
| Full extra stamina on revival | Grants 100 yellow stamina to each scout on the first detected feeding revival per biome; existing Book of Bones/Scout Effigy detection limitation remains |
| Human Carry-On | Approved behavior: a normally paused carried timer drains at **15% of the normal 60-second rate**. If native rules already advance it while carried, that progress is 15% faster. Carrier-side weight reduction stays 35% |
| Other numerical edits | Added penalties, adjusted fall/curse/status/stamina values and submitted names/descriptions; IDs, rarities and deck size stay stable |

“Flying” uses the native mushroom **Low Gravity** effect. It provides floating; unrestricted flight would need separate movement rules. “Burn/freeze” refers to native Hot/Cold status buildup. Maximum stamina shrinking by 15% means 15 fewer clean bar units; other bonuses still add, and the existing normal-card floor still applies.

You confirmed Human Carry-On's gentler penalty: drain at 15% of the normal death rate while carrying would otherwise pause the timer. In this implementation `carriedDeathRate = 1.15` means 15% of normal drain during the pause, or 15% faster native progression when it is already active.

## ROOTS Survivor: paired replacement pending

Your proposed immunity to Mandrakes and Zombies is technically plausible through source-specific interaction and damage hooks. A guaranteed random zombie in the *next biome* additionally needs the real network prefab, valid spawning terrain, biome transition timing and cleanup verified on 2.6.b. Available API metadata does not establish that arbitrary-biome spawning works.

The candidate keeps ROOTS Survivor's **35% spores resistance**. It does not apply the immunity alone, because that would omit your paired drawback. This request is **unverified, not fundamentally impossible**. It needs a development pass using 2.6.b game assets and an actual multiplayer playtest.

## Upgrade notes

Balance version 3 refreshes numerical card configuration defaults once so previous modifier values do not override this redesign. Existing `Enabled` choices remain. If an earlier version disabled Bing Bong's Baggage, set **`[Card - PackRat] Enabled = true`** to include its now-working weight/locked-pocket design. Restart after editing the config, and match card settings across the lobby.

Ability cooldowns and native buff instances still follow the existing local ability lifecycle. Rebuilding abilities through reconnect/state restoration does not preserve their elapsed cooldowns; the random permanent penalty and successful Balloon Bunch flag are explicitly included in synced state. Quicksaved runs still start without cards.

## Validation and next playtest

Checks passed: **148 C# tests**, **5 catalog tests**, design preflight, and a full Release build with **zero warnings/errors**. All **53 Harmony hook signatures** and 15 capability groups were found in the available reference metadata. Preflight retains a warning for the reserved `invSlots` channel, which no current card uses.

Automated validation runs the production manual recovery abilities and pick session logic against small substitutes for the game boundary, plus modifier, timer and wire-format checks. The full plugin compiles against the available references. [API metadata results](research/peak-api-redesign.json) record the assembly hash and hook signatures. Compilation cannot substitute for the following live checks:

1. With everyone on 0.6.0, check opening/campfire offers, 60-second and unlimited timing, ghosts, revival after lighting and death during a pick in both modes.
2. Test all three manual abilities together; verify separate keys, failed attempts preserving charges, five-minute cooldown and biome refill behavior.
3. Compare dynamite with unrelated Injury, chained explosions, and thorn removal by yourself and a teammate; arrows should keep their normal timing.
4. Check cold walls, natural warming, huddles, and medical cleanses; verify that fire/night Cold while off a cold wall is unaffected by its conditional benefit.
5. Try every revival route with Book of Broken Bones and preserve unrelated Curse. Check carried-scout death timing on both carrier and victim clients.
6. Try cooked/raw food, teammate feeding, backpack shells/contents, carried scouts, temporary hands slots and pocket-three pickup/retrieval.
7. Pick Balright at the beach and a fire, confirm a single Balloon Bunch at a later fire, and verify the used state after host migration. Check item placement at each biome fire.
8. Run the mushroom cycle through death, pass-out, consumed buffs, expiration, sacrifice and run end; verify native effect synchronization and overlap cleanup.
9. Check the random penalty on each client, after state restoration, and after sacrificing its card. Verify the Current Build screen shows its selected modifier.

## Implementation evidence

- [PEAKGameLibs 2.5.a](https://github.com/fake-game-developers/PEAKGameLibs/tree/d5ac82ca48d8766d45b3a630afc455949a4aba9e) supplies the inspected reference APIs; method bodies are stripped.
- [BingBongTouch's item catalog](https://thunderstore.io/c/peak/p/Witherstyle/BingBongTouch/v/1.0.0/) lists the native `BalloonBunch` prefab name.
- [MushroomsNOW](https://thunderstore.io/c/peak/p/Kirshoo/MushroomsNOW/) describes native Infinite Stamina, Invincibility and Low Gravity mushroom effects. The candidate creates timed native afflictions rather than adding a flight system.
- [PeakVs source](https://old.thunderstore.io/c/peak/p/Ugleh/PeakVs/source/) demonstrates distinguishing dynamite-created AOEs; the candidate implements its own provenance tags and exception-safe context restoration.
- [BetterNoRevivePenalty source](https://old.thunderstore.io/c/peak/p/Bouncing/BetterNoRevivePenalty/source/) demonstrates the native revive-status interception point.
- [SimpleTweaks](https://thunderstore.io/c/peak/p/khalil/SimpleTweaks/) documents native death-timer pausing while carried; its [source](https://old.thunderstore.io/c/peak/p/khalil/SimpleTweaks/source/) confirms timer direction and a normal-rate timing basis. These are implementation precedents, not 2.6.b runtime validation for this mod.

## All submitted edits

The table below is generated from the preserved review data. “Implemented” describes the candidate code; live behavior still needs the playtest above.

<!-- reviewed-cards -->

| ID | Card | Status | Implementation notes |
|---|---|---|---|
| 1 | Piton Fingers | Implemented — candidate | Submitted names/text and numerical effects applied; other mechanics unchanged. |
| 2 | Mini Scout Cannon | Implemented — candidate | Submitted names/text and numerical effects applied; other mechanics unchanged. |
| 3 | To The Bone | Implemented — candidate | Cold gain is reduced on cold-emitting climbing surfaces. Warming rates use 1/1.1 so normal warming takes 10% longer; medical cleanses remain exact. |
| 4 | Pitonless Pit Stop | Implemented — candidate | Submitted names/text and numerical effects applied; other mechanics unchanged. |
| 5 | Bing BOING | Implemented — candidate | Submitted names/text and numerical effects applied; other mechanics unchanged. |
| 7 | Tenderfoot Insurance | Implemented — candidate | Approved 3x dynamite Injury. Other explosion/impact sources keep their usual Injury multipliers. |
| 8 | Daddy | Implemented — candidate | 10% more for every positive UseStamina call, including sprinting/climbing; natural regeneration is unchanged. |
| 9 | Cookie Addiction | Implemented — candidate | Submitted names/text and numerical effects applied; other mechanics unchanged. |
| 10 | Nic of Time | Implemented — candidate | Manual V, below 20 stamina, restores 25 capped by current usable capacity. Five-minute cooldown crosses biome boundaries. |
| 11 | Hasty Badge Training | Implemented — candidate | Submitted names/text and numerical effects applied; other mechanics unchanged. |
| 12 | HOLD | Implemented — candidate | 15 fewer units of clean maximum stamina; card bonuses still add and the normal 50-unit floor still applies. |
| 13 | Meditation | Implemented — candidate | The humorous drawback remains text only; no added numerical penalty. |
| 14 | I'm getting Scoutmastered | Implemented — candidate | Submitted names/text and numerical effects applied; other mechanics unchanged. |
| 15 | Endurance Badge | Implemented — candidate | Submitted names/text and numerical effects applied; other mechanics unchanged. |
| 16 | Marshmallow Morale | Implemented — candidate | Clears Hunger when a new campfire is lit while you own the card; acquisition after a fire is lit does not retroactively clear it. Cooked food is rejected on owner cast and recipient feeding paths. |
| 17 | Cool, Calm, and Collected. | Implemented — candidate | Burn/freeze wording refers to PEAK Hot/Cold status buildup, not direct physical or dynamite Injury. |
| 18 | Just hot. | Implemented — candidate | Burn/freeze wording refers to PEAK Hot/Cold status buildup, not direct physical or dynamite Injury. |
| 19 | Antidote Enthusiast | Implemented — candidate | Submitted names/text and numerical effects applied; other mechanics unchanged. |
| 20 | ROOTS Survivor | Pending paired replacement | Paired replacement held back: guaranteed random next-biome zombie spawn requires verified 2.6.b prefab, placement and network lifecycle. Existing 35% spores resistance retained. Selective immunity could be written, but shipping just that half would omit the agreed drawback. |
| 21 | Cactus? What Cactus? | Implemented — candidate | Submitted names/text and numerical effects applied; other mechanics unchanged. |
| 22 | Aloe | Implemented — candidate | Submitted names/text and numerical effects applied; other mechanics unchanged. |
| 23 | Heat Pack | Implemented — candidate | Submitted names/text and numerical effects applied; other mechanics unchanged. |
| 24 | Fartberry Fitness | Implemented — candidate | Strictly above 20 Poison; Spores no longer qualifies. Poison alone decays 30% slower. |
| 25 | BingBong | Implemented — candidate | Native Infinite Stamina, Invincibility or Low Gravity for 20 seconds every ten minutes alive. Native Low Gravity provides floating, not unrestricted flight. Ghost time pauses the interval; a pending buff waits while passed out. |
| 26 | Tara | Implemented — candidate | Submitted names/text and numerical effects applied; other mechanics unchanged. |
| 27 | Walk It Off | Implemented — candidate | Submitted names/text and numerical effects applied; other mechanics unchanged. |
| 28 | Discount Cure-All | Implemented — candidate | Manual K per user separate-key preference; one use per biome clears 20 from the worst eligible status at 35+. |
| 29 | BingBong Parachute | Implemented — candidate | Fatal fall is prevented without the old extra 40 Injury. Other existing Injury stays. The next card pick is still skipped. |
| 31 | Book of Broken Bones | Implemented — candidate | Removes only new revive Curse via the native ApplyPostReviveStatus path. Existing Curse stays; unchanged +25% Injury drawback is retained. |
| 32 | Mini  Tenacity | Implemented — candidate | Submitted names/text and numerical effects applied; other mechanics unchanged. |
| 33 | Bing Bong's Baggage | Implemented — candidate | 50% less total carried weight, including a scout, backpack shell and contents. Third pocket blocks new items; existing contents remain retrievable. Fresh config enables it, but an old Enabled=false setting is preserved. |
| 34 | Carry-On Only | Implemented — candidate | 25% lighter held items; 10% heavier backpack contents. The bag shell follows held/carried item rules, not the contents penalty. |
| 36 | Cook It, Keep It | Implemented — candidate | Submitted names/text and numerical effects applied; other mechanics unchanged. |
| 38 | Balright | Implemented — candidate | One BalloonBunch reward at the next fire after acquiring the card; only a successful spawn consumes the run flag. Missed/failed fire rewards retry at a later eligible fire. 10% more for every stamina use. |
| 40 | Clown Luggage Special | Implemented — candidate | Approved pool: +15% climbing cost, +15% sprinting cost, +20% Injury, or +20% Hunger buildup. One selected penalty is kept in state sync and shown in Current Build. The former pocket lock is removed. |
| 41 | Rule Zero | Implemented — candidate | Submitted names/text and numerical effects applied; other mechanics unchanged. |
| 42 | First Aid Badge | Implemented — candidate | Submitted names/text and numerical effects applied; other mechanics unchanged. |
| 43 | Human Carry-On | Implemented — candidate | User confirmed the gentler penalty: 15% of a normal 60-second timer while carrying would otherwise pause it. If native rules already drain the timer (e.g. high Spores), their progress remains 15% faster. Victim owner reads carrier cards. |
| 44 | Happy Camper Huddle | Implemented — candidate | Alone means no living teammate within 6 m; native huddle warming/range bonus is unchanged. |
| 45 | Otto-Matic Clutch | Implemented — candidate | Grants 100 yellow stamina to each scout on the first detected teammate revival per biome. Existing feeding revive detection remains; Book of Bones and Scout Effigy are not covered. |
| 46 | You're alright, you're alright | Implemented — candidate | Spend 10 normal stamina units once when a valid teammate pull starts during a continuous reach, rather than every frame. Other stamina-cost cards can scale that cost. |
| 48 | Friendship Bugle Choir | Implemented — candidate | Alone means no living teammate within the existing 6 m aura. All positive stamina uses cost 10% more. |
| 49 | Scout Cannon Legs | Implemented — candidate | Submitted names/text and numerical effects applied; other mechanics unchanged. |
| 50 | Crown of Bad Decisions | Implemented — candidate | Submitted names/text and numerical effects applied; other mechanics unchanged. |
| 51 | Pandora's Pocket Gamble | Implemented — candidate | Submitted names/text and numerical effects applied; other mechanics unchanged. |
| 52 | Bootleg Scoutmaster Bugle | Implemented — candidate | Manual B per user separate-key preference; 15 Injury for 60 yellow stamina once per biome. |
| 55 | Glass Scout Cannon | Implemented — candidate | Submitted names/text and numerical effects applied; other mechanics unchanged. |
| 61 | Petrify Procrastination | Implemented — candidate | Submitted names/text and numerical effects applied; other mechanics unchanged. |
| 62 | Fine Print | Implemented — candidate | Submitted names/text and numerical effects applied; other mechanics unchanged. |
| 63 | Scoutmaster | Implemented — candidate | Submitted names/text and numerical effects applied; other mechanics unchanged. |
