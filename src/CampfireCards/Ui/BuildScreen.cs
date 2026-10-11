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
    /// It fits the screen (at most ui.json Size, safeMargin from every edge); what does not fit is split into pages at
    /// full size, and the key steps through them (scrolling would need the wheel or a stick, which the game uses).
    /// </summary>
    public static class BuildScreen
    {
        private static Image _panel;
        private static TextMeshProUGUI _title, _cards, _stats, _hint;
        private static float _nextRefresh;
        private static readonly ModResult _always = new ModResult();
        /// <summary>The page shown (1-based) and how many the longer column needs right now.</summary>
        private static int _page = 1, _pages = 1;

        public static bool IsOpen => _panel != null && _panel.gameObject.activeSelf;

        /// <summary>The key: open on page 1, then the next page, then close after the last.</summary>
        public static void Toggle(Character me)
        {
            if (IsOpen)
            {
                if (_page >= _pages) { Hide(); return; }
                _page++;
                Refresh(me);
                return;
            }
            if (!RunState.OnIsland || me == null) return;
            Ensure();
            Layout();
            _page = 1;
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

            _title = UiKit.Text(_panel.transform, "Title", new Vector2(100f, Ui_BuildScreen.TitleHeight), Ui_BuildScreen.TitleFontSize, Ui_BuildScreen.HeaderColor, TextAlignmentOptions.Center, FontStyles.Bold);
            UiKit.Place(_title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, Ui_BuildScreen.TitleY));
            _title.text = Ui_BuildScreen.Title;

            _cards = Column("Cards");
            _stats = Column("Stats");

            _hint = UiKit.Text(_panel.transform, "Hint", new Vector2(100f, Ui_BuildScreen.HintHeight), Ui_BuildScreen.HintFontSize, Ui_BuildScreen.InactiveColor, TextAlignmentOptions.Center, FontStyles.Italic);
            UiKit.AutoSize(_hint, Ui_BuildScreen.MinFontSize);
            UiKit.Place(_hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, Ui_BuildScreen.HintY));
            _panel.gameObject.SetActive(false);
        }

        /// <summary>Size the panel and its columns for the screen as it is now (resolution and UI scale can change between openings).</summary>
        private static void Layout()
        {
            var canvas = UiKit.CanvasSize;
            var size = new Vector2(Mathf.Min(Ui_BuildScreen.Size.x, canvas.x - 2f * UiTiming.SafeMargin),
                                   Mathf.Min(Ui_BuildScreen.Size.y, canvas.y - 2f * UiTiming.SafeMargin));
            _panel.rectTransform.sizeDelta = size;
            float pad = Ui_BuildScreen.PadX, inner = size.x - 2f * pad;
            _title.rectTransform.sizeDelta = new Vector2(inner, Ui_BuildScreen.TitleHeight);
            _hint.rectTransform.sizeDelta = new Vector2(inner, Ui_BuildScreen.HintHeight);

            float top = Ui_BuildScreen.ColumnsTop, gap = Ui_BuildScreen.ColumnGap;
            float leftW = (inner - gap) * Ui_BuildScreen.LeftShare, rightW = inner - gap - leftW, h = size.y - top - Ui_BuildScreen.ColumnsBottom;
            _cards.rectTransform.sizeDelta = new Vector2(leftW, h);
            _cards.rectTransform.anchoredPosition = new Vector2(pad, -top);
            _stats.rectTransform.sizeDelta = new Vector2(rightW, h);
            _stats.rectTransform.anchoredPosition = new Vector2(pad + leftW + gap, -top);
        }

        /// <summary>A text column at full size; what does not fit goes onto further pages.</summary>
        private static TextMeshProUGUI Column(string name)
        {
            var t = UiKit.Text(_panel.transform, name, new Vector2(100f, 100f), Ui_BuildScreen.FontSize, UiPalette.Text, TextAlignmentOptions.TopLeft);
            UiKit.Place(t.rectTransform, new Vector2(0f, 1f), Vector2.zero);
            UiKit.LineHeight(t, Ui_BuildScreen.LineHeight);
            t.overflowMode = TextOverflowModes.Page;
            return t;
        }

        private static void Refresh(Character me)
        {
            _nextRefresh = Time.unscaledTime + UiTiming.BuildRefreshSeconds;
            _cards.text = CardsText(me);
            _stats.text = StatsText();

            // pages: the longer column sets the count; a shorter one stays on its last page
            _cards.ForceMeshUpdate(true);
            _stats.ForceMeshUpdate(true);
            int cardPages = Mathf.Max(1, _cards.textInfo.pageCount), statPages = Mathf.Max(1, _stats.textInfo.pageCount);
            _pages = Mathf.Max(cardPages, statPages);
            _page = Mathf.Clamp(_page, 1, _pages);
            _cards.pageToDisplay = Mathf.Min(_page, cardPages);
            _stats.pageToDisplay = Mathf.Min(_page, statPages);

            string key = Plugin.BuildScreenKeyLabel;
            string action = _pages <= 1 ? string.Format(Ui_BuildScreen.CloseHint, key)
                : string.Format(_page < _pages ? Ui_BuildScreen.NextPageHint : Ui_BuildScreen.LastPageHint, key, _page, _pages);
            _hint.text = Ui_BuildScreen.Hint + " " + action;
        }

        /// <summary>Opening size tags for the build screen's type roles (ui.json BuildScreen); each is closed with &lt;/size&gt;.</summary>
        private static readonly string Header = "<size=" + Ui_BuildScreen.HeaderSizePercent.ToString("0") + "%>";
        private static readonly string Meta = "<size=" + Ui_BuildScreen.MetaSizePercent.ToString("0") + "%>";
        private static readonly string Detail = "<size=" + Ui_BuildScreen.DetailSizePercent.ToString("0") + "%>";
        /// <summary>A detail line under a card or a stat: wrapped lines hang under its marker, not back at the margin.</summary>
        private static readonly string Indent = "<indent=" + Ui_BuildScreen.DetailIndent.ToString("0") + ">";
        private const string EndIndent = "</indent>";

        /// <summary>A column header, the same step above the body in both columns.</summary>
        private static void AppendHeader(StringBuilder sb, string text) =>
            sb.Append(Header).Append("<b>").Append(UiKit.ColorTag(Ui_BuildScreen.HeaderColor)).Append(text).Append("</color></b></size>\n");

        // ------------------------------------------------------------------ left: the cards
        private static string CardsText(Character me)
        {
            var mine = Effects.CardsOf(NetSend.LocalActor);
            var sb = new StringBuilder();
            AppendHeader(sb, Ui_MyCardsHud.Header);
            if (mine.Count == 0) return sb.Append(UiKit.ColorTag(Ui_BuildScreen.InactiveColor)).Append(Ui_BuildScreen.NoCards).Append("</color>").ToString();
            var abilities = LocalCards.All.ToList();
            foreach (var id in mine)
            {
                CardDef def;
                if (!Deck.TryGet(id, out def)) continue;
                Color rarity = Deck.RarityColor(def.Rarity);
                sb.Append(UiKit.ColorTag(rarity)).Append("■ <b>").Append(def.Name).Append("</b></color>  ").Append(Meta)
                  .Append(UiKit.ColorTag(rarity)).Append("<cspace=0.08em><uppercase>").Append(Deck.RarityName(def.Rarity))
                  .Append("</uppercase></cspace></color></size>\n");
                sb.Append(UiKit.ColorTag(Ui_CardEffects.GoodColor)).Append(Indent).Append("+ ").Append(def.Positive).Append(EndIndent).Append("</color>\n");
                if (string.IsNullOrEmpty(def.Negative))
                    sb.Append(UiKit.ColorTag(Ui_CardFlavor.Color)).Append(Indent).Append(Ui_CardEffects.NoDrawback).Append(EndIndent).Append("</color>\n");
                else
                    sb.Append(UiKit.ColorTag(Ui_CardEffects.BadColor)).Append(Indent).Append("- ").Append(def.Negative).Append(EndIndent).Append("</color>\n");
                AppendConditional(sb, def);
                foreach (var a in abilities)
                {
                    if (a.Card == null || a.Card.Id != def.Id) continue;
                    AbilityStatus status;
                    try { status = a.Status(me); } catch { status = AbilityStatus.None; }
                    if (!status.IsEmpty)
                        sb.Append(UiKit.ColorTag(Ui_BuildScreen.HeaderColor)).Append(Indent).Append(status.Text).Append(EndIndent).Append("</color>\n");
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
                    line = string.Format(Ui_BuildScreen.TeammatesTemplate, s.Param(def, "radius", 6f).ToString("0.#"), LowerFirst(effect));
                    active = false;
                }
                else
                {
                    int c = (int)m.When;
                    line = effect + " while " + (c < ConditionTable.Shown.Length ? ConditionTable.Shown[c] : m.When.ToString());
                    active = IsActive(def.Id, i);
                }
                string state = forTeam ? "" : "  " + (active ? Ui_BuildScreen.OnNow : Ui_BuildScreen.OffNow);
                var color = active ? Ui_BuildScreen.ActiveColor : Ui_BuildScreen.InactiveColor;
                sb.Append(Detail).Append(UiKit.ColorTag(color)).Append(Indent).Append("· ").Append(line).Append(state).Append(EndIndent).Append("</color></size>\n");
            }
        }

        private static bool IsActive(byte cardId, int modIndex)
        {
            foreach (var a in Effects.ActiveMods) if (a.CardId == cardId && a.ModIndex == modIndex) return true;
            return false;
        }

        private static string LowerFirst(string t) => string.IsNullOrEmpty(t) ? t : char.ToLowerInvariant(t[0]) + t.Substring(1);

        /// <summary>Green when the value helps the scout, red when it hurts (stats.json better).</summary>
        private static Color GoodOrBad(StatLine l, float v) => BuildStats.Helps(v, l.IsMul, l.Words) ? Ui_CardEffects.GoodColor : Ui_CardEffects.BadColor;

        /// <summary>The same + / - marker the cards use, so help and harm never rest on green and red alone.</summary>
        private static string Mark(StatLine l, float v) => BuildStats.Helps(v, l.IsMul, l.Words) ? Ui_CardEffects.GoodPrefix : Ui_CardEffects.BadPrefix;

        // ------------------------------------------------------------------ right: combined effects and auras
        private static string StatsText()
        {
            var sb = new StringBuilder();
            AppendHeader(sb, Ui_BuildScreen.StatsHeader);
            Effects.FoldAlways(_always);
            var lines = BuildStats.Lines(Effects.Local, _always);
            if (lines.Count == 0) sb.Append(UiKit.ColorTag(Ui_BuildScreen.InactiveColor)).Append(Ui_BuildScreen.NothingNow).Append("</color>\n");
            Color grey = Ui_BuildScreen.InactiveColor;
            foreach (var l in lines)
            {
                string now = BuildStats.Say(l, l.Now), always = BuildStats.Say(l, l.Always);
                if (!l.Conditional)
                    sb.Append(UiKit.ColorTag(GoodOrBad(l, l.Now))).Append(Mark(l, l.Now)).Append(now).Append(".</color>\n");
                else if (now == null)
                    sb.Append(UiKit.ColorTag(grey)).Append(Mark(l, l.Always)).Append(always).Append(". ").Append(Meta).Append(Ui_BuildScreen.OffNow).Append("</size></color>\n");
                else
                {
                    sb.Append(UiKit.ColorTag(GoodOrBad(l, l.Now))).Append(Mark(l, l.Now)).Append(now).Append(".</color> ").Append(Meta).Append(UiKit.ColorTag(grey)).Append(Ui_BuildScreen.OnNow).Append("</color></size>\n");
                    sb.Append(Meta).Append(UiKit.ColorTag(grey)).Append(Indent).Append(Ui_BuildScreen.Otherwise).Append(always != null ? LowerFirst(always) : Ui_BuildScreen.NoChange).Append('.').Append(EndIndent).Append("</color></size>\n");
                }
            }

            var auras = Auras();
            if (auras.Count > 0)
            {
                sb.Append('\n');
                AppendHeader(sb, Ui_BuildScreen.AurasHeader);
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
                list.Add(UiKit.ColorTag(Ui_BuildScreen.ActiveColor) + Deck.NameOf(m.CardId) + "</color> (" + string.Join(", ", owners) + ")");
            }
            if (Effects.SharedBurdenOwner != int.MinValue && Effects.SharedBurdenOwner != me)
                list.Add(UiKit.ColorTag(Ui_BuildScreen.ActiveColor) + Deck.NameOfAbility("SharedBurden") + "</color> (" + NetSend.NameOf(Effects.SharedBurdenOwner) + "): "
                         + LowerFirst(BuildStats.Say(Effects.SharedBurdenFactor, true, null, StatWords.Of(Channel.allInjury)) ?? Ui_BuildScreen.NoChange));
            return list;
        }
    }
}
