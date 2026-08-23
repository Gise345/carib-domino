#nullable enable
using System;
using System.Threading.Tasks;
using Firebase;
using Firebase.Auth;
using Firebase.Functions;
using UnityEngine;

namespace Pose.Net
{
    /// <summary>
    /// Single entry point for every authentication path (M7): guest (anonymous),
    /// email/password, and Facebook. Wraps <see cref="FirebaseAuth"/> so the rest
    /// of the app never touches provider credentials directly.
    ///
    /// Linking policy (ADR 0019): a guest who signs up with email or connects
    /// Facebook is <em>linked</em>, preserving their uid/coins/stats. If the
    /// credential already belongs to another account we sign into that one
    /// instead (Firebase would otherwise reject the link).
    ///
    /// Created by <see cref="FirebaseBootstrap"/> after Firebase dependencies are
    /// resolved — <see cref="FirebaseAuth.DefaultInstance"/> is only safe then.
    /// Survives scene loads via <c>DontDestroyOnLoad</c>.
    /// </summary>
    public sealed class AuthService : MonoBehaviour
    {
        public static AuthService? Instance { get; private set; }

        private FirebaseAuth? _auth;

        // Completes when Firebase has reported the initial auth state (its
        // StateChanged fires once on subscribe with the restored session). The
        // boot awaits this so a persisted user is never mistaken for signed-out.
        private readonly TaskCompletionSource<bool> _initialState = new();

        /// <summary>The signed-in Firebase user, or null if signed out.</summary>
        public FirebaseUser? CurrentUser => _auth?.CurrentUser;

        /// <summary>The current uid, or null if signed out.</summary>
        public string? Uid => _auth?.CurrentUser?.UserId;

        /// <summary>The signed-in user's photo URL (Facebook profile picture), or null.</summary>
        public string? PhotoUrl => _auth?.CurrentUser?.PhotoUrl?.ToString();

        /// <summary>True when someone (guest or otherwise) is signed in.</summary>
        public bool IsSignedIn => _auth?.CurrentUser != null;

        /// <summary>True when the current session is an anonymous guest.</summary>
        public bool IsGuest => _auth?.CurrentUser?.IsAnonymous ?? false;

        /// <summary>True when a Facebook credential is linked to the current user.</summary>
        public bool IsFacebookLinked => HasProvider(FacebookAuthProvider.ProviderId);

        /// <summary>True when an email/password credential is linked to the current user.</summary>
        public bool HasEmail => HasProvider(EmailAuthProvider.ProviderId);

        /// <summary>Fires whenever the signed-in user changes (sign-in, link, sign-out).</summary>
        public event Action? AuthChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _auth = FirebaseAuth.DefaultInstance;
            _auth.StateChanged += OnAuthStateChanged;
        }

        private void OnDestroy()
        {
            if (_auth != null)
            {
                _auth.StateChanged -= OnAuthStateChanged;
            }
        }

        private void OnAuthStateChanged(object sender, EventArgs e)
        {
            _initialState.TrySetResult(true);
            AuthChanged?.Invoke();
        }

        /// <summary>
        /// Completes once Firebase has restored (or confirmed the absence of) a
        /// persisted session. Await this before deciding login-vs-lobby so a
        /// signed-in user isn't shown the login screen during the cold-start race.
        /// </summary>
        public Task InitialAuthStateAsync() => _initialState.Task;

        /// <summary>
        /// Ensures a session exists for boot: if a persisted user is already
        /// signed in (returning player), keeps it; otherwise signs in as a guest.
        /// This is the first-run fallback — the login screen calls the explicit
        /// methods below instead.
        /// </summary>
        public async Task EnsureSessionAsync()
        {
            _auth ??= FirebaseAuth.DefaultInstance;
            if (_auth.CurrentUser != null)
            {
                return;
            }
            await SignInAsGuestAsync();
        }

        /// <summary>Signs in anonymously (Continue as Guest).</summary>
        public async Task SignInAsGuestAsync()
        {
            _auth ??= FirebaseAuth.DefaultInstance;
            await _auth.SignInAnonymouslyAsync();
            // The email and Facebook paths both raise this; a guest sign-in is
            // just as much a change of session.
            AuthChanged?.Invoke();
        }

        /// <summary>
        /// Creates an email/password account, or links it onto the current guest
        /// to preserve their progress.
        /// </summary>
        public async Task SignUpWithEmailAsync(string email, string password)
        {
            _auth ??= FirebaseAuth.DefaultInstance;
            FirebaseUser? user = _auth.CurrentUser;
            if (user != null && user.IsAnonymous)
            {
                Credential credential = EmailAuthProvider.GetCredential(email, password);
                await user.LinkWithCredentialAsync(credential);
            }
            else
            {
                await _auth.CreateUserWithEmailAndPasswordAsync(email, password);
            }
            AuthChanged?.Invoke();
        }

        /// <summary>Signs into an existing email/password account.</summary>
        public async Task SignInWithEmailAsync(string email, string password)
        {
            _auth ??= FirebaseAuth.DefaultInstance;
            await _auth.SignInWithEmailAndPasswordAsync(email, password);
            AuthChanged?.Invoke();
        }

