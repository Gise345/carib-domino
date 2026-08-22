#nullable enable
using Pose.Net;
using UnityEditor;
using UnityEngine;

namespace Pose.EditorTools
{
    /// <summary>
    /// Editor-only helpers for reaching the sign-in screen during development.
    ///
    /// BoardBootstrap sends a player straight to the lobby when Firebase
    /// reports a persisted session, which is right for players and awkward for
    /// testing: once you have signed in even once, the login screen never
    /// appears again. Signing out clears that session, so the next Play starts
    /// at login.
    /// </summary>
    internal static class AuthDevMenu
    {
        private const string SignOutItem = "Pose/Auth/Sign Out (clears saved session)";
        private const string StatusItem = "Pose/Auth/Log Session Status";

        [MenuItem(SignOutItem, isValidateFunction: false, priority = 100)]
        private static void SignOut()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog(
                    "Enter Play mode first",
                    "AuthService only exists while the game is running.\n\n" +
                    "Press Play, run this again, then stop and Play once more — " +
                    "the sign-in screen will be waiting.",
                    "OK");
                return;
            }

            if (AuthService.Instance == null)
            {
                Debug.LogWarning("[AuthDevMenu] AuthService has not started yet. Give it a moment.");
                return;
            }

            AuthService.Instance.SignOut();
            Debug.Log(
                "[AuthDevMenu] Signed out. Stop Play mode and press Play again to " +
                "land on the sign-in screen.");
        }

        [MenuItem(StatusItem, isValidateFunction: false, priority = 101)]
        private static void LogStatus()
        {
            if (!Application.isPlaying || AuthService.Instance == null)
            {
                Debug.Log("[AuthDevMenu] Not in Play mode — no session to report.");
                return;
            }

            AuthService auth = AuthService.Instance;
            Debug.Log(
                $"[AuthDevMenu] signed in: {auth.IsSignedIn}, uid: {auth.Uid ?? "(none)"}, " +
                $"email linked: {auth.HasEmail}, facebook linked: {auth.IsFacebookLinked}");
        }
    }
}
