#nullable enable
using System;
using Pose.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Pose.Game
{
    /// <summary>
    /// Visual orientation of a tile. Portrait = 60 wide × 120 tall, two pips
    /// stacked top/bottom. Landscape = 120 wide × 60 tall, two pips side-by-
    /// side left/right.
    /// </summary>
    public enum TileOrientation
    {
        Portrait,
        Landscape,
    }

    /// <summary>
    /// How the tile responds to input.
    /// <list type="bullet">
    ///   <item><b>None</b>: dim, no events. Used for non-current players' tiles
    ///         and for the current player's un-playable tiles.</item>
    ///   <item><b>Display</b>: bright, no events. Used for chain tiles (they
    ///         render at full brightness but never play.)</item>
    ///   <item><b>Click</b>: bright, fires <see cref="TileView.Clicked"/> on
    ///         tap. Used when a tile is playable but there's no meaningful
    ///         end choice — either it has only one legal placement, or both
    ///         chain ends share the same pip value so the result is identical.</item>
    ///   <item><b>Drag</b>: bright, fires drag events. Used when the player
    ///         must pick which end (tile matches both ends and the two pip
    ///         values differ).</item>
    /// </list>
    /// </summary>
    public enum TileInteractionMode
    {
        None,
        Display,
        Click,
        Drag,
    }

    /// <summary>
    /// Renders a single domino tile. Orientation chosen via <see cref="Init"/>;
    /// interaction mode set per-render via <see cref="Mode"/>. Drag-aware:
    /// Drag-mode tiles can be lifted out of the hand and dropped onto a
    /// chain end's <see cref="EndDropZone"/>.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class TileView : MonoBehaviour,
        IPointerClickHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        /// <summary>Default tile size — opponents' hands and the chain.</summary>
        public const float ShortDim = 60f;

        /// <inheritdoc cref="ShortDim"/>
        public const float LongDim = 120f;

        /// <summary>
        /// The local player's hand. Bigger than everyone else's because it is
        /// the only hand you aim at — opponents' tiles render as backs, so
        /// there is nothing on them to read and no reason to spend width.
        /// </summary>
        public const float LocalShortDim = 65f;

        /// <inheritdoc cref="LocalShortDim"/>
        public const float LocalLongDim = 130f;

        // True white, not cream. At the larger size the tile has to stay the
        // lightest thing on screen or it stops reading as the subject
        // (DESIGN_SYSTEM.md §1).
        private static readonly Color BodyColor = Color.white;
        private static readonly Color PipColor = new(0.10f, 0.07f, 0.06f);
        // Near-black, matching the pips. The old brown read as a decorative
        // inlay; on a real tile the centre line is just a moulded groove.
        private static readonly Color DividerColor = new(0.16f, 0.14f, 0.13f);

        // Depth, in two layers that both point straight DOWN so they agree
        // with the string lights overhead. The old single shadow threw up and
        // to the right, implying a light source below-left, and that
        // contradiction is what made tiles read as stickers on the felt.
        //
        //   SideColor  — the tile's own edge, its thickness.
        //   ShadowColor — the shadow it casts on the table.
        //
        // Unity's Shadow component is a hard offset with no blur, so the cast
        // shadow is a stepped silhouette rather than a soft one. A genuinely
        // soft shadow needs a pre-blurred sprite behind the tile; not worth a
        // texture until the art pass.
        private static readonly Color SideColor = new(0.79f, 0.76f, 0.69f);
        private static readonly Color ShadowColor = new(0f, 0f, 0f, 0.5f);
        private const float SideDepth = 4f;
        private const float CastDepth = 7f;

        // A dimmed tile stays SOLID — it is a physical domino, so it never goes
        // see-through and shows the table through itself. Dimming darkens the
        // face instead, which is what "out of turn" looks like under a lamp.
        private static readonly Color DimmedBodyColor = new(0.67f, 0.67f, 0.67f);
        private static readonly Color DimmedSideColor = new(0.50f, 0.50f, 0.50f);

        // Corner rounding as a fraction of the sprite, baked into its alpha.
        private const float CornerRadius01 = 0.12f;
        private static Sprite? _bodySprite;

        // Pips grow with the tile so they stay countable at a glance.
        private const float DotSizeRatio = 0.22f;
        // Divider splitting the tile's two pip halves: a hairline groove with a
        // small round node at its midpoint. The node is the reason the hairline
        // works — it gives the eye something to catch, so the line can stay
        // thin without landscape tiles reading as one undivided rectangle,
        // which is what the old heavy bar was compensating for.
        private const float DividerRatio = 0.032f;
        private const float DividerNodeRatio = 0.10f;

        // Where the ink actually sits inside divider.png, measured from the
        // file: the bar runs the middle 48% of the sprite's height and 14.8% of
        // its width, and everything around it is transparent padding. Sizing
        // the frame to the tile therefore drew a 2-unit hairline across half
        // the tile's width. These let the frame be derived from the bar we want
        // rather than from the sprite's bounds.
        private const float DividerArtLengthFraction = 0.48f;
        private const float DividerArtWidthFraction = 0.148f;

        // The bar we actually want on screen, as fractions of the tile's short
        // side: nearly the full width, and thick enough to read as a moulded
        // groove rather than a scratch.
        private const float DividerBarLengthRatio = 0.90f;
        private const float DividerBarThicknessRatio = 0.075f;

        private float DividerThickness => _shortDim * DividerRatio;
        private float DividerNodeSize => _shortDim * DividerNodeRatio;

        private static readonly Vector2[][] DotPositions =
        {
            Array.Empty<Vector2>(),
            new[] { new Vector2(0.5f, 0.5f) },
            new[] { new Vector2(0.75f, 0.75f), new Vector2(0.25f, 0.25f) },
            new[]
            {
                new Vector2(0.75f, 0.75f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.25f, 0.25f),
            },
            new[]
            {
                new Vector2(0.25f, 0.75f), new Vector2(0.75f, 0.75f),
                new Vector2(0.25f, 0.25f), new Vector2(0.75f, 0.25f),
            },
            new[]
            {
                new Vector2(0.25f, 0.75f), new Vector2(0.75f, 0.75f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.25f, 0.25f), new Vector2(0.75f, 0.25f),
            },
            new[]
            {
                new Vector2(0.25f, 0.80f), new Vector2(0.75f, 0.80f),
                new Vector2(0.25f, 0.50f), new Vector2(0.75f, 0.50f),
                new Vector2(0.25f, 0.20f), new Vector2(0.75f, 0.20f),
            },
        };

        private static Sprite? _dotSprite;

        // ---- 2-tap selection state (static, shared across all tiles) -----

        /// <summary>
        /// When true, a tile's first click selects (lifts + highlights) and
        /// the second click plays it. When false, a click plays immediately.
        /// Set by <see cref="GameSettings"/> at boot. Defaults to false so
        /// in-progress games keep behaving even before settings are read.
        /// </summary>
        public static bool TwoTapModeStatic { get; set; }

        /// <summary>
        /// Sprites every tile draws from, or null to draw procedurally. Static
        /// because tiles are created from code all over the board — hands, the
        /// chain, the Last Play badge — and none of them are prefabs with an
        /// Inspector slot to wire. <see cref="BoardBootstrap"/> sets this once
        /// at boot from its serialized <see cref="TileArtSet"/>.
        /// </summary>
        public static TileArtSet? Art { get; set; }

        private static TileView? _currentlySelected;

        public Tile Tile { get; private set; }

        public event Action<TileView>? Clicked;
        public event Action<TileView>? DragStarted;
        public event Action<TileView>? DragEnded;

        /// <summary>
        /// Raised when a two-end tile is tapped and armed. The board responds
        /// by lighting both playable ends so the player can pick one.
        /// </summary>
        public event Action<TileView>? Selected;

        /// <summary>Raised when an armed tile is tapped again, cancelling it.</summary>
        public event Action<TileView>? Deselected;

        private TileOrientation _orientation = TileOrientation.Portrait;

        // Per-instance so one hand can be bigger than another. Defaults to the
        // shared size; the local hand overrides via Init.
        private float _shortDim = ShortDim;
        private float _longDim = LongDim;

        // Multiplier on the edge and cast-shadow depth. The chain runs its
        // tiles almost touching, so it renders at a fraction of full depth.
        private float _depthScale = 1f;

        private bool _layoutBuilt;

        private RectTransform? _firstPipPanel;
        private RectTransform? _secondPipPanel;
        private Image? _body;
        private Shadow? _sideShadow;
        private Shadow? _castShadow;
        private CanvasGroup? _canvasGroup;
        private Outline? _selectionOutline;
        private bool _isSelected;

        private TileInteractionMode _mode = TileInteractionMode.None;

        private static readonly Color SelectionOutlineColor = new(1f, 0.92f, 0.50f, 1f);
        private const float SelectionScale = 1.10f;

        // How the tile behaves in the hand while picked up: bigger, tilted, and
        // throwing a deeper shadow, so it reads as lifted off the table rather
        // than sliding along it.
        private const float DragScale = 1.18f;
        private const float DragTiltDegrees = -5f;
        private const float DragShadowMultiplier = 3.5f;
        private const int DragSortingOrder = 30000;

        // Drag state.
        private Transform? _originalParent;
        private int _originalSiblingIndex;
        private Vector3 _originalLocalPosition;

        // The layout group that owns this tile in the hand drives its anchors
        // and size. Reparenting to the canvas root leaves those values behind,
        // so they are captured here and restored if the drag is cancelled.
        private Vector2 _originalAnchorMin;
        private Vector2 _originalAnchorMax;
        private Vector2 _originalPivot;
        private Vector2 _originalSizeDelta;
        private Canvas? _rootCanvas;
        private bool _dropAccepted;
        private bool _dragging;

        public TileInteractionMode Mode
        {
            get => _mode;
            set
            {
                _mode = value;
                if (_canvasGroup == null)
                {
                    return;
                }
                // Bright for Display/Click/Drag; dim for None. Dimming is a
                // COLOUR change, never an alpha one — the old alpha fade let the
                // table show through the tile, which read as ghostly plastic
                // rather than a domino sitting out of turn.
                bool dim = value == TileInteractionMode.None;
                _canvasGroup.alpha = 1f;
                if (_body != null)
                {
                    _body.color = dim ? DimmedBodyColor : BodyColor;
                }
                if (_sideShadow != null)
                {
                    _sideShadow.effectColor = dim ? DimmedSideColor : SideColor;
                }
                // Only Click and Drag receive events. Display tiles render
                // bright but ignore input (chain tiles).
                bool receivesInput = value == TileInteractionMode.Click
                    || value == TileInteractionMode.Drag;
                _canvasGroup.interactable = receivesInput;
                _canvasGroup.blocksRaycasts = receivesInput;
            }
        }

        /// <summary>
        /// Fixes the tile's orientation and size. Must be called before the
        /// first <see cref="Setup"/>; once the visuals are built the call is
        /// ignored, because size is baked into the layout element and the pip
        /// diameters.
        /// </summary>
        /// <param name="orientation">Portrait (hand) or landscape (side hand).</param>
        /// <param name="shortDim">
        /// The tile's short side. Defaults to <see cref="ShortDim"/>; the local
        /// player's hand passes <see cref="LocalShortDim"/>.
        /// </param>
        /// <param name="longDim">
        /// The tile's long side. Defaults to <see cref="LongDim"/>.
        /// </param>
        /// <param name="depthScale">
        /// Multiplier on the tile's edge and cast-shadow depth. The chain
        /// passes a fraction because its tiles nearly touch.
        /// </param>
        public void Init(
            TileOrientation orientation,
            float shortDim = ShortDim,
            float longDim = LongDim,
            float depthScale = 1f)
        {
            if (_layoutBuilt)
            {
                return;
            }
            _orientation = orientation;
            _shortDim = shortDim;
            _longDim = longDim;
            _depthScale = depthScale;
        }

        /// <summary>
        /// The shared rounded-rectangle sprite every tile body uses. Flat white
        /// with the corner rounding baked into its alpha — the sprite stretches
        /// to the tile rect, so on a 1:2 tile the corners read as slightly
        /// elliptical, which at this radius is not distinguishable from round.
        /// </summary>
        private static Sprite BodySprite()
        {
            _bodySprite ??= GradientSprite.RoundedDiagonal(
                CornerRadius01, Color.white, Color.white);
            return _bodySprite;
        }

        public void NotifyDropAccepted()
        {
            _dropAccepted = true;
        }

        public void Setup(Tile tile)
        {
            EnsureLayoutBuilt();
            Tile = tile;
            ClearChildren(_firstPipPanel!);
            ClearChildren(_secondPipPanel!);
            RenderPips(_firstPipPanel!, tile.A, PipDiameter);
            RenderPips(_secondPipPanel!, tile.B, PipDiameter);
        }

        /// <summary>
        /// Renders the tile with EXPLICIT pip values on the two panels rather
        /// than the canonical Tile.A / Tile.B order. Used by the chain renderer
        /// so a placed [3|5] played on a chain that ended in 5 shows up with
        /// the 5 facing inward — the canonical Setup would render it backwards
        /// (3 inward, 5 outward) because Tile stores pips in sorted order.
        ///
        /// For portrait orientation: <paramref name="firstPip"/> is the TOP pip,
        /// <paramref name="secondPip"/> is the BOTTOM. For landscape orientation:
        /// first is the LEFT pip, second is the RIGHT.
        /// </summary>
        public void Setup(Tile tile, byte firstPip, byte secondPip)
        {
            EnsureLayoutBuilt();
            Tile = tile;
            ClearChildren(_firstPipPanel!);
            ClearChildren(_secondPipPanel!);
            RenderPips(_firstPipPanel!, firstPip, PipDiameter);
            RenderPips(_secondPipPanel!, secondPip, PipDiameter);
        }

        /// <summary>
        /// Renders this tile as a face-down "back" — no pips, but the same
        /// cream BodyColor as a face-up tile (Giselle's preference — reads as
        /// a blank tile rather than a flipped-over piece). Used for the
        /// opponent's hand in online play, where we know HOW MANY tiles they
        /// hold but not WHICH.
        /// </summary>
        public void SetupAsBack()
        {
            EnsureLayoutBuilt();
            ClearChildren(_firstPipPanel!);
            ClearChildren(_secondPipPanel!);
        }

        // ---- Input handlers ------------------------------------------------

        public void OnPointerClick(PointerEventData eventData)
        {
            // Display / None mode tiles are not tappable.
            if (_mode != TileInteractionMode.Click && _mode != TileInteractionMode.Drag)
            {
                return;
            }

            // A Drag-mode tile is one that can legally go on EITHER end, on two
            // different pips. Tapping it never plays it, in either tap mode —
            // which end it lands on is the player's decision, not the rule
            // engine's enumeration order. Tapping selects it and lights both
            // ends; the player then taps an end, or drags the tile to one.
            //
            // This used to auto-play the first legal placement, so a [6|4]
            // tapped on a board open at 6 and 4 always went down on the 6 and
            // there was no way to ask for the 4.
            if (_mode == TileInteractionMode.Drag)
            {
                if (_currentlySelected == this)
                {
                    SetSelected(false);
                    _currentlySelected = null;
                    Deselected?.Invoke(this);
                    return;
                }

                if (_currentlySelected != null)
                {
                    _currentlySelected.SetSelected(false);
                }
                SetSelected(true);
                _currentlySelected = this;
                Selected?.Invoke(this);
                return;
            }

            // Click-mode tiles have only one possible placement, so a tap is
            // unambiguous and plays it — immediately in 1-tap mode, on the
            // confirming tap in 2-tap mode.
            if (!TwoTapModeStatic)
            {
                Clicked?.Invoke(this);
                return;
            }

            if (_currentlySelected == this)
            {
                SetSelected(false);
                _currentlySelected = null;
                Clicked?.Invoke(this);
                return;
            }

            if (_currentlySelected != null)
            {
                _currentlySelected.SetSelected(false);
                _currentlySelected.Deselected?.Invoke(_currentlySelected);
            }
            SetSelected(true);
            _currentlySelected = this;
        }

        /// <summary>
        /// Clears any in-progress 2-tap selection. Called by BoardBootstrap
        /// when the local player's turn ends (a move was applied) or the
        /// hand is about to be re-rendered.
        /// </summary>
        public static void ClearSelection()
        {
            if (_currentlySelected != null)
            {
                _currentlySelected.SetSelected(false);
                _currentlySelected = null;
            }
        }

        /// <summary>
        /// Picks the tile up off the table, or sets it back down: scale, tilt
        /// and a deeper shadow. The shadow is what actually sells the height —
        /// a tile that only grows reads as zooming, not lifting.
        /// </summary>
        private void ApplyDragLift(bool lifted)
        {
            transform.localScale = lifted
                ? new Vector3(DragScale, DragScale, 1f)
                : Vector3.one;
            transform.localRotation = lifted
                ? Quaternion.Euler(0f, 0f, DragTiltDegrees)
                : Quaternion.identity;

            float multiplier = lifted ? DragShadowMultiplier : 1f;
            if (_sideShadow != null)
            {
                _sideShadow.effectDistance = new Vector2(0f, -SideDepth * _depthScale * multiplier);
            }
            if (_castShadow != null)
            {
                _castShadow.effectDistance = new Vector2(0f, -CastDepth * _depthScale * multiplier);
            }
        }

        private void SetSelected(bool selected)
        {
            _isSelected = selected;
            if (_selectionOutline != null)
            {
                _selectionOutline.enabled = selected;
            }
            transform.localScale = selected
                ? new Vector3(SelectionScale, SelectionScale, 1f)
                : Vector3.one;
        }

        private void OnDestroy()
        {
            if (_currentlySelected == this)
            {
                _currentlySelected = null;
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            // ANY playable tile can be dragged, not just the two-end ones.
            // Restricting this to Drag mode meant dragging a single-placement
            // tile did nothing at all — the tile stayed in the hand and only
            // played on release, which reads as "the drag is invisible".
            if (_mode != TileInteractionMode.Drag && _mode != TileInteractionMode.Click)
            {
                return;
            }

            _dropAccepted = false;
            _dragging = true;
            _originalParent = transform.parent;
            _originalSiblingIndex = transform.GetSiblingIndex();
            _originalLocalPosition = transform.localPosition;

            RectTransform rt = (RectTransform)transform;
            _originalAnchorMin = rt.anchorMin;
            _originalAnchorMax = rt.anchorMax;
            _originalPivot = rt.pivot;
            _originalSizeDelta = rt.sizeDelta;

            // Get the rootCanvas (topmost in hierarchy) so we can reparent to
            // the absolute root layer — not whatever sub-canvas the hand might
            // be nested in.
            Canvas? parentCanvas = GetComponentInParent<Canvas>();
            _rootCanvas = parentCanvas?.rootCanvas ?? parentCanvas;
            if (_rootCanvas != null)
            {
                transform.SetParent(_rootCanvas.transform, worldPositionStays: true);
                transform.SetAsLastSibling();

                // Add a Canvas component with overrideSorting and a high
                // sortingOrder so the dragged tile renders above the board
                // background, played tiles, and everything else. Without
                // this the tile can disappear behind the board art mid-drag
                // because sibling order alone doesn't override sorting layer.
                Canvas dragOverlay = gameObject.GetComponent<Canvas>();
                if (dragOverlay == null)
                {
                    dragOverlay = gameObject.AddComponent<Canvas>();
                    // GraphicRaycaster lets the dragged tile still receive
                    // pointer events while floating in the overlay; without
                    // it, OnDrag callbacks would stop firing once the Canvas
                    // grants its own sort context.
                    if (gameObject.GetComponent<GraphicRaycaster>() == null)
                    {
                        gameObject.AddComponent<GraphicRaycaster>();
                    }
                }
                dragOverlay.overrideSorting = true;
                // Match the root canvas's sorting LAYER, not just its order. A
                // high order inside a different layer still draws underneath —
                // which is how a dragged tile ends up invisible rather than on
                // top of the board.
                dragOverlay.sortingLayerID = _rootCanvas.sortingLayerID;
                dragOverlay.sortingOrder = DragSortingOrder;

                // The hand's layout group drove this tile's anchors and size.
                // Away from that group those values no longer describe a tile —
                // stretch anchors turn sizeDelta into an inset, and the rect
                // collapses. Pin it to an explicit centred rect at its real
                // size so it is visible wherever it is put.
                RectTransform dragRt = (RectTransform)transform;
                dragRt.anchorMin = new Vector2(0.5f, 0.5f);
                dragRt.anchorMax = new Vector2(0.5f, 0.5f);
                dragRt.pivot = new Vector2(0.5f, 0.5f);
                dragRt.sizeDelta = _orientation == TileOrientation.Portrait
                    ? new Vector2(_shortDim, _longDim)
                    : new Vector2(_longDim, _shortDim);
            }

            if (_canvasGroup != null)
            {
                // Never let a lifted tile be see-through; the player is meant
                // to be tracking it.
                _canvasGroup.alpha = 1f;
                _canvasGroup.blocksRaycasts = false;
            }

            ApplyDragLift(true);
            DragStarted?.Invoke(this);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragging)
            {
                return;
            }

            // Use WORLD point conversion instead of LOCAL point so we can
            // set transform.position directly. The previous local-point
            // approach was wrong when the tile's anchor didn't match the
            // canvas pivot (the hand tiles have anchor (0.5, 1) but the
            // canvas pivot is (0.5, 0.5), so the tile jumped half a canvas
            // height off — Giselle saw "the tile is at the left center of
            // the screen instead of under my finger"). ScreenPointToWorld
            // returns a world position any RectTransform can be placed at
            // regardless of anchor / pivot.
            if (_rootCanvas != null)
            {
                RectTransform canvasRt = (RectTransform)_rootCanvas.transform;
                Camera? cam = _rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                    ? null
                    : _rootCanvas.worldCamera;
                if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                        canvasRt, eventData.position, cam, out Vector3 worldPoint))
                {
                    transform.position = worldPoint;
                    return;
                }
            }

            transform.position = eventData.position;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_dragging)
            {
                return;
            }
            _dragging = false;

            DragEnded?.Invoke(this);

            if (_dropAccepted)
            {
                Destroy(gameObject);
                return;
            }

            // A Click-mode tile has no meaningful choice to make — either one
            // legal placement, or two onto ends showing the same pip, which
            // come to the same board. So letting go of it anywhere plays it:
            // there is nowhere else it could have gone, and making the player
            // drag it all the way onto a small target to say something the game
            // already knows is just work.
            //
            // This also covers the case it was written for, a press that
            // wandered a few pixels and so arrived as a drag rather than a tap.
            //
            // Never for a two-end tile. Dropping one of those away from an end
            // must return it to the hand, because the end is exactly what the
            // player has not said yet.
            bool treatAsTap = _mode == TileInteractionMode.Click;

            ApplyDragLift(false);

            transform.SetParent(_originalParent, worldPositionStays: false);
            transform.SetSiblingIndex(_originalSiblingIndex);
            transform.localPosition = _originalLocalPosition;

            // Hand back the anchors and size the layout group was driving, so
            // the tile drops into its slot instead of sitting centred on it.
            RectTransform rt = (RectTransform)transform;
            rt.anchorMin = _originalAnchorMin;
            rt.anchorMax = _originalAnchorMax;
            rt.pivot = _originalPivot;
            rt.sizeDelta = _originalSizeDelta;

            if (_canvasGroup != null)
            {
                _canvasGroup.blocksRaycasts = _mode == TileInteractionMode.Click
                    || _mode == TileInteractionMode.Drag;
            }

            // Strip the temporary drag-overlay Canvas + GraphicRaycaster so
            // back in the hand the tile uses its parent canvas's sort order.
            Canvas? dragOverlay = gameObject.GetComponent<Canvas>();
            if (dragOverlay != null)
            {
                Destroy(dragOverlay);
            }
            GraphicRaycaster? overlayRaycaster = gameObject.GetComponent<GraphicRaycaster>();
            if (overlayRaycaster != null)
            {
                Destroy(overlayRaycaster);
            }

            if (treatAsTap)
            {
                Clicked?.Invoke(this);
            }
        }

        // ---- Visual construction ------------------------------------------

        private void EnsureLayoutBuilt()
        {
            if (_layoutBuilt)
            {
                return;
            }
            BuildVisuals();
            _layoutBuilt = true;
        }

        private void BuildVisuals()
        {
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();

            Sprite? artBody = Art?.BodyFor(_orientation);

            _body = gameObject.AddComponent<Image>();
            _body.sprite = artBody != null ? artBody : BodySprite();
            _body.color = BodyColor;
            _body.raycastTarget = true;

            // The tile's own thickness. Drawn as an offset copy of the body
            // when there is no art, but the art bakes its edge into the sprite,
            // so adding this on top of it would double the lip.
            //
            // Depth scales with the tile. Chain tiles butt up against each
            // other with only 2 units between them, so a full-depth shadow
            // would spill across the neighbour below and the chain would read
            // as smeared rather than laid out.
            if (artBody == null)
            {
                _sideShadow = gameObject.AddComponent<Shadow>();
                _sideShadow.effectColor = SideColor;
                _sideShadow.effectDistance = new Vector2(0f, -SideDepth * _depthScale);
            }

            _castShadow = gameObject.AddComponent<Shadow>();
            _castShadow.effectColor = ShadowColor;
            _castShadow.effectDistance = new Vector2(0f, -CastDepth * _depthScale);

            // Yellow outline used for the 2-tap selection highlight. Disabled
            // by default; SetSelected toggles it on/off.
            _selectionOutline = gameObject.AddComponent<Outline>();
            _selectionOutline.effectColor = SelectionOutlineColor;
            _selectionOutline.effectDistance = new Vector2(4f, -4f);
            _selectionOutline.enabled = false;

            float w = _orientation == TileOrientation.Portrait ? _shortDim : _longDim;
            float h = _orientation == TileOrientation.Portrait ? _longDim : _shortDim;

            LayoutElement layout = gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = w;
            layout.preferredHeight = h;

            RectTransform rt = (RectTransform)transform;
            rt.sizeDelta = new Vector2(w, h);

            if (_orientation == TileOrientation.Portrait)
            {
                _firstPipPanel = CreatePipPanel(
                    "TopPip", new Vector2(0f, 0.5f), new Vector2(1f, 1f));
                _secondPipPanel = CreatePipPanel(
                    "BottomPip", new Vector2(0f, 0f), new Vector2(1f, 0.5f));
                CreateDivider(horizontal: true);
            }
            else
            {
                _firstPipPanel = CreatePipPanel(
                    "LeftPip", new Vector2(0f, 0f), new Vector2(0.5f, 1f));
                _secondPipPanel = CreatePipPanel(
                    "RightPip", new Vector2(0.5f, 0f), new Vector2(1f, 1f));
                CreateDivider(horizontal: false);
            }

            // Apply the current Mode now that the CanvasGroup exists. This
            // ensures the alpha/raycast flags reflect whatever was set before
            // BuildVisuals ran.
            Mode = _mode;
        }

        private RectTransform CreatePipPanel(string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            GameObject child = new(name, typeof(RectTransform));
            child.transform.SetParent(transform, worldPositionStays: false);

            RectTransform rt = (RectTransform)child.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        private void CreateDivider(bool horizontal)
        {
            // The art divider is one sprite carrying bar and node together, so
            // it replaces both procedural pieces. It is drawn vertically, which
            // suits a landscape tile; a portrait tile turns it a quarter turn.
            if (Art?.Divider != null)
            {
                CreateArtDivider(horizontal, Art.Divider);
                return;
            }

            GameObject divider = new("Divider", typeof(RectTransform));
            divider.transform.SetParent(transform, worldPositionStays: false);
            RectTransform divRt = (RectTransform)divider.transform;

            if (horizontal)
            {
                divRt.anchorMin = new Vector2(0.05f, 0.5f);
                divRt.anchorMax = new Vector2(0.95f, 0.5f);
                divRt.offsetMin = new Vector2(0f, -DividerThickness * 0.5f);
                divRt.offsetMax = new Vector2(0f, DividerThickness * 0.5f);
            }
            else
            {
                divRt.anchorMin = new Vector2(0.5f, 0.05f);
                divRt.anchorMax = new Vector2(0.5f, 0.95f);
                divRt.offsetMin = new Vector2(-DividerThickness * 0.5f, 0f);
                divRt.offsetMax = new Vector2(DividerThickness * 0.5f, 0f);
            }

            Image divImg = divider.AddComponent<Image>();
            divImg.color = DividerColor;
            divImg.raycastTarget = false;

            // The node, centred on the tile and so on the line.
            GameObject node = new("DividerNode", typeof(RectTransform));
            node.transform.SetParent(transform, worldPositionStays: false);
            RectTransform nodeRt = (RectTransform)node.transform;
            nodeRt.anchorMin = new Vector2(0.5f, 0.5f);
            nodeRt.anchorMax = new Vector2(0.5f, 0.5f);
            nodeRt.pivot = new Vector2(0.5f, 0.5f);
            nodeRt.anchoredPosition = Vector2.zero;
            float node1 = DividerNodeSize;
            nodeRt.sizeDelta = new Vector2(node1, node1);

            Image nodeImg = node.AddComponent<Image>();
            nodeImg.sprite = GetDotSprite();
            nodeImg.color = DividerColor;
            nodeImg.raycastTarget = false;
        }

        private static void ClearChildren(RectTransform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Destroy(parent.GetChild(i).gameObject);
            }
        }

        /// <summary>
        /// Pip diameter for this tile. Scales with the tile, so the local
        /// hand's larger tiles get proportionally larger pips.
        /// </summary>
        private float PipDiameter => _shortDim * DotSizeRatio;

        // Static because they touch no instance state beyond the size, which
        // the caller passes in — tiles differ in size now, so it can no longer
        // be read from a shared constant.
        private static void RenderPips(RectTransform panel, byte count, float diameter)
        {
            if (count > 6)
            {
                count = 6;
            }
            Vector2[] positions = DotPositions[count];
            for (int i = 0; i < positions.Length; i++)
            {
                CreateDot(panel, positions[i], diameter);
            }
        }

        private static void CreateDot(RectTransform parent, Vector2 normalizedPos, float diameter)
        {
            GameObject dot = new("Pip", typeof(RectTransform));
            dot.transform.SetParent(parent, worldPositionStays: false);

            RectTransform rt = (RectTransform)dot.transform;
            rt.anchorMin = normalizedPos;
            rt.anchorMax = normalizedPos;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(diameter, diameter);
            rt.anchoredPosition = Vector2.zero;

            Image img = dot.AddComponent<Image>();
            img.sprite = GetDotSprite();
            img.color = PipColor;
            img.raycastTarget = false;
        }

        /// <summary>
        /// Places the art divider. Sized from the tile's short side so it scales
        /// with the tile, and rotated rather than stretched so the node stays
        /// round instead of turning into an ellipse on a portrait tile.
        /// </summary>
        private void CreateArtDivider(bool horizontal, Sprite sprite)
        {
            GameObject divider = new("Divider", typeof(RectTransform));
            divider.transform.SetParent(transform, worldPositionStays: false);
            RectTransform rt = (RectTransform)divider.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;

            // Size the frame so the BAR lands where we want it, scaling up
            // through the sprite's padding. The two fractions are close enough
            // to the sprite's own aspect that the node stays round.
            float barLength = _shortDim * DividerBarLengthRatio;
            float barThickness = _shortDim * DividerBarThicknessRatio;
            rt.sizeDelta = new Vector2(
                barThickness / DividerArtWidthFraction,
                barLength / DividerArtLengthFraction);
            // Authored vertical: a landscape tile wants it as-is, a portrait
            // tile wants it lying across.
            rt.localRotation = horizontal ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.identity;

            Image img = divider.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            // Deliberately off: preserving the aspect would letterbox the
            // sprite back to its authored ratio and silently cancel the width
            // boost above.
            img.preserveAspect = false;
        }

        private static Sprite GetDotSprite()
        {
            if (Art?.Pip != null)
            {
                return Art.Pip;
            }

            if (_dotSprite != null)
            {
                return _dotSprite;
            }

            const int size = 64;
            Texture2D tex = new(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            Vector2 center = new(size / 2f, size / 2f);
            float radius = (size / 2f) - 1f;

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    float alpha = Mathf.Clamp01(radius - dist + 0.5f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();

            _dotSprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, size, size),
                pivot: new Vector2(0.5f, 0.5f),
                pixelsPerUnit: 100f);
            return _dotSprite;
        }
    }
}
