#nullable enable
using System;
using Pose.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Pose.Game
{
    /// <summary>
    /// Renders a single domino tile as a bone-coloured rectangle with two pip
    /// patterns stacked vertically (top half = pip A, bottom half = pip B), a
    /// dark divider line between them, and a soft drop shadow. Pip dots use
    /// dice-style positioning. Procedurally constructed in <see cref="Awake"/> —
    /// a small circular sprite is generated once and shared by every dot on
    /// every tile, so we don't need any art assets for the M1 step 4 spike.
    ///
    /// Click-aware: implements <see cref="IPointerClickHandler"/> and raises
    /// <see cref="Clicked"/> when tapped, *if* <see cref="Interactable"/> is true.
    /// Non-interactable tiles dim out via a <c>CanvasGroup</c> so the player can
    /// tell at a glance which tiles they're allowed to play.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class TileView : MonoBehaviour, IPointerClickHandler
    {
        public const float Width = 60f;
        public const float Height = 120f;

        // Bone/ivory body, dark walnut pips, brown divider, soft drop shadow.
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
        public event Action<Tile>? Clicked;

        private RectTransform? _topPipPanel;
        private RectTransform? _bottomPipPanel;
        private CanvasGroup? _canvasGroup;
        private bool _interactable = true;

        public bool Interactable
        {
            get => _interactable;
            set
            {
                _interactable = value;
                if (_canvasGroup != null)
                {
                    _canvasGroup.alpha = value ? 1f : DimmedAlpha;
                    _canvasGroup.interactable = value;
                    _canvasGroup.blocksRaycasts = value;
                }
            }
        }

        private void Awake()
        {
            BuildVisuals();
        }

        public void Setup(Tile tile)
        {
            Tile = tile;
            ClearChildren(_topPipPanel!);
            ClearChildren(_bottomPipPanel!);
            RenderPips(_topPipPanel!, tile.A);
            RenderPips(_bottomPipPanel!, tile.B);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!_interactable)
            {
                return;
            }
            Clicked?.Invoke(Tile);
        }

        private void BuildVisuals()
        {
            // CanvasGroup at the root so dimming + click-blocking propagate to all
            // children (body, divider, pips) with a single alpha/flag flip.
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();

            Image body = gameObject.AddComponent<Image>();
            body.color = BodyColor;
            body.raycastTarget = true;

            Shadow shadow = gameObject.AddComponent<Shadow>();
            shadow.effectColor = ShadowColor;
            shadow.effectDistance = new Vector2(3f, -3f);

            LayoutElement layout = gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = Width;
            layout.preferredHeight = Height;

            _topPipPanel = CreatePipPanel("TopPip", new Vector2(0f, 0.5f), new Vector2(1f, 1f));
            _bottomPipPanel = CreatePipPanel("BottomPip", new Vector2(0f, 0f), new Vector2(1f, 0.5f));

            // Divider line between the two halves. raycastTarget = false so a
            // click on the centre line still hits the body's IPointerClickHandler.
            GameObject divider = new("Divider", typeof(RectTransform));
            divider.transform.SetParent(transform, worldPositionStays: false);
            RectTransform divRt = (RectTransform)divider.transform;
            divRt.anchorMin = new Vector2(0.05f, 0.5f);
            divRt.anchorMax = new Vector2(0.95f, 0.5f);
            divRt.offsetMin = new Vector2(0f, -DividerThickness * 0.5f);
            divRt.offsetMax = new Vector2(0f, DividerThickness * 0.5f);
            Image divImg = divider.AddComponent<Image>();
            divImg.color = DividerColor;
            divImg.raycastTarget = false;
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
            float diameter = Width * DotSizeRatio;
            rt.sizeDelta = new Vector2(diameter, diameter);
            rt.anchoredPosition = Vector2.zero;

            Image img = dot.AddComponent<Image>();
            img.sprite = GetDotSprite();
            img.color = PipColor;
            // Dots must not intercept clicks — let the body's IPointerClickHandler
            // see every tap on the tile, including those that land on a pip.
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
