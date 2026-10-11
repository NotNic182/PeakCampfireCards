using System.Collections.Generic;
using CampfireCards.Cards;
using CampfireCards.Core;
using CampfireCards.Generated;
using CampfireCards.Net;
using Peak.Afflictions;
using UnityEngine;

namespace CampfireCards.Abilities
{
    public sealed class Ability_CampfireMeal : Ability
    {
        public override void OnCampfireLit(Character me, bool acquiredThisFire)
        {
            if (!me.data.dead) me.refs.afflictions.SetStatus(CharacterAfflictions.STATUSTYPE.Hunger, 0f);
        }
    }

    public sealed class Ability_NextCampfireSpawn : Ability
    {
        public override void OnCampfireLit(Character me, bool acquiredThisFire)
        {
            if (acquiredThisFire || me.data.dead || (Effects.Flags(NetSend.LocalActor) & Effects.FlagBalloonUsed) != 0) return;
            var fire = LocalCards.LastCampfire;
            int item = Mathf.RoundToInt(P("item", 2f));
            if (fire == null || item < 0 || item >= CardTable.SpawnItems.Length) return;
            NetSend.RaiseToMaster(new SpawnItem { item = (byte)item, segment = (byte)fire.advanceToSegment });
        }
        public override string Status(Character me) => Card.Name + ": " +
            ((Effects.Flags(NetSend.LocalActor) & Effects.FlagBalloonUsed) == 0 ? "Balloon Bunch at next fire" : "Balloon Bunch received");
    }

    public sealed class Ability_HelpCost : Ability
    {
        private bool _paid;
        public override void Tick(Character me, float dt) { if (!me.data.isReaching) _paid = false; }
        public void OnPull(Character me)
        {
            if (_paid) return;
            _paid = true;
            me.UseStamina(P("stamina", .1f));
        }
    }

    /// <summary>Uses PEAK's own timed/synced mushroom afflictions, without eating an item or applying its bad effects.</summary>
    public sealed class Ability_MushroomCycle : Ability
    {
        private readonly PeriodicPowerClock _clock = new PeriodicPowerClock();
        private Affliction _granted;
        private string _power;
        private float _originalDuration;
        private static readonly Affliction.AfflictionType[] Powers =
            { Affliction.AfflictionType.InfiniteStamina, Affliction.AfflictionType.Invincibility, Affliction.AfflictionType.LowGravity };

        public override void Tick(Character me, float dt)
        {
            _clock.Advance(dt, !me.data.dead);
            if (me.data.dead || me.data.passedOut || me.data.fullyPassedOut || !_clock.Ready(P("interval", 600f))) return;
            var available = new List<int>();
            for (int i = 0; i < Powers.Length; i++)
                if (!me.refs.afflictions.HasAfflictionType(Powers[i], out _)) available.Add(i);
            if (available.Count == 0) return; // do not merge a card buff into an existing consumable buff
            int choice = available[Random.Range(0, available.Count)];
            float duration = Mathf.Max(.1f, P("duration", 20f));
            switch (choice)
            {
                case 0: _granted = new Affliction_InfiniteStamina(duration) { climbDelay = 0f }; _power = "Infinite Stamina"; break;
                case 1: _granted = new Affliction_Invincibility { totalTime = duration }; _power = "Invincibility"; break;
                default: _granted = new Affliction_LowGravity(Mathf.Max(1, Mathf.RoundToInt(P("lowGravity", 1f))), duration); _power = "Low Gravity"; break;
            }
            _originalDuration = duration;
            me.refs.afflictions.AddAffliction(_granted);
            _clock.Consume();
            Ui.Hud.Toast(Card.Name + ": " + _power + " for " + duration.ToString("0.#") + " seconds");
        }

        public override void OnRemoved(Character me)
        {
            // If an item extended this buff, leave that native extension alone.
            if (_granted != null && _granted.totalTime == _originalDuration && _granted.bonusTime <= 0f
                && me.refs.afflictions.afflictionList.Contains(_granted)) me.refs.afflictions.RemoveAffliction(_granted);
            _granted = null;
        }

        public override string Status(Character me)
        {
            if (_granted != null && me.refs.afflictions.afflictionList.Contains(_granted))
                return Card.Name + ": " + _power + " active (" + CardCopy.Countdown(_granted.totalTime + _granted.bonusTime - _granted.timeElapsed) + ")";
            return Card.Name + ": next power in " + CardCopy.Countdown(_clock.Remaining(P("interval", 600f)));
        }
    }
}
