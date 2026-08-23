#nullable enable
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Pose.Game
{
    /// <summary>
    /// The pieces the three game rooms are built from, on top of the shared
    /// chrome in <see cref="UiKit"/>.
    ///
    /// A room is painted rather than labelled: the room's title art carries the
    /// screen instead of a text header, the format is chosen by picture, the
    /// table size is counted in heads rather than read as a digit, and the
    /// stake sits on a carved board. That is a different job from the tabs,
    /// which are read — so it lives here instead of swelling the kit every
    /// screen shares.
    ///
    /// Every piece falls back to something drawn when its sprite is missing, so
    /// a room ships and reads correctly before the art lands, and art can arrive
    /// one file at a time. Sprites also arrive <em>after</em> the screen is
    /// built (<c>BoardBootstrap</c> hands them over post-Awake), which is why
    /// the art setters re-run the layout rather than assuming a sprite was
    /// present at build time.
    /// </summary>
    public static class RoomKit
    {
        // ---- Metrics --------------------------------------------------------

        /// <summary>Fraction of the screen width a stacked title logo occupies.</summary>
        private const float HeroWidth = 0.62f;

        /// <summary>Wide banner titles (One-Love) get more width, being shorter.</summary>
        private const float HeroWidthWide = 0.82f;

        /// <summary>Above this aspect a title reads as a banner, not a stacked block.</summary>
        private const float BannerAspect = 2.2f;

        /// <summary>Height of the drawn title used until the art arrives.</summary>
        private const float HeroFallbackHeight = 150f;

        /// <summary>Art box every format tile fits inside, whatever its own ratio.</summary>
        private const float TileArtAspect = 2.1f;

        // The carved board is a frame, not a picture: its plank is empty by
        // design. These fractions are where the numbers can sit without
        // colliding with the painted flowers at lower-left or the treasure
        // chest, which starts about 69% across. Measured off the supplied art,
        // trimmed to its own bounds.
        private const float PlankLeft = 0.09f;
        private const float PlankRight = 0.25f;
        private const float PlankTop = 0.28f;
        private const float PlankBottom = 0.17f;
        private const float PlankCaptionTop = 0.10f;

        // The drawn stand-in has no painted furniture to avoid, so its numbers
        // use honest padding instead.
        private const float FallbackInset = 0.07f;
        private const float FallbackVInset = 0.16f;

        /// <summary>
        /// Unchosen options: a dark, faintly green-cast grey. Flat and quiet, so
        /// the lit one is the only thing in the row asking to be looked at.
        /// </summary>
        private static readonly Color BoxFill = new(0.145f, 0.161f, 0.153f, 0.94f);

        /// <summary>
        /// The chosen option is see-through — the room shows through it, which is
        /// what makes the gold ring read as lit rather than as another border.
        /// </summary>
        private static readonly Color BoxFillOn = new(0.055f, 0.129f, 0.098f, 0.35f);

        /// <summary>Head icons on an unchosen option — the yard's green.</summary>
        private static readonly Color HeadIdle = new(0.357f, 0.816f, 0.478f);
        private static readonly Color BoardInk = new(0.965f, 0.914f, 0.784f);
        private static readonly Color BoardValue = new(1f, 0.855f, 0.541f);

        private static Sprite? _glow;
        private static Sprite? _boxFill;

        /// <summary>The lit ring, built once and shared by every choice box.</summary>
        private static Sprite Glow() => _glow ??= GradientSprite.RoundedGlow(UiKit.BrassLit);

        private static Sprite Box() =>
            _boxFill ??= GradientSprite.RoundedDiagonal(0.14f, Color.white, Color.white);

        // ---- The hero -------------------------------------------------------

        /// <summary>
        /// A room screen: the title art across the top with the back ring
        /// floating over it, a brass rule, and the body below.
        ///
        /// There is no text header. The logo is the header — which is why the
        /// back control is a ring rather than a bar: it has to hold its own
        /// silhouette over painted art.
        /// </summary>
        /// <param name="root">The screen root to fill.</param>
        /// <param name="fallbackTitle">Drawn as text until the title art arrives.</param>
        /// <param name="onBack">Invoked by the back ring.</param>
        /// <returns>The hero image to feed art to, and the body stack to fill.</returns>
        public static (Image hero, RectTransform body) Screen(
            RectTransform root, string fallbackTitle, Action onBack)
        {
            VerticalLayoutGroup column = root.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(0, 0, 18, 0);
            column.spacing = 14f;
            column.childAlignment = TextAnchor.UpperCenter;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;

            GameObject heroGo = UiKit.Child(root, "Hero");
            LayoutElement heroLe = heroGo.AddComponent<LayoutElement>();
            heroLe.preferredHeight = HeroFallbackHeight;
            heroLe.minHeight = HeroFallbackHeight;

            // The drawn title, shown until art is supplied and hidden after.
            TextMeshProUGUI drawn = UiKit.Label(
                heroGo.transform, fallbackTitle, 46f, UiKit.Bone, TextAlignmentOptions.Center);
            drawn.fontStyle = FontStyles.Bold;
            UiKit.Stretch((RectTransform)drawn.transform, left: 120f, right: 120f);

            GameObject artGo = UiKit.Child(heroGo.transform, "Art");
            UiKit.Stretch((RectTransform)artGo.transform);
            Image hero = artGo.AddComponent<Image>();
            hero.preserveAspect = true;
            hero.raycastTarget = false;
            hero.enabled = false;

            // Over the art, not beside it — the title owns the full width.
            GameObject ring = UiKit.BackRing((RectTransform)heroGo.transform, onBack);
            RectTransform rrt = (RectTransform)ring.transform;
            rrt.anchorMin = rrt.anchorMax = new Vector2(0f, 1f);
            rrt.pivot = new Vector2(0f, 1f);
            rrt.anchoredPosition = new Vector2(24f, -6f);

            GameObject rule = UiKit.Child(root, "Rule");
            LayoutElement ruleLe = rule.AddComponent<LayoutElement>();
            ruleLe.preferredHeight = 2f;
            ruleLe.minHeight = 2f;
            Image ruleImg = rule.AddComponent<Image>();
            ruleImg.color = new Color(UiKit.Brass.r, UiKit.Brass.g, UiKit.Brass.b, 0.65f);
            ruleImg.raycastTarget = false;

            GameObject body = UiKit.Child(root, "Body");
            LayoutElement bodyLe = body.AddComponent<LayoutElement>();
            bodyLe.flexibleHeight = 1f;
            VerticalLayoutGroup stack = body.AddComponent<VerticalLayoutGroup>();
            stack.padding = new RectOffset(30, 30, 14, 16);
            stack.spacing = UiKit.CardGap;
            stack.childAlignment = TextAnchor.UpperCenter;
            stack.childControlWidth = true;
            stack.childControlHeight = true;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = false;

            return (hero, (RectTransform)body.transform);
        }

        /// <summary>
        /// Hands a hero its art, sizing the band from the art's own proportions
        /// so a wide banner does not reserve the height of a stacked block.
        /// Safe to call with null — the drawn title stays.
        /// </summary>
        /// <param name="hero">The hero image returned by <see cref="Screen"/>.</param>
        /// <param name="art">The title art, or null to keep the drawn title.</param>
        /// <param name="screenWidth">Width the room is laid out at.</param>
        public static void SetHeroArt(Image? hero, Sprite? art, float screenWidth)
        {
            if (hero == null)
            {
                return;
            }

            hero.sprite = art;
            hero.enabled = art != null;

            Transform heroRoot = hero.transform.parent;
            TextMeshProUGUI? drawn = heroRoot.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
            if (drawn != null)
            {
                drawn.enabled = art == null;
            }

            LayoutElement? le = heroRoot.GetComponent<LayoutElement>();
            if (le == null)
            {
                return;
            }

            if (art == null)
            {
                le.preferredHeight = HeroFallbackHeight;
                le.minHeight = HeroFallbackHeight;
                return;
            }

            float aspect = art.rect.width / art.rect.height;
            float width = screenWidth * (aspect >= BannerAspect ? HeroWidthWide : HeroWidth);
            float height = width / aspect;
            le.preferredHeight = height;
            le.minHeight = height;
        }

        // ---- Sections -------------------------------------------------------

        /// <summary>
        /// A section heading, centred over what it introduces. Sections are no
        /// longer boxed: a card border around two picture tiles fenced them off
        /// from the room they belong to, so the heading now does the whole job of
        /// separating one choice from the next.
        /// </summary>
        /// <param name="parent">The body stack.</param>
        /// <param name="text">The heading, set in caps.</param>
        public static void Caption(RectTransform parent, string text)
        {
            TextMeshProUGUI t = UiKit.Label(
                parent, text.ToUpperInvariant(), 24f, UiKit.Bone, TextAlignmentOptions.Center);
            t.fontStyle = FontStyles.Bold;
            t.characterSpacing = 8f;
            t.raycastTarget = false;
            LayoutElement le = t.GetComponent<LayoutElement>();
            le.preferredHeight = 40f;
            le.minHeight = 40f;
        }

        /// <summary>
        /// A transparent vertical group inside the body stack — used where a
        /// whole run of sections has to appear and disappear together, as when a
        /// friends room switches between Cut-Throat and Partner.
        /// </summary>
        /// <param name="parent">The body stack.</param>
        /// <returns>The container, laid out like the body it sits in.</returns>
        public static RectTransform Section(RectTransform parent)
        {
            GameObject go = UiKit.Child(parent, "Section");
            VerticalLayoutGroup v = go.AddComponent<VerticalLayoutGroup>();
            v.spacing = UiKit.CardGap;
            v.childAlignment = TextAnchor.UpperCenter;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return (RectTransform)go.transform;
        }

        // ---- Choice rows ----------------------------------------------------

        /// <summary>
        /// A row of choices, evenly divided. Used for format tiles, table sizes
        /// and room types alike, so every choice in a room behaves the same way.
        /// </summary>
        /// <param name="parent">The body stack or a section.</param>
        /// <param name="spacing">Gap between the options.</param>
        /// <returns>The row, to parent options into.</returns>
        public static RectTransform ChoiceRow(RectTransform parent, float spacing = 18f)
        {
            GameObject row = UiKit.Child(parent, "Choices");
            HorizontalLayoutGroup h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = false;
            row.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return (RectTransform)row.transform;
        }

        /// <summary>
        /// The shell every choice shares: a rounded box, and a lit ring that only
        /// the chosen one wears. The ring is the signal — a shape, not just a
        /// colour, and nothing else is added on top of it.
        /// </summary>
        /// <param name="row">The row from <see cref="ChoiceRow"/>.</param>
        /// <param name="name">Name for the object, for debugging.</param>
        /// <param name="onClick">Invoked when this option is chosen.</param>
        /// <returns>The box, ready to be filled and passed to <see cref="SetChosen"/>.</returns>
        private static GameObject ChoiceBox(RectTransform row, string name, Action onClick)
        {
            GameObject go = UiKit.Child(row, name);

            Image bg = go.AddComponent<Image>();
            bg.sprite = Box();
            bg.type = Image.Type.Sliced;
            bg.color = BoxFill;

            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => onClick());

            // Traces the box itself, just outside it. It must sit OUT of the
            // layout: the box lays its content out in a vertical group, and a
            // ring left in that flow is treated as one more row and stretched
            // into a solid block.
            GameObject glow = UiKit.Child(go.transform, "Glow");
            glow.AddComponent<LayoutElement>().ignoreLayout = true;
            RectTransform grt = (RectTransform)glow.transform;
            grt.anchorMin = Vector2.zero;
            grt.anchorMax = Vector2.one;
            grt.offsetMin = new Vector2(-10f, -10f);
            grt.offsetMax = new Vector2(10f, 10f);
            Image glowImg = glow.AddComponent<Image>();
            glowImg.sprite = Glow();
            glowImg.type = Image.Type.Sliced;
            glowImg.color = Color.white;
            glowImg.raycastTarget = false;
            glow.SetActive(false);

            return go;
        }

        // ---- Format tiles ---------------------------------------------------

        /// <summary>
        /// One picture choice: art in a fixed box, its name, and one line saying
        /// what picking it means. The name stays even once art lands — a player
        /// who has not learned the pictures yet still has to be able to pick.
        /// </summary>
        /// <param name="row">The row from <see cref="ChoiceRow"/>.</param>
        /// <param name="label">The format's name.</param>
        /// <param name="blurb">One line on what this format plays like.</param>
        /// <param name="onClick">Invoked when the tile is chosen.</param>
        /// <returns>The tile, for <see cref="SetChosen"/> and art assignment.</returns>
        public static GameObject Tile(RectTransform row, string label, string blurb, Action onClick)
        {
            GameObject go = ChoiceBox(row, $"Tile_{label}", onClick);

            VerticalLayoutGroup v = go.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(16, 16, 18, 14);
            v.spacing = 4f;
            v.childAlignment = TextAnchor.MiddleCenter;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            GameObject artGo = UiKit.Child(go.transform, "Art");
            LayoutElement artLe = artGo.AddComponent<LayoutElement>();
            artLe.preferredHeight = 120f;
            Image art = artGo.AddComponent<Image>();
            art.preserveAspect = true;
            art.raycastTarget = false;
            art.color = Color.white;
            art.enabled = false;

            TextMeshProUGUI name = UiKit.Label(
                go.transform, label, 26f, UiKit.Bone, TextAlignmentOptions.Center);
            name.fontStyle = FontStyles.Bold;
            name.raycastTarget = false;

            TextMeshProUGUI sub = UiKit.Label(
                go.transform, blurb, 19f, UiKit.Muted, TextAlignmentOptions.Center);
            sub.raycastTarget = false;
            sub.name = "Blurb";
            return go;
        }

        /// <summary>
        /// Gives a tile its picture, and sizes the art box from the row width so
        /// both tiles agree regardless of what shape the two pictures are.
        /// </summary>
        /// <param name="tile">A tile from <see cref="Tile"/>.</param>
        /// <param name="art">The picture, or null to leave the tile lettered.</param>
        /// <param name="rowWidth">Width available to the whole row.</param>
        public static void SetTileArt(GameObject? tile, Sprite? art, float rowWidth)
        {
            if (tile == null)
            {
                return;
            }

            Transform artGo = tile.transform.Find("Art");
            if (artGo == null)
            {
                return;
            }

            Image img = artGo.GetComponent<Image>();
            img.sprite = art;
            img.enabled = art != null;

            // Two tiles side by side, each with its own padding and the gap
            // between them.
            float tileWidth = (rowWidth - 18f) / 2f;
            artGo.GetComponent<LayoutElement>().preferredHeight = (tileWidth - 32f) / TileArtAspect;
        }

        // ---- Seat choice ----------------------------------------------------

        /// <summary>
        /// One table size: that many heads, and what the table is called. Heads
        /// rather than a digit, because the difference between two people and
        /// four is the actual substance of the choice and is counted faster than
        /// it is read.
        /// </summary>
        /// <param name="row">The row from <see cref="ChoiceRow"/>.</param>
        /// <param name="seats">How many heads to draw.</param>
        /// <param name="caption">What players call a table this size.</param>
        /// <param name="onClick">Invoked when this size is chosen.</param>
        /// <returns>The option, for <see cref="SetChosen"/>.</returns>
        public static GameObject SeatOption(RectTransform row, int seats, string caption, Action onClick)
        {
            GameObject go = ChoiceBox(row, $"Seats_{seats}", onClick);
            go.AddComponent<LayoutElement>().preferredHeight = 128f;

            VerticalLayoutGroup v = go.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(6, 6, 16, 12);
            v.spacing = 4f;
            v.childAlignment = TextAnchor.MiddleCenter;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            GameObject heads = UiKit.Child(go.transform, "Heads");
            heads.AddComponent<LayoutElement>().preferredHeight = 46f;
            HorizontalLayoutGroup hh = heads.AddComponent<HorizontalLayoutGroup>();
            hh.spacing = 3f;
            hh.childAlignment = TextAnchor.MiddleCenter;
            hh.childControlWidth = true;
            hh.childControlHeight = true;
            hh.childForceExpandWidth = false;
            hh.childForceExpandHeight = false;

            Sprite person = IconFactory.Person();
            for (int i = 0; i < seats; i++)
            {
                GameObject head = UiKit.Child(heads.transform, "Head");
                LayoutElement hle = head.AddComponent<LayoutElement>();
                hle.preferredWidth = 42f;
                hle.preferredHeight = 42f;
                Image img = head.AddComponent<Image>();
                img.sprite = person;
                img.color = HeadIdle;
                img.preserveAspect = true;
                img.raycastTarget = false;
            }

            TextMeshProUGUI cap = UiKit.Label(
                go.transform, caption, 21f, UiKit.Muted, TextAlignmentOptions.Center);
            cap.fontStyle = FontStyles.Bold;
            cap.raycastTarget = false;
            return go;
        }

        /// <summary>
        /// A plain worded choice — the kind with nothing to picture, like whether
        /// a friends room plays Cut-Throat or Partner.
        /// </summary>
        /// <param name="row">The row from <see cref="ChoiceRow"/>.</param>
        /// <param name="label">The choice.</param>
        /// <param name="onClick">Invoked when it is chosen.</param>
        /// <returns>The option, for <see cref="SetChosen"/>.</returns>
        public static GameObject WordOption(RectTransform row, string label, Action onClick)
        {
            GameObject go = ChoiceBox(row, $"Opt_{label}", onClick);
            go.AddComponent<LayoutElement>().preferredHeight = 88f;

            TextMeshProUGUI t = UiKit.Label(
                go.transform, label, 26f, UiKit.Muted, TextAlignmentOptions.Center);
            t.fontStyle = FontStyles.Bold;
            UiKit.Stretch((RectTransform)t.transform, left: 14f, right: 14f);
            t.raycastTarget = false;
            return go;
        }

        // ---- Chosen state ---------------------------------------------------

        /// <summary>
        /// Lights an option as the chosen one: the box goes see-through so the
        /// room shows through it, a gold ring blooms around it, and its heads and
        /// lettering warm up. The unchosen keep a flat grey.
        /// </summary>
        /// <param name="option">A tile, seat option or worded option.</param>
        /// <param name="chosen">Whether this is the current choice.</param>
        public static void SetChosen(GameObject? option, bool chosen)
        {
            if (option == null)
            {
                return;
            }

            Image? bg = option.GetComponent<Image>();
            if (bg != null)
            {
                bg.color = chosen ? BoxFillOn : BoxFill;
            }

            Transform? glow = option.transform.Find("Glow");
            if (glow != null)
            {
                glow.gameObject.SetActive(chosen);
            }

            Transform? heads = option.transform.Find("Heads");
            if (heads != null)
            {
                foreach (Transform head in heads)
                {
                    Image? hi = head.GetComponent<Image>();
                    if (hi != null)
                    {
                        hi.color = chosen ? UiKit.BrassLit : HeadIdle;
                    }
                }
            }

            foreach (TextMeshProUGUI label in option.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                bool blurb = label.gameObject.name == "Blurb";
                label.color = chosen
                    ? (blurb ? UiKit.BrassLit : UiKit.Bone)
                    : (blurb ? UiKit.Muted : new Color(UiKit.Muted.r, UiKit.Muted.g, UiKit.Muted.b, 0.92f));
            }
        }

        // ---- The rewards board ----------------------------------------------

        /// <summary>
        /// The carved board the stake is written on. The supplied art is a frame
        /// rather than a picture — its plank is deliberately empty — so the
        /// numbers are set inside it, clear of the painted flowers and the
        /// treasure chest. Without art it falls back to a plain carved panel and
        /// the numbers use honest padding instead.
        /// </summary>
        /// <param name="parent">The body stack.</param>
        /// <param name="caption">Small heading burnt into the plank.</param>
        /// <returns>The board image to feed art to, and the region rows go in.</returns>
        public static (Image board, RectTransform rows) Board(RectTransform parent, string caption)
        {
            GameObject go = UiKit.Child(parent, "RewardsBoard");
            go.AddComponent<LayoutElement>().preferredHeight = 260f;

            Image board = go.AddComponent<Image>();
            board.sprite = GradientSprite.RoundedDiagonal(
                0.10f, Hex("#5A3A20"), Hex("#3E2716"), Hex("#2A1A0F"));
            board.type = Image.Type.Sliced;
            board.color = Color.white;
            board.raycastTarget = false;

            TextMeshProUGUI cap = UiKit.Label(
                go.transform, caption.ToUpperInvariant(), 21f, BoardValue, TextAlignmentOptions.Center);
            cap.fontStyle = FontStyles.Bold;
            cap.characterSpacing = 8f;
            cap.raycastTarget = false;
            RectTransform crt = (RectTransform)cap.transform;
            crt.anchorMin = new Vector2(FallbackInset, 1f - PlankCaptionTop - 0.09f);
            crt.anchorMax = new Vector2(1f - FallbackInset, 1f - PlankCaptionTop);
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;

            GameObject rowsGo = UiKit.Child(go.transform, "Rows");
            RectTransform rows = (RectTransform)rowsGo.transform;
            rows.anchorMin = new Vector2(FallbackInset, FallbackVInset);
            rows.anchorMax = new Vector2(1f - FallbackInset, 1f - FallbackVInset - 0.06f);
            rows.offsetMin = Vector2.zero;
            rows.offsetMax = Vector2.zero;

            VerticalLayoutGroup v = rowsGo.AddComponent<VerticalLayoutGroup>();
            v.spacing = 4f;
            v.childAlignment = TextAnchor.MiddleCenter;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            // Not force-expanded: the headline is meant to be bigger than the
            // lines under it, and an even split would flatten all three.
            v.childForceExpandHeight = false;

            return (board, rows);
        }

        /// <summary>
        /// Hands the board its art and moves the numbers onto the plank, where
        /// nothing painted collides with them. Safe to call with null — the
        /// drawn panel and its padded numbers stay.
        /// </summary>
        /// <param name="board">The board image from <see cref="Board"/>.</param>
        /// <param name="art">The carved-board art, trimmed to its own bounds.</param>
        /// <param name="width">Width the board is laid out at.</param>
        public static void SetBoardArt(Image? board, Sprite? art, float width)
        {
            if (board == null)
            {
                return;
            }

            bool painted = art != null;
            if (painted)
            {
                board.sprite = art;
                board.type = Image.Type.Simple;
                board.preserveAspect = true;

                float aspect = art!.rect.width / art.rect.height;
                LayoutElement? le = board.GetComponent<LayoutElement>();
                if (le != null)
                {
                    le.preferredHeight = width / aspect;
                }
            }

            RectTransform? rows = (RectTransform?)board.transform.Find("Rows");
            if (rows != null)
            {
                rows.anchorMin = painted
                    ? new Vector2(PlankLeft, PlankBottom)
                    : new Vector2(FallbackInset, FallbackVInset);
                rows.anchorMax = painted
                    ? new Vector2(1f - PlankRight, 1f - PlankTop)
                    : new Vector2(1f - FallbackInset, 1f - FallbackVInset - 0.06f);
                rows.offsetMin = Vector2.zero;
                rows.offsetMax = Vector2.zero;
            }

            Transform? cap = board.transform.Find("Label");
            if (cap != null)
            {
                RectTransform crt = (RectTransform)cap;
                float left = painted ? PlankLeft : FallbackInset;
                float right = painted ? PlankRight : FallbackInset;
                crt.anchorMin = new Vector2(left, 1f - PlankCaptionTop - 0.09f);
                crt.anchorMax = new Vector2(1f - right, 1f - PlankCaptionTop);
                crt.offsetMin = Vector2.zero;
                crt.offsetMax = Vector2.zero;
            }
        }

        /// <summary>
        /// What the board is announcing, set big. A carved sign is a poster, not
        /// a receipt — a column of small label/value pairs reads as a form and
        /// wastes the one piece of art on the screen built to carry a statement.
        /// </summary>
        /// <param name="rows">The text region from <see cref="Board"/>.</param>
        /// <param name="font">Display face, if one has been supplied.</param>
        /// <returns>The headline, to restate when the choice changes.</returns>
        public static TextMeshProUGUI BoardHeadline(RectTransform rows, TMP_FontAsset? font)
        {
            TextMeshProUGUI t = UiKit.Label(
                rows, string.Empty, 46f, Color.white, TextAlignmentOptions.Center);
            t.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
            t.characterSpacing = 2f;
            t.enableAutoSizing = true;
            t.fontSizeMin = 28f;
            t.fontSizeMax = 50f;
            t.raycastTarget = false;
            if (font != null)
            {
                t.font = font;
            }

            // Carved letters catch a shadow; without one the white sits on the
            // wood rather than in it.
            Shadow cut = t.gameObject.AddComponent<Shadow>();
            cut.effectColor = new Color(0f, 0f, 0f, 0.75f);
            cut.effectDistance = new Vector2(0f, -3f);
            t.GetComponent<LayoutElement>().preferredHeight = 58f;
            return t;
        }

        /// <summary>
        /// The line under the headline, where the actual numbers live. Rich text,
        /// so the amounts can be lit gold inside an otherwise plain sentence.
        /// </summary>
        /// <param name="rows">The text region from <see cref="Board"/>.</param>
        /// <returns>The line, to restate when the choice changes.</returns>
        public static TextMeshProUGUI BoardLine(RectTransform rows)
        {
            TextMeshProUGUI t = UiKit.Label(
                rows, string.Empty, 25f, BoardInk, TextAlignmentOptions.Center);
            t.richText = true;
            t.raycastTarget = false;
            Shadow cut = t.gameObject.AddComponent<Shadow>();
            cut.effectColor = new Color(0f, 0f, 0f, 0.8f);
            cut.effectDistance = new Vector2(0f, -2f);
            t.GetComponent<LayoutElement>().preferredHeight = 34f;
            return t;
        }

        /// <summary>Wraps an amount in the board's gold, for use inside a line.</summary>
        /// <param name="amount">Already-formatted coin amount.</param>
        /// <returns>Rich-text markup lighting the amount.</returns>
        public static string Lit(string amount) =>
            $"<b><color=#{ColorUtility.ToHtmlStringRGB(BoardValue)}>{amount}</color></b>";

        // ---- Shared -----------------------------------------------------------

        /// <summary>Applies the chosen look across a set of options.</summary>
        /// <typeparam name="T">What each option stands for.</typeparam>
        /// <param name="options">Every option in the row.</param>
        /// <param name="chosen">The value currently chosen.</param>
        public static void Refresh<T>(IReadOnlyList<(GameObject go, T value)> options, T chosen)
        {
            foreach ((GameObject go, T value) in options)
            {
                SetChosen(go, EqualityComparer<T>.Default.Equals(value, chosen));
            }
        }

        private static Color Hex(string hex) =>
            ColorUtility.TryParseHtmlString(hex, out Color c) ? c : Color.magenta;
    }
}
