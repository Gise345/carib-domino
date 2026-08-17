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
        public const float LocalShortDim = 84f;

        /// <inheritdoc cref="LocalShortDim"/>
        public const float LocalLongDim = 168f;

        // True white, not cream. At the larger size the tile has to stay the
        // lightest thing on screen or it stops reading as the subject
        // (DESIGN_SYSTEM.md §1).
        private static readonly Color BodyColor = Color.white;
        private static readonly Color PipColor = new(0.10f, 0.07f, 0.06f);
        private static readonly Color DividerColor = new(0.40f, 0.30f, 0.22f);

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
        private const float SideDepth = 7f;
        private const float CastDepth = 14f;

        // Pips grow with the tile so they stay countable at a glance.
        private const float DotSizeRatio = 0.22f;
        // Divider splitting the tile's two pip halves. Proportional rather
        // than fixed: a flat 6 units reads as a moulded centre rule on the
        // 84-wide local tile but as a heavy bar on the 60-wide chain tiles.
        // 0.07 gives ~6 on the local hand and ~4 elsewhere, which is still
        // well clear of the hairline that used to make landscape bridge tiles
        // look like one undivided rectangle.
        private const float DividerRatio = 0.07f;

        private float DividerThickness => _shortDim * DividerRatio;
        private const float DimmedAlpha = 0.45f;

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

        private static TileView? _currentlySelected;

        public Tile Tile { get; private set; }

        public event Action<TileView>? Clicked;
        public event Action<TileView>? DragStarted;
        public event Action<TileView>? DragEnded;

        private TileOrientation _orientation = TileOrientation.Portrait;

        // Per-instance so one hand can be bigger than another. Defaults to the
        // shared size; the local hand overrides via Init.
        private float _shortDim = ShortDim;
        private float _longDim = LongDim;

        private bool _layoutBuilt;

        private RectTransform? _firstPipPanel;
        private RectTransform? _secondPipPanel;
        private CanvasGroup? _canvasGroup;
        private Outline? _selectionOutline;
        private bool _isSelected;

        private TileInteractionMode _mode = TileInteractionMode.None;

        private static readonly Color SelectionOutlineColor = new(1f, 0.92f, 0.50f, 1f);
        private const float SelectionScale = 1.10f;

        // Drag state.
        private Transform? _originalParent;
        private int _originalSiblingIndex;
        private Vector3 _originalLocalPosition;
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
                // Bright for Display/Click/Drag; dim for None.
                _canvasGroup.alpha = value == TileInteractionMode.None ? DimmedAlpha : 1f;
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
        public void Init(
            TileOrientation orientation,
            float shortDim = ShortDim,
            float longDim = LongDim)
        {
            if (_layoutBuilt)
            {
                return;
            }
            _orientation = orientation;
            _shortDim = shortDim;
            _longDim = longDim;
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
            RenderPips(_firstPipPanel!, tile.A);
            RenderPips(_secondPipPanel!, tile.B);
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
            RenderPips(_firstPipPanel!, firstPip);
            RenderPips(_secondPipPanel!, secondPip);
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

            if (!TwoTapModeStatic)
            {
                // 1-tap mode: Click tiles play immediately; Drag tiles ignore
                // taps and require the explicit drag-to-end interaction.
                if (_mode == TileInteractionMode.Click)
                {
                    Clicked?.Invoke(this);
                }
                return;
            }

            // 2-tap mode applies to both Click and Drag mode tiles. For a
            // Drag-mode tile the player can still drag for explicit end
            // choice; 2-tap plays the first legal placement (whichever the
            // rule engine returns first, typically LEFT) — handy when there
            // are only one or two tiles left and dragging is fiddly.
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
            if (_mode != TileInteractionMode.Drag)
            {
                return;
            }

            _dropAccepted = false;
            _dragging = true;
            _originalParent = transform.parent;
            _originalSiblingIndex = transform.GetSiblingIndex();
            _originalLocalPosition = transform.localPosition;

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
                dragOverlay.sortingOrder = 999;
            }

            if (_canvasGroup != null)
            {
                _canvasGroup.blocksRaycasts = false;
            }

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
            // Forgiveness: if a Click-mode tile got dragged past the threshold
            // (Unity treats it as a drag and won't fire OnPointerClick), still
            // treat the release as a click so the player isn't stuck.
            if (_mode == TileInteractionMode.Click)
            {
                Clicked?.Invoke(this);
                return;
            }

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

            transform.SetParent(_originalParent, worldPositionStays: false);
            transform.SetSiblingIndex(_originalSiblingIndex);
            transform.localPosition = _originalLocalPosition;
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

            Image body = gameObject.AddComponent<Image>();
            body.color = BodyColor;
            body.raycastTarget = true;

            // Order matters: the side band is added first so the cast shadow,
            // added second, is thrown by the body AND its edge together —
            // which is what a solid slab actually does.
            Shadow side = gameObject.AddComponent<Shadow>();
            side.effectColor = SideColor;
            side.effectDistance = new Vector2(0f, -SideDepth);

            Shadow cast = gameObject.AddComponent<Shadow>();
            cast.effectColor = ShadowColor;
            cast.effectDistance = new Vector2(0f, -CastDepth);

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
        }

        private static void ClearChildren(RectTransform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Destroy(parent.GetChild(i).gameObject);
            }
        }

        private static void RenderPips(RectTransform panel, byte count)
        {
            if (count > 6)
            {
                count = 6;
            }
            Vector2[] positions = DotPositions[count];
            for (int i = 0; i < positions.Length; i++)
            {
                CreateDot(panel, positions[i]);
            }
        }

        private static void CreateDot(RectTransform parent, Vector2 normalizedPos)
        {
            GameObject dot = new("Pip", typeof(RectTransform));
            dot.transform.SetParent(parent, worldPositionStays: false);

            RectTransform rt = (RectTransform)dot.transform;
            rt.anchorMin = normalizedPos;
            rt.anchorMax = normalizedPos;
            rt.pivot = new Vector2(0.5f, 0.5f);
            float diameter = _shortDim * DotSizeRatio;
            rt.sizeDelta = new Vector2(diameter, diameter);
            rt.anchoredPosition = Vector2.zero;

            Image img = dot.AddComponent<Image>();
            img.sprite = GetDotSprite();
            img.color = PipColor;
            img.raycastTarget = false;
        }

        private static Sprite GetDotSprite()
        {
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
