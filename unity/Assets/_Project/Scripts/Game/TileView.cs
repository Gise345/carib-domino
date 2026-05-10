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
        public const float ShortDim = 60f;
        public const float LongDim = 120f;

        private static readonly Color BodyColor = new(0.97f, 0.95f, 0.88f);
        private static readonly Color PipColor = new(0.10f, 0.07f, 0.06f);
        private static readonly Color DividerColor = new(0.40f, 0.30f, 0.22f);
        private static readonly Color ShadowColor = new(0f, 0f, 0f, 0.45f);

        private const float DotSizeRatio = 0.18f;
        private const float DividerThickness = 1.5f;
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

        public Tile Tile { get; private set; }

        public event Action<TileView>? Clicked;
        public event Action<TileView>? DragStarted;
        public event Action<TileView>? DragEnded;

        private TileOrientation _orientation = TileOrientation.Portrait;
        private bool _layoutBuilt;

        private RectTransform? _firstPipPanel;
        private RectTransform? _secondPipPanel;
        private CanvasGroup? _canvasGroup;

        private TileInteractionMode _mode = TileInteractionMode.None;

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

        public void Init(TileOrientation orientation)
        {
            if (_layoutBuilt)
            {
                return;
            }
            _orientation = orientation;
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

        // ---- Input handlers ------------------------------------------------

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_mode != TileInteractionMode.Click)
            {
                return;
            }
            Clicked?.Invoke(this);
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

            _rootCanvas ??= GetComponentInParent<Canvas>();
            if (_rootCanvas != null)
            {
                transform.SetParent(_rootCanvas.transform, worldPositionStays: true);
                transform.SetAsLastSibling();
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

            Shadow shadow = gameObject.AddComponent<Shadow>();
            shadow.effectColor = ShadowColor;
            shadow.effectDistance = new Vector2(3f, -3f);

            float w = _orientation == TileOrientation.Portrait ? ShortDim : LongDim;
            float h = _orientation == TileOrientation.Portrait ? LongDim : ShortDim;

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
            float diameter = ShortDim * DotSizeRatio;
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
