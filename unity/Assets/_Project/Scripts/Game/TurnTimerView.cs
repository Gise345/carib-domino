#nullable enable
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Pose.Game
{
    /// <summary>
    /// The turn clock: a radial countdown ring with the seconds remaining in its
    /// centre, plus a nudge banner that appears beside it once the current
    /// player has stalled.
    ///
    /// Docks bottom-left, directly above the Last Play block in
    /// <see cref="BoardRoomHud"/>'s action bar, so "the tile just played" and
    /// "how long until the next one" sit together in one corner and the middle
    /// of the board stays clear. It positions itself — the caller only creates
    /// the object.
    ///
    /// Purely a display — it owns no timing logic. The driver
    /// (<see cref="BoardBootstrap"/> offline, <c>OnlineMatchController</c> on the
    /// table authority) ticks a <see cref="Pose.Core.TurnTimer"/> and pushes the
    /// readout here via <see cref="SetProgress"/>.
    ///
    /// The ring shows on every turn, yours or an opponent's, so the table can see
    /// who is holding things up. It turns urgent in the closing seconds. The
    /// banner's wording differs by whose turn it is — the caller supplies the
    /// already-localized string.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class TurnTimerView : MonoBehaviour
    {
        private const float RingSize = 108f;
        private const float SecondsFontSize = 40f;
        private const float BannerFontSize = 24f;
        private const float BannerHeight = 54f;
        private const float BannerWidth = 470f;

        // Gap between the ring and the banner sitting beside it.
        private const float BannerGap = 14f;

        // Clearance between the ring and the Last Play block underneath.
        private const float BottomGap = 16f;

        // Below this many seconds the ring switches to the urgent palette and
        // starts pulsing. Matches the point where the nudge has already fired.
        private const float UrgentBelowSeconds = 10f;
        private const float PulsesPerSecond = 2f;
        private const float PulseDepth = 0.12f;

        private static readonly Color RingCalmColor = new(0.35f, 0.85f, 0.55f);
        private static readonly Color RingUrgentColor = new(1.0f, 0.40f, 0.32f);
        private static readonly Color RingTrackColor = new(0.03f, 0.18f, 0.11f, 0.85f);
        private static readonly Color SecondsColor = new(0.97f, 0.95f, 0.88f);
        private static readonly Color BannerColor = new(0.72f, 0.14f, 0.10f, 0.94f);
        private static readonly Color BannerTextColor = new(1.0f, 0.96f, 0.88f);

        private static Sprite? _discSprite;

        private RectTransform _ringRoot = null!;
        private Image _ringFill = null!;
        private TextMeshProUGUI _secondsLabel = null!;
        private GameObject _banner = null!;
        private TextMeshProUGUI _bannerLabel = null!;

        private bool _urgent;

        private void Awake()
        {
            BuildLayout();
            Hide();
        }

        private void Update()
        {
            // Pulse only while urgent, and only the ring — a moving number is
            // hard to read at a glance.
            if (!_urgent || !_ringRoot.gameObject.activeSelf)
            {
                return;
            }

            float pulse = 1f + (PulseDepth * Mathf.Sin(Time.time * PulsesPerSecond * Mathf.PI * 2f));
            _ringRoot.localScale = new Vector3(pulse, pulse, 1f);
        }

        /// <summary>
        /// Updates the countdown readout.
        /// </summary>
        /// <param name="progress">Fraction of the turn consumed, 0..1.</param>
        /// <param name="secondsRemaining">
        /// Seconds left before auto-play; displayed rounded up, so the ring reads
        /// "1" for the whole final second rather than flicking to "0" early.
        /// </param>
        public void SetProgress(float progress, float secondsRemaining)
        {
            if (!_ringRoot.gameObject.activeSelf)
            {
                _ringRoot.gameObject.SetActive(true);
            }

            // Ring drains clockwise as the turn burns down.
            _ringFill.fillAmount = Mathf.Clamp01(1f - progress);
            _secondsLabel.text = Mathf.CeilToInt(Mathf.Max(0f, secondsRemaining))
                .ToString(System.Globalization.CultureInfo.InvariantCulture);

            bool urgent = secondsRemaining <= UrgentBelowSeconds;
            if (urgent != _urgent)
            {
                _urgent = urgent;
                _ringFill.color = urgent ? RingUrgentColor : RingCalmColor;
                if (!urgent)
                {
                    _ringRoot.localScale = Vector3.one;
                }
            }
        }

        /// <summary>
        /// Shows the nudge banner with an already-localized message.
        /// </summary>
        public void ShowNudge(string message)
        {
            _bannerLabel.text = message;
            _banner.SetActive(true);
        }

        /// <summary>Hides the nudge banner, leaving the ring alone.</summary>
        public void ClearNudge()
        {
            _banner.SetActive(false);
        }

        /// <summary>
        /// Hides the whole widget — no turn is being timed (round over, waiting
        /// for players, or between rounds).
        /// </summary>
        public void Hide()
        {
            _ringRoot.gameObject.SetActive(false);
            _ringRoot.localScale = Vector3.one;
            _urgent = false;
            _ringFill.color = RingCalmColor;
            ClearNudge();
        }

        // ---- Construction ----------------------------------------------------

        private void BuildLayout()
        {
            // The root stretches full-screen so its two pieces can sit in
            // completely different places: the ring docks bottom-left above
            // Last Play, while the banner needs the centre of the board.
            RectTransform root = (RectTransform)transform;
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            _ringRoot = CreateRing(root);
            (_banner, _bannerLabel) = CreateBanner(root);
        }

        private RectTransform CreateRing(RectTransform parent)
        {
            GameObject go = new("TurnRing", typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            // Centre pivot even though we anchor bottom-left: the urgent pulse
            // scales this transform, and an off-centre pivot would make it
            // lurch out of the corner instead of breathing in place.
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(
                BoardRoomHud.ActionBarLeftInset + (RingSize * 0.5f),
                BoardRoomHud.ActionBarHeight + BottomGap + (RingSize * 0.5f));
            rt.sizeDelta = new Vector2(RingSize, RingSize);

            // Track sits behind the fill so the drained portion still reads as
            // part of a dial rather than empty space.
            Image track = go.AddComponent<Image>();
            track.sprite = Disc();
            track.color = RingTrackColor;
            track.raycastTarget = false;

            GameObject fillGo = new("Fill", typeof(RectTransform));
            fillGo.transform.SetParent(go.transform, worldPositionStays: false);
            RectTransform fillRt = (RectTransform)fillGo.transform;
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = new Vector2(6f, 6f);
            fillRt.offsetMax = new Vector2(-6f, -6f);

            _ringFill = fillGo.AddComponent<Image>();
            _ringFill.sprite = Disc();
            _ringFill.color = RingCalmColor;
            _ringFill.type = Image.Type.Filled;
            _ringFill.fillMethod = Image.FillMethod.Radial360;
            _ringFill.fillOrigin = (int)Image.Origin360.Top;
            _ringFill.fillClockwise = true;
            _ringFill.fillAmount = 1f;
            _ringFill.raycastTarget = false;

            GameObject labelGo = new("Seconds", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, worldPositionStays: false);
            RectTransform labelRt = (RectTransform)labelGo.transform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;

            _secondsLabel = labelGo.AddComponent<TextMeshProUGUI>();
            _secondsLabel.alignment = TextAlignmentOptions.Center;
            _secondsLabel.fontSize = SecondsFontSize;
            _secondsLabel.fontStyle = FontStyles.Bold;
            _secondsLabel.color = SecondsColor;
            _secondsLabel.raycastTarget = false;

            return rt;
        }

        private (GameObject banner, TextMeshProUGUI label) CreateBanner(RectTransform parent)
        {
            // Dead centre of the board. It sat beside the ring, which put it
            // straight across the local player's own hand — the one thing it
            // must never cover, since it is telling them to play from it. The
            // middle of the board is empty during a stalled turn by definition.
            GameObject go = new("NudgeBanner", typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(BannerWidth, BannerHeight);

            Image bg = go.AddComponent<Image>();
            bg.color = BannerColor;
            bg.raycastTarget = false;

            GameObject labelGo = new("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, worldPositionStays: false);
            RectTransform labelRt = (RectTransform)labelGo.transform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(16f, 0f);
            labelRt.offsetMax = new Vector2(-16f, 0f);

            TextMeshProUGUI tmp = labelGo.AddComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = BannerFontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = BannerTextColor;
            tmp.raycastTarget = false;

            go.SetActive(false);
            return (go, tmp);
        }

        /// <summary>
        /// A white anti-aliased disc, generated once and shared. Radial-filled by
        /// the Image component to make the countdown dial.
        /// </summary>
        private static Sprite Disc()
        {
            if (_discSprite != null)
            {
                return _discSprite;
            }

            const int size = 128;
            const float radius = size / 2f;
            Texture2D tex = new(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - radius;
                    float dy = y + 0.5f - radius;
                    float distance = Mathf.Sqrt((dx * dx) + (dy * dy));
                    // One-pixel feather at the rim so the dial doesn't alias.
                    float alpha = Mathf.Clamp01(radius - distance);
                    pixels[(y * size) + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();

            _discSprite = Sprite.Create(
                tex,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f),
                100f);
            return _discSprite;
        }
    }
}
