using System;
using System.Collections.Generic;
using System.Linq;
using CampfireCards.Cards;
using CampfireCards.Core;
using CampfireCards.Net;

// Game/UI/transport and modifier-fold storage are replaced. PickSession, Effects acquisition and LocalCards run unchanged.
public class Character
{
    public static Character localCharacter;
    public CharacterData data = new CharacterData();
    public bool isBot;
    public bool warping;
    public UnityEngine.Vector3 Center;
    public bool IsSliding() => false;
    public float MaxStamina = 1f;
    public readonly Dictionary<CharacterAfflictions.STATUSTYPE, float> Statuses = new Dictionary<CharacterAfflictions.STATUSTYPE, float>();
    public float GetMaxStamina() => MaxStamina;
    public void AddStamina(float amount) => data.currentStamina = Math.Min(MaxStamina, data.currentStamina + amount);
}
public class CharacterData
{
    public bool dead, passedOut, fullyPassedOut, isGrounded;
    public float currentStamina, groundedFor;
    public UnityEngine.Vector3 avarageVelocity;
}
public class Campfire { }
public class Item { }
public class CharacterAfflictions { public enum STATUSTYPE { Poison, Spores, Drowsy, Hot, Curse } }
public static class PlayerHandler
{
    public static readonly Dictionary<int, Character> Characters = new Dictionary<int, Character>();
    public static bool TryGetCharacter(int actor, out Character c) => Characters.TryGetValue(actor, out c);
}
namespace UnityEngine
{
    public static class Time { public static float unscaledTime, time; public static int frameCount; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float magnitude => (float)Math.Sqrt(x * x + y * y + z * z);
        public static float Distance(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z).magnitude;
    }
    public static class Random
    {
        public static int Next = 1;
        public static int Range(int min, int max) => Math.Clamp(Next, min, max - 1);
    }
    public static class Mathf { public static float Max(float a, float b) => Math.Max(a, b); public static int CeilToInt(float f) => (int)Math.Ceiling(f); }
}
namespace HarmonyLib
{
    public static class AccessTools
    {
        public static System.Reflection.MethodInfo Method(Type type, string name) => type.GetMethod(name);
        public static T MethodDelegate<T>(System.Reflection.MethodInfo method) where T : Delegate =>
            (T)Delegate.CreateDelegate(typeof(T), method);
    }
}
namespace Photon.Realtime
{
    public enum ReceiverGroup { All }
    public class Player { public int ActorNumber; }
    public class Room { public Dictionary<int, Player> Players = new Dictionary<int, Player>(); }
}
namespace Photon.Pun
{
    public static class PhotonNetwork
    {
        public static double Time;
        public static Photon.Realtime.Room CurrentRoom;
    }
}
namespace CampfireCards
{
    public class Setting<T> { public T Value; public Setting(T value) { Value = value; } }
    public class TestLog { public void LogWarning(string s) { } public void LogInfo(string s) { } public void LogError(string s) { } }
    public static class Plugin
    {
        public static Setting<float> PickTimeoutSeconds;
        public static Setting<PickMode> PickModeSetting;
        public static readonly TestLog Log = new TestLog();
        public static string SecondWindKeyLabel => "V";
        public static string EmergencyPurgeKeyLabel => "K";
        public static void Verbose(string s) { }
    }
}
namespace CampfireCards.Generated
{
    public static class UiTiming { public const float PickTimeoutSeconds = 60f; }
    public static class Ui_WaitingBanner
    {
        public const string Template = "{0} is picking", SimultaneousTemplate = "Waiting for {0}", OrderLabel = "Pick order: ";
        public const string DealOrderLabel = "Dealt in this order: ", Separator = "  ·  ", StatusTemplate = "{0}  {1}s";
        public const string DoneColor = "done", PickingColor = "picking", QueuedColor = "queued";
    }
    public static class Ui_Toast
    {
        public const string TookTemplate = "{0} took {1}", AutoTookTemplate = "{0} ran out of time and got {1}", You = "You";
        public const string SkipYouTemplate = "You skip this pick ({0})", SkipTemplate = "{0} skips this pick ({1})", BonusTemplate = "{0} also got {1}";
    }
    // ability bar wording as in design/ui.json AbilityBar (the abilities build their status lines from it)
    public static class Ui_AbilityBar
    {
        public const string Ready = "ready", UsedThisBiome = "used this biome", UsedThisRun = "used";
        public const string AlreadyUsedThisBiome = "{0}: already used this biome", StatusTemplate = "{0}: {1}", KeyedTemplate = "{0} [{1}]: {2}";
    }
}
namespace CampfireCards.Cards
{
    public static class Deck
    {
        public static bool Ready = true;
        public static byte[] BonusCards = new byte[0];
        public static byte[] Deal(Random rng, ICollection<byte> owned, int n, bool solo, HashSet<byte> claimed) =>
            Enumerable.Range(1, n).Select(i => (byte)i).ToArray();
        public static byte[] DealBonus(Random rng, ICollection<byte> owned, int n, bool solo, Generated.CardRarity rarity, ICollection<byte> offered) => BonusCards.Take(n).ToArray();
        public static string NameOf(byte id) => CardSettings.Current.Card(id).Name;
        public static string NameOfAbility(string ability) => ability;
    }
    public static partial class Effects
    {
        public const byte FlagSkipNext = 1, FlagOfferOne = 2;
        public static readonly Dictionary<int, List<byte>> CardsByActor = new Dictionary<int, List<byte>>();
        public static Dictionary<int, List<byte>> Owned => CardsByActor;
        public static readonly PenaltyLedger Penalties = new PenaltyLedger();
        public static readonly ModResult Local = new ModResult();
        public static int SharedBurdenOwner;
        public static float SharedBurdenFactor;
        public static readonly Dictionary<int, byte> PickFlags = new Dictionary<int, byte>();
        public static HashSet<byte> ClaimedMythics() => new HashSet<byte>();
        public static List<byte> CardsOf(int actor)
        {
            if (!Owned.ContainsKey(actor)) Owned[actor] = new List<byte>();
            return Owned[actor];
        }
        public static byte Flags(int actor) => PickFlags.TryGetValue(actor, out var f) ? f : (byte)0;
        public static void SetFlag(int actor, byte flags, bool on) => PickFlags[actor] = on ? (byte)(Flags(actor) | flags) : (byte)(Flags(actor) & ~flags);
        public static int MythicOwner(byte id) => Owned.FirstOrDefault(p => p.Value.Contains(id)).Value == null
            ? int.MinValue : Owned.First(p => p.Value.Contains(id)).Key;
        public static float PenaltyValue(Generated.CardDef card, byte variant) =>
            CardSettings.Current.Param(card, CardPenaltyRules.Parameter(variant), variant <= 2 ? 1.15f : 1.2f);
        private static bool Condition(Generated.Condition condition, Character me, Generated.CardDef card) => condition == Generated.Condition.always;
        public static void ApplyFields(Character me) { }
        private static void AddPenalties(List<ActiveMod> mods, int actor) { }
    }
}
namespace CampfireCards.Abilities
{
    // Native status storage is a game boundary; the recovery abilities themselves run unchanged.
    public static class CardStatus
    {
        public static float Get(Character c, CharacterAfflictions.STATUSTYPE t) => c.Statuses.TryGetValue(t, out var v) ? v : 0f;
        public static void Remove(Character c, CharacterAfflictions.STATUSTYPE t, float amount) => c.Statuses[t] = Math.Max(0f, Get(c, t) - amount);
        public static void Add(Character c, CharacterAfflictions.STATUSTYPE t, float amount) => c.Statuses[t] = Get(c, t) + amount;
    }
    public static class SafeSpot { public static bool Validate(UnityEngine.Vector3 candidate, out UnityEngine.Vector3 pos) { pos = candidate; return true; } }
    public static class Ability_EternalCampfire { public static bool RequestFromTeammate(Character me, int incapEvent) => false; }
}
namespace CampfireCards.Net
{
    public static class NetSend
    {
        public static bool IsHost = true;
        public static int LocalActor = 1;
        public static bool IsSolo => Photon.Pun.PhotonNetwork.CurrentRoom.Players.Count <= 1;
        public static readonly List<INetEvent> Sent = new List<INetEvent>();
        public static bool Deliver = true;
        public static string NameOf(int actor) => "Player " + actor;
        public static void Raise(INetEvent e, Photon.Realtime.ReceiverGroup g)
        {
            Sent.Add(e);
            if (!Deliver) return;
            // Synchronous loopback exercises the offline/reentrant paths as well as the host's normal decisions.
            if (e is PickSessionStart start) PickSession.OnStart(start);
            if (e is PickMade pick) PickSession.OnPick(pick);
            if (e is PickSkipped skip) PickSession.OnSkipped(skip);
            if (e is SessionEnd end) PickSession.OnSessionEnd(end);
            if (e is CardsGranted granted) PickSession.OnCardsGranted(granted);
            if (e is CardPenalty penalty) Effects.Penalties.TrySet(penalty.actor, penalty.slot, penalty.variant);
        }
        public static void RaiseToMaster(INetEvent e) => Sent.Add(e);
    }
}
namespace CampfireCards.Art { public static class Sfx { public static void Play(string s) { } } }
namespace CampfireCards.Ui
{
    public class CardPicker
    {
        public enum Mode { Pick, Reveal, Sacrifice }
        public static CardPicker Instance;
        public bool isOpen;
        public Mode Current;
        public float Timeout;
        public static CardPicker Ensure() => Instance ?? (Instance = new CardPicker());
        public static void HideIfOpen() { if (Instance != null) Instance.isOpen = false; }
        public void Show(byte reason, byte[] hand, float timeout, Mode mode) { isOpen = true; Current = mode; Timeout = timeout; }
        public void ShowReveal(byte[] hand) { isOpen = true; Current = Mode.Reveal; }
        public void ShowSacrifice(byte[] cards, Action<byte> picked) { isOpen = true; Current = Mode.Sacrifice; }
    }
    public static class Hud
    {
        public static string Waiting;
        public static void RefreshMyCards() { }
        public static void Toast(string s) { }
        public static void HideWaiting() => Waiting = null;
        public static void ShowWaiting(string status, string order) => Waiting = status;
    }
    public static class UiKit { public static string Hex(string c) => "FFFFFF"; public static string ColorTag(string c) => "<color=#FFFFFF>"; }
}
