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
    /// The opening shuffle: a full set of face-down tiles sweeps in, slides over
    /// itself, collapses into a stack, and deals out to the seats.
    ///
    /// It exists to cover the wait for the server-issued deal, so it is driven
    /// by <see cref="ShuffleSequence"/>, which loops the swirl until
    /// <see cref="NotifyDealReady"/> says the seed has landed. The board is
    /// never held up by the animation — at worst the swirl finishes the cycle
    /// it is in.
    ///
    /// Everything random here is cosmetic: which way a tile drifts, how far it
    /// jitters. The hands themselves come from the server seed and are dealt by
    /// the rule engine, so this deliberately uses plain <c>UnityEngine.Random</c>
    /// (permitted for cosmetic motion) and never touches, reveals or influences
    /// the real deal.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class ShuffleAnimation : MonoBehaviour
    {
        /// <summary>Tiles in a double-six set — what the shuffle shows.</summary>
        private const int TileCount = 28;

        private const float TileShort = 62f;
        private const float TileLong = 124f;

        // The set is laid OUT, not heaped: every tile gets its own cell in a
        // grid, so nothing overlaps and the whole set is readable while it
        // shuffles. It is allowed to take up most of the board.
        private const int GridColumns = 5;
        private const float CellPadX = 26f;
        private const float CellPadY = 22f;

        // Drift stays inside each tile's own share of the padding, so tiles
        // jostle without ever climbing over one another.
        private const float SwirlDrift = 9f;
        private const float SwirlSpinDegrees = 5f;

        // Where tiles start: off board, spread around the edges.
        private const float EntryDistance = 900f;

        // Neat stack the cluster collapses into before dealing.
        private const float StackSpread = 1.6f;
        private const float StackTiltDegrees = 2.4f;

        // Seats, clockwise from the local player, as fractions of the screen.
        private static readonly Vector2[] SeatDirections =
        {
            new(0f, -1f),   // bottom — you
            new(1f, 0.15f), // right
            new(0f, 1f),    // top
            new(-1f, 0.15f) // left
        };

        // Deep table shade, not pure black — the board stays faintly present.
        private static readonly Color ScrimColor = new(0.03f, 0.10f, 0.08f, 0.93f);
        private static readonly Color LabelColor = new(0.91f, 0.87f, 0.79f);
        private const float LabelFontSize = 30f;

        private static float CellW => TileShort + CellPadX;
        private static float CellH => TileLong + CellPadY;
        private static int GridRows => (TileCount + GridColumns - 1) / GridColumns;

        private sealed class Flying
        {
            public RectTransform Rt = null!;
            public Vector2 Entry;      // off-board start
            public Vector2 Slot;       // place in the swirl cluster
            public Vector2 Stacked;     // place in the final stack
            public Vector2 Target;     // seat it is dealt to
            public float Phase;        // per-tile offset so they don't move in lockstep
            public float EntryAngle;
            public float StackAngle;
            public float DealDelay;    // 0..1 through the deal phase
        }

        private readonly List<Flying> _tiles = new();
        private ShuffleSequence _sequence = new();
        private RectTransform _root = null!;
        private Image? _scrim;
        private TextMeshProUGUI? _label;
        private Action? _onComplete;
        private bool _running;

        /// <summary>True while the shuffle is on screen.</summary>
        public bool IsPlaying => _running;

        /// <summary>
        /// Builds the tiles and starts the sweep-in. The board underneath should
        /// already be hidden by the caller.
        /// </summary>
        /// <param name="onComplete">
        /// Optional. Raised once the last tile has landed. Null is the normal
        /// case — the board renders behind the scrim while this plays, so there
        /// is usually nothing left to do when it ends.
        /// </param>
        public void Play(Action? onComplete)
        {
            _onComplete = onComplete;
            _sequence = new ShuffleSequence();
            _running = true;
            gameObject.SetActive(true);
            BuildTiles();
        }

        /// <summary>
        /// The deal has landed. Lets the swirl end at its next cycle boundary.
        /// Safe to call before <see cref="Play"/>, more than once, or never —
        /// the sequence gives up swirling on its own ceiling either way.
        /// </summary>
        public void NotifyDealReady()
        {
            _sequence.RequestFinish();
        }

        private void Awake()
        {
            _root = (RectTransform)transform;
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.one;
            _root.offsetMin = Vector2.zero;
            _root.offsetMax = Vector2.zero;
            BuildScrim();
            BuildLabel();
            gameObject.SetActive(false);
        }

        private void Update()
        {
            if (!_running)
            {
                return;
            }

            ShufflePhase phase = _sequence.Advance(Time.deltaTime);
            float t = _sequence.PhaseProgress;

            switch (phase)
            {
                case ShufflePhase.Gather:
                    DrawGather(t);
                    break;
                case ShufflePhase.Swirl:
                    DrawSwirl(t);
                    break;
                case ShufflePhase.Stack:
                    DrawStack(t);
                    break;
                case ShufflePhase.Deal:
                    DrawDeal(t);
                    break;
                case ShufflePhase.Done:
                    Finish();
                    break;
            }
        }

        // ---- Phase drawing ---------------------------------------------------

        private void DrawGather(float t)
        {
            float e = EaseOutCubic(t);
            foreach (Flying f in _tiles)
            {
                // Staggered so they arrive as a stream rather than a wall.
                float local = Mathf.Clamp01((e * 1.35f) - (f.Phase * 0.35f));
                local = EaseOutCubic(local);
                f.Rt.anchoredPosition = Vector2.LerpUnclamped(f.Entry, f.Slot, local);
                f.Rt.localRotation = Quaternion.Euler(
                    0f, 0f, Mathf.LerpUnclamped(f.EntryAngle, 0f, local));
                SetAlpha(f, local);
            }
        }

        private void DrawSwirl(float t)
        {
            float angle = t * Mathf.PI * 2f;
            foreach (Flying f in _tiles)
            {
                float a = angle + (f.Phase * Mathf.PI * 2f);
                Vector2 drift = new(Mathf.Cos(a) * SwirlDrift, Mathf.Sin(a * 1.3f) * SwirlDrift);
                f.Rt.anchoredPosition = f.Slot + drift;
                f.Rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(a) * SwirlSpinDegrees);
                SetAlpha(f, 1f);
            }
        }

        private void DrawStack(float t)
        {
            float e = EaseOutCubic(t);
            foreach (Flying f in _tiles)
            {
                // Carry on from wherever the swirl left this tile.
                Vector2 from = f.Slot;
                f.Rt.anchoredPosition = Vector2.LerpUnclamped(from, f.Stacked, e);
                f.Rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.LerpUnclamped(0f, f.StackAngle, e));
            }
            SetLabelAlpha(1f - e);
            SetScrimAlpha(1f);
        }

        private void DrawDeal(float t)
        {
            foreach (Flying f in _tiles)
            {
                // Each tile leaves on its own beat, so the deal reads as a
                // sequence of flicks rather than an explosion.
                float local = Mathf.Clamp01((t - f.DealDelay) / (1f - f.DealDelay + 0.0001f));
                float e = EaseInCubic(local);
                f.Rt.anchoredPosition = Vector2.LerpUnclamped(f.Stacked, f.Target, e);
                f.Rt.localRotation = Quaternion.Euler(0f, 0f, f.StackAngle + (e * 90f));
                // Shrink slightly on the way out so they read as leaving the table.
                float s = Mathf.LerpUnclamped(1f, 0.75f, e);
                f.Rt.localScale = new Vector3(s, s, 1f);
                SetAlpha(f, 1f - (e * e));
            }
            SetLabelAlpha(0f);
            SetScrimAlpha(1f - EaseInCubic(t));
        }

        private void Finish()
        {
            _running = false;
            gameObject.SetActive(false);
            ClearTiles();
            Action? done = _onComplete;
            _onComplete = null;
            done?.Invoke();
        }

        // ---- Construction ----------------------------------------------------

        private void BuildTiles()
        {
            ClearTiles();

            for (int i = 0; i < TileCount; i++)
            {
                GameObject go = new($"ShuffleTile_{i}", typeof(RectTransform));
                go.transform.SetParent(_root, worldPositionStays: false);

                RectTransform rt = (RectTransform)go.transform;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(TileShort, TileLong);

                Image img = go.AddComponent<Image>();
                Sprite? body = TileView.Art?.BodyPortrait;
                if (body != null)
                {
                    img.sprite = body;
                }
                img.color = Color.white;
                img.raycastTarget = false;

                CanvasGroup cg = go.AddComponent<CanvasGroup>();
                cg.alpha = 0f;

                // One cell each, centred on the board.
                float k = i / (float)TileCount;
                int col = i % GridColumns;
                int rowIdx = i / GridColumns;
                Vector2 slot = new(
                    (col - ((GridColumns - 1) * 0.5f)) * CellW,
                    -(rowIdx - ((GridRows - 1) * 0.5f)) * CellH);

                float entryAngle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                Vector2 entry = new(
                    Mathf.Cos(entryAngle) * EntryDistance,
                    Mathf.Sin(entryAngle) * EntryDistance * 0.8f);

                Vector2 dir = SeatDirections[i % SeatDirections.Length];
                Vector2 target = new(dir.x * 520f, dir.y * 700f);

                _tiles.Add(new Flying
                {
                    Rt = rt,
                    Entry = entry,
                    Slot = slot,
                    Stacked = new Vector2(
                        (i - (TileCount / 2f)) * StackSpread,
                        (i - (TileCount / 2f)) * StackSpread * 0.35f),
                    Target = target,
                    Phase = k,
                    EntryAngle = UnityEngine.Random.Range(-160f, 160f),
                    StackAngle = UnityEngine.Random.Range(-StackTiltDegrees, StackTiltDegrees),
                    DealDelay = k * 0.55f,
                });
            }

            SetLabelAlpha(1f);
        }

        private void ClearTiles()
        {
            foreach (Flying f in _tiles)
            {
                if (f.Rt != null)
                {
                    Destroy(f.Rt.gameObject);
                }
            }
            _tiles.Clear();
        }

        /// <summary>
        /// Darkens the board underneath. The board renders normally behind this
        /// while the shuffle plays, so nothing has to be reordered or delayed —
        /// the scrim simply hides it, then fades as the tiles are dealt out and
        /// the finished board is revealed behind them. It also swallows taps,
        /// so nobody can play into a board they cannot see.
        /// </summary>
        private void BuildScrim()
        {
            GameObject go = new("Scrim", typeof(RectTransform));
            go.transform.SetParent(_root, worldPositionStays: false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _scrim = go.AddComponent<Image>();
            _scrim.color = ScrimColor;
            _scrim.raycastTarget = true;
        }

        private void SetScrimAlpha(float a)
        {
            if (_scrim != null)
            {
                Color c = ScrimColor;
                c.a = ScrimColor.a * Mathf.Clamp01(a);
                _scrim.color = c;
            }
        }

        private void BuildLabel()
        {
            GameObject go = new("ShuffleLabel", typeof(RectTransform));
            go.transform.SetParent(_root, worldPositionStays: false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, -260f);
            rt.sizeDelta = new Vector2(600f, 60f);

            _label = go.AddComponent<TextMeshProUGUI>();
            _label.alignment = TextAlignmentOptions.Center;
            _label.fontSize = LabelFontSize;
            _label.fontStyle = FontStyles.Bold;
            _label.color = LabelColor;
            _label.raycastTarget = false;
            _label.text = L10n.Get("shuffle_dealing");
        }

        private static void SetAlpha(Flying f, float a)
        {
            if (f.Rt.TryGetComponent(out CanvasGroup cg))
            {
                cg.alpha = Mathf.Clamp01(a);
            }
        }

        private void SetLabelAlpha(float a)
        {
            if (_label != null)
            {
                Color c = _label.color;
                c.a = Mathf.Clamp01(a);
                _label.color = c;
            }
        }

        private static float EaseOutCubic(float t)
        {
            float inv = 1f - Mathf.Clamp01(t);
            return 1f - (inv * inv * inv);
        }

        private static float EaseInCubic(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t;
        }
    }
}
