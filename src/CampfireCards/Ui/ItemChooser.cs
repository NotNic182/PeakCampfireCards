using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using CampfireCards.Generated;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CampfireCards.Ui
{
    /// <summary>
    /// ui.json ItemChooser: a small MenuWindow listing item names as buttons (Balright's mythic pick). Only items the
    /// game's ItemDatabase can actually spawn are listed; Esc postpones the choice to the next campfire.
    /// </summary>
    public class ItemChooser : MenuWindow
    {
        public static ItemChooser Instance { get; private set; }
        private static readonly MethodInfo OpenM = AccessTools.Method(typeof(MenuWindow), "Open");
        private static readonly MethodInfo CloseM = AccessTools.Method(typeof(MenuWindow), "Close");

        public override bool openOnStart => false;
        public override bool selectOnOpen => true;
        public override Selectable objectToSelectOnOpen => _buttons.Count > 0 ? _buttons[0] : null;
        public override bool closeOnPause => false;
        public override bool closeOnUICancel => false;
        public override bool blocksPlayerInput => true;
        public override bool showCursorWhileOpen => true;
        public override bool autoHideOnClose => true;
        public override GameObject panel => gameObject;

        public static bool IsOpen => Instance != null && Instance.isOpen;

        private Image _panel;
        private TextMeshProUGUI _title, _hint;
        private readonly List<Button> _buttons = new List<Button>();
        private System.Action<byte> _onChosen;
        private System.Action<bool> _onClosed;
        private bool _chosen;

        /// <summary>Open the chooser for the pool names that resolve to spawnable items. done(true) = postponed (Esc).</summary>
        public static void Show(string title, string[] pool, System.Action<byte> onChosen, System.Action<bool> done)
        {
            var choices = new List<KeyValuePair<string, byte>>();
            foreach (var name in pool)
            {
                int index = System.Array.IndexOf(CardTable.SpawnItems, name);
                Item prefab;
                if (index < 0 || index > byte.MaxValue || !ItemDatabase.TryGetItem(name, out prefab) || prefab == null)
                {
                    Plugin.Log.LogWarning("Mythic pool item not spawnable, skipped: " + name);
                    continue;
                }
                choices.Add(new KeyValuePair<string, byte>(Pretty(prefab, name), (byte)index));
            }
            if (choices.Count == 0)
            {
                // nothing in the pool resolves on this game version: this fire is skipped, the card re-arms at the next one
                Plugin.Log.LogWarning("Mythic pool empty after filtering; nothing to choose this fire.");
                done(false);
                return;
            }
            Ensure().Open(title, choices, onChosen, done);
        }

        /// <summary>The item's display name when the prefab carries UI data, else the prefab name.</summary>
        private static string Pretty(Item prefab, string fallback)
        {
            try { var n = prefab.GetItemName(); if (!string.IsNullOrEmpty(n)) return n; } catch { }
            return fallback;
        }

        private static ItemChooser Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("ItemChooser", typeof(RectTransform));
            go.transform.SetParent(UiKit.Canvas.transform, false);
            UiKit.Stretch(UiKit.Rect(go));
            go.SetActive(false);
            Instance = go.AddComponent<ItemChooser>();
            Instance.Build();
            return Instance;
        }

        private void Build()
        {
            var backdrop = UiKit.Panel(transform, "Backdrop", Vector2.zero, Ui_ItemChooser.Backdrop);
            UiKit.Stretch(backdrop.rectTransform);
            backdrop.raycastTarget = true;

            _panel = UiKit.Panel(transform, "Panel", Ui_ItemChooser.Size, Ui_ItemChooser.Panel, Art.Shapes.Panel);
            _panel.type = Image.Type.Sliced;
            UiKit.Place(_panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero);

            _title = UiKit.Text(_panel.transform, "Title", new Vector2(Ui_ItemChooser.Size.x - 40f, 48f), Ui_ItemChooser.TitleFontSize,
                Ui_ItemChooser.TitleColor, TextAlignmentOptions.Center, FontStyles.Bold | FontStyles.UpperCase);
            UiKit.Place(_title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -18f));
            UiKit.AutoSize(_title, 20f);

            _hint = UiKit.Text(_panel.transform, "Hint", new Vector2(Ui_ItemChooser.Size.x - 40f, 30f), Ui_ItemChooser.HintFontSize,
                Ui_ItemChooser.HintColor, TextAlignmentOptions.Center, FontStyles.Italic);
            UiKit.Place(_hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 14f));
            _hint.text = Ui_ItemChooser.Hint;
            gameObject.SetActive(false);
        }

        private void Open(string title, List<KeyValuePair<string, byte>> choices, System.Action<byte> onChosen, System.Action<bool> done)
        {
            _onChosen = onChosen;
            _onClosed = done;
            _chosen = false;
            _title.text = title;
            // the Button sits on the slot itself; destroying its parent would take the shared panel with it
            foreach (var b in _buttons) if (b != null) Destroy(b.gameObject);
            _buttons.Clear();

            float h = choices.Count * (Ui_ItemChooser.ButtonHeight + Ui_ItemChooser.ButtonGap) - Ui_ItemChooser.ButtonGap;
            _panel.rectTransform.sizeDelta = new Vector2(Ui_ItemChooser.Size.x, Mathf.Max(Ui_ItemChooser.Size.y, h + 130f));
            float y = -78f;
            for (int i = 0; i < choices.Count; i++)
            {
                var choice = choices[i];
                var slot = UiKit.Node(_panel.transform, "Choice", new Vector2(Ui_ItemChooser.ButtonWidth, Ui_ItemChooser.ButtonHeight));
                UiKit.Place(UiKit.Rect(slot), new Vector2(0.5f, 1f), new Vector2(0f, y));
                var bg = slot.AddComponent<Image>();
                bg.sprite = Art.Shapes.Panel; bg.type = Image.Type.Sliced;
                bg.color = Ui_ItemChooser.ButtonColor;
                bg.raycastTarget = true;
                var label = UiKit.Text(slot.transform, "Label", new Vector2(Ui_ItemChooser.ButtonWidth - 24f, Ui_ItemChooser.ButtonHeight), Ui_ItemChooser.FontSize,
                    Ui_ItemChooser.TextColor, TextAlignmentOptions.Center, FontStyles.Bold);
                UiKit.Place(label.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero);
                UiKit.AutoSize(label, 16f);
                label.text = choice.Key;
                var inner = slot.AddComponent<Button>();
                inner.targetGraphic = bg;
                byte captured = choice.Value;
                inner.onClick.AddListener(() => Choose(captured));
                _buttons.Add(inner);
                y -= Ui_ItemChooser.ButtonHeight + Ui_ItemChooser.ButtonGap;
            }
            for (int i = 0; i < _buttons.Count; i++)
            {
                var nav = new Navigation { mode = Navigation.Mode.Explicit };
                nav.selectOnUp = i > 0 ? _buttons[i - 1] : null;
                nav.selectOnDown = i + 1 < _buttons.Count ? _buttons[i + 1] : null;
                _buttons[i].navigation = nav;
            }
            if (!isOpen) OpenM.Invoke(this, null);
            Art.Sfx.Play(Ui_ItemChooser.SoundOn);
            try { UIInputHandler.SetSelectedObject(_buttons[0].gameObject); }
            catch (System.Exception e) { Plugin.Verbose("controller selection skipped: " + e.Message); }
        }

        private void Choose(byte index)
        {
            if (_chosen) return;
            _chosen = true;
            Art.Sfx.Play("cardPick");
            var chosen = _onChosen;
            HideNow();
            if (chosen != null) chosen(index);
        }

        public void HideNow()
        {
            if (!isOpen) return;
            CloseM.Invoke(this, null);
            var closed = _onClosed;
            _onClosed = null;
            _onChosen = null;
            if (closed != null) closed(!_chosen);
        }

        protected override void Update()
        {
            base.Update();
            if (!isOpen) return;
            bool cancel = false;
            try { cancel = UIInputHandler.action_cancel != null && UIInputHandler.action_cancel.WasPressedThisFrame(); } catch { }
            if (cancel) HideNow();
        }
    }
}
