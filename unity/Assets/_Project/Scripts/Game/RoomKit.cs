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
        private const float PlankLeft = 0.23f;
        private const float PlankRight = 0.29f;
        private const float PlankTop = 0.28f;
        private const float PlankBottom = 0.17f;
        private const float PlankCaptionTop = 0.10f;

        // The drawn stand-in has no painted furniture to avoid, so its numbers
        // use honest padding instead.
        private const float FallbackInset = 0.07f;
        private const float FallbackVInset = 0.16f;

        private static readonly Color TileFill = new(0.078f, 0.196f, 0.165f, 0.72f);
        private static readonly Color TileFillOn = new(0.792f, 0.541f, 0.016f, 0.16f);
        private static readonly Color BoardInk = new(0.965f, 0.914f, 0.784f);
        private static readonly Color BoardValue = new(1f, 0.855f, 0.541f);

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

        // ---- Format tiles ---------------------------------------------------

        /// <summary>
        /// A row of picture choices — the format, chosen by looking rather than
        /// reading. Tiles share one art box so a wide banner and a squarer pile
        /// sit at the same size without either being stretched.
        /// </summary>
        /// <param name="card">The card to add the row to.</param>
        /// <returns>The row, to parent tiles into.</returns>
        public static RectTransform TileRow(RectTransform card)
        {
            GameObject row = UiKit.Child(card, "Tiles");
            HorizontalLayoutGroup h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 16f;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = false;
            row.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return (RectTransform)row.transform;
        }

        /// <summary>
        /// One picture choice: art in a fixed box with its name beneath. The
        /// name stays even once art lands — a player who has not learned the
        /// pictures yet still has to be able to pick.
        /// </summary>
        /// <param name="row">The row from <see cref="TileRow"/>.</param>
        /// <param name="label">The format's name.</param>
        /// <param name="onClick">Invoked when the tile is chosen.</param>
        /// <returns>The tile, for <see cref="SetChosen"/> and art assignment.</returns>
        public static GameObject Tile(RectTransform row, string label, Action onClick)
        {
            GameObject go = UiKit.Child(row, $"Tile_{label}");
            Image bg = go.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.12f, Color.white, Color.white);
            bg.type = Image.Type.Sliced;
            bg.color = TileFill;

            Outline edge = go.AddComponent<Outline>();
            edge.effectColor = new Color(UiKit.Lamplight.r, UiKit.Lamplight.g, UiKit.Lamplight.b, 0.2f);
            edge.effectDistance = new Vector2(1.5f, -1.5f);

            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => onClick());

            VerticalLayoutGroup v = go.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(12, 12, 12, 10);
            v.spacing = 6f;
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
                go.transform, label, 24f, UiKit.Lamplight, TextAlignmentOptions.Center);
            name.fontStyle = FontStyles.Bold;
            name.raycastTarget = false;
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
            float tileWidth = (rowWidth - 16f) / 2f;
            artGo.GetComponent<LayoutElement>().preferredHeight = (tileWidth - 24f) / TileArtAspect;
        }

        // ---- Seat choice ----------------------------------------------------

        /// <summary>
        /// How many people are at the table, counted in heads. A digit has to be
        /// read; heads are counted at a glance, and the difference between two
        /// and four people is the point of the choice.
        /// </summary>
        /// <param name="card">The card to add the row to.</param>
        /// <returns>The row, to parent seat options into.</returns>
        public static RectTransform SeatRow(RectTransform card)
        {
            GameObject row = UiKit.Child(card, "Seats");
            HorizontalLayoutGroup h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 14f;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = false;
            row.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return (RectTransform)row.transform;
        }

        /// <summary>
        /// One table size: that many heads, and what the table is called.
        /// </summary>
        /// <param name="row">The row from <see cref="SeatRow"/>.</param>
        /// <param name="seats">How many heads to draw.</param>
        /// <param name="caption">What players call a table this size.</param>
        /// <param name="onClick">Invoked when this size is chosen.</param>
        /// <returns>The option, for <see cref="SetChosen"/>.</returns>
        public static GameObject SeatOption(RectTransform row, int seats, string caption, Action onClick)
        {
            GameObject go = UiKit.Child(row, $"Seats_{seats}");
            go.AddComponent<LayoutElement>().preferredHeight = 116f;

            Image bg = go.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.18f, Color.white, Color.white);
            bg.type = Image.Type.Sliced;
            bg.color = TileFill;

            Outline edge = go.AddComponent<Outline>();
            edge.effectColor = new Color(UiKit.Lamplight.r, UiKit.Lamplight.g, UiKit.Lamplight.b, 0.2f);
            edge.effectDistance = new Vector2(1.5f, -1.5f);

            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => onClick());

            VerticalLayoutGroup v = go.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(6, 6, 12, 8);
            v.spacing = 2f;
            v.childAlignment = TextAnchor.MiddleCenter;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            GameObject heads = UiKit.Child(go.transform, "Heads");
            heads.AddComponent<LayoutElement>().preferredHeight = 44f;
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
                hle.preferredWidth = 40f;
                hle.preferredHeight = 40f;
                Image img = head.AddComponent<Image>();
                img.sprite = person;
                img.color = UiKit.Muted;
                img.preserveAspect = true;
                img.raycastTarget = false;
            }

            TextMeshProUGUI cap = UiKit.Label(
                go.transform, caption, 20f, UiKit.Muted, TextAlignmentOptions.Center);
            cap.raycastTarget = false;
            return go;
        }

        // ---- Chosen state ---------------------------------------------------

        /// <summary>
        /// Lights a tile or seat option as the chosen one — brass edge, warm
        /// ground, and the heads and lettering brought up with it. Colour is
        /// never the only signal: the chosen option also carries the only solid
        /// brass edge in the row.
        /// </summary>
        /// <param name="option">A tile or seat option.</param>
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
                bg.color = chosen ? TileFillOn : TileFill;
            }

            Outline? edge = option.GetComponent<Outline>();
            if (edge != null)
            {
                edge.effectColor = chosen
                    ? UiKit.Brass
                    : new Color(UiKit.Lamplight.r, UiKit.Lamplight.g, UiKit.Lamplight.b, 0.2f);
                edge.effectDistance = chosen ? new Vector2(2.5f, -2.5f) : new Vector2(1.5f, -1.5f);
            }

            Color ink = chosen ? UiKit.BrassLit : UiKit.Muted;
            foreach (Transform child in option.transform)
            {
                if (child.name == "Heads")
                {
                    foreach (Transform head in child)
                    {
                        Image? hi = head.GetComponent<Image>();
                        if (hi != null)
                        {
                            hi.color = ink;
                        }
                    }
                }
            }

            foreach (TextMeshProUGUI label in option.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                label.color = chosen ? UiKit.BrassLit : UiKit.Lamplight;
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
                go.transform, caption.ToUpperInvariant(), 20f, BoardValue, TextAlignmentOptions.MidlineLeft);
            cap.fontStyle = FontStyles.Bold;
            cap.characterSpacing = 8f;
            cap.raycastTarget = false;
            RectTransform crt = (RectTransform)cap.transform;
            crt.anchorMin = new Vector2(FallbackInset, 1f - PlankCaptionTop - 0.08f);
            crt.anchorMax = new Vector2(0.7f, 1f - PlankCaptionTop);
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;

            GameObject rowsGo = UiKit.Child(go.transform, "Rows");
            RectTransform rows = (RectTransform)rowsGo.transform;
            rows.anchorMin = new Vector2(FallbackInset, FallbackVInset);
            rows.anchorMax = new Vector2(1f - FallbackInset, 1f - FallbackVInset - 0.06f);
            rows.offsetMin = Vector2.zero;
            rows.offsetMax = Vector2.zero;

            VerticalLayoutGroup v = rowsGo.AddComponent<VerticalLayoutGroup>();
            v.spacing = 2f;
            v.childAlignment = TextAnchor.MiddleCenter;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = true;

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
                crt.anchorMin = new Vector2(left, 1f - PlankCaptionTop - 0.08f);
                crt.anchorMax = new Vector2(left + 0.45f, 1f - PlankCaptionTop);
                crt.offsetMin = Vector2.zero;
                crt.offsetMax = Vector2.zero;
            }
        }

        /// <summary>
        /// One line of the stake: what it is, and how much. Carved into the
        /// plank, so the ink is warm rather than the cool bone used on cards.
        /// </summary>
        /// <param name="rows">The rows region from <see cref="Board"/>.</param>
        /// <param name="label">What the number means.</param>
        /// <returns>The label and value, so both can be restated when the choice changes.</returns>
        public static (TextMeshProUGUI label, TextMeshProUGUI value) BoardRow(
            RectTransform rows, string label)
        {
            GameObject row = UiKit.Child(rows, "Row");
            HorizontalLayoutGroup h = row.AddComponent<HorizontalLayoutGroup>();
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;

            TextMeshProUGUI l = UiKit.Label(row.transform, label, 24f, BoardInk, TextAlignmentOptions.MidlineLeft);
            l.GetComponent<LayoutElement>().flexibleWidth = 1f;
            l.raycastTarget = false;

            TextMeshProUGUI v = UiKit.Label(row.transform, "—", 24f, BoardValue, TextAlignmentOptions.MidlineRight);
            v.fontStyle = FontStyles.Bold;
            v.raycastTarget = false;
            return (l, v);
        }

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
