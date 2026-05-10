#nullable enable
using System;
using Pose.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Pose.Game
{
    /// <summary>
    /// A drop target at one end of the chain. Bound to a <see cref="ChainEnd"/>
    /// (LEFT or RIGHT). Hidden by default; shown by <see cref="BoardBootstrap"/>
    /// during a drag when the dragged tile is legally playable on this end.
    /// On a successful drop, raises <see cref="Dropped"/> with the dragged
    /// <see cref="TileView"/> and the bound end so the bootstrap can apply
    /// the matching legal move.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class EndDropZone : MonoBehaviour, IDropHandler
    {
        public const float Width = 100f;
        public const float Height = TileView.LongDim;

        private static readonly Color VisibleColor = new(1f, 0.92f, 0.50f, 0.30f);
        private const float LabelFontSize = 36f;

        public ChainEnd End { get; private set; }
        public event Action<TileView, ChainEnd>? Dropped;

        private CanvasGroup? _canvasGroup;
        private TextMeshProUGUI? _label;

        private void Awake()
        {
            BuildVisuals();
        }

        public void Init(ChainEnd end)
        {
            End = end;
        }

        /// <summary>
        /// Toggles the zone's visibility (alpha + raycast blocking). When
        /// <paramref name="visible"/> is true and a non-empty
        /// <paramref name="label"/> is supplied, displays it (typically the
        /// pip value the dragged tile must match).
        /// </summary>
        public void SetVisible(bool visible, string label = "")
        {
            _canvasGroup!.alpha = visible ? 1f : 0f;
            _canvasGroup.blocksRaycasts = visible;
            _label!.text = label;
        }

        public void OnDrop(PointerEventData eventData)
        {
            TileView? tv = eventData.pointerDrag != null
                ? eventData.pointerDrag.GetComponent<TileView>()
                : null;
            if (tv == null)
            {
                return;
            }
            Dropped?.Invoke(tv, End);
        }

        private void BuildVisuals()
        {
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();

            Image bg = gameObject.AddComponent<Image>();
            bg.color = VisibleColor;
            bg.raycastTarget = true;

            LayoutElement le = gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = Width;
            le.preferredHeight = Height;

            // Label sits inside the zone showing the matching pip value.
            GameObject labelGo = new("Label", typeof(RectTransform));
            labelGo.transform.SetParent(transform, worldPositionStays: false);
            RectTransform labelRt = (RectTransform)labelGo.transform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;

            _label = labelGo.AddComponent<TextMeshProUGUI>();
            _label.alignment = TextAlignmentOptions.Center;
            _label.fontSize = LabelFontSize;
            _label.fontStyle = FontStyles.Bold;
            _label.color = new Color(0.97f, 0.95f, 0.88f);
            _label.text = string.Empty;
            _label.raycastTarget = false;

            // Start hidden — BoardBootstrap will show on drag-start.
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;
        }
    }
}
