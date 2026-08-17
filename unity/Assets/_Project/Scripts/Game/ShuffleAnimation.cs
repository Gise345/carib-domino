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
    /// Everything unpredictable here is cosmetic: where a tile lands, how it
    /// tilts, which way it sweeps in. Layout comes from <see cref="ShuffleScatter"/>
    /// and the rest from plain <c>UnityEngine.Random</c>, both permitted for
    /// cosmetic motion. The hands themselves come from the server seed and are
    /// dealt by the rule engine, so nothing here touches, reveals or influences
    /// the real deal.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class ShuffleAnimation : MonoBehaviour
    {
        /// <summary>Tiles in a double-six set — what the shuffle shows.</summary>
        private const int TileCount = 28;

        private const float TileShort = 62f;
        private const float TileLong = 124f;

        // The set is spread, not heaped. An invisible grid hands every tile its
        // own cell, which is the only thing stopping the shuffle bulking up in
        // one corner — everything you actually see is the offset inside that
        // cell, the tilt on top of it, and the churn between cycles. Cells are
        // deliberately tighter than the tile, so neighbours overlap where they
        // fall rather than lining up. See ShuffleScatter.
        private const int GridColumns = 5;
        private const float CellW = TileShort + 2f;
        private const float CellH = TileLong - 16f;
        private const float ScatterJitter = 0.26f;
        private const float RestAngleSpread = 22f;
        private const float FieldMargin = 28f;

        // How far a travelling tile bows off the straight line between its old
        // spot and its new one, per pattern. Through pulls hardest — its whole
        // point is that the set passes through the middle.
        private const float FanArc = 34f;
        private const float ThroughArc = 78f;
        private const float RiffleArc = 52f;

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

        private static int GridRows => (TileCount + GridColumns - 1) / GridColumns;

        private sealed class Flying
        {
            public RectTransform Rt = null!;
            public Vector2 Entry;      // off-board start
            public Vector2 SlotFrom;   // where the current churn started
            public Vector2 SlotTo;     // where it is travelling to
            public Vector2 Stacked;    // place in the final stack
            public Vector2 Target;     // seat it is dealt to
            public float Phase;        // per-tile offset so they don't move in lockstep
            public float EntryAngle;
            public float AngleFrom;
            public float AngleTo;
            public float ArcSign;      // which side of the line it bows out on
            public float StackAngle;
            public float DealDelay;    // 0..1 through the deal phase
        }

        private readonly List<Flying> _tiles = new();
        private readonly List<int> _drawOrder = new();
        private ShuffleSequence _sequence = new();
        private ShuffleScatter? _scatter;
        private ShufflePattern _pattern = ShufflePattern.Fan;
        private int _lastCycle = -1;
        private int _tileBaseIndex;
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
            _lastCycle = -1;
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
            // Tiles live above the scrim and the label, and their draw order is
            // re-dealt every cycle, so they need a fixed floor to sort from.
            _tileBaseIndex = _root.childCount;
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

            // Each cycle re-deals the cells, which is what sends tiles across
            // the table past one another. Only while swirling: once the sequence
            // has moved on, tiles must stay where the last cycle left them.
            if (phase == ShufflePhase.Swirl && _sequence.SwirlCyclesCompleted != _lastCycle)
            {
                _lastCycle = _sequence.SwirlCyclesCompleted;
                Rescatter(_lastCycle + 1);
            }

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
                f.Rt.anchoredPosition = Vector2.LerpUnclamped(f.Entry, f.SlotTo, local);
                f.Rt.localRotation = Quaternion.Euler(
                    0f, 0f, Mathf.LerpUnclamped(f.EntryAngle, f.AngleTo, local));
                SetAlpha(f, local);
            }
        }

        private void DrawSwirl(float t)
        {
            foreach (Flying f in _tiles)
            {
                // Staggered starts, so the set churns the way a hand moves over
                // it — tiles going one after another, not the whole set at once.
                float local = Mathf.Clamp01((t * 1.6f) - (f.Phase * 0.6f));
                float e = EaseInOutCubic(local);

                f.Rt.anchoredPosition = PathPoint(f, e);
                f.Rt.localRotation = Quaternion.Euler(
                    0f, 0f, Mathf.LerpUnclamped(f.AngleFrom, f.AngleTo, e));
                SetAlpha(f, 1f);
            }
        }

        /// <summary>
        /// Where a tile sits part way through its journey to the next layout.
        /// The route is what makes one cycle read differently from the last —
        /// the destinations are always a fresh scatter, but going around the
        /// table looks nothing like cutting through the middle of it.
        /// </summary>
        private Vector2 PathPoint(Flying f, float e)
        {
            if (_pattern == ShufflePattern.Around)
            {
                return AroundPoint(f, e);
            }

            Vector2 straight = Vector2.LerpUnclamped(f.SlotFrom, f.SlotTo, e);
            float swell = Mathf.Sin(e * Mathf.PI);
            Vector2 line = f.SlotTo - f.SlotFrom;
            Vector2 side = new Vector2(-line.y, line.x).normalized;

            switch (_pattern)
            {
                case ShufflePattern.Through:
                {
                    // Pull the whole route toward the middle of the table, so the
                    // set draws in through the centre and opens out beyond it.
                    Vector2 mid = (f.SlotFrom + f.SlotTo) * 0.5f;
                    Vector2 inward = mid.sqrMagnitude < 1f ? side : -mid.normalized;
                    return straight + (inward * (swell * ThroughArc));
                }

                case ShufflePattern.Riffle:
                {
                    // Each half bows toward the other, so the two sides pass
                    // through each other rather than round.
                    float toward = f.SlotFrom.x < 0f ? 1f : -1f;
                    return straight + (side * (swell * RiffleArc * toward));
                }

                default:
                    // Fan: alternating sides, the loose hand-over-the-table churn.
                    return straight + (side * (swell * FanArc * f.ArcSign));
            }
        }

        /// <summary>
        /// Carries a tile around the middle of the table rather than across it,
        /// by travelling in angle and radius instead of a straight line. Every
        /// tile turns the same way, so the set reads as one rotating mass.
        /// </summary>
        private static Vector2 AroundPoint(Flying f, float e)
        {
            float fromAngle = Mathf.Atan2(f.SlotFrom.y, f.SlotFrom.x);
            float toAngle = Mathf.Atan2(f.SlotTo.y, f.SlotTo.x);

            float sweep = toAngle - fromAngle;
            if (sweep <= 0f)
            {
                sweep += Mathf.PI * 2f;
            }

            float angle = fromAngle + (sweep * e);
            float radius = Mathf.LerpUnclamped(f.SlotFrom.magnitude, f.SlotTo.magnitude, e);
            return new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
        }

        private void DrawStack(float t)
        {
            float e = EaseOutCubic(t);
            foreach (Flying f in _tiles)
            {
                // Carry on from wherever the last cycle left this tile.
                f.Rt.anchoredPosition = Vector2.LerpUnclamped(f.SlotTo, f.Stacked, e);
                f.Rt.localRotation = Quaternion.Euler(
                    0f, 0f, Mathf.LerpUnclamped(f.AngleTo, f.StackAngle, e));
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
            ShuffleScatter scatter = BuildScatter();
            _scatter = scatter;

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

                float k = i / (float)TileCount;
                ScatterPlacement rest = scatter.Placement(i, 0);
                Vector2 slot = new(rest.X, rest.Y);

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
                    SlotFrom = slot,
                    SlotTo = slot,
                    AngleFrom = rest.AngleDegrees,
                    AngleTo = rest.AngleDegrees,
                    ArcSign = (i % 2 == 0) ? 1f : -1f,
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

        /// <summary>
        /// Sizes the scatter field. The board canvas is constant-pixel, so this
        /// is measured off the live rect rather than assumed — on a narrow
        /// screen the cells tighten and the tiles overlap more, instead of the
        /// set running off the table.
        /// </summary>
        private ShuffleScatter BuildScatter()
        {
            // The field carries the columns plus the slack each row slides
            // within, so a staggered row still lands on the table.
            float wanted = (GridColumns + (2f * ShuffleScatter.StaggerFraction)) * CellW;

            Rect rect = _root.rect;
            float width = Mathf.Min(
                wanted,
                Mathf.Max(CellW, rect.width - (FieldMargin * 2f)));
            float height = Mathf.Min(
                GridRows * CellH,
                Mathf.Max(CellH, rect.height - (FieldMargin * 2f)));

            return new ShuffleScatter(
                TileCount, GridColumns, width, height, RestAngleSpread, ScatterJitter);
        }

        /// <summary>
        /// Sends every tile to a freshly dealt cell. This is the shuffle itself:
        /// tiles cross the table and each other, rather than jostling in place.
        /// </summary>
        private void Rescatter(int cycle)
        {
            ShuffleScatter? scatter = _scatter;
            if (scatter == null)
            {
                return;
            }

            _pattern = ShuffleScatter.PatternOf(cycle);

            for (int i = 0; i < _tiles.Count; i++)
            {
                Flying f = _tiles[i];
                f.SlotFrom = f.SlotTo;
                f.AngleFrom = f.AngleTo;

                ScatterPlacement next = scatter.Placement(i, cycle);
                f.SlotTo = new Vector2(next.X, next.Y);
                f.AngleTo = next.AngleDegrees;
                f.ArcSign = ((i + cycle) % 2 == 0) ? 1f : -1f;
            }

            ReorderTiles(cycle);
        }

        /// <summary>
        /// Re-deals which tile draws over which, so overlapping pairs keep
        /// swapping rather than one always sitting on top. Applied in ascending
        /// order so each call lands where it is put.
        /// </summary>
        private void ReorderTiles(int cycle)
        {
            ShuffleScatter? scatter = _scatter;
            if (scatter == null)
            {
                return;
            }

            _drawOrder.Clear();
            for (int i = 0; i < _tiles.Count; i++)
            {
                _drawOrder.Add(i);
            }

            _drawOrder.Sort((a, b) =>
                scatter.CellOf(a, cycle).CompareTo(scatter.CellOf(b, cycle)));

            for (int k = 0; k < _drawOrder.Count; k++)
            {
                _tiles[_drawOrder[k]].Rt.SetSiblingIndex(_tileBaseIndex + k);
            }
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

        private static float EaseInOutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            if (t < 0.5f)
            {
                return 4f * t * t * t;
            }

            float inv = (-2f * t) + 2f;
            return 1f - (inv * inv * inv * 0.5f);
        }

        private static float EaseInCubic(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t;
        }
    }
}
