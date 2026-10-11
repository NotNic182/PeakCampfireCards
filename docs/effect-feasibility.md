# Effect possibilities for Campfire Cards

Target: **PEAK 2.6.b**, as reported by NotNic. Investigation date: 2026-10-10.

The original catalog investigation is now extended by the [current card edit review](card-edit-review.md).
All 64 stable card IDs remain. The editable files contain the current shipped effects; the review preserves
your requested changes and explains the pending zombie spawn and native Low Gravity behavior.

The user’s supplied **0.6.0** playtest log shows successful startup and solo picks in a session reporting PEAK 2.6. It does not establish every effect, ghost or co-op behavior. [The 0.6.1 copy and playtest notes](card-text-and-playtest.md) explain that evidence and the new wording and PNG support.

## What was checked

- The existing card logic suite passed before any change: 107 tests.
- Final automated checks passed: **165 C# tests** and **5 catalog tests**, plus the normal design preflight.
  Preflight retains the known warning for the unimplemented `invSlots` channel.
- Session regression tests execute the real `PickSession` and `PickTurns` against small substitutes for the
  game, UI and transport. They cover ghost offers, revival after lighting, queued fires, death before picking,
  host rejection, timeout picks, an hour-long untimed wait, both pick modes, and delayed event loopback.
- Sandbox modifier tests confirm that +100 clean maximum stamina and an eightfold non-fall Injury penalty
  can coexist, and that half climbing costs still respect the normal-card stacking cap. They add no deck entry.
- A read-only metadata probe found all **53 Harmony hook signatures** in the available PEAK **2.5.a**
  reference assembly. The additional size/physics, impact-source, death and stamina members listed below
  were present. The [JSON report](research/peak-api-redesign.json) records the assembly hash and each check.
- The full mod compiled with zero warnings/errors against those reference assemblies and BepInEx 5.4.23.5.
  Those game references are publicized; their `MenuWindow.Update` accessibility was restored to protected
  in a local reference copy to match the mod's existing override. No game methods were executed.
- The editable CSV and XLSX preserve all original card data when imported without changes; the catalog tests
  also check identity protection and that suggested mechanics never silently become gameplay changes.

**PEAK and Unity physics were not run here.** The available game APIs are from 2.5.a, not your installed
2.6.b. Compilation and member presence cannot establish physics, networking, prefab layout or behavior
on 2.6.b. The supplied mod zip is a candidate build for playtesting, not a verified 2.6.b release.

