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
        private const float ButtonWidth = 520f;
        private const float ButtonHeight = 104f;
        private const int MinPasswordLength = 6;

        // The screen is one top-anchored stack: logo, subtitle, then the
        // controls. Each position is derived from the one above it, so the
        // relationship holds instead of relying on three separate numbers
        // agreeing.
        //
        // Everything here anchors to the TOP for the same reason. The canvas
        // matches on WIDTH, so its height changes with the device aspect — a
        // centre-anchored column rides up on a shorter screen and lands on the
        // subtitle, which is exactly what it did.
        //
        // The logo is the hero: square art, sat high on the screen with the
        // scene behind it doing the rest of the work.
        private const float LogoSize = 470f;
        private const float LogoTopMargin = 150f;

        // Round icon badge on the left of each button.
        private const float IconBadgeSize = 62f;
        private const float IconBadgeInset = 22f;

        private const float SubtitleGap = 12f;
        private const float SubtitleHeight = 100f;
        private const float SubtitleWidth = 560f;
        private static float SubtitleTop => LogoTopMargin + LogoSize + SubtitleGap;

        // Controls start a clear gap below the subtitle.
        private const float LegalLinkWidth = 210f;
        private const float ControlsGap = 30f;
        private static float ControlsTop => SubtitleTop + SubtitleHeight + ControlsGap;

        // Gap between the three chooser buttons. Three 104-tall buttons plus
        // two of these is 384, inside the column's 400.
        private const float ChooserSpacing = 36f;

        // Placeholders until the marketing site is live; the buttons are real
        // so the flow can be tested, and these are the single place to change.
        private const string TermsUrl = "https://posedominoes.com/terms";
        private const string PrivacyUrl = "https://posedominoes.com/privacy";

        private static readonly Color BodyText = new(0.97f, 0.95f, 0.88f);
        private static readonly Color MutedText = new(0.85f, 0.90f, 0.82f, 0.75f);
        private static readonly Color ErrorText = new(1.0f, 0.55f, 0.45f);

        /// <summary>Raised once, when any sign-in path completes successfully.</summary>
        public event Action? LoggedIn;

        private Sprite? _logoSprite;
        private Sprite? _backgroundSprite;

        private GameObject _chooser = null!;
        private GameObject _emailChoice = null!;
        private GameObject _legal = null!;
        private GameObject _emailForm = null!;
        private TMP_InputField _emailField = null!;
        private TMP_InputField _passwordField = null!;
        private TextMeshProUGUI _status = null!;
        private TextMeshProUGUI _emailActionLabel = null!;
        private TextMeshProUGUI _forgotLink = null!;

        private bool _signUpMode;
        private bool _busy;

        /// <summary>Logo shown at the top of the screen. Call before <see cref="Start"/>.</summary>
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

            // No card. The artwork is the screen — a panel floating on top of
            // it would hide the thing that gives this page its character. The
            // controls sit directly on the scene, and a soft scrim behind the
            // lower half keeps the buttons and the small print legible over
            // whatever the background is doing there.
            BuildLowerScrim(root);
            BuildLogo(root);
            BuildSubtitle(root);
            BuildChooser(root);
            BuildEmailChoice(root);
            BuildEmailForm(root);
            BuildStatus(root);
            BuildLegalFooter(root);

            Show(Panel.Chooser);
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

        private void BuildLogo(RectTransform root)
        {
            if (_logoSprite == null)
            {
                return;
            }
            GameObject go = AddChild(root, "Logo");
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -LogoTopMargin);
            rt.sizeDelta = new Vector2(LogoSize, LogoSize);
            Image img = go.AddComponent<Image>();
            img.sprite = _logoSprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
        }

        private void BuildSubtitle(RectTransform root)
        {
            TextMeshProUGUI sub = AddText(root, L10n.Get("login_subtitle"), 26f, BodyText, TextAlignmentOptions.Center);
            sub.textWrappingMode = TextWrappingModes.Normal;
            RectTransform rt = (RectTransform)sub.transform;
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(
                0f, _logoSprite == null ? -LogoTopMargin : -SubtitleTop);
            rt.sizeDelta = new Vector2(SubtitleWidth, SubtitleHeight);
        }

        private void BuildChooser(RectTransform root)
        {
            // Sits below the subtitle, not through it: at the old +90 the column
            // ran 575-975 while the subtitle occupies 632-732, so the text drew
            // straight over the first button. -97 puts the column at 762-1162.
            _chooser = AddTopColumn(
                root, "Chooser", new Vector2(ButtonWidth, 400f), ControlsTop, ChooserSpacing);

            // Facebook keeps its own blue and a white mark — it is a brand
            // people recognise by colour before they read it.
            MakeButton(_chooser.transform, L10n.Get("login_facebook"),
                Hex("#1877F2"), Hex("#0F5AC4"), Color.white,
                OnFacebookClicked, IconKind.Facebook);
            MakeButton(_chooser.transform, L10n.Get("login_email"),
                Hex("#3FC55A"), Hex("#22A244"), Color.white,
                () => Show(Panel.EmailChoice), IconKind.Envelope);
            MakeButton(_chooser.transform, L10n.Get("login_guest"),
                Hex("#39393B"), Hex("#232325"), Color.white,
                OnGuestClicked, IconKind.Person);
        }

        /// <summary>
        /// Between the chooser and the form: sign in, or create an account.
        /// Asked outright rather than defaulting to sign-in with a link to
        /// switch, because the two are different intentions and a player who
        /// picked the wrong one only finds out when the form rejects them.
        /// </summary>
        private void BuildEmailChoice(RectTransform root)
        {
            _emailChoice = AddTopColumn(
                root, "EmailChoice", new Vector2(ButtonWidth, 400f), ControlsTop, ChooserSpacing);

            MakeButton(_emailChoice.transform, L10n.Get("login_have_account"),
                Hex("#3FC55A"), Hex("#22A244"), Color.white,
                () => OpenEmailForm(signUp: false));

            MakeButton(_emailChoice.transform, L10n.Get("login_new_account"),
                Hex("#39393B"), Hex("#232325"), Color.white,
                () => OpenEmailForm(signUp: true));

            MakeLink(_emailChoice.transform, L10n.Get("login_back"), () => Show(Panel.Chooser));
        }

        /// <summary>Badge drawn at the left edge of a chooser button.</summary>
        private enum IconKind
        {
            None,
            Facebook,
            Envelope,
            Person,
        }

        /// <summary>
        /// Darkens the lower half of the artwork so the buttons and the legal
        /// line stay readable whatever the background does behind them.
        /// </summary>
        private void BuildLowerScrim(RectTransform root)
        {
            GameObject go = AddChild(root, "LowerScrim");
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0.55f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            Image img = go.AddComponent<Image>();
            // Transparent at the TOP of this rect, dark at the bottom —
            // GradientSprite.Vertical runs stops[0] to stops[last] downward, so
            // passing the dark first shaded the sky and left the small print
            // sitting on bare artwork.
            img.sprite = GradientSprite.Vertical(
                new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0.80f));
            img.color = Color.white;
            img.raycastTarget = false;
        }

        /// <summary>
        /// The terms line. Kept as plain text with two tappable links rather
        /// than a rich-text link field, so the tap targets are real rects.
        /// </summary>
        private void BuildLegalFooter(RectTransform root)
        {
            GameObject row = AddChild(root, "Legal");
            _legal = row;
            RectTransform rt = (RectTransform)row.transform;
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 40f);
            rt.sizeDelta = new Vector2(660f, 96f);

            VerticalLayoutGroup col = row.AddComponent<VerticalLayoutGroup>();
            col.childAlignment = TextAnchor.MiddleCenter;
            col.spacing = 2f;
            col.childControlWidth = true;
            col.childControlHeight = true;
            col.childForceExpandWidth = false;
            col.childForceExpandHeight = false;

            TextMeshProUGUI intro = AddText(
                row.transform, L10n.Get("login_legal_intro"), 22f, MutedText, TextAlignmentOptions.Center);
            // AddText does not attach a LayoutElement, so this has to add one.
            intro.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;

            GameObject links = AddChild(row.transform, "Links");
            links.AddComponent<LayoutElement>().preferredHeight = 34f;
            HorizontalLayoutGroup h = links.AddComponent<HorizontalLayoutGroup>();
            h.childAlignment = TextAnchor.MiddleCenter;
            h.spacing = 10f;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;

            MakeLink(links.transform, L10n.Get("login_terms"), OnTermsClicked, LegalLinkWidth);
            TextMeshProUGUI and = AddText(
                links.transform, L10n.Get("login_legal_and"), 22f, MutedText, TextAlignmentOptions.Center);
            and.gameObject.AddComponent<LayoutElement>().preferredWidth = 40f;
            MakeLink(links.transform, L10n.Get("login_privacy"), OnPrivacyClicked, LegalLinkWidth);
        }

        private void OnTermsClicked() => Application.OpenURL(TermsUrl);

        private void OnPrivacyClicked() => Application.OpenURL(PrivacyUrl);

        private void BuildEmailForm(RectTransform root)
        {
            // Sits exactly where the chooser was: same anchor, same top edge, so
            // choosing Email swaps the panel rather than shifting the screen.
            _emailForm = AddTopColumn(
                root, "EmailForm", new Vector2(ButtonWidth, 470f), ControlsTop, 16f);

            _emailField = MakeInput(_emailForm.transform, L10n.Get("login_email_placeholder"), password: false);
            _passwordField = MakeInput(_emailForm.transform, L10n.Get("login_password_placeholder"), password: true);

            GameObject primary = MakeButton(
                _emailForm.transform, L10n.Get("login_signin"), Hex("#4CD964"), Hex("#1FA845"), Hex("#06231A"), OnEmailSubmit);
            _emailActionLabel = primary.GetComponentInChildren<TextMeshProUGUI>();

            // Nothing to switch here any more — the mode was chosen on the way
            // in. Forgot-password only belongs to signing in; there is nothing
            // to recover on an account that does not exist yet.
            _forgotLink = MakeLink(_emailForm.transform, L10n.Get("login_forgot"), OnForgotPassword);
            MakeLink(_emailForm.transform, L10n.Get("login_back"), () => Show(Panel.EmailChoice));
        }

        private void BuildStatus(RectTransform root)
        {
            _status = AddText(root, string.Empty, 24f, ErrorText, TextAlignmentOptions.Center);
            _status.textWrappingMode = TextWrappingModes.Normal;
            RectTransform rt = (RectTransform)_status.transform;
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 36f);
            rt.sizeDelta = new Vector2(520f, 80f);
        }

        // ---- Interaction ---------------------------------------------------

        /// <summary>Which of the three panels is on screen.</summary>
        private enum Panel
        {
            /// <summary>Facebook / Email / Guest.</summary>
            Chooser,

            /// <summary>Sign in, or create an account.</summary>
            EmailChoice,

            /// <summary>The email and password fields themselves.</summary>
            EmailForm,
        }

        private void Show(Panel panel)
        {
            _chooser.SetActive(panel == Panel.Chooser);
            _emailChoice.SetActive(panel == Panel.EmailChoice);
            _emailForm.SetActive(panel == Panel.EmailForm);
            // The terms line belongs to the choice of how to sign in, and it is
            // agreed on the way in. Dropping it past that point keeps the email
            // screens to one job, and gives the taller form the room it needs on
            // a short display.
            _legal.SetActive(panel == Panel.Chooser);
            SetStatus(string.Empty, isError: false);
        }

        /// <summary>
        /// Opens the form in the mode the player picked, and dresses it to
        /// match: the button says what it will do, and forgot-password is only
        /// offered where it means anything.
        /// </summary>
        private void OpenEmailForm(bool signUp)
        {
            _signUpMode = signUp;
            _emailActionLabel.text = L10n.Get(signUp ? "login_create" : "login_signin");
            _forgotLink.gameObject.SetActive(!signUp);
            _emailField.text = string.Empty;
            _passwordField.text = string.Empty;
            Show(Panel.EmailForm);
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
                Debug.LogWarning($"[LoginView] sign-in failed: {ex}");
                // Say which thing went wrong. Firebase reports them all the
                // same way to a plain catch, so AuthService unwraps the code.
                SetStatus(
                    L10n.Get(emailError ? AuthService.DescribeError(ex) : "login_err_generic"),
                    isError: true);
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

        private GameObject MakeButton(
            Transform parent,
            string label,
            Color top,
            Color bottom,
            Color textColor,
            Action onClick,
            IconKind icon = IconKind.None)
        {
            GameObject go = AddChild(parent, "Button");
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = ButtonWidth;
            le.preferredHeight = ButtonHeight;
            Image img = go.AddComponent<Image>();
            img.sprite = GradientSprite.RoundedDiagonal(0.22f, top, bottom);
            img.color = Color.white;
            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick());

            // The label stays centred on the BUTTON rather than in the space
            // left over beside the badge, so the three labels line up with each
            // other down the column.
            TextMeshProUGUI text = AddText(go.transform, label, 32f, textColor, TextAlignmentOptions.Center);
            Stretch((RectTransform)text.transform);
            text.raycastTarget = false;

            if (icon != IconKind.None)
            {
                AddIconBadge(go.transform, icon, top);
            }
            return go;
        }

        /// <summary>
        /// The round mark at a button's leading edge. Facebook gets its filled
        /// white disc, since that IS the brand; the other two get an outlined
        /// ring, which reads as a set without pretending to be a logo.
        /// </summary>
        private static void AddIconBadge(Transform parent, IconKind kind, Color buttonColor)
        {
            GameObject badge = AddChild(parent, "Badge");
            RectTransform rt = (RectTransform)badge.transform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(IconBadgeInset, 0f);
            rt.sizeDelta = new Vector2(IconBadgeSize, IconBadgeSize);

            Image disc = badge.AddComponent<Image>();
            disc.raycastTarget = false;
            if (kind == IconKind.Facebook)
            {
                disc.sprite = GradientSprite.RoundedDiagonal(0.5f, Color.white, Color.white);
                disc.color = Color.white;

                TextMeshProUGUI f = AddText(
                    badge.transform, "f", IconBadgeSize * 0.72f, buttonColor, TextAlignmentOptions.Center);
                f.fontStyle = FontStyles.Bold;
                RectTransform frt = (RectTransform)f.transform;
                Stretch(frt);
                // The glyph sits high in its line box; nudge it onto the disc.
                frt.offsetMin = new Vector2(0f, -IconBadgeSize * 0.10f);
                f.raycastTarget = false;
                return;
            }

            disc.sprite = IconFactory.Ring();
            disc.color = Color.white;

            GameObject glyph = AddChild(badge.transform, "Glyph");
            RectTransform grt = (RectTransform)glyph.transform;
            Stretch(grt);
            grt.offsetMin = new Vector2(IconBadgeSize * 0.24f, IconBadgeSize * 0.24f);
            grt.offsetMax = new Vector2(-IconBadgeSize * 0.24f, -IconBadgeSize * 0.24f);
            Image gi = glyph.AddComponent<Image>();
            gi.sprite = kind == IconKind.Envelope ? IconFactory.Envelope() : IconFactory.Person();
            gi.color = Color.white;
            gi.preserveAspect = true;
            gi.raycastTarget = false;
        }

        private TextMeshProUGUI MakeLink(
            Transform parent, string label, Action onClick, float width = FieldWidth)
        {
            GameObject go = AddChild(parent, "Link");
            LayoutElement le = go.AddComponent<LayoutElement>();
            // The default is a full-width row link. The legal line puts two side
            // by side, so those pass their own width or the row blows out.
            le.preferredWidth = width;
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

        /// <summary>
        /// A column pinned to the top of the screen at <paramref name="topY"/>
        /// canvas units down. Top-anchored so the stack keeps its shape on any
        /// aspect; the canvas matches on width, so its height is not fixed.
        /// </summary>
        private GameObject AddTopColumn(
            RectTransform parent, string name, Vector2 size, float topY, float spacing)
        {
            GameObject go = AddChild(parent, name);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -topY);
            rt.sizeDelta = size;
            VerticalLayoutGroup vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperCenter;
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
