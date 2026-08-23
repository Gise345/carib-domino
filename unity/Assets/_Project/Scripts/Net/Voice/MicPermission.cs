#nullable enable
using System.Threading.Tasks;
using Pose.Core.Voice;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace Pose.Net.Voice
{
    /// <summary>
    /// The OS microphone permission, as a state the game can reason about
    /// (ADR 0024). Follows the <c>Haptics</c> precedent: a small static wrapper
    /// over a platform capability, with the platform mess confined to one file.
    ///
    /// The permission is asked for at FIRST MIC USE, never at launch. minSdk is 25,
    /// so the Android prompt is mandatory, and a prompt that arrives before the
    /// player has any idea why is reliably denied — after which Android and iOS
    /// both stop showing it, and only a trip to system settings can undo it. That
    /// is why <see cref="MicPermissionState.Denied"/> is a distinct state from
    /// <see cref="MicPermissionState.Unknown"/> rather than both being "false".
    /// </summary>
    public static class MicPermission
    {
        /// <summary>
        /// What the OS currently says. Cheap enough to poll — call it again after
        /// the app regains focus, since the player may have changed it in system
        /// settings while away.
        /// </summary>
        public static MicPermissionState Current
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return Permission.HasUserAuthorizedPermission(Permission.Microphone)
                    ? MicPermissionState.Granted
                    : MicPermissionState.Unknown;
#elif UNITY_IOS && !UNITY_EDITOR
                return Application.HasUserAuthorization(UserAuthorization.Microphone)
                    ? MicPermissionState.Granted
                    : MicPermissionState.Unknown;
#else
                // The Editor has no permission model; the device build is where
                // this is actually exercised.
                return MicPermissionState.Granted;
#endif
            }
        }

        /// <summary>
        /// Asks the OS for the microphone, showing the system prompt if it has not
        /// been answered yet. Safe to call when already granted — it returns
        /// immediately without prompting.
        /// </summary>
        /// <returns>The state after the player has answered.</returns>
        public static async Task<MicPermissionState> RequestAsync()
        {
            if (Current == MicPermissionState.Granted)
            {
                return MicPermissionState.Granted;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            return await RequestAndroidAsync();
#elif UNITY_IOS && !UNITY_EDITOR
            return await RequestIosAsync();
#else
            await Task.CompletedTask;
            return MicPermissionState.Granted;
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
        /// <summary>
        /// Bridges iOS's authorisation request onto a Task.
        ///
        /// Driven off <c>AsyncOperation.completed</c> rather than by awaiting the
        /// operation directly: whether an <c>AsyncOperation</c> is awaitable has
        /// varied across Unity versions, while the completion event has not.
        /// </summary>
        /// <returns>The state the player chose.</returns>
        private static Task<MicPermissionState> RequestIosAsync()
        {
            TaskCompletionSource<MicPermissionState> completion = new();

            AsyncOperation request = Application.RequestUserAuthorization(UserAuthorization.Microphone);
            request.completed += _ =>
                completion.TrySetResult(
                    Application.HasUserAuthorization(UserAuthorization.Microphone)
                        ? MicPermissionState.Granted
                        : MicPermissionState.Denied);

            return completion.Task;
        }
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>
        /// Bridges Android's callback-based permission request onto a Task.
        ///
        /// <c>Permission.RequestUserPermission</c> returns void and answers through
        /// <c>PermissionCallbacks</c>, so there is nothing to await directly. The
        /// callbacks are also not guaranteed to fire — backgrounding the app while
        /// the dialog is up can lose them — so the task is completed defensively
        /// and the caller should re-read <see cref="Current"/> on resume rather
        /// than trusting this result forever.
        /// </summary>
        /// <returns>The state the player chose.</returns>
        private static Task<MicPermissionState> RequestAndroidAsync()
        {
            TaskCompletionSource<MicPermissionState> completion = new();
            PermissionCallbacks callbacks = new();

            // TrySetResult, not SetResult: Android can deliver more than one of
            // these, and a second completion would throw.
            callbacks.PermissionGranted += _ => completion.TrySetResult(MicPermissionState.Granted);
            callbacks.PermissionDenied += _ => completion.TrySetResult(MicPermissionState.Denied);
            callbacks.PermissionDeniedAndDontAskAgain +=
                _ => completion.TrySetResult(MicPermissionState.Denied);

            Permission.RequestUserPermission(Permission.Microphone, callbacks);
            return completion.Task;
        }
#endif
    }
}
