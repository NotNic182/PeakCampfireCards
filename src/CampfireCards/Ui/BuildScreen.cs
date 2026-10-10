using System.Collections.Generic;
using System.Linq;
using System.Text;
using CampfireCards.Abilities;
using CampfireCards.Cards;
using CampfireCards.Core;
using CampfireCards.Generated;
using CampfireCards.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CampfireCards.Ui
{
    /// <summary>
    /// ui.json BuildScreen: the Current Build. Every owned card with its benefit, drawback, conditional modifiers (active
    /// or not right now) and charge or cooldown; the combined stats from the live Effects fold, next to the always-on
    /// value whenever a condition or an aura changes it; and the teammates' auras affecting you. A non-blocking overlay
    /// toggled with the BuildScreen key; it never computes a modifier itself (Effects and Core.BuildStats do).
    /// </summary>
    public static class BuildScreen
    {
        private static Image _panel;
        private static TextMeshProUGUI _cards, _stats, _hint;
        private static float _nextRefresh;
        private static readonly ModResult _always = new ModResult();

        public static bool IsOpen => _panel != null && _panel.gameObject.activeSelf;

        public static void Toggle(Character me)
        {
            if (IsOpen) { Hide(); return; }
            if (!RunState.OnIsland || me == null) return;
            Ensure();
            _panel.gameObject.SetActive(true);
            Refresh(me);
        }

        public static void Hide()
        {
            if (_panel != null) _panel.gameObject.SetActive(false);
        }

        /// <summary>Per frame from Hooks.Tick: closes for a pick or off the island, refreshes 4 times a second.</summary>
        public static void Tick(Character me)
        {
            if (!IsOpen) return;
            if (!RunState.OnIsland || me == null || (CardPicker.Instance != null && CardPicker.Instance.isOpen)) { Hide(); return; }
            if (Time.unscaledTime < _nextRefresh) return;
            Refresh(me);
        }

        private static void Ensure()
        {
            if (_panel != null) return;
            _panel = UiKit.Panel(UiKit.Canvas.transform, "BuildScreen", Ui_BuildScreen.Size, Ui_BuildScreen.Panel, Art.Shapes.Panel);
            _panel.type = Image.Type.Sliced;
            UiKit.Place(_panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero);

            var size = Ui_BuildScreen.Size;
            var title = UiKit.Text(_panel.transform, "Title", new Vector2(size.x - 60f, 50f), Ui_BuildScreen.TitleFontSize, Ui_BuildScreen.HeaderColor, TextAlignmentOptions.Center, FontStyles.Bold);
            UiKit.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -18f));
            title.text = Ui_BuildScreen.Title;

            float top = 84f, bottom = 56f, gap = 40f;
            float leftW = (size.x - 60f - gap) * 0.56f, rightW = size.x - 60f - gap - leftW, h = size.y - top - bottom;
            _cards = Column("Cards", new Vector2(leftW, h), new Vector2(30f, -top));
            _stats = Column("Stats", new Vector2(rightW, h), new Vector2(30f + leftW + gap, -top));

            _hint = UiKit.Text(_panel.transform, "Hint", new Vector2(size.x - 60f, 36f), Ui_BuildScreen.MinFontSize + 3f, Ui_BuildScreen.InactiveColor, TextAlignmentOptions.Center, FontStyles.Italic);
            UiKit.Place(_hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 14f));
            _panel.gameObject.SetActive(false);
        }

        private static TextMeshProUGUI Column(string name, Vector2 size, Vector2 topLeft)
        {
            var t = UiKit.Text(_panel.transform, name, size, Ui_BuildScreen.FontSize, Color.white, TextAlignmentOptions.TopLeft);
            UiKit.Place(t.rectTransform, new Vector2(0f, 1f), topLeft);
            t.enableAutoSizing = true;
            t.fontSizeMin = Ui_BuildScreen.MinFontSize;
            t.fontSizeMax = Ui_BuildScreen.FontSize;
            return t;
        }

        private static void Refresh(Character me)
        {
            _nextRefresh = Time.unscaledTime + 0.25f;
            _cards.text = CardsText(me);
            _stats.text = StatsText();
            _hint.text = string.Format(Ui_BuildScreen.Hint, Plugin.BuildScreenKeyLabel);
        }

        private static string Hex(Color c) => UiKit.Hex(c);

        // ------------------------------------------------------------------ left: the cards
        private static string CardsText(Character me)
        {
            var mine = Effects.CardsOf(NetSend.LocalActor);
            if (mine.Count == 0) return "<color=#" + Hex(Ui_BuildScreen.InactiveColor) + ">No cards yet. Pick one at the next campfire.</color>";
            var abilities = LocalCards.All.ToList();
            var sb = new StringBuilder();
            foreach (var id in mine)
            {
                CardDef def;
                if (!Deck.TryGet(id, out def)) continue;
                string rarity = Hex(Deck.RarityColor(def.Rarity));
                sb.Append("<color=#").Append(rarity).Append(">■ <b>").Append(def.Name).Append("</b></color>  <size=75%><color=#")
                  .Append(rarity).Append('>').Append(Deck.RarityName(def.Rarity)).Append("</color></size>\n");
                sb.Append("<color=#").Append(Hex(Ui_CardEffects.GoodColor)).Append(">   + ").Append(def.Positive).Append("</color>\n");
                if (string.IsNullOrEmpty(def.Negative))
                    sb.Append("<color=#").Append(Hex(Ui_CardFlavor.Color)).Append(">   ").Append(Ui_CardEffects.NoDrawback).Append("</color>\n");
                else
                    sb.Append("<color=#").Append(Hex(Ui_CardEffects.BadColor)).Append(">   - ").Append(def.Negative).Append("</color>\n");
                AppendConditional(sb, def);
                foreach (var a in abilities)
                {
                    if (a.Card == null || a.Card.Id != def.Id) continue;
                    string status = null;
                    try { status = a.Status(me); } catch { }
                    if (!string.IsNullOrEmpty(status))
                        sb.Append("<color=#").Append(Hex(Ui_BuildScreen.HeaderColor)).Append(">   ").Append(status).Append("</color>\n");
                }
                sb.Append('\n');
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>Each modifier with a condition or a teammate target, and whether the live fold applies it right now.</summary>
        private static void AppendConditional(StringBuilder sb, CardDef def)
        {
            var s = CardSettings.Current;
            for (int i = 0; i < def.Mods.Length; i++)
            {
                var m = def.Mods[i];
                bool forTeam = m.Target == ModTarget.team;
                if (m.When == Generated.Condition.always && !forTeam) continue;
                var ch = ChannelTable.Defs[(int)m.Channel];
                string effect = BuildStats.Say(s.ModValue(def, i), ch.IsMul, BuildStats.UnitOf(m.Channel), StatWords.Of(m.Channel));
                if (effect == null) continue;
                string line;
                bool active;
                if (forTeam)
                {
                    line = "Teammates within " + s.Param(def, "radius", 6f).ToString("0.#") + " m: " + LowerFirst(effect);
                    active = false;
                }
                else
                {
                    int c = (int)m.When;
                    line = effect + " while " + (c < ConditionTable.Shown.Length ? ConditionTable.Shown[c] : m.When.ToString());
                    active = IsActive(def.Id, i);
                }
                string state = forTeam ? "" : active ? "  (on now)" : "  (off now)";
                var color = active ? Ui_BuildScreen.ActiveColor : Ui_BuildScreen.InactiveColor;
                sb.Append("<size=88%><color=#").Append(Hex(color)).Append(">   · ").Append(line).Append(state).Append("</color></size>\n");
            }
        }

        private static bool IsActive(byte cardId, int modIndex)
        {
            foreach (var a in Effects.ActiveMods) if (a.CardId == cardId && a.ModIndex == modIndex) return true;
            return false;
        }

        private static string LowerFirst(string t) => string.IsNullOrEmpty(t) ? t : char.ToLowerInvariant(t[0]) + t.Substring(1);

        /// <summary>Green when the value helps the scout, red when it hurts (stats.json better).</summary>
        private static string GoodOrBad(StatLine l, float v) => Hex(BuildStats.Helps(v, l.IsMul, l.Words) ? Ui_CardEffects.GoodColor : Ui_CardEffects.BadColor);

        // ------------------------------------------------------------------ right: combined effects and auras
        private static string StatsText()
        {
            var sb = new StringBuilder();
            sb.Append("<b><color=#").Append(Hex(Ui_BuildScreen.HeaderColor)).Append('>').Append(Ui_BuildScreen.StatsHeader).Append("</color></b>\n");
            Effects.FoldAlways(_always);
            var lines = BuildStats.Lines(Effects.Local, _always);
            if (lines.Count == 0) sb.Append("<color=#").Append(Hex(Ui_BuildScreen.InactiveColor)).Append(">Nothing changed right now.</color>\n");
            string grey = Hex(Ui_BuildScreen.InactiveColor);
            foreach (var l in lines)
            {
                string now = BuildStats.Say(l, l.Now), always = BuildStats.Say(l, l.Always);
                if (!l.Conditional)
                    sb.Append("<color=#").Append(GoodOrBad(l, l.Now)).Append('>').Append(now).Append(".</color>\n");
                else if (now == null)
                    sb.Append("<color=#").Append(grey).Append('>').Append(always).Append(". <size=80%>(not right now)</size></color>\n");
                else
                {
                    sb.Append("<color=#").Append(GoodOrBad(l, l.Now)).Append('>').Append(now).Append(".</color> <size=80%><color=#").Append(grey).Append(">(right now)</color></size>\n");
                    sb.Append("<size=80%><color=#").Append(grey).Append(">   Otherwise: ").Append(always != null ? LowerFirst(always) : "no change").Append(".</color></size>\n");
                }
            }

            var auras = Auras();
            if (auras.Count > 0)
            {
                sb.Append("\n<b><color=#").Append(Hex(Ui_BuildScreen.HeaderColor)).Append('>').Append(Ui_BuildScreen.AurasHeader).Append("</color></b>\n");
                foreach (var a in auras) sb.Append(a).Append('\n');
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>Teammates' cards whose aura applies to the local scout right now (from the live fold).</summary>
        private static List<string> Auras()
        {
            var list = new List<string>();
            int me = NetSend.LocalActor;
            var mine = Effects.CardsOf(me);
            var seen = new HashSet<byte>();
            foreach (var m in Effects.ActiveMods)
            {
                if (mine.Contains(m.CardId) || !seen.Add(m.CardId)) continue;
                var owners = Effects.CardsByActor.Where(kv => kv.Key != me && kv.Value.Contains(m.CardId)).Select(kv => NetSend.NameOf(kv.Key));
                list.Add("<color=#" + Hex(Ui_BuildScreen.ActiveColor) + ">" + Deck.NameOf(m.CardId) + "</color> (" + string.Join(", ", owners) + ")");
            }
            if (Effects.SharedBurdenOwner != int.MinValue && Effects.SharedBurdenOwner != me)
                list.Add("<color=#" + Hex(Ui_BuildScreen.ActiveColor) + ">" + Deck.NameOfAbility("SharedBurden") + "</color> (" + NetSend.NameOf(Effects.SharedBurdenOwner) + "): "
                         + LowerFirst(BuildStats.Say(Effects.SharedBurdenFactor, true, null, StatWords.Of(Channel.allInjury)) ?? "no change"));
            return list;
        }
    }
}
