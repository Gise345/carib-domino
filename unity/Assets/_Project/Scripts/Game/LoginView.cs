#nullable enable
using System;
using System.Threading.Tasks;
using Pose.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Pose.Game
{
    /// <summary>
    /// The startup login screen (M7): a card offering <em>Continue with
    /// Facebook</em>, <em>Sign in with Email</em>, or <em>Continue as Guest</em>.
    /// The email choice reveals an inline form (sign-in / create-account toggle +
    /// forgot-password). All three routes drive <see cref="AuthService"/>; on a
    /// successful sign-in this fires <see cref="LoggedIn"/> and
    /// <see cref="BoardBootstrap"/> proceeds to profile load + lobby.
    ///
    /// Built procedurally (no prefab) and mounted full-screen by
    /// <see cref="BoardBootstrap"/>, mirroring <see cref="LobbyView"/>. Sprites are
    /// pushed in via deferred setters before <see cref="Start"/> runs the build.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class LoginView : MonoBehaviour
    {
        private const float FieldWidth = 480f;
        private const float ButtonWidth = 480f;
        private const float ButtonHeight = 92f;
        private const int MinPasswordLength = 6;

        private static readonly Color BodyText = new(0.97f, 0.95f, 0.88f);
        private static readonly Color MutedText = new(0.85f, 0.90f, 0.82f, 0.75f);
        private static readonly Color ErrorText = new(1.0f, 0.55f, 0.45f);

        /// <summary>Raised once, when any sign-in path completes successfully.</summary>
        public event Action? LoggedIn;

        private Sprite? _logoSprite;
        private Sprite? _backgroundSprite;

        private GameObject _chooser = null!;
        private GameObject _emailForm = null!;
        private TMP_InputField _emailField = null!;
        private TMP_InputField _passwordField = null!;
        private TextMeshProUGUI _status = null!;
        private TextMeshProUGUI _emailActionLabel = null!;
        private TextMeshProUGUI _emailToggleLabel = null!;

        private bool _signUpMode;
        private bool _busy;

        /// <summary>Logo shown at the top of the card. Call before <see cref="Start"/>.</summary>
        public void SetLogoSprite(Sprite? sprite) => _logoSprite = sprite;

        /// <summary>Full-screen backdrop. Call before <see cref="Start"/>.</summary>
        public void SetBackgroundSprite(Sprite? sprite) => _backgroundSprite = sprite;

        private void Start() => Build();

        private void Build()
        {
            RectTransform root = (RectTransform)transform;
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            BuildBackground(root);

            GameObject card = AddChild(root, "Card");
            RectTransform cardRt = (RectTransform)card.transform;
            cardRt.anchorMin = new Vector2(0.5f, 0.5f);
            cardRt.anchorMax = new Vector2(0.5f, 0.5f);
            cardRt.pivot = new Vector2(0.5f, 0.5f);
            cardRt.sizeDelta = new Vector2(600f, 820f);
            Image cardBg = card.AddComponent<Image>();
            cardBg.sprite = GradientSprite.RoundedDiagonal(0.06f, Hex("#0B3D2E"), Hex("#06231A"));
            cardBg.color = new Color(1f, 1f, 1f, 0.97f);

            BuildLogo(cardRt);
            BuildSubtitle(cardRt);
            BuildChooser(cardRt);
            BuildEmailForm(cardRt);
            BuildStatus(cardRt);

            ShowEmailForm(false);
        }

        private void BuildBackground(RectTransform root)
        {
            GameObject bg = AddChild(root, "Background");
            RectTransform rt = (RectTransform)bg.transform;
            Stretch(rt);
            Image img = bg.AddComponent<Image>();
            if (_backgroundSprite != null)
            {
                img.sprite = _backgroundSprite;
                img.type = Image.Type.Simple;
                img.preserveAspect = false;
                img.color = Color.white;
            }
            else
            {
                img.sprite = GradientSprite.Vertical(Hex("#0A2E22"), Hex("#04150F"));
                img.color = Color.white;
            }
        }

        private void BuildLogo(RectTransform card)
        {
            if (_logoSprite == null)
            {
                return;
            }
            GameObject go = AddChild(card, "Logo");
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -44f);
            rt.sizeDelta = new Vector2(380f, 180f);
            Image img = go.AddComponent<Image>();
            img.sprite = _logoSprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
        }

        private void BuildSubtitle(RectTransform card)
        {
            TextMeshProUGUI sub = AddText(card, L10n.Get("login_subtitle"), 26f, BodyText, TextAlignmentOptions.Center);
            sub.textWrappingMode = TextWrappingModes.Normal;
            RectTransform rt = (RectTransform)sub.transform;
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, _logoSprite == null ? -60f : -236f);
            rt.sizeDelta = new Vector2(520f, 80f);
        }

        private void BuildChooser(RectTransform card)
        {
            _chooser = AddCenterColumn(card, "Chooser", new Vector2(520f, 360f), new Vector2(0f, -40f), 22f);
            MakeButton(_chooser.transform, L10n.Get("login_facebook"), Hex("#1877F2"), Hex("#0F5AC4"), Color.white, OnFacebookClicked);
            MakeButton(_chooser.transform, L10n.Get("login_email"), Hex("#4CD964"), Hex("#1FA845"), Hex("#06231A"), () => ShowEmailForm(true));
            MakeButton(_chooser.transform, L10n.Get("login_guest"), Hex("#2C4A3C"), Hex("#1B3125"), BodyText, OnGuestClicked);
        }

        private void BuildEmailForm(RectTransform card)
        {
            _emailForm = AddCenterColumn(card, "EmailForm", new Vector2(520f, 470f), new Vector2(0f, -30f), 16f);

            _emailField = MakeInput(_emailForm.transform, L10n.Get("login_email_placeholder"), password: false);
            _passwordField = MakeInput(_emailForm.transform, L10n.Get("login_password_placeholder"), password: true);

            GameObject primary = MakeButton(
                _emailForm.transform, L10n.Get("login_signin"), Hex("#4CD964"), Hex("#1FA845"), Hex("#06231A"), OnEmailSubmit);
            _emailActionLabel = primary.GetComponentInChildren<TextMeshProUGUI>();

            _emailToggleLabel = MakeLink(_emailForm.transform, L10n.Get("login_to_signup"), ToggleEmailMode);
            MakeLink(_emailForm.transform, L10n.Get("login_forgot"), OnForgotPassword);
            MakeLink(_emailForm.transform, L10n.Get("login_back"), () => ShowEmailForm(false));
        }

        private void BuildStatus(RectTransform card)
        {
            _status = AddText(card, string.Empty, 24f, ErrorText, TextAlignmentOptions.Center);
            _status.textWrappingMode = TextWrappingModes.Normal;
            RectTransform rt = (RectTransform)_status.transform;
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 36f);
            rt.sizeDelta = new Vector2(520f, 80f);
        }

        // ---- Interaction ---------------------------------------------------

        private void ShowEmailForm(bool show)
        {
            _chooser.SetActive(!show);
            _emailForm.SetActive(show);
            SetStatus(string.Empty, isError: false);
        }

        private void ToggleEmailMode()
        {
            _signUpMode = !_signUpMode;
            _emailActionLabel.text = L10n.Get(_signUpMode ? "login_create" : "login_signin");
            _emailToggleLabel.text = L10n.Get(_signUpMode ? "login_to_signin" : "login_to_signup");
            SetStatus(string.Empty, isError: false);
        }

        private void OnGuestClicked() => RunAuth(() => AuthService.Instance!.SignInAsGuestAsync(), reportsSuccess: true);

        private async void OnFacebookClicked()
        {
            if (_busy)
            {
                return;
            }
            SetBusy(true);
            try
            {
                bool connected = await AuthService.Instance!.ConnectFacebookAsync();
                if (connected)
                {
                    LoggedIn?.Invoke();
                }
                else
                {
                    SetStatus(L10n.Get("login_cancelled"), isError: false);
                    SetBusy(false);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LoginView] Facebook sign-in failed: {ex.Message}");
                SetStatus(L10n.Get("login_err_generic"), isError: true);
                SetBusy(false);
            }
        }

        private void OnEmailSubmit()
        {
            string email = _emailField.text.Trim();
            string password = _passwordField.text;
            if (!IsValidEmail(email) || password.Length < MinPasswordLength)
            {
                SetStatus(L10n.Get("login_err_email_pw"), isError: true);
                return;
            }
            RunAuth(
                () => _signUpMode
                    ? AuthService.Instance!.SignUpWithEmailAsync(email, password)
                    : AuthService.Instance!.SignInWithEmailAsync(email, password),
                reportsSuccess: true,
                emailError: true);
        }

        private void OnForgotPassword()
        {
            string email = _emailField.text.Trim();
            if (!IsValidEmail(email))
            {
                SetStatus(L10n.Get("login_err_email_pw"), isError: true);
                return;
            }
            RunAuthConfirm(() => AuthService.Instance!.SendPasswordResetAsync(email), L10n.Get("login_reset_sent"));
        }

        // Runs an auth task with the busy/guard/error boilerplate. On success,
        // either fires LoggedIn (reportsSuccess) or shows a confirmation message.
        private async void RunAuth(Func<Task> action, bool reportsSuccess, bool emailError = false)
        {
            if (_busy)
            {
                return;
            }
            SetBusy(true);
            try
            {
                await action();
                if (reportsSuccess)
                {
                    LoggedIn?.Invoke();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LoginView] sign-in failed: {ex.Message}");
                SetStatus(L10n.Get(emailError ? "login_err_signin" : "login_err_generic"), isError: true);
                SetBusy(false);
            }
        }

        private async void RunAuthConfirm(Func<Task> action, string confirmation)
        {
            if (_busy)
            {
                return;
            }
            SetBusy(true);
            try
            {
                await action();
                SetStatus(confirmation, isError: false);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LoginView] request failed: {ex.Message}");
                SetStatus(L10n.Get("login_err_generic"), isError: true);
            }
            SetBusy(false);
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            if (busy)
            {
                SetStatus(L10n.Get("login_busy"), isError: false);
            }
        }

        private void SetStatus(string message, bool isError)
        {
            _status.text = message;
            _status.color = isError ? ErrorText : MutedText;
        }

        private static bool IsValidEmail(string email) =>
            email.Length >= 3 && email.Contains('@') && email.IndexOf('.', email.IndexOf('@')) > 0;

        // ---- Build helpers -------------------------------------------------

        private GameObject MakeButton(Transform parent, string label, Color top, Color bottom, Color textColor, Action onClick)
        {
            GameObject go = AddChild(parent, "Button");
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = ButtonWidth;
            le.preferredHeight = ButtonHeight;
            Image img = go.AddComponent<Image>();
            img.sprite = GradientSprite.RoundedDiagonal(0.32f, top, bottom);
            img.color = Color.white;
            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick());

            TextMeshProUGUI text = AddText(go.transform, label, 30f, textColor, TextAlignmentOptions.Center);
            Stretch((RectTransform)text.transform);
            return go;
        }

        private TextMeshProUGUI MakeLink(Transform parent, string label, Action onClick)
        {
            GameObject go = AddChild(parent, "Link");
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = FieldWidth;
            le.preferredHeight = 44f;
            Image hit = go.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = hit;
            btn.onClick.AddListener(() => onClick());

            TextMeshProUGUI text = AddText(go.transform, label, 24f, Hex("#FFD24A"), TextAlignmentOptions.Center);
            Stretch((RectTransform)text.transform);
            return text;
        }

        private TMP_InputField MakeInput(Transform parent, string placeholder, bool password)
        {
            GameObject root = AddChild(parent, password ? "PasswordField" : "EmailField");
            LayoutElement le = root.AddComponent<LayoutElement>();
            le.preferredWidth = FieldWidth;
            le.preferredHeight = 88f;
            Image bg = root.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.35f, Hex("#0C2A1E"), Hex("#0C2A1E"));
            bg.color = Color.white;

            TMP_InputField field = root.AddComponent<TMP_InputField>();

            GameObject area = AddChild(root.transform, "TextArea");
            RectTransform areaRt = (RectTransform)area.transform;
            areaRt.anchorMin = Vector2.zero;
            areaRt.anchorMax = Vector2.one;
            areaRt.offsetMin = new Vector2(24f, 6f);
            areaRt.offsetMax = new Vector2(-24f, -6f);
            area.AddComponent<RectMask2D>();

            TextMeshProUGUI ph = AddText(area.transform, placeholder, 28f, MutedText, TextAlignmentOptions.MidlineLeft);
            Stretch((RectTransform)ph.transform);
            TextMeshProUGUI text = AddText(area.transform, string.Empty, 28f, BodyText, TextAlignmentOptions.MidlineLeft);
            Stretch((RectTransform)text.transform);

            field.textViewport = areaRt;
            field.textComponent = text;
            field.placeholder = ph;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.contentType = password
                ? TMP_InputField.ContentType.Password
                : TMP_InputField.ContentType.EmailAddress;
            return field;
        }

        private GameObject AddCenterColumn(RectTransform parent, string name, Vector2 size, Vector2 pos, float spacing)
        {
            GameObject go = AddChild(parent, name);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            VerticalLayoutGroup vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.spacing = spacing;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = false;
            vlg.childForceExpandHeight = false;
            return go;
        }

        private static GameObject AddChild(Transform parent, string name)
        {
            GameObject go = new(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            return go;
        }

        private static TextMeshProUGUI AddText(Transform parent, string text, float size, Color color, TextAlignmentOptions align)
        {
            GameObject go = new("Text", typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out Color c);
            return c;
        }
    }
}
