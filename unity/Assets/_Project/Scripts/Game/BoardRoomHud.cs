#nullable enable
using System;
using System.Collections.Generic;
using Pose.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Pose.Game
{
    /// <summary>
    /// The cinematic in-match "board room" chrome that overlays the felt board:
    /// a gold scoreboard (top-left, collapsible), ringed seat avatars around the
    /// table, the local player's profile + chat button in the bottom-right, a
    /// toggle-open chat/voice panel, and a bottom action bar (last play · Pass
    /// Turn · turn tag). Purely presentational — it never touches game state; a
    /// host (see <see cref="BoardBootstrap"/>) builds it, wires
    /// <see cref="PassClicked"/> / <see cref="HomeClicked"/>, and feeds it live
    /// data through the setters. Built entirely in code (no prefab), consistent
    /// with the rest of the UI. Design mockup: the "Pose Board Room" artifact.
    /// </summary>
    public sealed class BoardRoomHud : MonoBehaviour
    {
        /// <summary>Per-seat accent colours, indexed by player seat (0..3).</summary>
        public static readonly Color[] SeatColors =
        {
            new(0.216f, 0.784f, 0.443f), // green
            new(0.290f, 0.639f, 1.000f), // blue
            new(0.961f, 0.651f, 0.137f), // orange
            new(0.608f, 0.424f, 0.965f), // purple
        };

        public event Action? PassClicked;
        public event Action? HomeClicked;
        public event Action? SettingsClicked;

        /// <summary>
        /// Height of the bottom action bar (Last Play · Pass · turn tag).
        /// Public because anything docking above the bar needs to clear it —
        /// see <see cref="TurnTimerView"/> — and two copies of this number
        /// would drift the first time the bar is resized.
        /// </summary>
        public const float ActionBarHeight = 110f;

        /// <summary>
        /// Left inset shared by the Last Play block and anything stacked above
        /// it, so the bottom-left column reads as one edge.
        /// </summary>
        public const float ActionBarLeftInset = 24f;

        // Opponent profile sizes. The top seat shares a band with its hand and
        // the side seats sit in 150-wide columns, so neither can carry the old
        // 110 without pushing into the tiles.
        private const float TopSeatAvatarSize = 108f;
        private const float SideSeatAvatarSize = 100f;

        // Collapsed scoreboard: the title bar and nothing else.
        private const float CrownHeight = 46f;

        // The Last Play badge. Small, and its shadow scales down to suit —
        // full depth on a tile this size reads as a smudge.
        private const float LastPlayTileShort = 48f;
        private const float LastPlayTileLong = 96f;
        private const float LastPlayDepthScale = 0.5f;

        // ---- palette ------------------------------------------------------
        private static readonly Color Panel = new(0.075f, 0.059f, 0.047f, 0.92f);
        private static readonly Color Gold = new(0.961f, 0.769f, 0.318f);
        private static readonly Color TextCol = new(0.957f, 0.929f, 0.882f);
        private static readonly Color Muted = new(0.702f, 0.643f, 0.533f);
        private static readonly Color Faint = new(0.490f, 0.443f, 0.361f);
        private static readonly Color ActionPurple = new(0.486f, 0.227f, 0.929f);
        private static readonly Color ActionDim = new(0.227f, 0.200f, 0.251f);
        private static readonly Color Online = new(0.247f, 0.733f, 0.349f);

        // ---- live widget refs ---------------------------------------------
        private sealed class SeatWidgets
        {
            public GameObject Root = null!;
            public Image Ring = null!;
            public Image Fill = null!;
            public TextMeshProUGUI Initials = null!;
            public TextMeshProUGUI Name = null!;
            public TextMeshProUGUI Score = null!;
            public Image ScorePill = null!;
            public GameObject TurnGlow = null!;
        }

        private readonly Dictionary<SeatPosition, SeatWidgets> _seats = new();
        private SeatWidgets _local = null!;

        private readonly List<(GameObject row, Image dot, TextMeshProUGUI name, TextMeshProUGUI games, TextMeshProUGUI pts)> _scoreRows = new();
        private TextMeshProUGUI _scoreSubtitle = null!;
        private TextMeshProUGUI _roundValue = null!;
        private TextMeshProUGUI _onBoardValue = null!;
        private GameObject _scorePanel = null!;
        private ContentSizeFitter _scoreFitter = null!;
        private GameObject _scoreBody = null!;
        private TextMeshProUGUI _scoreChev = null!;
        private bool _scoreCollapsed;

        private GameObject _chatPanel = null!;
        private TextMeshProUGUI _coinLabel = null!;

        private Image _passBg = null!;
        private TextMeshProUGUI _turnTag = null!;
        private TileView _lastPlayTile = null!;

        /// <summary>Builds the whole HUD. Call once after AddComponent.</summary>
        public void Init()
        {
            StretchFull((RectTransform)transform);
            BuildTopBar();
            BuildScoreboard();
            // Profiles sit BESIDE their hands, never over them.
            //
            // Top: to the right of its hand rather than above it. Stacked, the
            // profile and hand needed two bands; side by side they need one,
            // which is where the chain area's extra height comes from.
            //
            // Sides: docked at the top of each column, above the tile stack.
            // Anchored to the top edge rather than to mid-height so the
            // clearance holds on short screens.
            BuildSeat(SeatPosition.Top, new Vector2(1f, 1f), new Vector2(-94f, -270f), TopSeatAvatarSize);
            BuildSeat(SeatPosition.Left, new Vector2(0f, 1f), new Vector2(74f, -540f), SideSeatAvatarSize);
            BuildSeat(SeatPosition.Right, new Vector2(1f, 1f), new Vector2(-74f, -540f), SideSeatAvatarSize);
            BuildChatPanel();
            BuildCorner();
            BuildActionBar();
        }

        // ---- setters (fed by the host) ------------------------------------

        public void SetCoins(string text) => _coinLabel.text = text;

        public void SetScoreHeader(string subtitle, int round, int onBoard)
        {
            _scoreSubtitle.text = subtitle;
            _roundValue.text = round.ToString();
            _onBoardValue.text = onBoard.ToString();
        }

        /// <summary>Updates one scoreboard row (0..3); inactive rows are hidden.</summary>
        public void SetScoreRow(int index, bool active, string name, Color color, int games, int points)
        {
            if (index < 0 || index >= _scoreRows.Count)
            {
                return;
            }
            var r = _scoreRows[index];
            r.row.SetActive(active);
            if (!active)
            {
                return;
            }
            r.dot.color = color;
            r.name.text = Trim(name, 11);
            r.games.text = games.ToString();
            r.pts.text = points.ToString("N0");
        }

        /// <summary>Updates a seat's avatar. Bottom updates the local corner profile.</summary>
        /// <summary>
        /// Updates one seat's profile.
        /// </summary>
        /// <param name="tileCount">
        /// How many tiles the seat still holds — the number on the pill. It was
        /// the series score; the count is the thing you actually track during a
        /// round, and the scoreboard panel already carries the scores.
        /// </param>
        public void SetSeat(
            SeatPosition pos, bool active, string name, Color color, int tileCount,
            bool online, bool currentTurn)
        {
            SeatWidgets? w = pos == SeatPosition.Bottom ? _local : (_seats.TryGetValue(pos, out var s) ? s : null);
            if (w == null)
            {
                return;
            }
            w.Root.SetActive(active);
            if (!active)
            {
                return;
            }
            w.Ring.color = color;
            w.Fill.color = new Color(color.r * 0.55f, color.g * 0.55f, color.b * 0.55f, 1f);
            w.Initials.text = Initials(name);
            if (w.Name != null)
            {
                w.Name.text = Trim(name, 12);
            }
            w.Score.text = tileCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            w.ScorePill.color = color;
            w.TurnGlow.SetActive(currentTurn);
        }

        /// <summary>
        /// Drives the action bar. <paramref name="mustPass"/> lights the Pass
        /// button (a forced pass); otherwise it stays dim/disabled.
        /// </summary>
        public void SetTurn(bool isLocalTurn, bool mustPass)
        {
            _passBg.color = mustPass ? ActionPurple : ActionDim;
            _turnTag.text = mustPass
                ? L10n.Get("board_must_pass")
                : (isLocalTurn ? L10n.Get("board_turn_pick") : L10n.Get("board_waiting"));
            _turnTag.color = mustPass ? SeatColors[3] : Muted;
        }

        public void SetLastPlay(bool has, int a, int b)
        {
            _lastPlayTile.gameObject.SetActive(has);
            if (!has)
            {
                return;
            }
            // Explicit pips rather than the canonical Tile order, so the badge
            // shows the halves the way they were laid.
            _lastPlayTile.Setup(new Tile((byte)a, (byte)b), (byte)a, (byte)b);
        }

        // ---- top bar ------------------------------------------------------

        private void BuildTopBar()
        {
            GameObject bar = Child(transform, "TopBar");
            RectTransform rt = (RectTransform)bar.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(0f, -104f);
            rt.offsetMax = Vector2.zero;

            IconButton(bar.transform, IconFactory.Menu(), new Vector2(0f, 0.5f), new Vector2(24f, 0f), () => HomeClicked?.Invoke());
            IconButton(bar.transform, IconFactory.Gear(), new Vector2(1f, 0.5f), new Vector2(-24f, 0f), () => SettingsClicked?.Invoke());

            // Coin pill (centre).
            GameObject coin = Child(bar.transform, "Coins");
            RectTransform crt = (RectTransform)coin.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(260f, 64f);
            Image cbg = coin.AddComponent<Image>();
            cbg.sprite = GradientSprite.RoundedDiagonal(0.5f, new Color(0.14f, 0.10f, 0.07f), new Color(0.08f, 0.05f, 0.03f));
            cbg.color = Color.white;
            AddIcon(coin.transform, IconFactory.Coin(), 40f, Gold, new Vector2(30f, 0f), TextAnchor.MiddleLeft);
            _coinLabel = AddLabel(coin.transform, "10,000", 34f, TextCol, TextAlignmentOptions.Center, FontStyles.Bold);
        }

        // ---- scoreboard ---------------------------------------------------

        private void BuildScoreboard()
        {
            GameObject panel = Child(transform, "Scoreboard");
            _scorePanel = panel;
            RectTransform rt = (RectTransform)panel.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(24f, -116f);
            // Height is driven by the ContentSizeFitter below; this is only the
            // starting value. Narrower than before so the top profile, which
            // now sits in the same band, has room on the right.
            rt.sizeDelta = new Vector2(300f, 300f);
            PanelBg(panel);
            VerticalLayoutGroup vlg = panel.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(0, 0, 0, 0); // bottom padding lives in the body
            vlg.spacing = 0f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            _scoreFitter = panel.AddComponent<ContentSizeFitter>();
            _scoreFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Crown header (tap to collapse). Collapsed, this is the whole
            // panel, so it is pinned to exactly CrownHeight — min as well as
            // preferred. AddLabel gives every label a preferred height of
            // fontSize + 8, and with childControlHeight on, a 30pt title was
            // free to ask for more room than the bar and stretch it.
            GameObject head = Child(panel.transform, "Crown");
            LayoutElement headLe = head.AddComponent<LayoutElement>();
            headLe.preferredHeight = CrownHeight;
            headLe.minHeight = CrownHeight;
            headLe.flexibleHeight = 0f;
            HorizontalLayoutGroup hh = head.AddComponent<HorizontalLayoutGroup>();
            hh.padding = new RectOffset(16, 12, 0, 0);
            hh.childAlignment = TextAnchor.MiddleCenter;
            hh.childControlWidth = true; hh.childControlHeight = true; hh.childForceExpandWidth = true;
            Button hb = HeaderButton(head);
            hb.onClick.AddListener(ToggleScoreboard);
            TextMeshProUGUI title =
                AddFlexLabel(head.transform, L10n.Get("scoreboard_title"), 30f, Gold, FontStyles.Bold, 6f);
            title.GetComponent<LayoutElement>().preferredHeight = CrownHeight;
            _scoreChev = AddLabel(head.transform, "–", 30f, Muted, TextAlignmentOptions.Right);
            LayoutElement chevLe = _scoreChev.GetComponent<LayoutElement>();
            chevLe.preferredWidth = 30f;
            chevLe.preferredHeight = CrownHeight;

            _scoreBody = Child(panel.transform, "Body");
            VerticalLayoutGroup bvl = _scoreBody.AddComponent<VerticalLayoutGroup>();
            bvl.padding = new RectOffset(16, 16, 4, 12);
            bvl.spacing = 2f;
            bvl.childControlWidth = true; bvl.childControlHeight = true; bvl.childForceExpandWidth = true; bvl.childForceExpandHeight = false;

            _scoreSubtitle = AddLabel(_scoreBody.transform, string.Empty, 18f, Muted, TextAlignmentOptions.Center);
            _scoreSubtitle.GetComponent<LayoutElement>().preferredHeight = 26f;

            ScoreHeaderRow(_scoreBody.transform);
            for (int i = 0; i < 4; i++)
            {
                _scoreRows.Add(ScoreRow(_scoreBody.transform));
            }
            ScoreFooter(_scoreBody.transform);
        }

        private void ScoreHeaderRow(Transform parent)
        {
            GameObject row = Child(parent, "HeadRow");
            row.AddComponent<LayoutElement>().preferredHeight = 30f;
            GridColumns(row);
            AddCell(row.transform, L10n.Get("scoreboard_col_players"), 17f, Faint, TextAlignmentOptions.Left);
            AddCell(row.transform, L10n.Get("scoreboard_col_games"), 17f, Faint, TextAlignmentOptions.Center);
            AddCell(row.transform, L10n.Get("scoreboard_col_points"), 17f, Faint, TextAlignmentOptions.Center);
        }

        private (GameObject, Image, TextMeshProUGUI, TextMeshProUGUI, TextMeshProUGUI) ScoreRow(Transform parent)
        {
            GameObject row = Child(parent, "Row");
            row.AddComponent<LayoutElement>().preferredHeight = 40f;
            GridColumns(row);

            GameObject who = Child(row.transform, "Who");
            HorizontalLayoutGroup wl = who.AddComponent<HorizontalLayoutGroup>();
            wl.childAlignment = TextAnchor.MiddleLeft; wl.spacing = 10f; wl.childControlWidth = true; wl.childControlHeight = true; wl.childForceExpandWidth = false;
            GameObject dotGo = Child(who.transform, "Dot");
            LayoutElement dle = dotGo.AddComponent<LayoutElement>();
            dle.preferredWidth = 16f; dle.preferredHeight = 16f;
            Image dot = dotGo.AddComponent<Image>();
            dot.sprite = GradientSprite.RoundedDiagonal(0.5f, Color.white, Color.white);
            TextMeshProUGUI name = AddLabel(who.transform, "Player", 22f, TextCol, TextAlignmentOptions.Left, FontStyles.Normal);

            TextMeshProUGUI games = AddCell(row.transform, "0", 22f, TextCol, TextAlignmentOptions.Center);
            TextMeshProUGUI pts = AddCell(row.transform, "0", 22f, Gold, TextAlignmentOptions.Center, FontStyles.Bold);
            return (row, dot, name, games, pts);
        }

        private void ScoreFooter(Transform parent)
        {
            GameObject foot = Child(parent, "Foot");
            foot.AddComponent<LayoutElement>().preferredHeight = 68f;
            HorizontalLayoutGroup hl = foot.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 10f; hl.padding = new RectOffset(0, 0, 8, 0);
            hl.childControlWidth = true; hl.childControlHeight = true; hl.childForceExpandWidth = true;
            _roundValue = FooterStat(foot.transform, L10n.Get("scoreboard_round"), "1");
            _onBoardValue = FooterStat(foot.transform, L10n.Get("scoreboard_on_board"), "0");
        }

        private TextMeshProUGUI FooterStat(Transform parent, string key, string val)
        {
            GameObject box = Child(parent, "Stat");
            Image bg = box.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.4f, new Color(0f, 0f, 0f, 0.32f), new Color(0f, 0f, 0f, 0.32f));
            bg.color = Color.white;
            VerticalLayoutGroup vl = box.AddComponent<VerticalLayoutGroup>();
            vl.childAlignment = TextAnchor.MiddleCenter; vl.padding = new RectOffset(2, 2, 6, 6);
            vl.childControlWidth = true; vl.childControlHeight = true;
            AddLabel(box.transform, key, 15f, Faint, TextAlignmentOptions.Center);
            return AddLabel(box.transform, val, 26f, TextCol, TextAlignmentOptions.Center, FontStyles.Bold);
        }

        private void ToggleScoreboard()
        {
            _scoreCollapsed = !_scoreCollapsed;
            _scoreBody.SetActive(!_scoreCollapsed);
            _scoreChev.text = _scoreCollapsed ? "+" : "–";

            // Collapsed, the height is driven directly rather than left to the
            // ContentSizeFitter. The fitter has no layout group above it to
            // trigger a rebuild, so hiding the body left the panel at whatever
            // height it was last fitted to — a title bar with the dead space of
            // a full table still under it. Expanded, the fitter goes back to
            // owning the height, because that depends on the row count.
            RectTransform rt = (RectTransform)_scorePanel.transform;
            _scoreFitter.enabled = !_scoreCollapsed;
            if (_scoreCollapsed)
            {
                rt.sizeDelta = new Vector2(rt.sizeDelta.x, CrownHeight);
            }
            else
            {
                LayoutRebuilder.MarkLayoutForRebuild(rt);
            }
        }

        // ---- seat avatars -------------------------------------------------

        private void BuildSeat(SeatPosition pos, Vector2 anchor, Vector2 offset, float avatarSize)
        {
            // No name plate: the initials on the disc identify the seat, and the
            // pill under it is the live tile count. A name label under every
            // avatar was three more strings competing with the tiles.
            SeatWidgets w = MakeAvatarWidget(transform, $"Seat_{pos}", anchor, offset, withName: false, avatarSize: avatarSize);
            _seats[pos] = w;
            w.Root.SetActive(false);
        }

        private SeatWidgets MakeAvatarWidget(
            Transform parent, string name, Vector2 anchor, Vector2 offset, bool withName, float avatarSize)
        {
            GameObject root = Child(parent, name);
            RectTransform rt = (RectTransform)root.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = offset;
            // Height follows what is actually stacked: avatar, optionally a
            // name, then the count pill. Without the name the widget is 36
            // shorter, which is what lets the side seats clear their columns.
            rt.sizeDelta = new Vector2(avatarSize + 30f, avatarSize + (withName ? 80f : 44f));
            VerticalLayoutGroup vl = root.AddComponent<VerticalLayoutGroup>();
            vl.childAlignment = TextAnchor.MiddleCenter; vl.spacing = 4f;
            vl.childControlWidth = true; vl.childControlHeight = true; vl.childForceExpandWidth = false; vl.childForceExpandHeight = false;

            SeatWidgets w = new();

            // Avatar (ring + fill + initials + online + turn glow).
            GameObject av = Child(root.transform, "Avatar");
            LayoutElement ale = av.AddComponent<LayoutElement>();
            ale.preferredWidth = avatarSize; ale.preferredHeight = avatarSize;

            GameObject glow = Child(av.transform, "Glow");
            StretchFull((RectTransform)glow.transform, -14f);
            Image glowImg = glow.AddComponent<Image>();
            glowImg.sprite = GradientSprite.RoundedDiagonal(0.5f, new Color(1f, 1f, 1f, 0.5f), new Color(1f, 1f, 1f, 0f));
            glowImg.color = new Color(1f, 1f, 1f, 0.9f);
            glowImg.raycastTarget = false;
            w.TurnGlow = glow;

            GameObject ring = Child(av.transform, "Ring");
            StretchFull((RectTransform)ring.transform);
            w.Ring = ring.AddComponent<Image>();
            w.Ring.sprite = GradientSprite.RoundedDiagonal(0.5f, Color.white, Color.white);
            w.Ring.color = SeatColors[0];
            w.Ring.raycastTarget = false;

            GameObject fill = Child(ring.transform, "Fill");
            StretchFull((RectTransform)fill.transform, 4f);
            w.Fill = fill.AddComponent<Image>();
            w.Fill.sprite = GradientSprite.RoundedDiagonal(0.5f, Color.white, Color.white);
            w.Fill.color = new Color(0.2f, 0.2f, 0.2f);
            w.Fill.raycastTarget = false;

            w.Initials = AddChildLabel(fill.transform, "P", avatarSize * 0.34f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);

            GameObject dot = Child(av.transform, "Online");
            RectTransform drt = (RectTransform)dot.transform;
            drt.anchorMin = drt.anchorMax = new Vector2(0.85f, 0.15f);
            drt.pivot = new Vector2(0.5f, 0.5f);
            drt.sizeDelta = new Vector2(avatarSize * 0.22f, avatarSize * 0.22f);
            Image dotImg = dot.AddComponent<Image>();
            dotImg.sprite = GradientSprite.RoundedDiagonal(0.5f, Color.white, Color.white);
            dotImg.color = Online;
            dotImg.raycastTarget = false;

            if (withName)
            {
                w.Name = AddLabel(root.transform, "Player", 22f, TextCol, TextAlignmentOptions.Center, FontStyles.Bold);
                w.Name.GetComponent<LayoutElement>().preferredHeight = 28f;
            }
            else
            {
                w.Name = null!;
            }

            // Tile-count badge. Round rather than the old wide pill — it holds
            // a single digit, and a circle beside the avatar reads as a count
            // rather than as a score bar.
            GameObject pill = Child(root.transform, "Count");
            LayoutElement ple = pill.AddComponent<LayoutElement>();
            ple.preferredWidth = 40f; ple.preferredHeight = 40f;
            w.ScorePill = pill.AddComponent<Image>();
            w.ScorePill.sprite = GradientSprite.RoundedDiagonal(0.5f, Color.white, Color.white);
            w.ScorePill.color = SeatColors[0];
            w.ScorePill.raycastTarget = false;
            w.Score = AddChildLabel(pill.transform, "0", 22f, new Color(0.07f, 0.06f, 0.05f), TextAlignmentOptions.Center, FontStyles.Bold);

            w.Root = root;
            return w;
        }

        // ---- chat panel ---------------------------------------------------

        private void BuildChatPanel()
        {
            GameObject panel = Child(transform, "ChatPanel");
            RectTransform rt = (RectTransform)panel.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-24f, -120f);
            rt.sizeDelta = new Vector2(380f, 520f);
            PanelBg(panel);
            VerticalLayoutGroup vl = panel.AddComponent<VerticalLayoutGroup>();
            vl.childControlWidth = true; vl.childControlHeight = true; vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;

            GameObject head = Child(panel.transform, "Head");
            head.AddComponent<LayoutElement>().preferredHeight = 56f;
            HorizontalLayoutGroup hl = head.AddComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(16, 12, 0, 0); hl.spacing = 8f; hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = true; hl.childControlHeight = true; hl.childForceExpandWidth = false;
            Button hb = HeaderButton(head);
            hb.onClick.AddListener(ToggleChat);
            AddIconInline(head.transform, IconFactory.Chat(), 30f, Gold);
            AddFlexLabel(head.transform, L10n.Get("chat_title"), 24f, TextCol, FontStyles.Bold, 4f);
            AddIconInline(head.transform, IconFactory.Mic(), 28f, Muted);
            AddLabel(head.transform, "–", 30f, Muted, TextAlignmentOptions.Right).GetComponent<LayoutElement>().preferredWidth = 26f;

            // A few sample messages (static until chat is wired).
            GameObject log = Child(panel.transform, "Log");
            log.AddComponent<LayoutElement>().flexibleHeight = 1f;
            VerticalLayoutGroup lvl = log.AddComponent<VerticalLayoutGroup>();
            lvl.padding = new RectOffset(14, 14, 12, 12); lvl.spacing = 12f;
            lvl.childControlWidth = true; lvl.childControlHeight = true; lvl.childForceExpandWidth = true; lvl.childForceExpandHeight = false;
            AddChatMessage(log.transform, "Sly Mongoose", SeatColors[0], "Good luck everyone!");
            AddChatMessage(log.transform, "Swift Coconut", SeatColors[1], "Let's get it!");
            AddChatMessage(log.transform, "Brave Hibiscus", SeatColors[2], "Big chain coming!");
            AddChatMessage(log.transform, "Noble Marlin", SeatColors[3], "Watch yuhself!");

            // Input row.
            GameObject inrow = Child(panel.transform, "Input");
            inrow.AddComponent<LayoutElement>().preferredHeight = 60f;
            HorizontalLayoutGroup il = inrow.AddComponent<HorizontalLayoutGroup>();
            il.padding = new RectOffset(14, 14, 10, 12); il.spacing = 8f;
            il.childControlWidth = true; il.childControlHeight = true; il.childForceExpandWidth = false; il.childAlignment = TextAnchor.MiddleLeft;
            GameObject field = Child(inrow.transform, "Field");
            LayoutElement fle = field.AddComponent<LayoutElement>();
            fle.flexibleWidth = 1f; fle.preferredHeight = 44f;
            Image fbg = field.AddComponent<Image>();
            fbg.sprite = GradientSprite.RoundedDiagonal(0.5f, new Color(0f, 0f, 0f, 0.35f), new Color(0f, 0f, 0f, 0.35f));
            fbg.color = Color.white;
            AddChildLabel(field.transform, L10n.Get("chat_placeholder"), 18f, Faint, TextAlignmentOptions.Left);
            GameObject send = Child(inrow.transform, "Send");
            LayoutElement sle = send.AddComponent<LayoutElement>();
            sle.preferredWidth = 48f; sle.preferredHeight = 48f;
            Image sbg = send.AddComponent<Image>();
            sbg.sprite = GradientSprite.RoundedDiagonal(0.5f, new Color(0.27f, 0.70f, 0.35f), new Color(0.20f, 0.55f, 0.28f));
            sbg.color = Color.white;
            AddIcon(send.transform, IconFactory.Send(), 26f, Color.white, Vector2.zero, TextAnchor.MiddleCenter);

            _chatPanel = panel;
            panel.SetActive(false);
        }

        private void AddChatMessage(Transform parent, string name, Color color, string text)
        {
            GameObject msg = Child(parent, "Msg");
            VerticalLayoutGroup vl = msg.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 1f; vl.childControlWidth = true; vl.childControlHeight = true; vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            AddLabel(msg.transform, name, 17f, color, TextAlignmentOptions.Left, FontStyles.Bold).GetComponent<LayoutElement>().preferredHeight = 22f;
            AddLabel(msg.transform, text, 19f, Muted, TextAlignmentOptions.Left).GetComponent<LayoutElement>().preferredHeight = 24f;
        }

        private void ToggleChat() => _chatPanel.SetActive(!_chatPanel.activeSelf);

        // ---- bottom-right corner (local profile + chat button) ------------

        private void BuildCorner()
        {
            GameObject corner = Child(transform, "Corner");
            RectTransform rt = (RectTransform)corner.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-22f, 26f);
            rt.sizeDelta = new Vector2(120f, 260f);
            VerticalLayoutGroup vl = corner.AddComponent<VerticalLayoutGroup>();
            vl.childAlignment = TextAnchor.LowerCenter; vl.spacing = 12f;
            vl.childControlWidth = true; vl.childControlHeight = true; vl.childForceExpandWidth = false; vl.childForceExpandHeight = false;

            _local = MakeAvatarWidget(corner.transform, "MeProfile", new Vector2(0.5f, 0.5f), Vector2.zero, withName: false, avatarSize: 84f);
            // Reparent the built profile into the vertical stack instead of anchoring.
            RectTransform prt = (RectTransform)_local.Root.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            _local.Root.AddComponent<LayoutElement>().preferredHeight = 128f;
            _local.Root.SetActive(true);

            GameObject chat = Child(corner.transform, "ChatBtn");
            LayoutElement cle = chat.AddComponent<LayoutElement>();
            cle.preferredWidth = 84f; cle.preferredHeight = 84f;
            Image cbg = chat.AddComponent<Image>();
            cbg.sprite = GradientSprite.RoundedDiagonal(0.28f, new Color(0.14f, 0.10f, 0.07f), new Color(0.08f, 0.05f, 0.03f));
            cbg.color = Color.white;
            Button cbtn = chat.AddComponent<Button>();
            cbtn.onClick.AddListener(ToggleChat);
            AddIcon(chat.transform, IconFactory.Chat(), 42f, Gold, Vector2.zero, TextAnchor.MiddleCenter);
        }

        // ---- action bar ---------------------------------------------------

        private void BuildActionBar()
        {
            GameObject bar = Child(transform, "ActionBar");
            RectTransform rt = (RectTransform)bar.transform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(0f, 0f);
            rt.offsetMax = new Vector2(0f, ActionBarHeight);

            // Last Play (bottom-left). The turn clock docks directly above it.
            GameObject lp = Child(bar.transform, "LastPlay");
            RectTransform lprt = (RectTransform)lp.transform;
            lprt.anchorMin = lprt.anchorMax = new Vector2(0f, 0.5f);
            lprt.pivot = new Vector2(0f, 0.5f);
            lprt.anchoredPosition = new Vector2(ActionBarLeftInset, 0f);
            lprt.sizeDelta = new Vector2(150f, ActionBarHeight);
            VerticalLayoutGroup lpv = lp.AddComponent<VerticalLayoutGroup>();
            lpv.childAlignment = TextAnchor.MiddleLeft; lpv.spacing = 6f;
            AddLabel(lp.transform, L10n.Get("board_last_play"), 15f, Faint, TextAlignmentOptions.Left).GetComponent<LayoutElement>().preferredHeight = 20f;
            // A real tile rather than a hand-drawn imitation of one, so the
            // badge picks up the body, pips and divider the board uses and
            // cannot drift from them.
            GameObject mini = Child(lp.transform, "Mini");
            LayoutElement mle = mini.AddComponent<LayoutElement>();
            mle.preferredWidth = LastPlayTileLong;
            mle.preferredHeight = LastPlayTileShort;
            _lastPlayTile = mini.AddComponent<TileView>();
            _lastPlayTile.Init(
                TileOrientation.Landscape, LastPlayTileShort, LastPlayTileLong, LastPlayDepthScale);
            _lastPlayTile.Mode = TileInteractionMode.Display;
            SetLastPlay(false, 0, 0);

            // Pass + turn tag (centre).
            GameObject center = Child(bar.transform, "PassWrap");
            RectTransform crt = (RectTransform)center.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(300f, 100f);
            VerticalLayoutGroup cvl = center.AddComponent<VerticalLayoutGroup>();
            cvl.childAlignment = TextAnchor.MiddleCenter; cvl.spacing = 10f;
            cvl.childControlWidth = true; cvl.childControlHeight = true; cvl.childForceExpandWidth = false; cvl.childForceExpandHeight = false;
            _turnTag = AddLabel(center.transform, L10n.Get("board_turn_pick"), 18f, Muted, TextAlignmentOptions.Center, FontStyles.Bold);
            _turnTag.GetComponent<LayoutElement>().preferredHeight = 24f;

            GameObject pass = Child(center.transform, "Pass");
            LayoutElement passLe = pass.AddComponent<LayoutElement>();
            passLe.preferredWidth = 210f; passLe.preferredHeight = 66f;
            _passBg = pass.AddComponent<Image>();
            _passBg.sprite = GradientSprite.RoundedDiagonal(0.22f, Color.white, Color.white);
            _passBg.color = ActionDim;
            Button pbtn = pass.AddComponent<Button>();
            pbtn.targetGraphic = _passBg;
            pbtn.onClick.AddListener(() => PassClicked?.Invoke());
            AddChildLabel(pass.transform, L10n.Get("pass_button"), 26f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        }

        // ---- small builders ----------------------------------------------

        private void PanelBg(GameObject go)
        {
            Image bg = go.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.14f, Panel, Panel);
            bg.color = Color.white;
            bg.raycastTarget = false;
            AddShadow(go);
        }

        // A transparent, raycastable graphic so a header GameObject with no visible
        // Image can still be tapped (used for the collapse/minimize headers).
        private static Button HeaderButton(GameObject go)
        {
            Image hit = go.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = hit;
            return btn;
        }

        private void IconButton(Transform parent, Sprite icon, Vector2 anchor, Vector2 offset, UnityEngine.Events.UnityAction onClick)
        {
            GameObject go = Child(parent, "IconBtn");
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = offset;
            rt.sizeDelta = new Vector2(64f, 64f);
            Image bg = go.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.28f, new Color(0.13f, 0.10f, 0.07f), new Color(0.08f, 0.05f, 0.03f));
            bg.color = Color.white;
            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(onClick);
            AddIcon(go.transform, icon, 34f, TextCol, Vector2.zero, TextAnchor.MiddleCenter);
        }

        private static void GridColumns(GameObject row)
        {
            HorizontalLayoutGroup hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.childControlWidth = true; hl.childControlHeight = true; hl.childForceExpandWidth = false; hl.childForceExpandHeight = true; hl.spacing = 4f;
            hl.childAlignment = TextAnchor.MiddleLeft;
        }

        private TextMeshProUGUI AddCell(Transform parent, string text, float size, Color color, TextAlignmentOptions align, FontStyles style = FontStyles.Normal)
        {
            TextMeshProUGUI t = AddLabel(parent, text, size, color, align, style);
            LayoutElement le = t.GetComponent<LayoutElement>();
            bool wide = align == TextAlignmentOptions.Left;
            le.preferredWidth = wide ? 180f : 62f;
            le.flexibleWidth = wide ? 1f : 0f;
            return t;
        }

        private TextMeshProUGUI AddLabel(Transform parent, string text, float size, Color color, TextAlignmentOptions align, FontStyles style = FontStyles.Normal)
        {
            GameObject go = Child(parent, "Label");
            go.AddComponent<LayoutElement>().preferredHeight = size + 8f;
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = size; t.color = color; t.alignment = align; t.fontStyle = style; t.raycastTarget = false;
            return t;
        }

        private TextMeshProUGUI AddFlexLabel(Transform parent, string text, float size, Color color, FontStyles style, float tracking)
        {
            TextMeshProUGUI t = AddLabel(parent, text, size, color, TextAlignmentOptions.Center, style);
            t.characterSpacing = tracking;
            t.GetComponent<LayoutElement>().flexibleWidth = 1f;
            return t;
        }

        private TextMeshProUGUI AddChildLabel(Transform parent, string text, float size, Color color, TextAlignmentOptions align, FontStyles style = FontStyles.Normal)
        {
            GameObject go = Child(parent, "Label");
            StretchFull((RectTransform)go.transform);
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = size; t.color = color; t.alignment = align; t.fontStyle = style; t.raycastTarget = false;
            return t;
        }

        private void AddIcon(Transform parent, Sprite icon, float size, Color tint, Vector2 offset, TextAnchor anchor)
        {
            GameObject go = Child(parent, "Icon");
            RectTransform rt = (RectTransform)go.transform;
            Vector2 a = AnchorOf(anchor);
            rt.anchorMin = rt.anchorMax = a;
            rt.pivot = a;
            rt.anchoredPosition = offset;
            rt.sizeDelta = new Vector2(size, size);
            Image img = go.AddComponent<Image>();
            img.sprite = icon; img.color = tint; img.raycastTarget = false;
        }

        private void AddIconInline(Transform parent, Sprite icon, float size, Color tint)
        {
            GameObject go = Child(parent, "Icon");
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = size; le.preferredHeight = size;
            Image img = go.AddComponent<Image>();
            img.sprite = icon; img.color = tint; img.raycastTarget = false;
        }

        private static Vector2 AnchorOf(TextAnchor a) => a switch
        {
            TextAnchor.MiddleLeft => new Vector2(0f, 0.5f),
            TextAnchor.MiddleRight => new Vector2(1f, 0.5f),
            _ => new Vector2(0.5f, 0.5f),
        };

        private static void AddShadow(GameObject go)
        {
            Shadow sh = go.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.55f);
            sh.effectDistance = new Vector2(0f, -6f);
        }

        private static GameObject Child(Transform parent, string name)
        {
            GameObject go = new(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            return go;
        }

        private static void StretchFull(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-inset, -inset);
            rt.offsetMax = new Vector2(inset, inset);
        }

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

        private static string Trim(string s, int max) => s.Length <= max ? s : s.Substring(0, max);
    }
}
