using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using CampfireCards.Cards;
using CampfireCards.Core;
using CampfireCards.Generated;
using CampfireCards.Net;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace CampfireCards.Ui
{
    /// <summary>
    /// ui.json BugReport: the in-game bug report window (Keys.BugReport). Category chips, a comment box, an optional
    /// scrubbed log, and a Send button that posts to the report server (config Reporting.Endpoint) or saves to a local
    /// file when no server is reachable. A MenuWindow, so the game blocks player input while the tester types.
    /// </summary>
    public class BugReportScreen : MenuWindow
    {
        public static BugReportScreen Instance { get; private set; }
        private static readonly MethodInfo OpenM = AccessTools.Method(typeof(MenuWindow), "Open");
        private static readonly MethodInfo CloseM = AccessTools.Method(typeof(MenuWindow), "Close");

        public override bool openOnStart => false;
        public override bool selectOnOpen => false;
        public override Selectable objectToSelectOnOpen => null;
        public override bool closeOnPause => false;
        public override bool closeOnUICancel => false;
        public override bool blocksPlayerInput => true;
        public override bool showCursorWhileOpen => true;
        public override bool autoHideOnClose => true;
        public override GameObject panel => gameObject;

        /// <summary>Wire keys for the four chips, in ui.json order (CatEffect, CatBroke, CatLag, CatSurprise).</summary>
        private static readonly string[] CategoryKeys = { "effect", "broke", "lag", "expectation" };

        /// <summary>The draft survives closing the window: an Esc while typing must never eat the comment.</summary>
        private static string _draft = "";
        private static readonly bool[] _draftCats = new bool[4];
        private static bool _draftLog = true;
        private static float _nextSendAllowed;
        private const float SendCooldownSeconds = 30f;

        private RectTransform _panelRoot;
        private TMP_InputField _input;
        private readonly Image[] _chipImages = new Image[4];
        private readonly TextMeshProUGUI[] _chipTexts = new TextMeshProUGUI[4];
        private TextMeshProUGUI _check;
        /// <summary>Feedback that must be readable while the window is open (toasts render behind the backdrop).</summary>
        private TextMeshProUGUI _status;
        private int _backFrame = -1;
        private int _openedFrame;

        // ------------------------------------------------------------------ entry points
        /// <summary>The BugReport key: open the window, or close it again (the draft is kept).</summary>
        public static void Toggle()
        {
            if (Instance != null && Instance.isOpen) { Instance.CloseFromInput(); return; }
            if (!RunState.OnIsland) return;
            if (CardPicker.Instance != null && CardPicker.Instance.isOpen) return;   // never under a real choice
            // windowBlockingInput is recomputed in LateUpdate, so a deck screen opened THIS frame (a shared or
            // same-frame key) is invisible to it - check the mod's own windows directly
            if (DeckScreen.Instance != null && DeckScreen.Instance.isOpen) return;
            bool blocked = false;
            try { blocked = GUIManager.instance != null && (GUIManager.instance.windowBlockingInput || GUIManager.instance.wheelActive); } catch { }
            if (blocked) return;
            Ensure().OpenWindow();
        }

        public static void HideIfOpen()
        {
            if (Instance != null && Instance.isOpen) Instance.HideNow();
        }

        /// <summary>Per frame from Hooks.Tick: closes when a pick fan opens or the run ends under it.</summary>
        public static void Tick()
        {
            if (Instance == null || !Instance.isOpen) return;
            if (!RunState.OnIsland || (CardPicker.Instance != null && CardPicker.Instance.isOpen)) Instance.HideNow();
        }

        private static BugReportScreen Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("BugReportScreen", typeof(RectTransform));
            go.transform.SetParent(UiKit.Canvas.transform, false);
            UiKit.Stretch(UiKit.Rect(go));
            go.SetActive(false);
            Instance = go.AddComponent<BugReportScreen>();
            Instance.Build();
            return Instance;
        }

        // ------------------------------------------------------------------ building the window
        private void Build()
        {
            var backdrop = UiKit.Panel(transform, "Backdrop", Vector2.zero, Ui_BugReport.Backdrop);
            UiKit.Stretch(backdrop.rectTransform);
            backdrop.raycastTarget = true;
            backdrop.gameObject.AddComponent<BackdropClick>().OnClick = () => { if (isOpen) CloseFromInput(); };

            var size = Ui_BugReport.Size;
            _panelRoot = UiKit.Rect(UiKit.Node(transform, "Panel", size));
            var back = UiKit.Panel(_panelRoot, "Back", size, Ui_BugReport.Panel, Art.Shapes.Panel);
            back.type = Image.Type.Sliced;
            back.raycastTarget = true;   // clicks inside the panel must not reach the backdrop

            float pad = Ui_BugReport.Pad, gap = Ui_BugReport.RowGap;
            float inner = size.x - 2f * pad;
            float y = -pad;

            var title = UiKit.Text(_panelRoot, "Title", new Vector2(inner, Ui_BugReport.TitleFontSize * 1.3f), Ui_BugReport.TitleFontSize, Ui_BugReport.TitleColor, TextAlignmentOptions.Top, FontStyles.Bold);
            UiKit.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y));
            title.text = Ui_BugReport.Title;
            y -= Ui_BugReport.TitleFontSize * 1.3f + 4f;

            var privacy = UiKit.Text(_panelRoot, "Privacy", new Vector2(inner, Ui_BugReport.PrivacyFontSize * 1.5f), Ui_BugReport.PrivacyFontSize, Ui_BugReport.PrivacyColor, TextAlignmentOptions.Top);
            UiKit.Place(privacy.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y));
            privacy.text = Ui_BugReport.PrivacyLine;
            y -= Ui_BugReport.PrivacyFontSize * 1.5f + gap;

            string[] cats = { Ui_BugReport.CatEffect, Ui_BugReport.CatBroke, Ui_BugReport.CatLag, Ui_BugReport.CatSurprise };
            float chipW = (inner - Ui_BugReport.ChipGap) * 0.5f;
            for (int i = 0; i < 4; i++)
            {
                int row = i / 2, col = i % 2;
                float x = (col == 0 ? -1f : 1f) * (chipW + Ui_BugReport.ChipGap) * 0.5f;
                float cy = y - row * (Ui_BugReport.ChipHeight + Ui_BugReport.ChipGap);
                var chip = UiKit.Panel(_panelRoot, "Chip" + i, new Vector2(chipW, Ui_BugReport.ChipHeight), Ui_BugReport.ChipColor, Art.Shapes.Panel);
                chip.type = Image.Type.Sliced;
                UiKit.Place(chip.rectTransform, new Vector2(0.5f, 1f), new Vector2(x, cy));
                chip.raycastTarget = true;
                var label = UiKit.Text(chip.transform, "Label", new Vector2(chipW - 2f * Ui_BugReport.ChipPadding, Ui_BugReport.ChipHeight), Ui_BugReport.ChipFontSize, Ui_BugReport.ChipTextColor, TextAlignmentOptions.Center);
                UiKit.Place(label.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero);
                UiKit.AutoSize(label, 12f);
                label.text = cats[i];
                var b = chip.gameObject.AddComponent<Button>();
                b.targetGraphic = chip;
                int index = i;
                b.onClick.AddListener(() => { _draftCats[index] = !_draftCats[index]; StyleChip(index); });
                _chipImages[i] = chip;
                _chipTexts[i] = label;
            }
            y -= 2f * Ui_BugReport.ChipHeight + Ui_BugReport.ChipGap + gap;

            var commentLabel = UiKit.Text(_panelRoot, "CommentLabel", new Vector2(inner, Ui_BugReport.ChipFontSize * 1.4f), Ui_BugReport.ChipFontSize, Ui_BugReport.TitleColor, TextAlignmentOptions.TopLeft);
            UiKit.Place(commentLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y));
            commentLabel.text = Ui_BugReport.CommentLabel;
            y -= Ui_BugReport.ChipFontSize * 1.4f + 4f;

            y -= BuildInput(inner, y);
            y -= gap;
            y -= BuildLogToggle(inner, y);
            y -= gap;

            float bx = (Ui_BugReport.ButtonWidth + Ui_BugReport.ButtonGap) * 0.5f;
            BuildButton("Send", Ui_BugReport.SendLabel, Ui_BugReport.SendColor, new Vector2(-bx, y), Submit, bold: true);
            BuildButton("Cancel", Ui_BugReport.CancelLabel, Ui_BugReport.CancelColor, new Vector2(bx, y), CloseFromInput, bold: false);
            y -= Ui_BugReport.ButtonHeight + 8f;

            _status = UiKit.Text(_panelRoot, "Status", new Vector2(inner, Ui_BugReport.PrivacyFontSize * 1.6f), Ui_BugReport.PrivacyFontSize, Ui_CardEffects.BadColor, TextAlignmentOptions.Top);
            UiKit.Place(_status.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y));
            _status.text = "";

            gameObject.SetActive(false);
        }

        /// <summary>The multi-line comment box: a TMP input field with a masked viewport, built by hand like the rest.</summary>
        private float BuildInput(float width, float top)
        {
            float h = Ui_BugReport.CommentHeight, pad = 10f;
            var box = UiKit.Panel(_panelRoot, "CommentBox", new Vector2(width, h), Ui_BugReport.FieldColor, Art.Shapes.Panel);
            box.type = Image.Type.Sliced;
            UiKit.Place(box.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, top));
            box.raycastTarget = true;

            var viewport = UiKit.Node(box.transform, "Viewport", new Vector2(width - 2f * pad, h - 2f * pad));
            var vpRect = UiKit.Rect(viewport);
            viewport.AddComponent<RectMask2D>();

            var text = UiKit.Text(viewport.transform, "Text", new Vector2(width - 2f * pad, h - 2f * pad), Ui_BugReport.CommentFontSize, Ui_BugReport.TextColor, TextAlignmentOptions.TopLeft);
            text.richText = false;
            text.overflowMode = TextOverflowModes.Overflow;
            var placeholder = UiKit.Text(viewport.transform, "Placeholder", new Vector2(width - 2f * pad, h - 2f * pad), Ui_BugReport.CommentFontSize, Ui_BugReport.PrivacyColor, TextAlignmentOptions.TopLeft, FontStyles.Italic);
            placeholder.richText = false;
            placeholder.text = Ui_BugReport.Placeholder;

            _input = box.gameObject.AddComponent<TMP_InputField>();
            _input.targetGraphic = box;
            _input.textViewport = vpRect;
            _input.textComponent = text;
            _input.placeholder = placeholder;
            _input.lineType = TMP_InputField.LineType.MultiLineNewline;
            _input.characterLimit = Mathf.RoundToInt(Ui_BugReport.CommentLimit);
            _input.onFocusSelectAll = false;
            // TMP's default reverts the field to its text-at-focus on Esc (and the revert fires onValueChanged, which
            // would wipe the draft). Esc must close the window and KEEP the comment.
            _input.restoreOriginalTextOnEscape = false;
            _input.customCaretColor = true;
            _input.caretColor = Ui_BugReport.TextColor;
            _input.selectionColor = new Color(1f, 1f, 1f, 0.25f);
            if (text.font != null) _input.fontAsset = text.font;
            _input.onValueChanged.AddListener(v => _draft = v);
            return h;
        }

        private float BuildLogToggle(float width, float top)
        {
            float h = Ui_BugReport.CheckSize + 6f;
            var row = UiKit.Panel(_panelRoot, "LogToggle", new Vector2(width, h), new Color(1f, 1f, 1f, 0f));
            UiKit.Place(row.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, top));
            row.raycastTarget = true;
            var boxImg = UiKit.Panel(row.transform, "Box", Vector2.one * Ui_BugReport.CheckSize, Ui_BugReport.FieldColor, Art.Shapes.Panel);
            boxImg.type = Image.Type.Sliced;
            UiKit.Place(boxImg.rectTransform, new Vector2(0f, 0.5f), new Vector2(Ui_BugReport.CheckSize * 0.5f, 0f));
            _check = UiKit.Text(boxImg.transform, "Mark", Vector2.one * Ui_BugReport.CheckSize, Ui_BugReport.CheckSize * 0.8f, Ui_BugReport.ChipOnColor, TextAlignmentOptions.Center, FontStyles.Bold);
            UiKit.Place(_check.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 1f));
            _check.text = "x";
            var label = UiKit.Text(row.transform, "Label", new Vector2(width - Ui_BugReport.CheckSize - 12f, h), Ui_BugReport.ToggleFontSize, Ui_BugReport.ChipTextColor, TextAlignmentOptions.Left);
            UiKit.Place(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(Ui_BugReport.CheckSize + 12f, 0f));
            label.text = Ui_BugReport.LogToggle;
            var b = row.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => { _draftLog = !_draftLog; _check.gameObject.SetActive(_draftLog); });
            return h;
        }

        private void BuildButton(string name, string label, Color color, Vector2 pos, Action onClick, bool bold)
        {
            var img = UiKit.Panel(_panelRoot, name, new Vector2(Ui_BugReport.ButtonWidth, Ui_BugReport.ButtonHeight), color, Art.Shapes.Panel);
            img.type = Image.Type.Sliced;
            UiKit.Place(img.rectTransform, new Vector2(0.5f, 1f), pos);
            img.raycastTarget = true;
            var t = UiKit.Text(img.transform, "Label", new Vector2(Ui_BugReport.ButtonWidth, Ui_BugReport.ButtonHeight), Ui_BugReport.ButtonFontSize,
                bold ? Ui_BugReport.ChipOnTextColor : Ui_BugReport.ChipTextColor, TextAlignmentOptions.Center, bold ? FontStyles.Bold : FontStyles.Normal);
            UiKit.Place(t.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero);
            t.text = label;
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(() => onClick());
        }

        // ------------------------------------------------------------------ open, close, input
        private void OpenWindow()
        {
            if (!isOpen) OpenM.Invoke(this, null);
            _openedFrame = Time.frameCount;
            var canvas = UiKit.CanvasSize;
            var size = Ui_BugReport.Size;
            float s = Mathf.Min(1f, (canvas.x - 2f * UiTiming.SafeMargin) / size.x, (canvas.y - 2f * UiTiming.SafeMargin) / size.y);
            _panelRoot.localScale = Vector3.one * s;
            for (int i = 0; i < 4; i++) StyleChip(i);
            _check.gameObject.SetActive(_draftLog);
            _status.text = "";
            _input.SetTextWithoutNotify(_draft);
            _input.Select();
            _input.ActivateInputField();
        }

        private void StyleChip(int i)
        {
            bool on = _draftCats[i];
            _chipImages[i].color = on ? Ui_BugReport.ChipOnColor : Ui_BugReport.ChipColor;
            _chipTexts[i].color = on ? Ui_BugReport.ChipOnTextColor : Ui_BugReport.ChipTextColor;
        }

        protected override void Update()
        {
            base.Update();
            if (!isOpen || Time.frameCount == _openedFrame) return;   // a cancel-button binding must not close on the opening press
            // while the comment box has focus, keys that type belong to it: a report key rebound to a letter must not
            // close the window mid-sentence. The default F9 (and any non-typing key) still toggles the window closed.
            bool typing = _input != null && _input.isFocused && Controls.ProducesText(Controls.Act.BugReport);
            if (!typing && Controls.WasPressed(Controls.Act.BugReport)) { CloseFromInput(); return; }
            if (CancelPressed()) CloseFromInput();
        }

        private static bool CancelPressed()
        {
            try { return UIInputHandler.action_cancel != null && UIInputHandler.action_cancel.WasPressedThisFrame(); }
            catch { return false; }
        }

        /// <summary>Esc, cancel, the report key, a backdrop click or a pause press: close, keep the draft, eat the pause press.</summary>
        internal void CloseFromInput()
        {
            if (Time.frameCount == _backFrame) return;
            _backFrame = Time.frameCount;
            try
            {
                var local = Character.localCharacter;
                if (local != null && local.input != null) local.input.pauseWasPressed = false;
            }
            catch { }
            HideNow();
        }

        public void HideNow()
        {
            if (!isOpen) return;
            CloseM.Invoke(this, null);
        }

        // ------------------------------------------------------------------ sending
        private void Submit()
        {
            string comment = _draft.Trim();
            bool any = comment.Length > 0;
            foreach (var c in _draftCats) any |= c;
            if (!any) { Status(Ui_BugReport.EmptyToast); return; }
            float wait = _nextSendAllowed - Time.unscaledTime;
            if (wait > 0f) { Status(string.Format(Ui_BugReport.WaitToast, Mathf.CeilToInt(wait))); return; }

            string json = BuildJsonWithinBudget(comment);
            var cats = (bool[])_draftCats.Clone();   // kept so a total failure can hand the draft back

            string endpoint = Plugin.ReportEndpoint != null ? (Plugin.ReportEndpoint.Value ?? "").Trim() : "";
            bool saveOnly = endpoint.Length == 0;
            // the report can carry a log: it never travels unencrypted (localhost excepted, for testing the server)
            if (!saveOnly && !endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                          && !endpoint.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase)
                          && !endpoint.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase))
            {
                Plugin.Log.LogWarning("Reporting.Endpoint must be https:// (http:// only for localhost tests); saving the report locally instead.");
                saveOnly = true;
            }

            if (saveOnly)
            {
                string file;
                if (!TrySaveLocal(json, out file)) { Status(Ui_BugReport.KeptText); return; }   // draft untouched, no cooldown
                ClearDraft();
                _nextSendAllowed = Time.unscaledTime + SendCooldownSeconds;
                CloseFromInput();
                Hud.Toast(string.Format(Ui_BugReport.SavedToast, file), warning: true);
                return;
            }

            // the draft is cleared only once the report is on a path that cannot silently lose it: a failed post
            // falls back to the local file, and a failed save hands the words back (SendRoutine)
            ClearDraft();
            _nextSendAllowed = Time.unscaledTime + SendCooldownSeconds;
            CloseFromInput();
            Plugin.Instance.StartCoroutine(SendRoutine(endpoint, json, comment, cats));
        }

        private void Status(string message)
        {
            if (_status != null) _status.text = message;
        }

        private static void ClearDraft()
        {
            _draft = "";
            for (int i = 0; i < _draftCats.Length; i++) _draftCats[i] = false;
        }

        /// <summary>The payload, shrunk to fit the server's 1 MB body cap: the log tail gives way, the words never do.</summary>
        private string BuildJsonWithinBudget(string comment)
        {
            int[] tails = { 200_000, 100_000, 50_000, 0 };
            string json = null;
            foreach (var t in tails)
            {
                json = ReportJson.Build(BuildReport(comment, t));
                if (Encoding.UTF8.GetByteCount(json) <= 900_000) return json;
            }
            return json;
        }

        private Report BuildReport(string comment, int logTailChars)
        {
            var r = new Report
            {
                ModVersion = Plugin.Version,
                Balance = Plugin.CardBalanceVersion,
                Comment = comment,
                UtcTime = DateTime.UtcNow.ToString("o"),
                InstallId = Plugin.InstallId != null ? Plugin.InstallId.Value : "",
                IsHost = NetSend.IsHost,
                Players = 1,
            };
            try { r.GameVersion = Application.version; } catch { }
            try { r.Scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; } catch { }
            try { if (Photon.Pun.PhotonNetwork.CurrentRoom != null) r.Players = Photon.Pun.PhotonNetwork.CurrentRoom.PlayerCount; } catch { }
            var s = CardSettings.Current;
            r.DrawbacksEnabled = s.DrawbacksEnabled;
            r.BenefitStrength = s.BenefitStrength;
            for (int i = 0; i < 4; i++) if (_draftCats[i]) r.Categories.Add(CategoryKeys[i]);
            int me = NetSend.LocalActor;
            foreach (var id in Effects.CardsOf(me)) { if (r.Deck.Count >= 128) break; r.Deck.Add(id); }
            foreach (var kv in Effects.CardsByActor)
            {
                if (kv.Key == me || kv.Value.Count == 0 || r.TeamDecks.Count >= 16) continue;
                var deck = new List<int>();
                foreach (var id in kv.Value) { if (deck.Count >= 128) break; deck.Add(id); }
                r.TeamDecks.Add(deck);
            }
            if (_draftLog && logTailChars > 0) r.Log = CollectLog(logTailChars);
            return r;
        }

        /// <summary>
        /// The tail of BepInEx's live log, scrubbed of the OS user and every player name this SESSION has seen
        /// (NetSend.SeenNames) - the log reaches back through earlier lobbies, so the current room's list is not enough.
        /// </summary>
        private static string CollectLog(int maxChars)
        {
            try
            {
                string path = Path.Combine(BepInEx.Paths.BepInExRootPath, "LogOutput.log");
                if (!File.Exists(path)) return null;
                string text;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    fs.Seek(Math.Max(0, fs.Length - 400_000), SeekOrigin.Begin);
                    using (var sr = new StreamReader(fs)) text = sr.ReadToEnd();
                }
                var names = NetSend.SeenNames();
                try
                {
                    foreach (var p in Photon.Pun.PhotonNetwork.PlayerList)
                        if (p != null && !string.IsNullOrEmpty(p.NickName)) names.Add(p.NickName);
                    if (!string.IsNullOrEmpty(Photon.Pun.PhotonNetwork.NickName)) names.Add(Photon.Pun.PhotonNetwork.NickName);
                    var me = Character.localCharacter;
                    if (me != null && !string.IsNullOrEmpty(me.characterName)) names.Add(me.characterName);
                }
                catch { }
                return LogScrub.Scrub(LogScrub.Tail(text, maxChars), Environment.UserName, names);
            }
            catch (Exception e)
            {
                Plugin.Verbose("log attach failed: " + e.Message);
                return null;
            }
        }

        private static IEnumerator SendRoutine(string endpoint, string json, string comment, bool[] cats)
        {
            using (var req = new UnityWebRequest(endpoint, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("X-CC-Mod", "CampfireCards/" + Plugin.Version);
                req.timeout = 15;
                yield return req.SendWebRequest();
                bool ok = req.result == UnityWebRequest.Result.Success && req.responseCode >= 200 && req.responseCode < 300;
                if (ok)
                {
                    Hud.Toast(Ui_BugReport.SentToast);
                    Plugin.Verbose("bug report sent (" + json.Length + " chars)");
                    yield break;
                }
                Plugin.Log.LogWarning("Bug report post failed (" + req.responseCode + " " + req.error + "); saving locally.");
                string file;
                if (TrySaveLocal(json, out file))
                {
                    Hud.Toast(string.Format(Ui_BugReport.FailedToast, file), warning: true);
                }
                else
                {
                    // nothing left the machine and nothing was stored: hand the words back and lift the cooldown
                    if (_draft.Length == 0) { _draft = comment; for (int i = 0; i < cats.Length && i < _draftCats.Length; i++) _draftCats[i] = cats[i]; }
                    _nextSendAllowed = 0f;
                    Hud.Toast(Ui_BugReport.KeptText, warning: true);
                }
            }
        }

        /// <summary>The report as a file beside the mod (reports/), so a failed post never loses a tester's write-up.</summary>
        private static bool TrySaveLocal(string json, out string name)
        {
            try
            {
                string dir = Path.Combine(Plugin.PluginDir ?? ".", "reports");
                Directory.CreateDirectory(dir);
                name = "report-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json";
                File.WriteAllText(Path.Combine(dir, name), json);
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Bug report could not be saved: " + e.Message);
                name = null;
                return false;
            }
        }
    }
}
