# Card interactions — candidate 0.6.3

Mythics intentionally break normal balance. Offers remain random by rarity without build-compatibility filters.
Survival cards may cost later picks. Fine Print is the explicit exception that can remove an earlier non-Mythic card.
This patch changes interaction handling, not card numbers, rarity weights or the existing eligibility rules.

## Fixed interactions

- A fall cancelled by BingBong Parachute does not charge a Shared Burden helper or create a Payment Plan debt.
  Older debts are still owed. The nearest eligible Shared Burden owner receives an uncancelled hit's transfer.
- Vanilla-blocked Injury cannot spend Parachute, charge a helper or create delayed Injury. Native Milk feedback
  still runs. Parachute predicts native Skeleton/difficulty multipliers and the separate 2.5-unit installment rounding.
- Payment Plan debt that matures while dead waits for revival. Payments retain their original native immunity
  flags and arrive separately. Intentionally sacrificing Payment Plan still clears its remaining debt.
- Every accepted Clown or Dishonor acquisition pays its acquisition cost, including enabled duplicates.
  Network replays do not repeat the grant or cost. Clown penalties remain outside card ownership.
- State sync reconciles existing abilities rather than recreating them. Fine Print removal/reacquisition retains
  spent run/biome charges and cooldowns. Removed abilities stop ticking and clean up live movement/anchor state.
- Mini Tenacity recognizes the native Spore treatment that accompanies a medical Poison cure, then selects an
  eligible status the item did not already treat.

The cooked-food compatibility, discount soft cap, signed-weight correction, Human Carry-On timer hook and
nearest-thorn wording from 0.6.2 remain in place.

## In-game checks still required

1. Use Parachute, Payment Plan and a nearby Shared Burden owner together. A fatal fall should rescue the victim
   without injuring the helper or adding a new bill; a surviving fall should share and defer normally. Check Milk
   protection and Skeleton form separately, including a hit near a 2.5-unit boundary.
2. Let a delayed payment mature while dead, revive, and confirm it lands once. Ordinary unconscious scouts should
   still pay on time. Sacrificing Payment Plan should forgive its remaining debt.
3. Acquire Clown twice with duplicates enabled, then sacrifice and reacquire it. Check retained penalties, new
   acquisition costs, host/client displays and repeated network messages. Do the same for Dishonor's Curse.
4. Spend a run/biome charge or start Nic of Time's cooldown, sacrifice/reacquire the card, and check the original
   limit. An unchanged state sync must also preserve a queued payment, active boost and anchor.
5. Compare paused and advancing carried death timers on host-owned and client-owned victims. Move Balloon Bunch
   between hands, pockets and backpack with weight cards active. Test one through five new thorns.
6. Use medical Poison treatment with Poison and Spores high and Injury lower. Mini Tenacity should target Injury
   rather than treating Spores twice as its extra status.

Automated regressions and compilation do not run PEAK physics or multiplayer. Fresh-client reconnect still cannot
restore instance-held ability state, and host migration still does not preserve Eternal Campfire's used charges.
The existing disabled inventory-slot channel remains a known implementation limitation.
