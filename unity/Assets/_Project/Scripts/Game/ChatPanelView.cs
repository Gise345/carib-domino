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
    /// scrim. See <c>docs/prototypes/chat-panel.html</c> for the design this
    /// implements.
    ///
    /// <para>Two things govern every number in here.</para>
    ///
    /// <para><b>The canvas is Constant Pixel Size at scale 1</b>, so a size in
    /// this file is a device pixel on the player's phone — 22pt type is 22 real
    /// pixels on a 1080-wide screen, which is why the first pass was unreadable.
    /// Body copy is 34 and no text is under 21.</para>
    ///
    /// <para><b>Layout groups expand their children by default.</b> Unity's
    /// <c>childForceExpandHeight</c> starts <c>true</c>, which stretched a 40×40
    /// icon to the full height of its row and turned the chat glyph into a gold
    /// column. Every group below sets BOTH expand flags explicitly, and every
    /// icon sets <c>preserveAspect</c>.</para>
    ///
    /// Presentational only: it renders what it is given and raises events. The
    /// host (<see cref="BoardBootstrap"/>) owns <see cref="Pose.Net.ChatService"/>.
    /// The composer lock is cosmetic — the server refuses a guest's message
    /// regardless (ADR 0023 §3).
    /// </summary>
    public sealed class ChatPanelView : MonoBehaviour
    {
        /// <summary>Raised with the text to send (typed or a quick phrase).</summary>
        public event Action<string>? SendRequested;

        /// <summary>Raised with (messageId, reason, note) when a report is filed.</summary>
        public event Action<string, ChatReportReason, string>? ReportRequested;

        /// <summary>Raised when a guest taps the "create account" CTA.</summary>
        public event Action? CreateAccountRequested;

        /// <summary>Raised when the player retries a failed connection.</summary>
        public event Action? RetryRequested;

        /// <summary>Raised when the panel is dismissed.</summary>
        public event Action? Closed;

        // ---- palette (DESIGN_SYSTEM.md §2, warmed for the lacquered card) ---
        private static readonly Color Scrim = new(0f, 0f, 0f, 0.74f);
        private static readonly Color CardTop = new(0.090f, 0.071f, 0.051f, 0.99f);
        private static readonly Color CardBottom = new(0.071f, 0.055f, 0.039f, 0.99f);
        private static readonly Color Bone = new(0.949f, 0.918f, 0.855f);
        private static readonly Color BoneWorn = new(0.863f, 0.824f, 0.737f);
        private static readonly Color Brass = new(0.941f, 0.761f, 0.290f);
        private static readonly Color Ink = new(0.063f, 0.122f, 0.110f);
        private static readonly Color Muted = new(0.659f, 0.624f, 0.557f);
        private static readonly Color Faint = new(0.490f, 0.443f, 0.361f);
        private static readonly Color Danger = new(0.949f, 0.439f, 0.353f);
        private static readonly Color Bubble = new(0.118f, 0.098f, 0.075f, 0.96f);
        private static readonly Color BubbleOwn = new(0.106f, 0.141f, 0.086f, 0.96f);
        private static readonly Color Field = new(0f, 0f, 0f, 0.42f);
        private static readonly Color SendGreen = new(0.247f, 0.643f, 0.353f);
        private static readonly Color SendDeep = new(0.165f, 0.482f, 0.255f);

        // ---- metrics (ship pixels — see the class summary) -------------------
        private const float CardInsetX = 40f;
        private const float CardInsetY = 120f;
        private const float HeaderHeight = 132f;
        private const float HeadButton = 76f;
        private const float AvatarSize = 72f;
        private const float FlagSize = 56f;
        private const float FieldHeight = 104f;
        private const float SendSize = 104f;
        private const float QuickStripHeight = 74f;
        private const float LockedHeight = 132f;
        private const float CtaHeight = 84f;
        private const float BodyFont = 34f;
        private const float NameFont = 24f;
        private const float TimeFont = 21f;

        /// <summary>Characters left before the counter appears.</summary>
        private const int CounterVisibleFrom = 40;

        /// <summary>Widget handles for one rendered message.</summary>
        private sealed class RowWidgets
        {
            public GameObject Root = null!;
            public TextMeshProUGUI Body = null!;
            public GameObject? ReportButton;
            public GameObject? MaskedNote;
        }

        private readonly Dictionary<string, RowWidgets> _rows = new();

        private GameObject _root = null!;
        private RectTransform _logContent = null!;
        private ScrollRect _scroll = null!;
        private TMP_InputField _input = null!;
        private Button _sendButton = null!;
        private Image _sendBg = null!;
        private GameObject _composerRow = null!;
        private GameObject _quickStrip = null!;
        private GameObject _lockedRow = null!;
        private Image _lockedBg = null!;
        private Image _lockedIcon = null!;
        private TextMeshProUGUI _lockedTitle = null!;
        private TextMeshProUGUI _lockedBody = null!;
        private GameObject _lockedCta = null!;
        private TextMeshProUGUI _lockedCtaLabel = null!;
        private GameObject _banner = null!;
        private TextMeshProUGUI _bannerLabel = null!;
        private TextMeshProUGUI _counter = null!;
        private TextMeshProUGUI _subtitle = null!;
        private GameObject _codeChip = null!;
        private TextMeshProUGUI _codeLabel = null!;
        private GameObject _empty = null!;
        private TextMeshProUGUI _emptyTitle = null!;
        private TextMeshProUGUI _emptyBody = null!;
        private Image _micTint = null!;
        private ChatReportSheet _reportSheet = null!;

        private string? _localUid;
        private ChatEntitlement _entitlement;
        private DateTime? _mutedUntil;

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
            BuildBanner(card.transform);
            BuildQuickPhrases(card.transform);
            BuildComposer(card.transform);
            BuildLockedRow(card.transform);

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

        /// <summary>Id of the last message rendered — the unread marker.</summary>
        public string? LastRenderedMessageId { get; private set; }

        /// <summary>Shows the panel and focuses the composer when it is usable.</summary>
        public void Open()
        {
            _root.SetActive(true);
            _root.transform.SetAsLastSibling();
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

        /// <summary>
        /// Names the table: the ruleset and how many are at it, plus the join
        /// code as a chip when there is one worth showing. A matchmade session
        /// id is not — printing it is what put a raw GUID in the header.
        /// </summary>
        /// <param name="modeLabel">Localised ruleset name.</param>
        /// <param name="playerCount">Seats at the table, or 0 when unknown.</param>
        /// <param name="roomId">The Photon session name.</param>
        public void SetHeader(string modeLabel, int playerCount, string? roomId)
        {
            _subtitle.text = playerCount > 0
                ? L10n.Get("chat_subtitle_table", modeLabel, playerCount)
                : modeLabel;

            string? code = ChatRoomLabel.DisplayCode(roomId);
            _codeChip.SetActive(code != null);
            if (code != null)
            {
                _codeLabel.text = code;
            }
        }

        /// <summary>
        /// Applies what this session may do: an account holder types, a guest
        /// gets the sign-up CTA, a muted player is told when it lifts.
        /// </summary>
        /// <param name="entitlement">The evaluated entitlement.</param>
        /// <param name="mutedUntil">When a mute lifts, when one is in force.</param>
        public void SetEntitlement(ChatEntitlement entitlement, DateTime? mutedUntil = null)
        {
            _entitlement = entitlement;
            _mutedUntil = mutedUntil;

            bool canSend = entitlement.CanSend;
            _composerRow.SetActive(canSend);
            _quickStrip.SetActive(canSend);
            _lockedRow.SetActive(!canSend);
            _micTint.color = entitlement.CanUseVoice ? Muted : Faint;

            if (!canSend)
            {
                DressLockedRow(entitlement.LockReason);
            }
            RefreshSendState();
        }

        /// <summary>
        /// Applies the room's messages, oldest first. Rows are reconciled by id
        /// rather than rebuilt: the listener re-delivers the whole window on
        /// every change, and tearing down a hundred rows to add one line would
        /// yank the scroll position out from under someone mid-read.
        /// </summary>
        /// <param name="messages">The room's messages as delivered by the listener.</param>
        public void SetMessages(IReadOnlyList<ChatMessage> messages)
        {
            HashSet<string> live = new(messages.Count);
            foreach (ChatMessage message in messages)
            {
                live.Add(message.Id);
            }

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
                    RefreshRow(row, message);
                }
                row.Root.transform.SetSiblingIndex(i + 1); // +1: the empty state leads
            }

            _empty.SetActive(messages.Count == 0);
            LastRenderedMessageId = messages.Count > 0 ? messages[messages.Count - 1].Id : null;
            if (grew)
            {
                ScrollToBottom();
            }
        }

        /// <summary>
        /// The banner above the composer — a refusal, or the note that a message
        /// was filtered. Empty clears it.
        /// </summary>
        /// <param name="text">Already-localised status text.</param>
        /// <param name="isError">Colours it as a problem rather than a note.</param>
        public void SetStatus(string text, bool isError = false)
        {
            _bannerLabel.text = text;
            _bannerLabel.color = isError ? new Color(1f, 0.788f, 0.745f) : Brass;
            _banner.GetComponent<Image>().color = isError
                ? new Color(0.949f, 0.439f, 0.353f, 0.14f)
                : new Color(0.941f, 0.761f, 0.290f, 0.12f);
            _banner.SetActive(!string.IsNullOrEmpty(text));
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

        // ---- build: frame ---------------------------------------------------

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
            Stretch(rt);
            rt.offsetMin = new Vector2(CardInsetX, CardInsetY);
            rt.offsetMax = new Vector2(-CardInsetX, -CardInsetY);

            Image bg = card.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.05f, CardTop, CardBottom);
            bg.color = Color.white;
            Shadow shadow = card.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            shadow.effectDistance = new Vector2(0f, -12f);

            VerticalLayout(card, padding: new RectOffset(0, 0, 0, 0), spacing: 0f);
            return card;
        }

        private void BuildHeader(Transform parent)
        {
            GameObject head = Child(parent, "Header");
            head.AddComponent<LayoutElement>().preferredHeight = HeaderHeight;
            Image bg = head.AddComponent<Image>();
            bg.color = new Color(0.941f, 0.761f, 0.290f, 0.05f);
            HorizontalLayout(head, new RectOffset(28, 22, 0, 0), 20f);

            AddIcon(head.transform, IconFactory.Chat(), 56f, Brass);

            GameObject titles = Child(head.transform, "Titles");
            LayoutElement tle = titles.AddComponent<LayoutElement>();
            tle.flexibleWidth = 1f;
            tle.minWidth = 0f;
            VerticalLayout(titles, new RectOffset(0, 0, 0, 0), 0f, TextAnchor.MiddleLeft);
            Label(titles.transform, L10n.Get("chat_title"), 46f, Bone, TextAlignmentOptions.Left, FontStyles.Bold);

            GameObject subRow = Child(titles.transform, "SubRow");
            subRow.AddComponent<LayoutElement>().preferredHeight = 34f;
            HorizontalLayout(subRow, new RectOffset(0, 0, 0, 0), 10f);
            _subtitle = Label(subRow.transform, string.Empty, 26f, Muted, TextAlignmentOptions.Left);
            _subtitle.GetComponent<LayoutElement>().flexibleWidth = 0f;
            BuildCodeChip(subRow.transform);

            // Voice ships in ADR 0024; the control is here from the start so the
            // entitlement rule is visible rather than implied.
            _micTint = IconButton(head.transform, IconFactory.Mic(), OnMicClicked, Faint);
            IconButton(head.transform, IconFactory.Close(), Close, Muted);
        }

        private void BuildCodeChip(Transform parent)
        {
            _codeChip = Child(parent, "CodeChip");
            LayoutElement le = _codeChip.AddComponent<LayoutElement>();
            le.preferredHeight = 34f;
            le.preferredWidth = 140f;
            Image bg = _codeChip.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.5f, new Color(0.941f, 0.761f, 0.290f, 0.14f),
                                                             new Color(0.941f, 0.761f, 0.290f, 0.14f));
            bg.color = Color.white;
            _codeLabel = StretchedLabel(_codeChip.transform, string.Empty, 22f, Brass,
                TextAlignmentOptions.Center, FontStyles.Bold);
            _codeLabel.characterSpacing = 6f;
            _codeChip.SetActive(false);
        }

        private void BuildLog(Transform parent)
        {
            GameObject viewport = Child(parent, "Log");
            viewport.AddComponent<LayoutElement>().flexibleHeight = 1f;
            Image bg = viewport.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.18f);
            viewport.AddComponent<RectMask2D>();

            _scroll = viewport.AddComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 46f;

            GameObject content = Child(viewport.transform, "Content");
            RectTransform crt = (RectTransform)content.transform;
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;

            VerticalLayout(content, new RectOffset(26, 26, 26, 26), 22f);
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _logContent = crt;
            _scroll.content = crt;
            _scroll.viewport = (RectTransform)viewport.transform;

            BuildEmptyState(content.transform);
        }

        private void BuildEmptyState(Transform parent)
        {
            _empty = Child(parent, "Empty");
            _empty.AddComponent<LayoutElement>().preferredHeight = 320f;
            VerticalLayout(_empty, new RectOffset(40, 40, 60, 0), 14f, TextAnchor.UpperCenter);

            AddIcon(_empty.transform, IconFactory.Chat(), 120f, new Color(0.949f, 0.918f, 0.855f, 0.28f));
            _emptyTitle = Label(_empty.transform, L10n.Get("chat_empty_title"), 34f, Bone,
                TextAlignmentOptions.Center, FontStyles.Bold);
            _emptyBody = Label(_empty.transform, L10n.Get("chat_empty_body"), 26f, Muted,
                TextAlignmentOptions.Center);
            _emptyBody.textWrappingMode = TextWrappingModes.Normal;
            _emptyBody.GetComponent<LayoutElement>().preferredHeight = 80f;
        }

        private void BuildBanner(Transform parent)
        {
            _banner = Child(parent, "Banner");
            LayoutElement le = _banner.AddComponent<LayoutElement>();
            le.preferredHeight = 64f;
            le.minHeight = 64f;
            Image bg = _banner.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.3f, Color.white, Color.white);
            bg.color = new Color(0.949f, 0.439f, 0.353f, 0.14f);

            _bannerLabel = StretchedLabel(_banner.transform, string.Empty, 24f, Bone,
                TextAlignmentOptions.Center);
            _banner.SetActive(false);
        }

        // ---- build: composer -------------------------------------------------

        private void BuildQuickPhrases(Transform parent)
        {
            _quickStrip = Child(parent, "QuickPhrases");
            _quickStrip.AddComponent<LayoutElement>().preferredHeight = QuickStripHeight;
            _quickStrip.AddComponent<RectMask2D>();

            ScrollRect scroll = _quickStrip.AddComponent<ScrollRect>();
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            GameObject content = Child(_quickStrip.transform, "Content");
            RectTransform crt = (RectTransform)content.transform;
            crt.anchorMin = new Vector2(0f, 0f);
            crt.anchorMax = new Vector2(0f, 1f);
            crt.pivot = new Vector2(0f, 0.5f);
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;
            HorizontalLayout(content, new RectOffset(26, 26, 8, 0), 12f);
            content.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = crt;
            scroll.viewport = (RectTransform)_quickStrip.transform;

            foreach (string key in ChatQuickPhrases.Keys)
            {
                BuildQuickPhrase(content.transform, L10n.Get(key));
            }
        }

        private void BuildQuickPhrase(Transform parent, string phrase)
        {
            GameObject chip = Child(parent, "Phrase");
            LayoutElement le = chip.AddComponent<LayoutElement>();
            le.preferredHeight = 58f;
            // Roughly the rendered width: chips sit in a scrolling strip, so a
            // little slack costs nothing and a ContentSizeFitter per chip would
            // fight the strip's own fitter.
            le.preferredWidth = 44f + phrase.Length * 15f;

            Image bg = chip.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.5f, new Color(0.941f, 0.761f, 0.290f, 0.10f),
                                                             new Color(0.941f, 0.761f, 0.290f, 0.10f));
            bg.color = Color.white;
            Button btn = chip.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => SendRequested?.Invoke(phrase));

            StretchedLabel(chip.transform, phrase, 26f, new Color(0.910f, 0.835f, 0.659f),
                TextAlignmentOptions.Center, FontStyles.Bold);
        }

        private void BuildComposer(Transform parent)
        {
            _composerRow = Child(parent, "Composer");
            _composerRow.AddComponent<LayoutElement>().preferredHeight = FieldHeight + 44f;
            HorizontalLayout(_composerRow, new RectOffset(26, 26, 18, 26), 16f);

            GameObject field = Child(_composerRow.transform, "Field");
            LayoutElement fle = field.AddComponent<LayoutElement>();
            fle.flexibleWidth = 1f;
            fle.minWidth = 0f;
            fle.preferredHeight = FieldHeight;
            Image fbg = field.AddComponent<Image>();
            fbg.sprite = GradientSprite.RoundedDiagonal(0.28f, Field, Field);
            fbg.color = Color.white;

            GameObject textArea = Child(field.transform, "TextArea");
            Stretch((RectTransform)textArea.transform, 26f);
            textArea.AddComponent<RectMask2D>();

            TextMeshProUGUI placeholder = StretchedLabel(textArea.transform,
                L10n.Get("chat_placeholder"), BodyFont, new Color(0.659f, 0.624f, 0.557f, 0.7f),
                TextAlignmentOptions.Left);
            TextMeshProUGUI text = StretchedLabel(textArea.transform, string.Empty, BodyFont, Bone,
                TextAlignmentOptions.Left);

            _input = field.AddComponent<TMP_InputField>();
            _input.textViewport = (RectTransform)textArea.transform;
            _input.textComponent = text;
            _input.placeholder = placeholder;
            _input.characterLimit = ChatLimits.MaxMessageLength;
            _input.lineType = TMP_InputField.LineType.SingleLine;
            _input.onValueChanged.AddListener(OnDraftChanged);
            _input.onSubmit.AddListener(_ => TrySend());

            _counter = Label(_composerRow.transform, string.Empty, 24f, Faint, TextAlignmentOptions.Right);
            _counter.GetComponent<LayoutElement>().preferredWidth = 62f;

            GameObject send = Child(_composerRow.transform, "Send");
            LayoutElement sle = send.AddComponent<LayoutElement>();
            sle.preferredWidth = SendSize;
            sle.preferredHeight = SendSize;
            _sendBg = send.AddComponent<Image>();
            _sendBg.sprite = GradientSprite.RoundedDiagonal(0.29f, SendGreen, SendDeep);
            _sendBg.color = Color.white;
            _sendButton = send.AddComponent<Button>();
            _sendButton.targetGraphic = _sendBg;
            _sendButton.onClick.AddListener(TrySend);
            AddCentredIcon(send.transform, IconFactory.Send(), 46f, Color.white);

            RefreshSendState();
        }

        private void BuildLockedRow(Transform parent)
        {
            _lockedRow = Child(parent, "Locked");
            _lockedRow.AddComponent<LayoutElement>().preferredHeight = LockedHeight;
            HorizontalLayout(_lockedRow, new RectOffset(26, 26, 18, 26), 0f);

            GameObject bar = Child(_lockedRow.transform, "Bar");
            LayoutElement ble = bar.AddComponent<LayoutElement>();
            ble.flexibleWidth = 1f;
            ble.preferredHeight = 96f;
            _lockedBg = bar.AddComponent<Image>();
            _lockedBg.sprite = GradientSprite.RoundedDiagonal(0.28f, Color.white, Color.white);
            _lockedBg.color = new Color(0.941f, 0.761f, 0.290f, 0.09f);
            HorizontalLayout(bar, new RectOffset(24, 20, 0, 0), 18f);

            _lockedIcon = AddIcon(bar.transform, IconFactory.Lock(), 48f, Brass);

            GameObject copy = Child(bar.transform, "Copy");
            LayoutElement cle = copy.AddComponent<LayoutElement>();
            cle.flexibleWidth = 1f;
            cle.minWidth = 0f;
            VerticalLayout(copy, new RectOffset(0, 0, 0, 0), 0f, TextAnchor.MiddleLeft);
            _lockedTitle = Label(copy.transform, string.Empty, 28f, Bone, TextAlignmentOptions.Left, FontStyles.Bold);
            _lockedBody = Label(copy.transform, string.Empty, 24f, Muted, TextAlignmentOptions.Left);

            _lockedCta = Child(bar.transform, "Cta");
            LayoutElement gle = _lockedCta.AddComponent<LayoutElement>();
            gle.preferredHeight = CtaHeight;
            gle.preferredWidth = 300f;
            Image gbg = _lockedCta.AddComponent<Image>();
            gbg.sprite = GradientSprite.RoundedDiagonal(0.28f, Brass, new Color(0.722f, 0.525f, 0.043f));
            gbg.color = Color.white;
            Button gbtn = _lockedCta.AddComponent<Button>();
            gbtn.targetGraphic = gbg;
            gbtn.onClick.AddListener(OnLockedCtaClicked);
            _lockedCtaLabel = StretchedLabel(_lockedCta.transform, string.Empty, 28f, Ink,
                TextAlignmentOptions.Center, FontStyles.Bold);

            _lockedRow.SetActive(false);
        }

        /// <summary>
        /// Says which lock is in force and what to do about it. A guest gets an
        /// offer, a muted player gets an expiry, and neither gets a dead end.
        /// </summary>
        private void DressLockedRow(ChatLockReason reason)
        {
            bool guest = reason == ChatLockReason.Guest;
            bool muted = reason == ChatLockReason.Muted;

            _lockedBg.color = muted
                ? new Color(0.949f, 0.439f, 0.353f, 0.10f)
                : new Color(0.941f, 0.761f, 0.290f, 0.09f);
            _lockedIcon.color = muted ? Danger : Brass;
            _lockedIcon.sprite = muted ? IconFactory.MicOff() : IconFactory.Lock();

            _lockedTitle.text = L10n.Get(TitleKey(reason));
            _lockedTitle.color = muted ? new Color(1f, 0.788f, 0.745f) : Bone;

            _lockedBody.text = muted && _mutedUntil.HasValue
                ? L10n.Get("chat_muted_until", _mutedUntil.Value.ToLocalTime().ToString("t"))
                : L10n.Get(BodyKey(reason));

            // Only two locks have anything to offer: a guest can make an
            // account, and a dropped connection can be retried.
            bool showCta = guest || reason == ChatLockReason.NoRoom;
            _lockedCta.SetActive(showCta);
            if (showCta)
            {
                _lockedCtaLabel.text = L10n.Get(guest ? "chat_locked_cta" : "chat_retry");
            }
        }

        private static string TitleKey(ChatLockReason reason) => reason switch
        {
            ChatLockReason.Guest => "chat_locked_guest_title",
            ChatLockReason.Muted => "chat_locked_muted_title",
            ChatLockReason.NoRoom => "chat_locked_no_room_title",
            _ => "chat_locked_signed_out_title",
        };

        private static string BodyKey(ChatLockReason reason) => reason switch
        {
            ChatLockReason.Guest => "chat_locked_guest_body",
            ChatLockReason.Muted => "chat_locked_muted_body",
            ChatLockReason.NoRoom => "chat_locked_no_room_body",
            _ => "chat_locked_signed_out_body",
        };

        // ---- build: one message ---------------------------------------------

        private RowWidgets BuildMessageRow(ChatMessage message)
        {
            bool mine = message.IsFrom(_localUid);

            GameObject row = Child(_logContent, "Msg");
            HorizontalLayout(row, new RectOffset(0, 0, 0, 0), 16f, TextAnchor.UpperLeft);
            row.GetComponent<HorizontalLayoutGroup>().reverseArrangement = mine;

            AddAvatar(row.transform, message, mine);

            GameObject column = Child(row.transform, "Column");
            LayoutElement cle = column.AddComponent<LayoutElement>();
            cle.flexibleWidth = 1f;
            cle.minWidth = 0f;
            VerticalLayout(column, new RectOffset(0, 0, 0, 0), 6f);
            column.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            AddMeta(column.transform, message, mine);

            GameObject bubbleRow = Child(column.transform, "BubbleRow");
            HorizontalLayout(bubbleRow, new RectOffset(0, 0, 0, 0), 12f, TextAnchor.UpperLeft);
            bubbleRow.GetComponent<HorizontalLayoutGroup>().reverseArrangement = mine;
            bubbleRow.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            TextMeshProUGUI body = AddBubble(bubbleRow.transform, message, mine);
            GameObject? report = mine || message.Redacted
                ? null
                : AddReportButton(bubbleRow.transform, message);

            GameObject? masked = message.Filtered && !message.Redacted
                ? Label(column.transform, L10n.Get("chat_filtered_notice"), 22f, Faint,
                        mine ? TextAlignmentOptions.Right : TextAlignmentOptions.Left).gameObject
                : null;

            return new RowWidgets { Root = row, Body = body, ReportButton = report, MaskedNote = masked };
        }

        private void AddAvatar(Transform parent, ChatMessage message, bool mine)
        {
            GameObject avatar = Child(parent, "Avatar");
            LayoutElement le = avatar.AddComponent<LayoutElement>();
            le.preferredWidth = AvatarSize;
            le.preferredHeight = AvatarSize;
            Color seat = SeatColor(message.Seat);
            Image bg = avatar.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.5f, seat, seat);
            bg.color = Color.white;

            StretchedLabel(avatar.transform, Initials(mine ? L10n.Get("chat_you") : message.SenderName),
                30f, Ink, TextAlignmentOptions.Center, FontStyles.Bold);
        }

        private void AddMeta(Transform parent, ChatMessage message, bool mine)
        {
            GameObject meta = Child(parent, "Meta");
            meta.AddComponent<LayoutElement>().preferredHeight = 32f;
            HorizontalLayout(meta, new RectOffset(6, 6, 0, 0), 12f,
                mine ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft);
            meta.GetComponent<HorizontalLayoutGroup>().reverseArrangement = mine;

            Label(meta.transform, mine ? L10n.Get("chat_you") : message.SenderName, NameFont,
                mine ? new Color(0.910f, 0.835f, 0.659f) : SeatColor(message.Seat),
                TextAlignmentOptions.Left, FontStyles.Bold);

            if (message.CreatedAt != DateTime.MinValue)
            {
                Label(meta.transform, message.CreatedAt.ToLocalTime().ToString("t"), TimeFont,
                    new Color(0.659f, 0.624f, 0.557f, 0.7f), TextAlignmentOptions.Left);
            }
        }

        private TextMeshProUGUI AddBubble(Transform parent, ChatMessage message, bool mine)
        {
            GameObject bubble = Child(parent, "Bubble");
            LayoutElement le = bubble.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.minWidth = 0f;
            Color fill = mine ? BubbleOwn : Bubble;
            Image bg = bubble.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.22f, fill, fill);
            bg.color = Color.white;

            VerticalLayout(bubble, new RectOffset(24, 24, 18, 18), 0f);
            bubble.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            TextMeshProUGUI body = Label(bubble.transform, BodyText(message), BodyFont,
                message.Redacted ? Faint : Bone,
                mine ? TextAlignmentOptions.Right : TextAlignmentOptions.Left,
                message.Redacted ? FontStyles.Italic : FontStyles.Normal);
            body.textWrappingMode = TextWrappingModes.Normal;
            LayoutElement ble = body.GetComponent<LayoutElement>();
            ble.preferredHeight = -1f;
            ble.flexibleWidth = 1f;
            return body;
        }

        private GameObject AddReportButton(Transform parent, ChatMessage message)
        {
            GameObject flag = Child(parent, "Report");
            LayoutElement le = flag.AddComponent<LayoutElement>();
            le.preferredWidth = FlagSize;
            le.preferredHeight = FlagSize;
            Image bg = flag.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.3f, new Color(1f, 1f, 1f, 0.04f),
                                                            new Color(1f, 1f, 1f, 0.04f));
            bg.color = Color.white;
            Button btn = flag.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => _reportSheet.Show(message));
            AddCentredIcon(flag.transform, IconFactory.Flag(), 30f, new Color(0.659f, 0.624f, 0.557f, 0.75f));
            return flag;
        }

        /// <summary>
        /// Re-applies a message to a row already on screen. Only a moderator
        /// redaction can change one, and when it does the report control goes
        /// with the text — there is nothing left to report.
        /// </summary>
        private static void RefreshRow(RowWidgets row, ChatMessage message)
        {
            row.Body.text = BodyText(message);
            row.Body.color = message.Redacted ? Faint : Bone;
            row.Body.fontStyle = message.Redacted ? FontStyles.Italic : FontStyles.Normal;
            if (!message.Redacted)
            {
                return;
            }
            if (row.ReportButton != null)
            {
                row.ReportButton.SetActive(false);
            }
            if (row.MaskedNote != null)
            {
                row.MaskedNote.SetActive(false);
            }
        }

        private static string BodyText(ChatMessage message) =>
            message.Redacted ? L10n.Get("chat_message_removed") : message.Text;

        private static Color SeatColor(int seat) =>
            seat >= 0
                ? BoardRoomHud.SeatColors[seat % BoardRoomHud.SeatColors.Length]
                : Muted;

        private static string Initials(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "?";
            }
            string[] parts = name.Split(' ');
            if (parts.Length >= 2 && parts[0].Length > 0 && parts[1].Length > 0)
            {
                return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}";
            }
            return name.Substring(0, Math.Min(2, name.Length)).ToUpperInvariant();
        }

        // ---- behaviour -------------------------------------------------------

        private void OnDraftChanged(string value)
        {
            int remaining = ChatDraft.Remaining(value);
            _counter.text = remaining <= CounterVisibleFrom ? remaining.ToString() : string.Empty;
            _counter.color = remaining < 0 ? Danger : Faint;
            RefreshSendState();
        }

        private void RefreshSendState()
        {
            bool ready = _entitlement.CanSend && ChatDraft.IsSendable(_input?.text);
            if (_sendButton == null)
            {
                return;
            }
            _sendButton.interactable = ready;
            _sendBg.color = ready ? Color.white : new Color(1f, 1f, 1f, 0.4f);
        }

        private void TrySend()
        {
            if (!_entitlement.CanSend || !ChatDraft.IsSendable(_input.text))
            {
                return;
            }
            SendRequested?.Invoke(ChatDraft.Normalize(_input.text));
        }

        private void OnLockedCtaClicked()
        {
            if (_entitlement.LockReason == ChatLockReason.Guest)
            {
                CreateAccountRequested?.Invoke();
            }
            else
            {
                RetryRequested?.Invoke();
            }
        }

        private void OnMicClicked()
        {
            SetStatus(
                _entitlement.CanUseVoice
                    ? L10n.Get("chat_voice_soon")
                    : L10n.Get(TitleKey(_entitlement.LockReason)),
                isError: !_entitlement.CanUseVoice);
        }

        private void ScrollToBottom()
        {
            Canvas.ForceUpdateCanvases();
            _scroll.verticalNormalizedPosition = 0f;
        }

        // ---- small builders ---------------------------------------------------

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

        /// <summary>
        /// A horizontal group with BOTH expand flags stated. The force-expand
        /// defaults are what stretched icons into columns in the first build, so
        /// no group in this file is allowed to inherit them.
        /// </summary>
        private static void HorizontalLayout(
            GameObject go, RectOffset padding, float spacing,
            TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            HorizontalLayoutGroup hl = go.AddComponent<HorizontalLayoutGroup>();
            hl.padding = padding;
            hl.spacing = spacing;
            hl.childAlignment = alignment;
            hl.childControlWidth = true;
            hl.childControlHeight = true;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = false;
        }

        /// <summary>A vertical group with both expand flags stated.</summary>
        private static void VerticalLayout(
            GameObject go, RectOffset padding, float spacing,
            TextAnchor alignment = TextAnchor.UpperLeft)
        {
            VerticalLayoutGroup vl = go.AddComponent<VerticalLayoutGroup>();
            vl.padding = padding;
            vl.spacing = spacing;
            vl.childAlignment = alignment;
            vl.childControlWidth = true;
            vl.childControlHeight = true;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;
        }

        private static TextMeshProUGUI Label(
            Transform parent, string text, float size, Color color,
            TextAlignmentOptions align, FontStyles style = FontStyles.Normal)
        {
            GameObject go = Child(parent, "Label");
            go.AddComponent<LayoutElement>().preferredHeight = size + 12f;
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.raycastTarget = false;
            return t;
        }

        private static TextMeshProUGUI StretchedLabel(
            Transform parent, string text, float size, Color color,
            TextAlignmentOptions align, FontStyles style = FontStyles.Normal)
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

        /// <summary>An icon in a fixed square that keeps its aspect.</summary>
        private static Image AddIcon(Transform parent, Sprite sprite, float size, Color tint)
        {
            GameObject go = Child(parent, "Icon");
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = size;
            le.preferredHeight = size;
            le.minWidth = size;
            le.minHeight = size;
            Image img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = tint;
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
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
            img.preserveAspect = true;
            img.raycastTarget = false;
        }

        /// <summary>A square header button; returns its icon for later tinting.</summary>
        private Image IconButton(Transform parent, Sprite sprite, UnityEngine.Events.UnityAction onClick, Color tint)
        {
            GameObject go = Child(parent, "HeadBtn");
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = HeadButton;
            le.preferredHeight = HeadButton;
            le.minWidth = HeadButton;
            Image bg = go.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.29f, new Color(1f, 1f, 1f, 0.05f),
                                                             new Color(1f, 1f, 1f, 0.05f));
            bg.color = Color.white;
            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(onClick);

            GameObject iconGo = Child(go.transform, "Icon");
            RectTransform rt = (RectTransform)iconGo.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(40f, 40f);
            Image img = iconGo.AddComponent<Image>();
            img.sprite = sprite;
            img.color = tint;
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
        }
    }
}
