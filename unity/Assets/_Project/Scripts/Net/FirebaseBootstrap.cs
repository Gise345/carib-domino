#nullable enable
using System;
using Firebase;
using Firebase.Auth;
using UnityEngine;

namespace Pose.Net
{
    /// <summary>
    /// Singleton MonoBehaviour that initialises Firebase and signs in
    /// anonymously on app start. Survives scene loads via
    /// <c>DontDestroyOnLoad</c>. Other systems should:
    /// <list type="bullet">
    ///   <item>Read <see cref="IsReady"/> + <see cref="Uid"/> if a check
    ///         finds initialisation already complete (sign-in finishes faster
    ///         than a scene's first frame on a warm cache).</item>
    ///   <item>Subscribe to <see cref="Ready"/> / <see cref="Failed"/> when
    ///         <see cref="IsReady"/> is false on first check.</item>
    /// </list>
    /// On failure we log the error and remain in <see cref="HasFailed"/> state;
    /// callers can choose to either show an error or proceed offline (M2.1
    /// is the latter — gameplay doesn't require auth yet).
    /// </summary>
    public sealed class FirebaseBootstrap : MonoBehaviour
    {
        public static FirebaseBootstrap? Instance { get; private set; }

        public string? Uid { get; private set; }
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

                FirebaseAuth auth = FirebaseAuth.DefaultInstance;
                AuthResult result = await auth.SignInAnonymouslyAsync();

                Uid = result.User.UserId;
                IsReady = true;
                Debug.Log($"[FirebaseBootstrap] Signed in anonymously as {Uid}");
                Ready?.Invoke();
            }
            catch (Exception ex)
            {
                Fail($"Firebase init/sign-in failed: {ex.GetType().Name}: {ex.Message}");
            }
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
