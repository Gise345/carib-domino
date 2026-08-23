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
    /// It quotes the message being reported because a report filed against the
    /// wrong line wastes a moderator's time and, worse, an innocent player's
    /// record. Presentational — it raises <see cref="Submitted"/> and the host
    /// calls the server.
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

        private static readonly Color Scrim = new(0f, 0f, 0f, 0.82f);
        private static readonly Color Card = new(0.098f, 0.078f, 0.063f, 0.99f);
        private static readonly Color Gold = new(0.961f, 0.769f, 0.318f);
        private static readonly Color TextCol = new(0.957f, 0.929f, 0.882f);
        private static readonly Color Muted = new(0.702f, 0.643f, 0.533f);
        private static readonly Color Faint = new(0.490f, 0.443f, 0.361f);
        private static readonly Color Danger = new(0.949f, 0.439f, 0.353f);
        private static readonly Color Chip = new(0.157f, 0.129f, 0.102f);

        private GameObject _root = null!;
        private TextMeshProUGUI _quote = null!;
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
            rt.anchorMin = new Vector2(0.14f, 0.14f);
            rt.anchorMax = new Vector2(0.86f, 0.86f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            Image cbg = card.AddComponent<Image>();
            cbg.sprite = GradientSprite.RoundedDiagonal(0.08f, Card, Card);
            cbg.color = Color.white;

            VerticalLayoutGroup vl = card.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(28, 28, 24, 22);
            vl.spacing = 12f;
            vl.childControlWidth = true;
            vl.childControlHeight = true;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;

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
            _quote.text = $"{message.SenderName}: “{message.Text}”";
            RefreshChips();
            RefreshSubmit();
            _root.SetActive(true);
        }

        /// <summary>Closes the sheet without filing anything.</summary>
        public void Hide() => _root.SetActive(false);

        private void BuildHead(Transform parent)
        {
            GameObject head = Child(parent, "Head");
            head.AddComponent<LayoutElement>().preferredHeight = 44f;
            HorizontalLayoutGroup hl = head.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 10f;
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = true;
            hl.childControlHeight = true;
            hl.childForceExpandWidth = false;

            GameObject icon = Child(head.transform, "Icon");
            LayoutElement ile = icon.AddComponent<LayoutElement>();
            ile.preferredWidth = 32f;
            ile.preferredHeight = 32f;
            Image img = icon.AddComponent<Image>();
            img.sprite = IconFactory.Flag();
            img.color = Danger;
            img.raycastTarget = false;

            TextMeshProUGUI title = Label(head.transform, L10n.Get("chat_report_title"), 28f, TextCol, TextAlignmentOptions.Left, FontStyles.Bold);
            title.GetComponent<LayoutElement>().flexibleWidth = 1f;
        }

        private void BuildQuote(Transform parent)
        {
            GameObject box = Child(parent, "Quote");
            box.AddComponent<LayoutElement>().preferredHeight = 86f;
            Image bg = box.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.2f, new Color(0f, 0f, 0f, 0.4f), new Color(0f, 0f, 0f, 0.4f));
            bg.color = Color.white;

            GameObject inner = Child(box.transform, "Text");
            Stretch((RectTransform)inner.transform, 14f);
            _quote = inner.AddComponent<TextMeshProUGUI>();
            _quote.fontSize = 20f;
            _quote.color = Muted;
            _quote.alignment = TextAlignmentOptions.TopLeft;
            _quote.textWrappingMode = TextWrappingModes.Normal;
            _quote.raycastTarget = false;
        }

        private void BuildReasons(Transform parent)
        {
            Label(parent, L10n.Get("chat_report_reason_prompt"), 20f, Faint, TextAlignmentOptions.Left);

            GameObject grid = Child(parent, "Reasons");
            grid.AddComponent<LayoutElement>().flexibleHeight = 1f;
            GridLayoutGroup gl = grid.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(230f, 58f);
            gl.spacing = new Vector2(10f, 10f);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = 3;

            for (int i = 0; i < Reasons.Length; i++)
            {
                int index = i;
                GameObject chip = Child(grid.transform, "Reason");
                Image bg = chip.AddComponent<Image>();
                bg.sprite = GradientSprite.RoundedDiagonal(0.3f, Chip, Chip);
                bg.color = Color.white;
                Button btn = chip.AddComponent<Button>();
                btn.targetGraphic = bg;
                btn.onClick.AddListener(() => Select(index));

                GameObject labelGo = Child(chip.transform, "Label");
                Stretch((RectTransform)labelGo.transform);
                TextMeshProUGUI label = labelGo.AddComponent<TextMeshProUGUI>();
                label.text = L10n.Get(Reasons[index].LocalizationKey());
                label.fontSize = 19f;
                label.color = Muted;
                label.alignment = TextAlignmentOptions.Center;
                label.raycastTarget = false;

                _chips.Add((bg, label));
            }
        }

        private void BuildNote(Transform parent)
        {
            GameObject field = Child(parent, "Note");
            field.AddComponent<LayoutElement>().preferredHeight = 64f;
            Image bg = field.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.3f, new Color(0f, 0f, 0f, 0.42f), new Color(0f, 0f, 0f, 0.42f));
            bg.color = Color.white;

            GameObject area = Child(field.transform, "TextArea");
            Stretch((RectTransform)area.transform, 14f);
            area.AddComponent<RectMask2D>();

            TextMeshProUGUI placeholder = StretchedLabel(area.transform, L10n.Get("chat_report_note_placeholder"), 19f, Faint);
            TextMeshProUGUI text = StretchedLabel(area.transform, string.Empty, 19f, TextCol);

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
            row.AddComponent<LayoutElement>().preferredHeight = 68f;
            HorizontalLayoutGroup hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 12f;
            hl.childAlignment = TextAnchor.MiddleRight;
            hl.childControlWidth = true;
            hl.childControlHeight = true;
            hl.childForceExpandWidth = false;

            GameObject cancel = Child(row.transform, "Cancel");
            LayoutElement cle = cancel.AddComponent<LayoutElement>();
            cle.preferredWidth = 190f;
            cle.preferredHeight = 60f;
            Image cbg = cancel.AddComponent<Image>();
            cbg.sprite = GradientSprite.RoundedDiagonal(0.3f, Chip, Chip);
            cbg.color = Color.white;
            Button cbtn = cancel.AddComponent<Button>();
            cbtn.targetGraphic = cbg;
            cbtn.onClick.AddListener(Hide);
            StretchedLabel(cancel.transform, L10n.Get("chat_report_cancel"), 21f, Muted, TextAlignmentOptions.Center);

            GameObject submit = Child(row.transform, "Submit");
            LayoutElement sle = submit.AddComponent<LayoutElement>();
            sle.preferredWidth = 230f;
            sle.preferredHeight = 60f;
            _submitBg = submit.AddComponent<Image>();
            _submitBg.sprite = GradientSprite.RoundedDiagonal(0.3f, Danger, new Color(0.729f, 0.290f, 0.220f));
            _submitBg.color = Color.white;
            _submit = submit.AddComponent<Button>();
            _submit.targetGraphic = _submitBg;
            _submit.onClick.AddListener(SubmitReport);
            StretchedLabel(submit.transform, L10n.Get("chat_report_submit"), 21f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
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
                bg.color = on ? Gold : Color.white;
                label.color = on ? new Color(0.07f, 0.06f, 0.05f) : Muted;
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

        private static TextMeshProUGUI Label(
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

        private static TextMeshProUGUI StretchedLabel(
            Transform parent,
            string text,
            float size,
            Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Left,
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
    }
}
