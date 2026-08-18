#nullable enable
using System;
using System.Threading.Tasks;
using Facebook.Unity;
using UnityEngine;

namespace Pose.Net
{
    /// <summary>
    /// Thin async wrapper over the Facebook SDK for Unity, isolating the
    /// <c>Facebook.Unity</c> dependency behind Task-returning calls so
    /// <see cref="AuthService"/> (and nothing else) touches the SDK. Purely the
    /// Facebook side — it yields an access token; exchanging that for a Firebase
    /// credential and linking it to the account is <see cref="AuthService"/>'s
    /// job. See ADR 0019.
    /// </summary>
    public static class FacebookAuthService
    {
        // We request only what the social features need: the public profile (name
        // + picture, for the profile card / leaderboard) and the list of friends
        // who also play Pose (user_friends — Facebook only ever returns friends
        // who granted the same permission). No posting, no full friend list.
        private static readonly string[] ReadPermissions = { "public_profile", "user_friends" };

        /// <summary>
        /// Initialises the SDK if it hasn't been already. Safe to call repeatedly;
        /// resolves immediately once initialised.
        /// </summary>
        public static Task InitAsync()
        {
            if (FB.IsInitialized)
            {
                FB.ActivateApp();
                return Task.CompletedTask;
            }

            TaskCompletionSource<bool> tcs = new();
            FB.Init(() =>
            {
                FB.ActivateApp();
                tcs.TrySetResult(true);
            });
            return tcs.Task;
        }

        /// <summary>
        /// Shows the Facebook login dialog and returns the granted access token,
        /// or <c>null</c> if the player cancelled. Throws on an SDK-reported error.
        /// </summary>
        public static async Task<string?> LoginAsync()
        {
            await InitAsync();

            TaskCompletionSource<ILoginResult> tcs = new();
            FB.LogInWithReadPermissions(ReadPermissions, result => tcs.TrySetResult(result));
            ILoginResult login = await tcs.Task;

            if (login.Cancelled)
            {
                return null;
            }
            if (!string.IsNullOrEmpty(login.Error))
            {
                throw new InvalidOperationException($"Facebook login failed: {login.Error}");
            }

            return login.AccessToken?.TokenString;
        }

        /// <summary>Clears the local Facebook session (does not unlink Firebase).</summary>
        public static void Logout()
        {
            if (FB.IsInitialized && FB.IsLoggedIn)
            {
                FB.LogOut();
            }
        }
    }
}