        /// <summary>Sends a password-reset email.</summary>
        public async Task SendPasswordResetAsync(string email)
        {
            _auth ??= FirebaseAuth.DefaultInstance;
            await _auth.SendPasswordResetEmailAsync(email);
        }

        /// <summary>
        /// Runs the Facebook login flow and links (or signs into) the Firebase
        /// account, then records the verified identity server-side via
        /// <c>syncFacebookIdentity</c>. Returns false if the player cancelled.
        /// </summary>
        public async Task<bool> ConnectFacebookAsync()
        {
            _auth ??= FirebaseAuth.DefaultInstance;
            string? accessToken = await FacebookAuthService.LoginAsync();
            if (accessToken == null)
            {
                return false;
            }

            Credential credential = FacebookAuthProvider.GetCredential(accessToken);
            FirebaseUser? user = _auth.CurrentUser;
            try
            {
                if (user != null && user.IsAnonymous)
                {
                    await user.LinkWithCredentialAsync(credential);
                }
                else
                {
                    await _auth.SignInWithCredentialAsync(credential);
                }
            }
            catch (FirebaseException ex) when (IsCredentialInUse(ex))
            {
                // This Facebook account already belongs to another player — sign
                // into it rather than fail. Any guest-only progress is left behind
                // by design (the existing account is the real one).
                await _auth.SignInWithCredentialAsync(credential);
            }

            await CallSyncFacebookIdentity();
            AuthChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Unlinks Facebook from the current account. The caller must ensure
        /// another credential exists first (see <see cref="HasEmail"/>) — Firebase
        /// rejects unlinking the sole provider, and the profile UI treats a
        /// Facebook-only account's "disconnect" as a sign-out instead.
        /// </summary>
        public async Task UnlinkFacebookAsync()
        {
            FirebaseUser? user = _auth?.CurrentUser;
            if (user == null)
            {
                return;
            }
            await user.UnlinkAsync(FacebookAuthProvider.ProviderId);
            FacebookAuthService.Logout();
            AuthChanged?.Invoke();
        }

        /// <summary>Signs out of Firebase and clears the local Facebook session.</summary>
        /// <summary>
        /// Turns a failed auth call into a key the UI can show. Firebase reports
        /// every failure the same way to a plain catch, so without this a wrong
        /// password, an address already registered and a weak password all read
        /// as one vague "couldn't sign you in", which is no help to the player
        /// and no help debugging.
        ///
        /// Lives here rather than in the view because unwrapping it means
        /// touching Firebase types, and nothing in Pose.Game should have to.
        /// </summary>
        /// <returns>A localization key from the <c>login_err_*</c> family.</returns>
        public static string DescribeError(Exception ex)
        {
            // The SDK hands back an AggregateException around the real one.
            Exception inner = ex;
            if (ex is AggregateException aggregate)
            {
                AggregateException flat = aggregate.Flatten();
                if (flat.InnerExceptions.Count > 0)
                {
                    inner = flat.InnerExceptions[0];
                }
            }

            if (inner is not FirebaseException firebase)
            {
                return "login_err_generic";
            }

            return (AuthError)firebase.ErrorCode switch
            {
                AuthError.WrongPassword => "login_err_wrong_password",
                AuthError.InvalidCredential => "login_err_wrong_password",
                AuthError.UserNotFound => "login_err_no_account",
                AuthError.EmailAlreadyInUse => "login_err_email_taken",
                AuthError.AccountExistsWithDifferentCredentials => "login_err_email_taken",
                AuthError.WeakPassword => "login_err_weak_password",
                AuthError.InvalidEmail => "login_err_bad_email",
                AuthError.NetworkRequestFailed => "login_err_network",
                AuthError.TooManyRequests => "login_err_too_many",
                AuthError.UserDisabled => "login_err_disabled",
                _ => "login_err_signin",
            };
        }

        public void SignOut()
        {
            FacebookAuthService.Logout();
            _auth?.SignOut();
            AuthChanged?.Invoke();
        }

        private static bool IsCredentialInUse(FirebaseException ex)
        {
            AuthError error = (AuthError)ex.ErrorCode;
            return error == AuthError.CredentialAlreadyInUse || error == AuthError.AccountExistsWithDifferentCredentials;
        }

        private bool HasProvider(string providerId)
        {
            FirebaseUser? user = _auth?.CurrentUser;
            if (user == null)
            {
                return false;
            }
            foreach (IUserInfo info in user.ProviderData)
            {
                if (info.ProviderId == providerId)
                {
                    return true;
                }
            }
            return false;
        }

        private static async Task CallSyncFacebookIdentity()
        {
            try
            {
                FirebaseFunctions functions = FirebaseFunctions.DefaultInstance;
                await functions.GetHttpsCallable("syncFacebookIdentity").CallAsync();
            }
            catch (Exception ex)
            {
                // Non-fatal: the credential is already linked; the fbId->uid index
                // write can be retried on next launch. Surfaced as a warning rather
                // than failing the whole sign-in over a follow-up call.
                Debug.LogWarning($"[AuthService] syncFacebookIdentity failed: {ex.Message}");
            }
        }
    }
}
