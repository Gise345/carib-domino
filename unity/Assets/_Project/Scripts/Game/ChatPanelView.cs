#nullable enable
using System;
using System.Collections.Generic;
using Pose.Core.Chat;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Pose.Game
{
    /// <summary>
    /// The in-match chat panel: a large card centred over the board behind a
    /// scrim, replacing the corner card the HUD used to draw. Chat is a
    /// conversation between four people mid-game — it needs room to read, a real
    /// input field, and a reachable report control on every message, none of
    /// which fit a 380×520 box in the corner.
    ///
    /// Presentational only. It renders what it is given and raises events; the
    /// host (see <see cref="BoardBootstrap"/>) owns <see cref="Pose.Net.ChatService"/>
    /// and does the talking. The composer lock is cosmetic — the server refuses a
    /// guest's message regardless (ADR 0023 §3).
    ///
    /// Built in code, no prefab, like the rest of the UI.
    /// </summary>
    public sealed class ChatPanelView : MonoBehaviour
    {
        /// <summary>Raised with the typed text when the player sends.</summary>
        public event Action<string>? SendRequested;

        /// <summary>Raised with (messageId, reason, note) when a report is filed.</summary>
        public event Action<string, ChatReportReason, string>? ReportRequested;

        /// <summary>Raised when a guest taps the "create an account" CTA.</summary>
        public event Action? CreateAccountRequested;

        /// <summary>Raised when the panel is dismissed.</summary>
        public event Action? Closed;

        // ---- palette (matches the board-room chrome) -----------------------
        private static readonly Color Scrim = new(0f, 0f, 0f, 0.72f);
        private static readonly Color Card = new(0.075f, 0.059f, 0.047f, 0.98f);
        private static readonly Color Gold = new(0.961f, 0.769f, 0.318f);
        private static readonly Color TextCol = new(0.957f, 0.929f, 0.882f);
        private static readonly Color Muted = new(0.702f, 0.643f, 0.533f);
        private static readonly Color Faint = new(0.490f, 0.443f, 0.361f);
        private static readonly Color Bubble = new(0.129f, 0.106f, 0.086f, 0.92f);
        private static readonly Color OwnBubble = new(0.161f, 0.180f, 0.129f, 0.92f);
        private static readonly Color SendGreen = new(0.247f, 0.647f, 0.325f);
        private static readonly Color Danger = new(0.949f, 0.439f, 0.353f);

        // ---- metrics -------------------------------------------------------
        private const float HeaderHeight = 92f;
        private const float ComposerHeight = 104f;
        private const float SendButtonSize = 76f;
        private const float RowSpacing = 14f;

        /// <summary>Below this many characters left, the counter appears.</summary>
        private const int CounterVisibleFrom = 40;

        /// <summary>The widgets of one rendered message, kept so a redaction can
        /// be applied in place instead of rebuilding the row.</summary>
        private sealed class RowWidgets
        {
            public GameObject Root = null!;
            public TextMeshProUGUI Body = null!;
            public GameObject? ReportButton;
        }

        private readonly Dictionary<string, RowWidgets> _rows = new();

        private GameObject _root = null!;
        private RectTransform _logContent = null!;
        private ScrollRect _scroll = null!;
        private TMP_InputField _input = null!;
        private Button _sendButton = null!;
        private Image _sendBg = null!;
        private GameObject _composerRow = null!;
        private GameObject _lockedRow = null!;
        private TextMeshProUGUI _lockedLabel = null!;
        private TextMeshProUGUI _counter = null!;
        private TextMeshProUGUI _status = null!;
        private TextMeshProUGUI _subtitle = null!;
        private TextMeshProUGUI _emptyLabel = null!;
        private Image _micTint = null!;
        private ChatReportSheet _reportSheet = null!;

        private string? _localUid;
        private ChatEntitlement _entitlement;

        /// <summary>Builds the panel. Call once after AddComponent; starts hidden.</summary>
        public void Init()
        {
            Stretch((RectTransform)transform);

            _root = Child(transform, "ChatModal");
            Stretch((RectTransform)_root.transform);

            BuildScrim();
            GameObject card = BuildCard();
            BuildHeader(card.transform);
            BuildLog(card.transform);
            BuildComposer(card.transform);

            _reportSheet = gameObject.AddComponent<ChatReportSheet>();
            _reportSheet.Init((RectTransform)_root.transform);
            _reportSheet.Submitted += (id, reason, note) => ReportRequested?.Invoke(id, reason, note);

            _root.SetActive(false);
        }

        // ---- public surface ------------------------------------------------

        /// <summary>Identifies the local player, so their own messages read as theirs.</summary>
        /// <param name="uid">The signed-in uid, or null when signed out.</param>
        public void SetLocalUid(string? uid) => _localUid = uid;

        /// <summary>True while the panel is showing.</summary>
        public bool IsOpen => _root.activeSelf;

        /// <summary>Shows the panel and focuses the composer when it is usable.</summary>
        public void Open()
        {
            _root.SetActive(true);
            ScrollToBottom();
            if (_entitlement.CanSend)
            {
                _input.ActivateInputField();
            }
        }

        /// <summary>Hides the panel (and any open report sheet).</summary>
        public void Close()
        {
            _reportSheet.Hide();
            _root.SetActive(false);
            Closed?.Invoke();
        }

        /// <summary>Opens or closes the panel.</summary>
        public void Toggle()
        {
            if (IsOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        /// <summary>The room label under the title (ruleset and room code).</summary>
        /// <param name="text">Already-localised subtitle text.</param>
        public void SetSubtitle(string text) => _subtitle.text = text;

        /// <summary>
        /// Applies what this session may do: an account holder types, a guest
        /// gets the sign-up CTA instead of a composer, a muted player is told so.
        /// </summary>
        /// <param name="entitlement">The evaluated entitlement.</param>
        public void SetEntitlement(ChatEntitlement entitlement)
        {
            _entitlement = entitlement;

            bool canSend = entitlement.CanSend;
            _composerRow.SetActive(canSend);
            _lockedRow.SetActive(!canSend);
            _micTint.color = entitlement.CanUseVoice ? Muted : Faint;

            if (!canSend)
            {
                _lockedLabel.text = L10n.Get(LockKey(entitlement.LockReason));
            }
            RefreshSendState();
        }

        /// <summary>
        /// Applies the room's messages, oldest first. Rows are reconciled by
        /// message id rather than rebuilt: the listener re-delivers the whole
        /// window on every change, and tearing down a hundred rows to add one
        /// line would drop the player's scroll position mid-read and churn the
        /// UI on every message the table sends.
        /// </summary>
        /// <param name="messages">The room's messages as delivered by the listener.</param>
        public void SetMessages(IReadOnlyList<ChatMessage> messages)
        {
            HashSet<string> live = new(messages.Count);
            foreach (ChatMessage message in messages)
            {
                live.Add(message.Id);
            }

            // Drop rows that aged out of the window (or were swept).
            List<string> gone = new();
            foreach (KeyValuePair<string, RowWidgets> entry in _rows)
            {
                if (!live.Contains(entry.Key))
                {
                    Destroy(entry.Value.Root);
                    gone.Add(entry.Key);
                }
            }
            foreach (string id in gone)
            {
                _rows.Remove(id);
            }

            bool grew = false;
            for (int i = 0; i < messages.Count; i++)
            {
                ChatMessage message = messages[i];
                if (!_rows.TryGetValue(message.Id, out RowWidgets row))
                {
                    row = BuildMessageRow(message);
                    _rows[message.Id] = row;
                    grew = true;
                }
                else
                {
                    // A delivered message only ever changes one way: a moderator
                    // redacts it.
                    RefreshRow(row, message);
                }
                row.Root.transform.SetSiblingIndex(i + 1); // +1: the empty label leads
            }

            _emptyLabel.gameObject.SetActive(messages.Count == 0);
            if (grew)
            {
                ScrollToBottom();
            }
        }

        /// <summary>
        /// Shows a transient line under the composer — a refusal, a mute notice,
        /// or the note that a message was filtered.
        /// </summary>
        /// <param name="text">Already-localised status text; empty clears it.</param>
        /// <param name="isError">Colours the line as a problem.</param>
        public void SetStatus(string text, bool isError = false)
        {
            _status.text = text;
            _status.color = isError ? Danger : Muted;
            _status.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }

        /// <summary>Clears the composer after a successful send.</summary>
        public void ClearDraft()
        {
            _input.SetTextWithoutNotify(string.Empty);
            OnDraftChanged(string.Empty);
            if (_entitlement.CanSend)
            {
                _input.ActivateInputField();
            }
        }

        // ---- build ---------------------------------------------------------

        private void BuildScrim()
        {
            GameObject scrim = Child(_root.transform, "Scrim");
            Stretch((RectTransform)scrim.transform);
            Image bg = scrim.AddComponent<Image>();
            bg.color = Scrim;
            Button close = scrim.AddComponent<Button>();
            close.targetGraphic = bg;
            close.transition = Selectable.Transition.None;
            close.onClick.AddListener(Close);
        }

        private GameObject BuildCard()
        {
            GameObject card = Child(_root.transform, "Card");
            RectTransform rt = (RectTransform)card.transform;
            // Proportional margins rather than a fixed size: the panel should read
            // as "over the middle of the screen" on a phone and a tablet alike.
            rt.anchorMin = new Vector2(0.09f, 0.08f);
            rt.anchorMax = new Vector2(0.91f, 0.92f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Image bg = card.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.06f, Card, Card);
            bg.color = Color.white;
            Shadow shadow = card.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            shadow.effectDistance = new Vector2(0f, -10f);

            VerticalLayoutGroup vl = card.AddComponent<VerticalLayoutGroup>();
            vl.childControlWidth = true;
            vl.childControlHeight = true;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;
            return card;
        }

        private void BuildHeader(Transform parent)
        {
            GameObject head = Child(parent, "Header");
            head.AddComponent<LayoutElement>().preferredHeight = HeaderHeight;
            HorizontalLayoutGroup hl = head.AddComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(26, 20, 0, 0);
            hl.spacing = 14f;
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = true;
            hl.childControlHeight = true;
            hl.childForceExpandWidth = false;

            AddInlineIcon(head.transform, IconFactory.Chat(), 40f, Gold);

            GameObject titles = Child(head.transform, "Titles");
            LayoutElement tle = titles.AddComponent<LayoutElement>();
            tle.flexibleWidth = 1f;
            VerticalLayoutGroup tvl = titles.AddComponent<VerticalLayoutGroup>();
            tvl.childControlWidth = true;
            tvl.childControlHeight = true;
            tvl.childForceExpandWidth = true;
            tvl.childAlignment = TextAnchor.MiddleLeft;
            AddLabel(titles.transform, L10n.Get("chat_title"), 32f, TextCol, TextAlignmentOptions.Left, FontStyles.Bold);
            _subtitle = AddLabel(titles.transform, string.Empty, 20f, Faint, TextAlignmentOptions.Left);

            // Voice is the next milestone; the control ships locked so the
            // entitlement rule is visible from day one (ADR 0023 §3).
            GameObject mic = Child(head.transform, "Mic");
            LayoutElement mle = mic.AddComponent<LayoutElement>();
            mle.preferredWidth = 56f;
            mle.preferredHeight = 56f;
            _micTint = mic.AddComponent<Image>();
            _micTint.sprite = IconFactory.Mic();
            _micTint.color = Faint;
            Button micBtn = mic.AddComponent<Button>();
            micBtn.targetGraphic = _micTint;
            micBtn.onClick.AddListener(OnMicClicked);

            GameObject close = Child(head.transform, "Close");
            LayoutElement cle = close.AddComponent<LayoutElement>();
            cle.preferredWidth = 56f;
            cle.preferredHeight = 56f;
            Image closeIcon = close.AddComponent<Image>();
            closeIcon.sprite = IconFactory.Close();
            closeIcon.color = Muted;
            Button closeBtn = close.AddComponent<Button>();
            closeBtn.targetGraphic = closeIcon;
            closeBtn.onClick.AddListener(Close);
        }

        private void BuildLog(Transform parent)
        {
            GameObject viewport = Child(parent, "Log");
            viewport.AddComponent<LayoutElement>().flexibleHeight = 1f;
            Image mask = viewport.AddComponent<Image>();
            mask.color = new Color(0f, 0f, 0f, 0.18f);
            viewport.AddComponent<RectMask2D>();

            _scroll = viewport.AddComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 34f;

            GameObject content = Child(viewport.transform, "Content");
            RectTransform crt = (RectTransform)content.transform;
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;

            VerticalLayoutGroup vl = content.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(22, 22, 20, 20);
            vl.spacing = RowSpacing;
            vl.childControlWidth = true;
            vl.childControlHeight = true;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;
            ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _logContent = crt;
            _scroll.content = crt;
            _scroll.viewport = (RectTransform)viewport.transform;

            _emptyLabel = AddLabel(
                content.transform,
                L10n.Get("chat_empty"),
                22f,
                Faint,
                TextAlignmentOptions.Center);
            _emptyLabel.GetComponent<LayoutElement>().preferredHeight = 80f;
        }

        private void BuildComposer(Transform parent)
        {
            GameObject foot = Child(parent, "Composer");
            foot.AddComponent<LayoutElement>().preferredHeight = ComposerHeight;
            VerticalLayoutGroup fvl = foot.AddComponent<VerticalLayoutGroup>();
            fvl.padding = new RectOffset(22, 22, 8, 14);
            fvl.spacing = 4f;
            fvl.childControlWidth = true;
            fvl.childControlHeight = true;
            fvl.childForceExpandWidth = true;

            _status = AddLabel(foot.transform, string.Empty, 18f, Muted, TextAlignmentOptions.Left);
            _status.gameObject.SetActive(false);

            BuildInputRow(foot.transform);
            BuildLockedRow(foot.transform);
        }

        private void BuildInputRow(Transform parent)
        {
            _composerRow = Child(parent, "InputRow");
            _composerRow.AddComponent<LayoutElement>().preferredHeight = SendButtonSize;
            HorizontalLayoutGroup hl = _composerRow.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 12f;
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = true;
            hl.childControlHeight = true;
            hl.childForceExpandWidth = false;

            GameObject field = Child(_composerRow.transform, "Field");
            LayoutElement fle = field.AddComponent<LayoutElement>();
            fle.flexibleWidth = 1f;
            fle.preferredHeight = SendButtonSize;
            Image fbg = field.AddComponent<Image>();
            fbg.sprite = GradientSprite.RoundedDiagonal(0.4f, new Color(0f, 0f, 0f, 0.42f), new Color(0f, 0f, 0f, 0.42f));
            fbg.color = Color.white;

            GameObject textArea = Child(field.transform, "TextArea");
            Stretch((RectTransform)textArea.transform, 18f);
            textArea.AddComponent<RectMask2D>();

            TextMeshProUGUI placeholder = AddStretchedLabel(
                textArea.transform, L10n.Get("chat_placeholder"), 22f, Faint, TextAlignmentOptions.Left);
            TextMeshProUGUI text = AddStretchedLabel(
                textArea.transform, string.Empty, 22f, TextCol, TextAlignmentOptions.Left);

            _input = field.AddComponent<TMP_InputField>();
            _input.textViewport = (RectTransform)textArea.transform;
            _input.textComponent = text;
            _input.placeholder = placeholder;
            _input.characterLimit = ChatLimits.MaxMessageLength;
            _input.lineType = TMP_InputField.LineType.SingleLine;
            _input.onValueChanged.AddListener(OnDraftChanged);
            _input.onSubmit.AddListener(_ => TrySend());

            _counter = AddLabel(_composerRow.transform, string.Empty, 18f, Faint, TextAlignmentOptions.Right);
            _counter.GetComponent<LayoutElement>().preferredWidth = 52f;

            GameObject send = Child(_composerRow.transform, "Send");
            LayoutElement sle = send.AddComponent<LayoutElement>();
            sle.preferredWidth = SendButtonSize;
            sle.preferredHeight = SendButtonSize;
            _sendBg = send.AddComponent<Image>();
            _sendBg.sprite = GradientSprite.RoundedDiagonal(0.4f, SendGreen, SendGreen);
            _sendBg.color = Color.white;
            _sendButton = send.AddComponent<Button>();
            _sendButton.targetGraphic = _sendBg;
            _sendButton.onClick.AddListener(TrySend);
            AddCentredIcon(send.transform, IconFactory.Send(), 34f, Color.white);

            RefreshSendState();
        }

        private void BuildLockedRow(Transform parent)
        {
            _lockedRow = Child(parent, "LockedRow");
            _lockedRow.AddComponent<LayoutElement>().preferredHeight = SendButtonSize;
            Image bg = _lockedRow.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.4f, new Color(0f, 0f, 0f, 0.42f), new Color(0f, 0f, 0f, 0.42f));
            bg.color = Color.white;

            HorizontalLayoutGroup hl = _lockedRow.AddComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(20, 12, 0, 0);
            hl.spacing = 12f;
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = true;
            hl.childControlHeight = true;
            hl.childForceExpandWidth = false;

            AddInlineIcon(_lockedRow.transform, IconFactory.Lock(), 30f, Gold);
            _lockedLabel = AddLabel(_lockedRow.transform, string.Empty, 20f, Muted, TextAlignmentOptions.Left);
            _lockedLabel.GetComponent<LayoutElement>().flexibleWidth = 1f;

            GameObject cta = Child(_lockedRow.transform, "Cta");
            LayoutElement cle = cta.AddComponent<LayoutElement>();
            cle.preferredWidth = 230f;
            cle.preferredHeight = 60f;
            Image cbg = cta.AddComponent<Image>();
            cbg.sprite = GradientSprite.RoundedDiagonal(0.4f, Gold, new Color(0.831f, 0.612f, 0.196f));
            cbg.color = Color.white;
            Button cbtn = cta.AddComponent<Button>();
            cbtn.targetGraphic = cbg;
            cbtn.onClick.AddListener(() => CreateAccountRequested?.Invoke());
            AddStretchedLabel(
                cta.transform,
                L10n.Get("chat_locked_cta"),
                21f,
                new Color(0.07f, 0.06f, 0.05f),
                TextAlignmentOptions.Center,
                FontStyles.Bold);

            _lockedRow.SetActive(false);
        }

        private RowWidgets BuildMessageRow(ChatMessage message)
        {
            bool mine = message.IsFrom(_localUid);

            GameObject row = Child(_logContent, "Msg");
            HorizontalLayoutGroup hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 10f;
            hl.childAlignment = TextAnchor.UpperLeft;
            hl.childControlWidth = true;
            hl.childControlHeight = true;
            hl.childForceExpandWidth = false;

            GameObject bubble = Child(row.transform, "Bubble");
            LayoutElement ble = bubble.AddComponent<LayoutElement>();
            ble.flexibleWidth = 1f;
            Image bg = bubble.AddComponent<Image>();
            Color fill = mine ? OwnBubble : Bubble;
            bg.sprite = GradientSprite.RoundedDiagonal(0.22f, fill, fill);
            bg.color = Color.white;

            VerticalLayoutGroup bvl = bubble.AddComponent<VerticalLayoutGroup>();
            bvl.padding = new RectOffset(16, 16, 10, 12);
            bvl.spacing = 2f;
            bvl.childControlWidth = true;
            bvl.childControlHeight = true;
            bvl.childForceExpandWidth = true;
            bvl.childForceExpandHeight = false;
            ContentSizeFitter fitter = bubble.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Color accent = message.Seat >= 0
                ? BoardRoomHud.SeatColors[message.Seat % BoardRoomHud.SeatColors.Length]
                : Muted;
            string name = mine ? L10n.Get("chat_you") : message.SenderName;
            AddLabel(bubble.transform, name, 19f, accent, TextAlignmentOptions.Left, FontStyles.Bold);

            TextMeshProUGUI body = AddLabel(
                bubble.transform,
                BodyText(message),
                22f,
                message.Redacted ? Faint : TextCol,
                TextAlignmentOptions.Left,
                message.Redacted ? FontStyles.Italic : FontStyles.Normal);
            body.textWrappingMode = TextWrappingModes.Normal;
            LayoutElement bodyLe = body.GetComponent<LayoutElement>();
            bodyLe.preferredHeight = -1f;
            bodyLe.flexibleWidth = 1f;

            // Reporting is the backstop the whole moderation flow rests on, so it
            // is one tap from every message that isn't the player's own.
            GameObject? report = mine || message.Redacted
                ? null
                : AddReportButton(row.transform, message);

            return new RowWidgets { Root = row, Body = body, ReportButton = report };
        }

        /// <summary>
        /// Re-applies a message to a row already on screen. Only a moderator
        /// redaction can change one, and when it does the report control goes
        /// with the text — there is nothing left to report.
        /// </summary>
        private static void RefreshRow(RowWidgets row, ChatMessage message)
        {
            row.Body.text = BodyText(message);
            row.Body.color = message.Redacted ? Faint : TextCol;
            row.Body.fontStyle = message.Redacted ? FontStyles.Italic : FontStyles.Normal;
            if (message.Redacted && row.ReportButton != null)
            {
                row.ReportButton.SetActive(false);
            }
        }

        private GameObject AddReportButton(Transform parent, ChatMessage message)
        {
            GameObject flag = Child(parent, "Report");
            LayoutElement le = flag.AddComponent<LayoutElement>();
            le.preferredWidth = 48f;
            le.preferredHeight = 48f;
            Image icon = flag.AddComponent<Image>();
            icon.sprite = IconFactory.Flag();
            icon.color = Faint;
            Button btn = flag.AddComponent<Button>();
            btn.targetGraphic = icon;
            btn.onClick.AddListener(() => _reportSheet.Show(message));
            return flag;
        }

        private static string BodyText(ChatMessage message) =>
            message.Redacted ? L10n.Get("chat_message_removed") : message.Text;

        // ---- behaviour -----------------------------------------------------

        private void OnDraftChanged(string value)
        {
            int remaining = ChatDraft.Remaining(value);
            bool show = remaining <= CounterVisibleFrom;
            _counter.text = show ? remaining.ToString() : string.Empty;
            _counter.color = remaining < 0 ? Danger : Faint;
            RefreshSendState();
        }

        private void RefreshSendState()
        {
            bool ready = _entitlement.CanSend && ChatDraft.IsSendable(_input?.text);
            if (_sendButton != null)
            {
                _sendButton.interactable = ready;
                _sendBg.color = ready ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            }
        }

        private void TrySend()
        {
            if (!_entitlement.CanSend || !ChatDraft.IsSendable(_input.text))
            {
                return;
            }
            SendRequested?.Invoke(ChatDraft.Normalize(_input.text));
        }

        private void OnMicClicked()
        {
            // Voice ships later; until then say which it is — locked to guests, or
            // simply not built yet — rather than doing nothing.
            SetStatus(
                _entitlement.CanUseVoice
                    ? L10n.Get("chat_voice_soon")
                    : L10n.Get(LockKey(_entitlement.LockReason)),
                isError: !_entitlement.CanUseVoice);
        }

        private void ScrollToBottom()
        {
            Canvas.ForceUpdateCanvases();
            _scroll.verticalNormalizedPosition = 0f;
        }

        private static string LockKey(ChatLockReason reason) => reason switch
        {
            ChatLockReason.Guest => "chat_locked_guest",
            ChatLockReason.Muted => "chat_locked_muted",
            ChatLockReason.NoRoom => "chat_locked_no_room",
            _ => "chat_locked_signed_out",
        };

        // ---- small builders ------------------------------------------------

        private static GameObject Child(Transform parent, string name)
        {
            GameObject go = new(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            return go;
        }

        private static void Stretch(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }

        private static TextMeshProUGUI AddLabel(
            Transform parent,
            string text,
            float size,
            Color color,
            TextAlignmentOptions align,
            FontStyles style = FontStyles.Normal)
        {
            GameObject go = Child(parent, "Label");
            go.AddComponent<LayoutElement>().preferredHeight = size + 10f;
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.raycastTarget = false;
            return t;
        }

        private static TextMeshProUGUI AddStretchedLabel(
            Transform parent,
            string text,
            float size,
            Color color,
            TextAlignmentOptions align,
            FontStyles style = FontStyles.Normal)
        {
            GameObject go = Child(parent, "Label");
            Stretch((RectTransform)go.transform);
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.raycastTarget = false;
            return t;
        }

        private static void AddInlineIcon(Transform parent, Sprite sprite, float size, Color tint)
        {
            GameObject go = Child(parent, "Icon");
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = size;
            le.preferredHeight = size;
            Image img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = tint;
            img.raycastTarget = false;
        }

        private static void AddCentredIcon(Transform parent, Sprite sprite, float size, Color tint)
        {
            GameObject go = Child(parent, "Icon");
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(size, size);
            Image img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = tint;
            img.raycastTarget = false;
        }
    }
}
