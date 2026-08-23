#nullable enable
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Pose.Game
{
    /// <summary>
    /// The popup a player sees the moment they choose <em>Continue as Guest</em>:
    /// what a guest session can't do, and the one tap that fixes it.
    ///
    /// It exists because the limits are otherwise discovered at the worst moment —
    /// mid-match, with a locked composer and no explanation. Saying it up front
    /// costs one dismissal and turns a dead end into an offer. The limits it
    /// lists are the ones the SERVER enforces (chat and voice need a real
    /// identity to be moderatable, ADR 0023 §3); it does not invent restrictions
    /// of its own.
    ///
    /// Built in code and mounted over whatever screen raised it.
    /// </summary>
    public sealed class GuestLimitsDialog : MonoBehaviour
    {
        private static readonly Color Scrim = new(0f, 0f, 0f, 0.78f);
        private static readonly Color Card = new(0.075f, 0.059f, 0.047f, 0.99f);
        private static readonly Color Gold = new(0.961f, 0.769f, 0.318f);
        private static readonly Color TextCol = new(0.957f, 0.929f, 0.882f);
        private static readonly Color Muted = new(0.702f, 0.643f, 0.533f);
        private static readonly Color Faint = new(0.490f, 0.443f, 0.361f);

        /// <summary>The guest limits, in the order they are listed.</summary>
        private static readonly string[] LimitKeys =
        {
            "guest_limit_chat",
            "guest_limit_voice",
            "guest_limit_progress",
        };

        private Action? _onCreateAccount;
        private Action? _onContinue;

        /// <summary>
        /// Builds and shows the popup over the given canvas.
        /// </summary>
        /// <param name="parent">The canvas (or full-screen root) to mount into.</param>
        /// <param name="onCreateAccount">Runs when the player chooses to sign up.</param>
        /// <param name="onContinue">Runs when the player continues as a guest.</param>
        /// <returns>The dialog, which destroys itself once a choice is made.</returns>
        public static GuestLimitsDialog Show(
            RectTransform parent,
            Action onCreateAccount,
            Action onContinue)
        {
            GameObject go = new("GuestLimitsDialog", typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            GuestLimitsDialog dialog = go.AddComponent<GuestLimitsDialog>();
            dialog._onCreateAccount = onCreateAccount;
            dialog._onContinue = onContinue;
            dialog.Build();
            return dialog;
        }

        private void Build()
        {
            Stretch((RectTransform)transform);
            transform.SetAsLastSibling();

            GameObject scrim = Child(transform, "Scrim");
            Stretch((RectTransform)scrim.transform);
            Image sbg = scrim.AddComponent<Image>();
            sbg.color = Scrim;
            // No tap-to-dismiss: the player should read the two options and pick
            // one, and a stray tap behind a modal shouldn't count as a choice.
            scrim.AddComponent<Button>().transition = Selectable.Transition.None;

            GameObject card = Child(transform, "Card");
            RectTransform rt = (RectTransform)card.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(760f, 620f);
            Image cbg = card.AddComponent<Image>();
            cbg.sprite = GradientSprite.RoundedDiagonal(0.08f, Card, Card);
            cbg.color = Color.white;
            Shadow shadow = card.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            shadow.effectDistance = new Vector2(0f, -10f);

            VerticalLayoutGroup vl = card.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(40, 40, 36, 32);
            vl.spacing = 14f;
            vl.childControlWidth = true;
            vl.childControlHeight = true;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;

            BuildHeader(card.transform);
            foreach (string key in LimitKeys)
            {
                BuildLimitRow(card.transform, key);
            }
            BuildFooter(card.transform);
        }

        private void BuildHeader(Transform parent)
        {
            GameObject badge = Child(parent, "Badge");
            LayoutElement ble = badge.AddComponent<LayoutElement>();
            ble.preferredHeight = 72f;
            Image icon = badge.AddComponent<Image>();
            icon.sprite = IconFactory.Person();
            icon.color = Gold;
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            Label(parent, L10n.Get("guest_limits_title"), 34f, TextCol, TextAlignmentOptions.Center, FontStyles.Bold)
                .GetComponent<LayoutElement>().preferredHeight = 52f;

            TextMeshProUGUI body = Label(
                parent, L10n.Get("guest_limits_body"), 22f, Muted, TextAlignmentOptions.Center);
            body.textWrappingMode = TextWrappingModes.Normal;
            body.GetComponent<LayoutElement>().preferredHeight = 68f;
        }

        private void BuildLimitRow(Transform parent, string key)
        {
            GameObject row = Child(parent, "Limit");
            row.AddComponent<LayoutElement>().preferredHeight = 52f;
            HorizontalLayoutGroup hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 14f;
            hl.padding = new RectOffset(10, 10, 0, 0);
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = true;
            hl.childControlHeight = true;
            hl.childForceExpandWidth = false;

            GameObject icon = Child(row.transform, "Icon");
            LayoutElement ile = icon.AddComponent<LayoutElement>();
            ile.preferredWidth = 30f;
            ile.preferredHeight = 30f;
            Image img = icon.AddComponent<Image>();
            img.sprite = IconFactory.Lock();
            img.color = Faint;
            img.raycastTarget = false;

            Label(row.transform, L10n.Get(key), 22f, TextCol, TextAlignmentOptions.Left)
                .GetComponent<LayoutElement>().flexibleWidth = 1f;
        }

        private void BuildFooter(Transform parent)
        {
            GameObject create = Child(parent, "Create");
            LayoutElement gle = create.AddComponent<LayoutElement>();
            gle.preferredHeight = 88f;
            Image gbg = create.AddComponent<Image>();
            gbg.sprite = GradientSprite.RoundedDiagonal(0.3f, Gold, new Color(0.831f, 0.612f, 0.196f));
            gbg.color = Color.white;
            Button gbtn = create.AddComponent<Button>();
            gbtn.targetGraphic = gbg;
            gbtn.onClick.AddListener(() => Choose(_onCreateAccount));
            StretchedLabel(
                create.transform,
                L10n.Get("guest_limits_create"),
                24f,
                new Color(0.07f, 0.06f, 0.05f),
                FontStyles.Bold);

            GameObject stay = Child(parent, "Continue");
            LayoutElement sle = stay.AddComponent<LayoutElement>();
            sle.preferredHeight = 72f;
            Image sbg = stay.AddComponent<Image>();
            sbg.color = new Color(0f, 0f, 0f, 0f);
            Button sbtn = stay.AddComponent<Button>();
            sbtn.targetGraphic = sbg;
            sbtn.onClick.AddListener(() => Choose(_onContinue));
            StretchedLabel(stay.transform, L10n.Get("guest_limits_continue"), 22f, Muted, FontStyles.Normal);
        }

        private void Choose(Action? action)
        {
            // Detach the handlers before invoking: the callback may tear this
            // screen down, and a second tap mid-transition must do nothing.
            _onCreateAccount = null;
            _onContinue = null;
            Destroy(gameObject);
            action?.Invoke();
        }

        // ---- small builders ------------------------------------------------

        private static GameObject Child(Transform parent, string name)
        {
            GameObject go = new(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            return go;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static TextMeshProUGUI Label(
            Transform parent,
            string text,
            float size,
            Color color,
            TextAlignmentOptions align,
            FontStyles style = FontStyles.Normal)
        {
            GameObject go = Child(parent, "Label");
            go.AddComponent<LayoutElement>().preferredHeight = size + 12f;
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.raycastTarget = false;
            return t;
        }

        private static void StretchedLabel(
            Transform parent,
            string text,
            float size,
            Color color,
            FontStyles style)
        {
            GameObject go = Child(parent, "Label");
            Stretch((RectTransform)go.transform);
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.fontStyle = style;
            t.raycastTarget = false;
        }
    }
}
