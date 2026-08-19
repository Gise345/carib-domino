#nullable enable
using System;
using System.Threading.Tasks;
using Firebase;
using UnityEngine;

namespace Pose.Net
{
    /// <summary>
    /// Singleton MonoBehaviour that initialises Firebase and establishes a
    /// session on app start. Survives scene loads via <c>DontDestroyOnLoad</c>.
    /// Other systems should:
    /// <list type="bullet">
    ///   <item>Read <see cref="IsReady"/> + <see cref="Uid"/> if a check
    ///         finds initialisation already complete (sign-in finishes faster
    ///         than a scene's first frame on a warm cache).</item>
    ///   <item>Subscribe to <see cref="Ready"/> / <see cref="Failed"/> when
    ///         <see cref="IsReady"/> is false on first check.</item>
    /// </list>
    /// On failure we log the error and remain in <see cref="HasFailed"/> state;
    /// callers can choose to either show an error or proceed offline.
    ///
    /// M7: sign-in itself moved to <see cref="AuthService"/> (guest / email /
    /// Facebook). This class now owns only Firebase dependency init, then asks
    /// <see cref="AuthService"/> for a session. <see cref="EnsureSessionAsync"/>
    /// currently falls back to a guest sign-in so boot behaviour is unchanged;
    /// the login screen will replace that fallback with an explicit choice.
    /// </summary>
    public sealed class FirebaseBootstrap : MonoBehaviour
    {
        public static FirebaseBootstrap? Instance { get; private set; }

        /// <summary>
        /// The current signed-in uid, or null if signed out. Delegates to
        /// <see cref="AuthService"/> so it stays live across a login/sign-out that
        /// happens after boot (e.g. a guest signing in from the login screen).
        /// </summary>
        public string? Uid => AuthService.Instance?.Uid;

        public bool IsReady { get; private set; }
        public bool HasFailed { get; private set; }
        public string? ErrorMessage { get; private set; }

        public event Action? Ready;
        public event Action<string>? Failed;

        // async void is the standard pattern for Unity event methods. Awake
        // returns immediately to the engine; the actual init runs as a
        // continuation. Exceptions inside the body are caught by our try/catch.
        private async void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            try
            {
                // On Android, CheckAndFixDependenciesAsync ensures Google Play
                // Services is up to date and prompts the user if not. On other
                // platforms it's a near-instant no-op.
                DependencyStatus depStatus = await FirebaseApp.CheckAndFixDependenciesAsync();
                if (depStatus != DependencyStatus.Available)
                {
                    Fail($"Firebase dependencies unavailable: {depStatus}");
                    return;
                }

                EnsureAuthService();
                AuthService auth = AuthService.Instance!;

                // Wait for Firebase to restore any persisted session before we
                // decide login-vs-lobby — its StateChanged fires once with the
                // initial (restored) state. Bounded by a short timeout so a
                // genuinely signed-out cold start isn't held up.
                await Task.WhenAny(auth.InitialAuthStateAsync(), Task.Delay(2000));

                // Do NOT auto-sign-in. A returning player has a persisted session
                // (AuthService.Uid is non-null); a new player has none, and
                // BoardBootstrap shows the login screen to pick guest / email /
                // Facebook. Uid is a live passthrough to AuthService.
                IsReady = true;
                Debug.Log($"[FirebaseBootstrap] Init ready, session uid: {Uid ?? "<none>"}");
                Ready?.Invoke();
            }
            catch (Exception ex)
            {
                Fail($"Firebase init/sign-in failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void EnsureAuthService()
        {
            if (AuthService.Instance != null)
            {
                return;
            }
            GameObject go = new("AuthService");
            go.AddComponent<AuthService>();
        }

        private void Fail(string message)
        {
            HasFailed = true;
            ErrorMessage = message;
            Debug.LogError($"[FirebaseBootstrap] {message}");
            Failed?.Invoke(message);
        }
    }
}
