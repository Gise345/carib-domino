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
    /// The "report this message" sheet inside <see cref="ChatPanelView"/>: the
    /// quoted message, a reason to pick, an optional note, and submit.
    ///
    /// It rises from the bottom rather than sitting in the middle, because the
    /// hand that tapped the flag is at the bottom of the phone and the reasons
    /// are what it has to reach next. It quotes the message being reported
    /// because a report filed against the wrong line wastes a moderator's time
    /// and, worse, marks an innocent player's record.
    ///
    /// Sizes are ship pixels: the canvas is Constant Pixel Size at scale 1.
    /// Presentational — it raises <see cref="Submitted"/> and the host calls the
    /// server.
    /// </summary>
    public sealed class ChatReportSheet : MonoBehaviour
    {
        /// <summary>Raised with (messageId, reason, note) when the player submits.</summary>
        public event Action<string, ChatReportReason, string>? Submitted;

        private static readonly ChatReportReason[] Reasons =
        {
            ChatReportReason.Harassment,
            ChatReportReason.Hate,
            ChatReportReason.Threats,
            ChatReportReason.Sexual,
            ChatReportReason.Spam,
            ChatReportReason.Cheating,
            ChatReportReason.Other,
        };

        private static readonly Color Scrim = new(0f, 0f, 0f, 0.72f);
        private static readonly Color Card = new(0.090f, 0.071f, 0.051f, 0.995f);
        private static readonly Color Brass = new(0.941f, 0.761f, 0.290f);
        private static readonly Color Bone = new(0.949f, 0.918f, 0.855f);
        private static readonly Color BoneWorn = new(0.863f, 0.824f, 0.737f);
        private static readonly Color Ink = new(0.063f, 0.122f, 0.110f);
        private static readonly Color Muted = new(0.659f, 0.624f, 0.557f);
        private static readonly Color Faint = new(0.490f, 0.443f, 0.361f);
        private static readonly Color Danger = new(0.910f, 0.361f, 0.282f);
        private static readonly Color DangerDeep = new(0.659f, 0.227f, 0.173f);
        private static readonly Color Chip = new(1f, 1f, 1f, 0.05f);

        private const float SheetHeight = 940f;
        private const float ChipHeight = 68f;
        private const float ActionHeight = 92f;

        private GameObject _root = null!;
        private TextMeshProUGUI _quoteWho = null!;
        private TextMeshProUGUI _quoteText = null!;
        private TMP_InputField _note = null!;
        private Button _submit = null!;
        private Image _submitBg = null!;
        private readonly List<(Image bg, TextMeshProUGUI label)> _chips = new();

        private string _messageId = string.Empty;
        private int _selected = -1;

        /// <summary>Builds the sheet over the given panel root. Starts hidden.</summary>
        /// <param name="parent">The chat modal's root, so the sheet sits above it.</param>
        public void Init(RectTransform parent)
        {
            _root = Child(parent, "ReportSheet");
            Stretch((RectTransform)_root.transform);

            GameObject scrim = Child(_root.transform, "Scrim");
            Stretch((RectTransform)scrim.transform);
            Image sbg = scrim.AddComponent<Image>();
            sbg.color = Scrim;
            Button dismiss = scrim.AddComponent<Button>();
            dismiss.targetGraphic = sbg;
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(Hide);

            GameObject card = Child(_root.transform, "Card");
            RectTransform rt = (RectTransform)card.transform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(0f, 0f);
            rt.offsetMax = new Vector2(0f, SheetHeight);
            Image cbg = card.AddComponent<Image>();
            cbg.sprite = GradientSprite.RoundedDiagonal(0.06f, Card, Card);
            cbg.color = Color.white;

            VerticalLayout(card, new RectOffset(28, 28, 30, 30), 18f);

            BuildHead(card.transform);
            BuildQuote(card.transform);
            BuildReasons(card.transform);
            BuildNote(card.transform);
            BuildActions(card.transform);

            _root.SetActive(false);
        }

        /// <summary>Opens the sheet for one message.</summary>
        /// <param name="message">The message being reported.</param>
        public void Show(ChatMessage message)
        {
            _messageId = message.Id;
            _selected = -1;
            _note.SetTextWithoutNotify(string.Empty);
            _quoteWho.text = message.CreatedAt == DateTime.MinValue
                ? message.SenderName
                : $"{message.SenderName} · {message.CreatedAt.ToLocalTime():t}";
            _quoteText.text = message.Text;
            RefreshChips();
            RefreshSubmit();
            _root.SetActive(true);
            _root.transform.SetAsLastSibling();
        }

        /// <summary>Closes the sheet without filing anything.</summary>
        public void Hide() => _root.SetActive(false);

        private void BuildHead(Transform parent)
        {
            GameObject head = Child(parent, "Head");
            head.AddComponent<LayoutElement>().preferredHeight = 56f;
            HorizontalLayout(head, new RectOffset(0, 0, 0, 0), 16f);

            AddIcon(head.transform, IconFactory.Flag(), 48f, Danger);
            TextMeshProUGUI title = Label(head.transform, L10n.Get("chat_report_title"), 38f, Bone,
                TextAlignmentOptions.Left, FontStyles.Bold);
            title.GetComponent<LayoutElement>().flexibleWidth = 1f;
        }

        private void BuildQuote(Transform parent)
        {
            GameObject box = Child(parent, "Quote");
            box.AddComponent<LayoutElement>().preferredHeight = 150f;
            Image bg = box.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.16f, new Color(0f, 0f, 0f, 0.4f),
                                                             new Color(0f, 0f, 0f, 0.4f));
            bg.color = Color.white;
            HorizontalLayout(box, new RectOffset(0, 0, 0, 0), 18f, TextAnchor.UpperLeft);

            // A danger-coloured spine, so the quoted line reads as the thing
            // under review rather than as more chat.
            GameObject spine = Child(box.transform, "Spine");
            LayoutElement sle = spine.AddComponent<LayoutElement>();
            sle.preferredWidth = 8f;
            sle.flexibleHeight = 1f;
            Image sbg = spine.AddComponent<Image>();
            sbg.sprite = GradientSprite.RoundedDiagonal(0.5f, Danger, Danger);
            sbg.color = Color.white;

            GameObject copy = Child(box.transform, "Copy");
            LayoutElement cle = copy.AddComponent<LayoutElement>();
            cle.flexibleWidth = 1f;
            cle.minWidth = 0f;
            VerticalLayout(copy, new RectOffset(0, 20, 18, 18), 4f);

            _quoteWho = Label(copy.transform, string.Empty, 22f, Faint, TextAlignmentOptions.Left);
            _quoteText = Label(copy.transform, string.Empty, 28f, BoneWorn, TextAlignmentOptions.Left);
            _quoteText.textWrappingMode = TextWrappingModes.Normal;
            _quoteText.GetComponent<LayoutElement>().flexibleHeight = 1f;
        }

        private void BuildReasons(Transform parent)
        {
            Label(parent, L10n.Get("chat_report_reason_prompt"), 24f, Faint, TextAlignmentOptions.Left);

            GameObject grid = Child(parent, "Reasons");
            grid.AddComponent<LayoutElement>().preferredHeight = ChipHeight * 3f + 24f;
            GridLayoutGroup gl = grid.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(288f, ChipHeight);
            gl.spacing = new Vector2(12f, 12f);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = 3;

            for (int i = 0; i < Reasons.Length; i++)
            {
                int index = i;
                GameObject chip = Child(grid.transform, "Reason");
                Image bg = chip.AddComponent<Image>();
                bg.sprite = GradientSprite.RoundedDiagonal(0.4f, Color.white, Color.white);
                bg.color = Chip;
                Button btn = chip.AddComponent<Button>();
                btn.targetGraphic = bg;
                btn.onClick.AddListener(() => Select(index));

                TextMeshProUGUI label = StretchedLabel(chip.transform,
                    L10n.Get(Reasons[index].LocalizationKey()), 26f, BoneWorn,
                    TextAlignmentOptions.Center, FontStyles.Bold);
                _chips.Add((bg, label));
            }
        }

        private void BuildNote(Transform parent)
        {
            GameObject field = Child(parent, "Note");
            field.AddComponent<LayoutElement>().preferredHeight = 96f;
            Image bg = field.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.28f, new Color(0f, 0f, 0f, 0.42f),
                                                             new Color(0f, 0f, 0f, 0.42f));
            bg.color = Color.white;

            GameObject area = Child(field.transform, "TextArea");
            Stretch((RectTransform)area.transform, 24f);
            area.AddComponent<RectMask2D>();

            TextMeshProUGUI placeholder = StretchedLabel(area.transform,
                L10n.Get("chat_report_note_placeholder"), 26f, new Color(0.659f, 0.624f, 0.557f, 0.7f),
                TextAlignmentOptions.Left);
            TextMeshProUGUI text = StretchedLabel(area.transform, string.Empty, 26f, Bone,
                TextAlignmentOptions.Left);

            _note = field.AddComponent<TMP_InputField>();
            _note.textViewport = (RectTransform)area.transform;
            _note.textComponent = text;
            _note.placeholder = placeholder;
            _note.characterLimit = 500;
            _note.lineType = TMP_InputField.LineType.SingleLine;
        }

        private void BuildActions(Transform parent)
        {
            GameObject row = Child(parent, "Actions");
            row.AddComponent<LayoutElement>().preferredHeight = ActionHeight;
            HorizontalLayout(row, new RectOffset(0, 0, 0, 0), 14f);

            GameObject cancel = Child(row.transform, "Cancel");
            LayoutElement cle = cancel.AddComponent<LayoutElement>();
            cle.flexibleWidth = 1f;
            cle.preferredHeight = ActionHeight;
            Image cbg = cancel.AddComponent<Image>();
            cbg.sprite = GradientSprite.RoundedDiagonal(0.28f, Color.white, Color.white);
            cbg.color = Chip;
            Button cbtn = cancel.AddComponent<Button>();
            cbtn.targetGraphic = cbg;
            cbtn.onClick.AddListener(Hide);
            StretchedLabel(cancel.transform, L10n.Get("chat_report_cancel"), 30f, BoneWorn,
                TextAlignmentOptions.Center, FontStyles.Bold);

            GameObject submit = Child(row.transform, "Submit");
            LayoutElement sle = submit.AddComponent<LayoutElement>();
            sle.flexibleWidth = 1f;
            sle.preferredHeight = ActionHeight;
            _submitBg = submit.AddComponent<Image>();
            _submitBg.sprite = GradientSprite.RoundedDiagonal(0.28f, Danger, DangerDeep);
            _submitBg.color = Color.white;
            _submit = submit.AddComponent<Button>();
            _submit.targetGraphic = _submitBg;
            _submit.onClick.AddListener(SubmitReport);
            StretchedLabel(submit.transform, L10n.Get("chat_report_submit"), 30f, Color.white,
                TextAlignmentOptions.Center, FontStyles.Bold);
        }

        private void Select(int index)
        {
            _selected = index;
            RefreshChips();
            RefreshSubmit();
        }

        private void RefreshChips()
        {
            for (int i = 0; i < _chips.Count; i++)
            {
                bool on = i == _selected;
                (Image bg, TextMeshProUGUI label) = _chips[i];
                bg.color = on ? Brass : Chip;
                label.color = on ? Ink : BoneWorn;
            }
        }

        private void RefreshSubmit()
        {
            bool ready = _selected >= 0 && !string.IsNullOrEmpty(_messageId);
            _submit.interactable = ready;
            _submitBg.color = ready ? Color.white : new Color(1f, 1f, 1f, 0.4f);
        }

        private void SubmitReport()
        {
            if (_selected < 0 || string.IsNullOrEmpty(_messageId))
            {
                return;
            }
            Submitted?.Invoke(_messageId, Reasons[_selected], _note.text ?? string.Empty);
            Hide();
        }

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

        /// <summary>Both expand flags stated — see the note in ChatPanelView.</summary>
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

        private static void VerticalLayout(GameObject go, RectOffset padding, float spacing)
        {
            VerticalLayoutGroup vl = go.AddComponent<VerticalLayoutGroup>();
            vl.padding = padding;
            vl.spacing = spacing;
            vl.childAlignment = TextAnchor.UpperLeft;
            vl.childControlWidth = true;
            vl.childControlHeight = true;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;
        }

        private static void AddIcon(Transform parent, Sprite sprite, float size, Color tint)
        {
            GameObject go = Child(parent, "Icon");
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = size;
            le.preferredHeight = size;
            le.minWidth = size;
            Image img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = tint;
            img.preserveAspect = true;
            img.raycastTarget = false;
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
    }
}
