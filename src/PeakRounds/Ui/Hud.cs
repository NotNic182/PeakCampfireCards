using System.Text;
using PeakRounds.Cards;
using PeakRounds.Generated;
using PeakRounds.Net;
using PeakRounds.Rounds;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PeakRounds.Ui
{
    /// <summary>ui.json WaitingBanner, MyCardsHud, Toast, RoundsMissingNotice: plain overlays that never block input.</summary>
    public static class Hud
    {
        private static GameObject _root;
        private static Image _waitingPanel; private static TextMeshProUGUI _waitingText;
        private static TextMeshProUGUI _myCards;
        private static Image _toastPanel; private static TextMeshProUGUI _toastText; private static float _toastUntil;
        private static Image _missingPanel; private static TextMeshProUGUI _missingText; private static float _missingUntil;

        public static void Ensure()
        {
            if (_root != null) return;
            _root = new GameObject("PeakRoundsHud", typeof(RectTransform));
            _root.transform.SetParent(UiKit.Canvas.transform, false);
            UiKit.Stretch(UiKit.Rect(_root));

            _waitingPanel = UiKit.Panel(_root.transform, "WaitingBanner", Ui_WaitingBanner.Size, Ui_WaitingBanner.Panel);
            UiKit.Place(_waitingPanel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -120f));
            _waitingText = UiKit.Text(_waitingPanel.transform, "Text", Ui_WaitingBanner.Size, Ui_WaitingBanner.FontSize, Ui_WaitingBanner.Color, TextAlignmentOptions.Center, FontStyles.Bold);
            _waitingPanel.gameObject.SetActive(false);

            _myCards = UiKit.Text(_root.transform, "MyCardsHud", Ui_MyCardsHud.Size, Ui_MyCardsHud.FontSize, Ui_MyCardsHud.Color, TextAlignmentOptions.BottomLeft);
            UiKit.Place(_myCards.rectTransform, new Vector2(0f, 0f), new Vector2(20f, 160f));
            _myCards.gameObject.SetActive(false);

            _toastPanel = UiKit.Panel(_root.transform, "Toast", Ui_Toast.Size, Ui_Toast.Panel);
            UiKit.Place(_toastPanel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -200f));
            _toastText = UiKit.Text(_toastPanel.transform, "Text", Ui_Toast.Size, Ui_Toast.FontSize, Ui_Toast.Color, TextAlignmentOptions.Center);
            _toastPanel.gameObject.SetActive(false);

            _missingPanel = UiKit.Panel(_root.transform, "RoundsMissingNotice", Ui_RoundsMissingNotice.Size, Ui_RoundsMissingNotice.Panel);
            UiKit.Place(_missingPanel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -290f));
            _missingText = UiKit.Text(_missingPanel.transform, "Text", Ui_RoundsMissingNotice.Size - new Vector2(20f, 0f), Ui_RoundsMissingNotice.FontSize, Ui_RoundsMissingNotice.Color, TextAlignmentOptions.Center);
            _missingPanel.gameObject.SetActive(false);

            var ticker = _root.AddComponent<HudTicker>();
        }

        public static void ShowWaiting(string playerName)
        {
            Ensure();
            _waitingText.text = string.Format(Ui_WaitingBanner.Template, playerName);
            _waitingPanel.gameObject.SetActive(true);
        }

        public static void HideWaiting()
        {
            if (_waitingPanel != null) _waitingPanel.gameObject.SetActive(false);
        }

        public static void Toast(string message)
        {
            Ensure();
            _toastText.text = message;
            _toastPanel.gameObject.SetActive(true);
            _toastUntil = Time.unscaledTime + UiTiming.ToastSeconds;
            Plugin.Verbose("Toast: " + message);
        }

        public static void ShowMissing(string message)
        {
            Ensure();
            _missingText.text = message;
            _missingPanel.gameObject.SetActive(true);
            _missingUntil = Time.unscaledTime + UiTiming.MissingNoticeSeconds;
            Plugin.Log.LogWarning(message);
        }

        public static void RefreshMyCards()
        {
            if (_myCards == null) return;
            var cards = Effects.CardsOf(NetSend.LocalActor);
            if (cards.Count == 0) { _myCards.gameObject.SetActive(false); return; }
            var data = RoundsReader.Data;
            var sb = new StringBuilder();
            sb.Append("<b>").Append(Ui_MyCardsHud.Header).Append("</b>\n");
            foreach (var id in cards)
            {
                var rc = Deck.RoundsFor(id);
                var theme = data != null && rc != null ? data.Theme(rc.ColorTheme) : RoundsData.DefaultThemes()[3];
                sb.Append("<color=#").Append(UiKit.Hex(theme.Target)).Append(">■</color> ").Append(Deck.NameOf(id)).Append('\n');
            }
            _myCards.text = sb.ToString().TrimEnd();
            _myCards.gameObject.SetActive(true);
        }

        public static void CloseAll()
        {
            HideWaiting();
            CardPicker.HideIfOpen();
            if (_toastPanel != null) _toastPanel.gameObject.SetActive(false);
            if (_missingPanel != null) _missingPanel.gameObject.SetActive(false);
            if (_myCards != null) _myCards.gameObject.SetActive(false);
        }

        internal static void TickTimers()
        {
            if (_toastPanel != null && _toastPanel.gameObject.activeSelf && Time.unscaledTime > _toastUntil) _toastPanel.gameObject.SetActive(false);
            if (_missingPanel != null && _missingPanel.gameObject.activeSelf && Time.unscaledTime > _missingUntil) _missingPanel.gameObject.SetActive(false);
            if (_myCards != null && _myCards.gameObject.activeSelf && !RunState.OnIsland) _myCards.gameObject.SetActive(false);
        }
    }

    public class HudTicker : MonoBehaviour
    {
        private void Update() { Hud.TickTimers(); }
    }
}