Reference source: [PEAKGameLibs 2.5.a](https://github.com/fake-game-developers/PEAKGameLibs/tree/d5ac82ca48d8766d45b3a630afc455949a4aba9e).
These are stripped API definitions: their method bodies cannot be used to test gameplay.

## Effects the current mod can already express

These groups use existing stat channels and hooks. A new combination usually needs a card-sheet edit; a new
condition or trigger also needs code. A matching hook is evidence of an implementation path, not a playtest
of a proposed card.

| Effect group | Current controls | Limits to account for |
|---|---|---|
| Stamina | Maximum bar size; climbing and sprint costs; regeneration rate and delay; yellow stamina gain | Maximum stamina is a flat addition; normal cost reductions stack to a 60% cap, and the normal clean-bar floor is 50 |
| Movement | Sprint/climb speed, jump height, slipping, wall-grab and helping-hand reach | Owner computes movement; remote visuals may lag |
| Damage | Fall Injury, all non-fall Injury, all Injury | Dynamite AOEs now have provenance-specific Injury scaling; rocks, projectiles and other sources still need dedicated hooks |
| Afflictions | Hunger, cold, heat, poison, spores, drowsy, thorns, curse, petrification, all negative status gains | Status units are generally 0–1; thorns are discrete and petrification is handled separately |
| Recovery | Natural heat/cold/poison/spore decay; food hunger relief; cooked-food relief; healing received/given/self-use | Item healing is covered; healing amulet/dart are exceptions |
| Carrying | Carried/held item weight, backpack contents, carried-scout weight, total weight | A genuine extra inventory pocket is not implemented; `invSlots` currently does nothing |
| Team effects | Nearby auras, shared damage, stamina support, huddle warmth and range | Auras use living teammates; network authority and stacking need to remain consistent |
| Event abilities | Food/cure events, campfire/biome rewards, chance rolls, cooldowns, charges, double jump, rescue anchors, bonus cards, sacrifices | Existing callbacks can support new logic, but a novel trigger requires a handler |

Existing conditions now also include Poison strictly above 20 and cold-emitting climbing surfaces. Other conditions include hanging still, crouching still, grounded, Injury ≥20, Poison/Spores ≥20,
holding an item while climbing, backpack/no backpack, teammates near/far, and active sprint/climb boosts.
Extra conditions such as health thresholds, airborne time, night, biome or altitude require new evaluation code.

## Your half-size, double-stamina, fragile-to-falling-objects example

| Part | Evidence | Assessment |
|---|---|---|
| Half size | Unity `Transform.localScale`; PEAK `CharacterRagdoll.colliderList`, `rigidbodies`, `Bodypart.Rig`; an existing Tiny Scouts mod scales `Character` roots | **Plausible, new behavior required.** Visible shrinking has precedent; correctly shrinking every physics body, camera and interaction is unverified |
| Twice the clean stamina bar | Existing `maxStamina` add channel and `Character.GetMaxStamina`; sandbox calculation and HUD frame math | **Already expressible:** +1.0 internal units = +100 clean stamina. This is different from doubling capacity remaining after afflictions |
| Falling objects nearly kill you | `CollisionModifier.Collide(Character, ContactPoint, Collision, Bodypart)`, `onCollide`, `damage`; `Bodypart.OnCollisionEnter/Stay`; existing Injury hook | **Plausible, new source-aware hook required.** The current non-fall multiplier also amplifies unrelated hits |
| Actual instant ghost death | `Character.DieInstantlyLocal` / `RPCA_Die` exist in the inspected API | **Separate mechanic, untested.** A large Injury hit can cause a pass-out rather than ghost death; death must use the game's owner/network flow |

[Tiny Scouts](https://thunderstore.io/c/peak/p/JellyJam/Tiny_Scouts/) demonstrates visible shrinking, while its
author notes early-version issues and a re-shrink key. Inspection of its published DLL shows a root-scale
change in `Character.Update`, alongside movement changes. This supports feasibility; it does not prove
correct half-size collision or compatibility with 2.6.b. Its code is not included in this mod.

For “anything falls on you,” a card needs a precise trigger. A reasonable test definition would be a moving
physical object hitting from above faster than a chosen speed, with an Injury amount or an actual death
outcome specified. A static roof touch, jumping into a wall, a teammate landing on you and a falling rock are
different cases. Hooking the impact source before the Injury calculation avoids treating all Injury as a crush.
Debounce contacts so multiple ragdoll bodyparts do not apply the penalty repeatedly.

Before considering this effect complete, playtest camera height, actual hitboxes, wall grabs, ropes/vines,
jump and fall behavior, teammate carrying/revival, held items/backpacks, remote rendering, host migration,
and return to normal size after sacrificing the card or ending the run. Apply size from a saved baseline
once or as an absolute derived value, never repeatedly multiply the previous frame's scale.

## Other directions you can design

These are possibilities to write into the workbook, not new cards installed by this patch.

| Direction | What can be reused | What would need new work |
|---|---|---|
| Low stamina makes you lighter/faster, but drains hunger | Weight/speed/hunger channels | A low-stamina condition and clear threshold/hysteresis |
| Convert an affliction into temporary stamina | Status inspection/subtraction, yellow stamina, cooldown/charge ledger | A conversion handler with an explicit exchange rate and recursion protection |
| Store part of a fall's Injury, then pay it later or at a campfire | Existing deferred-Injury ability and campfire callback | A new release rule and interaction tests with rescue/damage-sharing effects |
| Food effects depend on your condition or what you ate | Food callbacks, healing/poison/bonus stamina, once-per-biome charges | Item classification and a new ability rule |
| Sprint charges a jump or emergency climbing boost | Existing momentum, double-jump and climbing-cost machinery | Charge state, activation rules, cooldown and feedback |
| Protect a teammate by taking their burden | Existing shared-Injury aura and transport | Any new burden type needs its own transfer and authority rules |
| Grow/shrink based on an event or affliction | Size/physics API evidence above, ability lifecycle | Same resize validation as the half-size concept; more state synchronization |
| Slower falling / low gravity | Ragdoll rigid bodies and Unity physics APIs | New airborne physics logic and extensive live climbing/network checks |
| Biome/night/altitude-dependent effects | Map/biome access and existing modifier conditions | New conditions; verify the actual 2.6.b environment members first |

Extra pockets, arbitrary new inventory layouts, global time control and teleportation without a validated
safe destination should not be treated as simple number changes. They touch inventory, shared simulation
or world geometry. They remain separate engineering tasks.

## Check your actual PEAK 2.6.b APIs

This tool reads DLL metadata, writes JSON, and applies no effects. It needs .NET SDK 8 (already required to
build the mod) and restores Mono.Cecil from NuGet on the first run.

```bash
dotnet run --project tools/PeakApiProbe -- "C:/Program Files (x86)/Steam/steamapps/common/PEAK/PEAK_Data/Managed" "build/peak-api-2.6.b.json" "design/hooks.json"
```

A successful probe establishes member/signature availability only. Return its JSON with the edited workbook
if available; then proposed effects can be implemented against the installed API and playtested individually.

## Playtest the fixes on 2.6.b

1. With everyone on 0.6.1, test opening and campfire offers in both pick modes. Default timer: 60 seconds;
   after 59 seconds no automatic card should have been taken. Custom durations should be preserved.
2. Set the host's `General/PickTimeoutSeconds = 0`. Wait longer than 70 seconds: no random card, countdown
   or forced session closure. Choose manually and confirm all living scouts can finish.
3. Kill a scout, light a fire, then revive them. They must not get that fire's offer or a timeout card, and must
   be eligible at a later fire if alive when it is lit. Ghost exclusion must not consume their next-pick flags.
4. Kill a scout after the offer opens but before they choose. Their fan should close; the host should skip them,
   with no granted card. The next turn should get its full timer, and untimed sessions should still finish.
5. Test a fire queued behind another open pick, a disconnect during picking and a host change. No ghost
   should hold up the living players or receive a card from a fire they missed while dead.

These live checks are pending. No experimental size, gravity or crush ability has been enabled.
