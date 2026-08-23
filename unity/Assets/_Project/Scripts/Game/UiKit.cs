#nullable enable
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Pose.Game
{
    /// <summary>
    /// The app's shared visual language: the pieces every screen outside the
    /// board is built from — the back ring, lacquered cards, brass rules, stat
    /// tiles, pills, segmented choices, sliders and switches.
    ///
    /// It lives in one place so the screens cannot drift apart. Before this,
    /// each tab drew its own rows and the result was five screens that looked
    /// like five different apps; a new screen now costs a handful of calls and
    /// arrives already matching.
    ///
    /// The accent is <see cref="Brass"/> — the same brass as the node on a
    /// tile's centre divider (see <c>TileArtSet</c>). Reusing it for the back
    /// ring, the header rule and the active tab is what ties this chrome to the
    /// board rather than to a generic dark theme. Colours are the
    /// <c>docs/DESIGN_SYSTEM.md</c> §2 tokens.
    ///
    /// Everything is built from code, like the rest of the UI — no prefabs.
    /// </summary>
    public static class UiKit
    {
        // ---- Palette (DESIGN_SYSTEM.md §2) ---------------------------------

        public static readonly Color Bone = new(0.949f, 0.918f, 0.855f);
        public static readonly Color Lamplight = new(0.910f, 0.835f, 0.659f);
        public static readonly Color Brass = new(0.792f, 0.541f, 0.016f);
        public static readonly Color BrassLit = new(0.941f, 0.761f, 0.290f);
        public static readonly Color Muted = new(0.659f, 0.624f, 0.557f);
        public static readonly Color Cta = new(0.933f, 0.498f, 0.373f);
        public static readonly Color CtaDeep = new(0.659f, 0.275f, 0.173f);
        public static readonly Color Success = new(0.482f, 0.714f, 0.380f);
        public static readonly Color Warning = new(0.914f, 0.635f, 0.231f);
        public static readonly Color Danger = new(0.949f, 0.439f, 0.353f);

        /// <summary>Lacquered card ground — dark enough to carry text over wood.</summary>
        private static readonly Color CardFill = new(0.035f, 0.102f, 0.082f, 0.88f);
        private static readonly Color TileFill = new(0.078f, 0.196f, 0.165f, 0.72f);
        private static readonly Color HairlineFill = new(0.910f, 0.835f, 0.659f, 0.14f);

        // ---- Metrics --------------------------------------------------------

        public const float HeaderHeight = 118f;
        public const float BackRingSize = 74f;
        public const float CardPadding = 26f;
        public const float CardGap = 18f;
        public const float RowHeight = 56f;

        private static Sprite? _disc;
        private static Sprite? _rounded;

        /// <summary>A plain white disc, tinted per use. Built once and shared.</summary>
        private static Sprite Disc() =>
            _disc ??= GradientSprite.RoundedDiagonal(0.5f, Color.white, Color.white);

        /// <summary>A rounded rectangle, tinted per use.</summary>
        private static Sprite Rounded() =>
            _rounded ??= GradientSprite.RoundedDiagonal(0.14f, Color.white, Color.white);

        // ---- Structure ------------------------------------------------------

        /// <summary>
        /// The header every screen wears: the back ring on the left, the title
        /// centred, and a brass hairline under it. Returns the body area below,
        /// already laid out as a vertical stack for cards.
        /// </summary>
        public static RectTransform Screen(RectTransform root, string title, Action onBack)
        {
            GameObject header = Child(root, "Header");
            RectTransform hrt = (RectTransform)header.transform;
            hrt.anchorMin = new Vector2(0f, 1f);
            hrt.anchorMax = new Vector2(1f, 1f);
            hrt.pivot = new Vector2(0.5f, 1f);
            hrt.offsetMin = new Vector2(0f, -HeaderHeight);
            hrt.offsetMax = Vector2.zero;

            BackRing(hrt, onBack);

            TextMeshProUGUI heading = Label(hrt, title, 44f, Bone, TextAlignmentOptions.Center);
            heading.fontStyle = FontStyles.Bold;
            Stretch((RectTransform)heading.transform, left: 120f, right: 120f);

            // The one repeated rule in the whole app.
            GameObject rule = Child(root, "Rule");
            RectTransform rrt = (RectTransform)rule.transform;
            rrt.anchorMin = new Vector2(0f, 1f);
            rrt.anchorMax = new Vector2(1f, 1f);
            rrt.pivot = new Vector2(0.5f, 1f);
            rrt.offsetMin = new Vector2(30f, -HeaderHeight - 2f);
            rrt.offsetMax = new Vector2(-30f, -HeaderHeight);
            Image ruleImg = rule.AddComponent<Image>();
            ruleImg.color = new Color(Brass.r, Brass.g, Brass.b, 0.65f);
            ruleImg.raycastTarget = false;

            GameObject body = Child(root, "Body");
            RectTransform brt = (RectTransform)body.transform;
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = new Vector2(30f, 24f);
            brt.offsetMax = new Vector2(-30f, -(HeaderHeight + 20f));

            VerticalLayoutGroup stack = body.AddComponent<VerticalLayoutGroup>();
            stack.childAlignment = TextAnchor.UpperCenter;
            stack.spacing = CardGap;
            stack.childControlWidth = true;
            stack.childControlHeight = true;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = false;
            return brt;
        }

        /// <summary>
        /// The back control: a brass ring around a left chevron. Round, and the
        /// only circular thing in the chrome, so it never reads as a card or a
        /// button. A bare chevron would vanish against the board art — the ring
        /// gives it a constant silhouette and an honest tap target.
        /// </summary>
        public static GameObject BackRing(RectTransform parent, Action onClick)
        {
            GameObject go = Child(parent, "BackRing");
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(24f, 0f);
            rt.sizeDelta = new Vector2(BackRingSize, BackRingSize);

            Image ring = go.AddComponent<Image>();
            ring.sprite = IconFactory.Ring();
            ring.color = Brass;

            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = ring;
            btn.onClick.AddListener(() => onClick());

            GameObject glyph = Child(go.transform, "Chevron");
            RectTransform grt = (RectTransform)glyph.transform;
            Stretch(grt, inset: BackRingSize * 0.27f);
            Image gi = glyph.AddComponent<Image>();
            gi.sprite = IconFactory.ChevronLeft();
            gi.color = BrassLit;
            gi.raycastTarget = false;
            return go;
        }

        /// <summary>A lacquered panel with a brass edge — the unit every screen is made of.</summary>
        public static RectTransform Card(RectTransform parent, string? head = null, string? tag = null)
        {
            GameObject go = Child(parent, "Card");
            Image bg = go.AddComponent<Image>();
            bg.sprite = Rounded();
            bg.color = CardFill;
            bg.type = Image.Type.Sliced;

            Outline edge = go.AddComponent<Outline>();
            edge.effectColor = new Color(Brass.r, Brass.g, Brass.b, 0.28f);
            edge.effectDistance = new Vector2(1.5f, -1.5f);

            VerticalLayoutGroup vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(
                (int)CardPadding, (int)CardPadding, (int)(CardPadding * 0.8f), (int)(CardPadding * 0.8f));
            vlg.spacing = 12f;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            if (head != null)
            {
                CardHead((RectTransform)go.transform, head, tag);
            }
            return (RectTransform)go.transform;
        }

        /// <summary>A card's gold uppercase heading, with an optional note on the right.</summary>
        public static void CardHead(RectTransform card, string head, string? tag = null)
        {
            GameObject row = Child(card, "Head");
            row.AddComponent<LayoutElement>().preferredHeight = 34f;
            HorizontalLayoutGroup h = row.AddComponent<HorizontalLayoutGroup>();
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = false;

            TextMeshProUGUI t = Label(row.transform, head.ToUpperInvariant(), 24f, Brass, TextAlignmentOptions.MidlineLeft);
            t.fontStyle = FontStyles.Bold;
            t.characterSpacing = 6f;
            t.GetComponent<LayoutElement>().flexibleWidth = 1f;

            if (tag != null)
            {
                Label(row.transform, tag, 20f, Muted, TextAlignmentOptions.MidlineRight);
            }
        }

        /// <summary>A label/value line inside a card.</summary>
        public static TextMeshProUGUI Row(RectTransform card, string label, string value, Color? valueColor = null)
        {
            GameObject row = Child(card, "Row");
            row.AddComponent<LayoutElement>().preferredHeight = RowHeight;
            HorizontalLayoutGroup h = row.AddComponent<HorizontalLayoutGroup>();
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = false;

            TextMeshProUGUI l = Label(row.transform, label, 26f, Lamplight, TextAlignmentOptions.MidlineLeft);
            l.GetComponent<LayoutElement>().flexibleWidth = 1f;

            TextMeshProUGUI v = Label(row.transform, value, 26f, valueColor ?? Bone, TextAlignmentOptions.MidlineRight);
            v.fontStyle = FontStyles.Bold;
            return v;
        }

        /// <summary>A two-across grid — the shape stats and coin bundles want.</summary>
        public static RectTransform Grid2(RectTransform parent, float cellHeight)
        {
            GameObject go = Child(parent, "Grid");
            GridLayoutGroup g = go.AddComponent<GridLayoutGroup>();
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = 2;
            g.spacing = new Vector2(14f, 14f);
            g.childAlignment = TextAnchor.UpperCenter;
            // Cell width is set by the caller once the parent width is known;
            // a sensible default keeps the Editor preview honest.
            g.cellSize = new Vector2(300f, cellHeight);
            go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return (RectTransform)go.transform;
        }

        /// <summary>A big-number tile: small key above, large value below.</summary>
        public static TextMeshProUGUI StatTile(RectTransform parent, string key, string value)
        {
            GameObject go = Child(parent, "Stat");
            Image bg = go.AddComponent<Image>();
            bg.sprite = Rounded();
            bg.color = TileFill;
            bg.type = Image.Type.Sliced;

            VerticalLayoutGroup v = go.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(18, 18, 12, 12);
            v.spacing = 0f;
            v.childAlignment = TextAnchor.MiddleLeft;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            TextMeshProUGUI k = Label(go.transform, key.ToUpperInvariant(), 19f, Muted, TextAlignmentOptions.MidlineLeft);
            k.characterSpacing = 5f;
            TextMeshProUGUI val = Label(go.transform, value, 40f, Bone, TextAlignmentOptions.MidlineLeft);
            val.fontStyle = FontStyles.Bold;
            return val;
        }

        /// <summary>Tone of a status pill.</summary>
        public enum PillTone
        {
            /// <summary>Good, live, linked.</summary>
            On,

            /// <summary>Neutral, absent, off.</summary>
            Off,

            /// <summary>Wants attention — pending requests, warnings.</summary>
            Hot,
        }

        /// <summary>A small status chip. Colour AND wording, never colour alone.</summary>
        public static TextMeshProUGUI Pill(Transform parent, string text, PillTone tone)
        {
            (Color fg, Color bg) = tone switch
            {
                PillTone.On => (Success, new Color(Success.r, Success.g, Success.b, 0.2f)),
                PillTone.Hot => (Danger, new Color(Danger.r, Danger.g, Danger.b, 0.2f)),
                _ => (Muted, new Color(Muted.r, Muted.g, Muted.b, 0.16f)),
            };

            GameObject go = Child(parent, "Pill");
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 40f;
            le.minWidth = 90f;

            Image img = go.AddComponent<Image>();
            img.sprite = Disc();
            img.color = bg;
            img.type = Image.Type.Sliced;

            TextMeshProUGUI t = Label(go.transform, text, 21f, fg, TextAlignmentOptions.Center);
            t.fontStyle = FontStyles.Bold;
            Stretch((RectTransform)t.transform, left: 16f, right: 16f);
            return t;
        }

        /// <summary>
        /// A segmented choice — format, table size, language. One option is lit
        /// brass; the rest are outlined.
        /// </summary>
        public static void Segment(
            RectTransform parent, string[] options, int selected, Action<int> onSelect)
        {
            GameObject go = Child(parent, "Segment");
            go.AddComponent<LayoutElement>().preferredHeight = 72f;
            HorizontalLayoutGroup h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 12f;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;

            for (int i = 0; i < options.Length; i++)
            {
                int index = i;
                bool on = i == selected;

                GameObject opt = Child(go.transform, "Opt");
                Image bg = opt.AddComponent<Image>();
                bg.sprite = Rounded();
                bg.type = Image.Type.Sliced;
                bg.color = on ? Brass : new Color(Lamplight.r, Lamplight.g, Lamplight.b, 0.10f);

                Button btn = opt.AddComponent<Button>();
                btn.targetGraphic = bg;
                btn.onClick.AddListener(() => onSelect(index));

                TextMeshProUGUI t = Label(
                    opt.transform, options[i], 26f,
                    on ? new Color(0.09f, 0.08f, 0.04f) : Lamplight,
                    TextAlignmentOptions.Center);
                t.fontStyle = FontStyles.Bold;
                Stretch((RectTransform)t.transform);
                t.raycastTarget = false;
            }
        }

        /// <summary>
        /// A volume line: name, current value, and a filled track. Volumes are
        /// sliders rather than on/off because the reason to open Settings is
        /// almost always "quieter", not "silent".
        /// </summary>
        public static Slider VolumeRow(RectTransform card, string label, float value01, Action<float> onChange)
        {
            GameObject wrap = Child(card, "Volume");
            VerticalLayoutGroup v = wrap.AddComponent<VerticalLayoutGroup>();
            v.spacing = 6f;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            TextMeshProUGUI readout = Row((RectTransform)wrap.transform, label,
                Mathf.RoundToInt(value01 * 100f).ToString(System.Globalization.CultureInfo.InvariantCulture));

            GameObject track = Child(wrap.transform, "Track");
            track.AddComponent<LayoutElement>().preferredHeight = 18f;
            Image tbg = track.AddComponent<Image>();
            tbg.sprite = Disc();
            tbg.type = Image.Type.Sliced;
            tbg.color = HairlineFill;

            GameObject fillArea = Child(track.transform, "Fill");
            RectTransform fr = (RectTransform)fillArea.transform;
            fr.anchorMin = Vector2.zero;
            fr.anchorMax = new Vector2(value01, 1f);
            fr.offsetMin = Vector2.zero;
            fr.offsetMax = Vector2.zero;
            Image fill = fillArea.AddComponent<Image>();
            fill.sprite = Disc();
            fill.type = Image.Type.Sliced;
            fill.color = Brass;

            Slider slider = track.AddComponent<Slider>();
            slider.transition = Selectable.Transition.None;
            slider.fillRect = fr;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.SetValueWithoutNotify(value01);
            slider.onValueChanged.AddListener(v01 =>
            {
                readout.text = Mathf.RoundToInt(v01 * 100f).ToString(System.Globalization.CultureInfo.InvariantCulture);
                onChange(v01);
            });
            return slider;
        }

        /// <summary>A labelled on/off switch.</summary>
        public static Toggle SwitchRow(RectTransform card, string label, bool on, Action<bool> onChange)
        {
            GameObject row = Child(card, "Switch");
            row.AddComponent<LayoutElement>().preferredHeight = RowHeight;
            HorizontalLayoutGroup h = row.AddComponent<HorizontalLayoutGroup>();
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = false;

            TextMeshProUGUI l = Label(row.transform, label, 26f, Lamplight, TextAlignmentOptions.MidlineLeft);
            l.GetComponent<LayoutElement>().flexibleWidth = 1f;

            GameObject knobTrack = Child(row.transform, "Track");
            LayoutElement le = knobTrack.AddComponent<LayoutElement>();
            le.preferredWidth = 92f;
            le.preferredHeight = 46f;
            Image tbg = knobTrack.AddComponent<Image>();
            tbg.sprite = Disc();
            tbg.type = Image.Type.Sliced;
            tbg.color = on ? Success : HairlineFill;

            GameObject knob = Child(knobTrack.transform, "Knob");
            RectTransform krt = (RectTransform)knob.transform;
            krt.anchorMin = new Vector2(on ? 1f : 0f, 0.5f);
            krt.anchorMax = krt.anchorMin;
            krt.pivot = new Vector2(on ? 1f : 0f, 0.5f);
            krt.anchoredPosition = new Vector2(on ? -6f : 6f, 0f);
            krt.sizeDelta = new Vector2(34f, 34f);
            Image ki = knob.AddComponent<Image>();
            ki.sprite = Disc();
            ki.color = Bone;

            Toggle toggle = knobTrack.AddComponent<Toggle>();
            toggle.transition = Selectable.Transition.None;
            toggle.SetIsOnWithoutNotify(on);
            toggle.onValueChanged.AddListener(isOn =>
            {
                tbg.color = isOn ? Success : HairlineFill;
                krt.anchorMin = new Vector2(isOn ? 1f : 0f, 0.5f);
                krt.anchorMax = krt.anchorMin;
                krt.pivot = new Vector2(isOn ? 1f : 0f, 0.5f);
                krt.anchoredPosition = new Vector2(isOn ? -6f : 6f, 0f);
                onChange(isOn);
            });
            return toggle;
        }

        /// <summary>The screen's one primary action. Pairs with <see cref="GhostButton"/>.</summary>
        public static GameObject PrimaryButton(RectTransform parent, string label, Action onClick)
        {
            GameObject go = Child(parent, "Cta");
            go.AddComponent<LayoutElement>().preferredHeight = 96f;
            Image bg = go.AddComponent<Image>();
            bg.sprite = Rounded();
            bg.type = Image.Type.Sliced;
            bg.color = Cta;

            Shadow lip = go.AddComponent<Shadow>();
            lip.effectColor = CtaDeep;
            lip.effectDistance = new Vector2(0f, -5f);

            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => onClick());

            TextMeshProUGUI t = Label(go.transform, label, 32f, new Color(0.11f, 0.06f, 0.03f), TextAlignmentOptions.Center);
            t.fontStyle = FontStyles.Bold;
            Stretch((RectTransform)t.transform);
            t.raycastTarget = false;
            return go;
        }

        /// <summary>A quieter action — secondary to the screen's primary.</summary>
        public static GameObject GhostButton(RectTransform parent, string label, Action onClick)
        {
            GameObject go = Child(parent, "Ghost");
            go.AddComponent<LayoutElement>().preferredHeight = 84f;
            Image bg = go.AddComponent<Image>();
            bg.sprite = Rounded();
            bg.type = Image.Type.Sliced;
            bg.color = new Color(Lamplight.r, Lamplight.g, Lamplight.b, 0.09f);

            Outline edge = go.AddComponent<Outline>();
            edge.effectColor = new Color(Lamplight.r, Lamplight.g, Lamplight.b, 0.3f);
            edge.effectDistance = new Vector2(1.2f, -1.2f);

            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => onClick());

            TextMeshProUGUI t = Label(go.transform, label, 28f, Bone, TextAlignmentOptions.Center);
            t.fontStyle = FontStyles.Bold;
            Stretch((RectTransform)t.transform);
            t.raycastTarget = false;
            return go;
        }

        /// <summary>
        /// A flexible gap. Content shorter than the screen should breathe into
        /// the space rather than stack at the top and leave a third of the
        /// phone empty.
        /// </summary>
        public static void Spring(RectTransform parent)
        {
            GameObject go = Child(parent, "Spring");
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;
            le.minHeight = 0f;
        }

        // ---- Primitives ------------------------------------------------------

        public static GameObject Child(Transform parent, string name)
        {
            GameObject go = new(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            return go;
        }

        public static TextMeshProUGUI Label(
            Transform parent, string text, float size, Color color, TextAlignmentOptions align)
        {
            GameObject go = Child(parent, "Label");
            go.AddComponent<LayoutElement>().preferredHeight = size + 10f;
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            return t;
        }

        public static void Stretch(RectTransform rt, float inset = 0f, float left = 0f, float right = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset + left, inset);
            rt.offsetMax = new Vector2(-(inset + right), -inset);
        }
    }
}
