#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Facebook.Unity;
using Firebase.Functions;

namespace Pose.Net
{
    /// <summary>
    /// Client data layer for the M7 social features: reads the player's Facebook
    /// friends who also play Pose and the leaderboard. Wraps the Facebook graph
    /// (<c>/me/friends</c>) and the <c>resolveFacebookFriends</c> /
    /// <c>getLeaderboard</c> Cloud Functions; the UI binds to the returned
    /// structs. Static — it only needs the initialised Facebook + Functions SDKs.
    /// See ADR 0019.
    /// </summary>
    public static class SocialService
    {
        /// <summary>An app friend resolved from the Facebook graph.</summary>
        public readonly struct Friend
        {
            public string Uid { get; }
            public string Name { get; }
            public string PhotoURL { get; }
            public int Wins { get; }
            public int MatchesPlayed { get; }

            public Friend(string uid, string name, string photoURL, int wins, int matchesPlayed)
            {
                Uid = uid;
                Name = name;
                PhotoURL = photoURL;
                Wins = wins;
                MatchesPlayed = matchesPlayed;
            }
        }

        /// <summary>One ranked leaderboard row.</summary>
        public readonly struct LeaderRow
        {
            public string Uid { get; }
            public string Name { get; }
            public string PhotoURL { get; }
            public int Wins { get; }
            public int Points { get; }
            public int MatchesPlayed { get; }
            public int Rank { get; }
            public bool IsSelf { get; }

            public LeaderRow(string uid, string name, string photoURL, int wins, int points, int matchesPlayed, int rank, bool isSelf)
            {
                Uid = uid;
                Name = name;
                PhotoURL = photoURL;
                Wins = wins;
                Points = points;
                MatchesPlayed = matchesPlayed;
                Rank = rank;
                IsSelf = isSelf;
            }
        }

        /// <summary>Outcome of sending a Facebook invite and claiming its reward.</summary>
        public readonly struct InviteResult
        {
            public bool Rewarded { get; }
            public int Coins { get; }
            public int RemainingToday { get; }
            /// <summary>"ok", "cap", "duplicate", "cancelled", or an error marker.</summary>
            public string Outcome { get; }

            public InviteResult(bool rewarded, int coins, int remainingToday, string outcome)
            {
                Rewarded = rewarded;
                Coins = coins;
                RemainingToday = remainingToday;
                Outcome = outcome;
            }
        }

        /// <summary>
        /// Opens the Facebook game-request (invite) dialog, then claims the reward
        /// via the <c>claimInviteReward</c> Cloud Function (server-authoritative:
        /// +250 coins, capped per day, de-duped by the request id). Rewards on
        /// send, per ADR 0017; returns a "cancelled" outcome if the player backs out.
        /// </summary>
        public static async Task<InviteResult> SendInviteAndClaimAsync(string message, string title)
        {
            await FacebookAuthService.InitAsync();
            if (!FB.IsLoggedIn)
            {
                return new InviteResult(false, 0, 0, "not_logged_in");
            }

            TaskCompletionSource<IAppRequestResult> tcs = new();
            FB.AppRequest(
                message: message,
                to: null,
                filters: null,
                excludeIds: null,
                maxRecipients: null,
                data: string.Empty,
                title: title,
                callback: result => tcs.TrySetResult(result));
            IAppRequestResult request = await tcs.Task;

            if (request.Cancelled)
            {
                return new InviteResult(false, 0, 0, "cancelled");
            }
            if (!string.IsNullOrEmpty(request.Error))
            {
                throw new InvalidOperationException($"Facebook invite failed: {request.Error}");
            }

            // Reward is de-duped server-side by this id; use the FB request id, or
            // a fresh guid if the SDK returned none.
            string inviteId = !string.IsNullOrEmpty(request.RequestID)
                ? request.RequestID
                : Guid.NewGuid().ToString();

            Dictionary<string, object> payload = new() { ["inviteId"] = inviteId };
            HttpsCallableResult result = await FirebaseFunctions.DefaultInstance
                .GetHttpsCallable("claimInviteReward").CallAsync(payload);

            if (result.Data is IDictionary d)
            {
                return new InviteResult(
                    AsBool(d["rewarded"]),
                    AsInt(d["coins"]),
                    AsInt(d["remainingToday"]),
                    AsString(d["outcome"]));
            }
            return new InviteResult(false, 0, 0, "unknown");
        }

        /// <summary>The player's own profile-card aggregate (name, coins, record).</summary>
        public readonly struct ProfileCard
        {
            public string Name { get; }
            public string PhotoURL { get; }
            public int Coins { get; }
            public int MatchesPlayed { get; }
            public int Wins { get; }
            public int Losses { get; }
            public int Draws { get; }
            public float WinRate { get; }

            public ProfileCard(string name, string photoURL, int coins, int matchesPlayed, int wins, int losses, int draws, float winRate)
            {
                Name = name;
                PhotoURL = photoURL;
                Coins = coins;
                MatchesPlayed = matchesPlayed;
                Wins = wins;
                Losses = losses;
                Draws = draws;
                WinRate = winRate;
            }
        }

        /// <summary>
        /// Reads the caller's own profile-card aggregate (name, coins, record) via
        /// the <c>getProfile</c> Cloud Function — one call for the Profile tab.
        /// </summary>
        public static async Task<ProfileCard> GetProfileAsync()
        {
            HttpsCallableResult result = await FirebaseFunctions.DefaultInstance
                .GetHttpsCallable("getProfile").CallAsync();

            if (result.Data is IDictionary d)
            {
                return new ProfileCard(
                    AsString(d["name"]),
                    AsString(d["photoURL"]),
                    AsInt(d["coins"]),
                    AsInt(d["matchesPlayed"]),
                    AsInt(d["wins"]),
                    AsInt(d["losses"]),
                    AsInt(d["draws"]),
                    AsFloat(d["winRate"]));
            }
            return default;
        }

        /// <summary>
        /// Convenience: fetches the player's Facebook friend ids and resolves them
        /// to app friends in one call. Empty if not signed into Facebook.
        /// </summary>
        public static async Task<List<Friend>> GetPlayingFriendsAsync()
        {
            List<string> ids = await GetFacebookFriendIdsAsync();
            return await ResolveFacebookFriendsAsync(ids);
        }

        /// <summary>
        /// Reads the Facebook ids of friends who also play Pose (Facebook only
        /// returns friends who granted <c>user_friends</c>). Empty if not logged in.
        /// </summary>
        public static async Task<List<string>> GetFacebookFriendIdsAsync()
        {
            List<string> ids = new();
            if (!FB.IsInitialized || !FB.IsLoggedIn)
            {
                return ids;
            }

            TaskCompletionSource<IGraphResult> tcs = new();
            FB.API("/me/friends", HttpMethod.GET, result => tcs.TrySetResult(result));
            IGraphResult result = await tcs.Task;
            if (!string.IsNullOrEmpty(result.Error))
            {
                throw new InvalidOperationException($"Facebook friends fetch failed: {result.Error}");
            }

            IDictionary<string, object> dict = result.ResultDictionary;
            if (dict != null
                && dict.TryGetValue("data", out object dataObj)
                && dataObj is IEnumerable<object> arr)
            {
                foreach (object item in arr)
                {
                    if (item is IDictionary<string, object> f
                        && f.TryGetValue("id", out object idObj)
                        && idObj is string id)
                    {
                        ids.Add(id);
                    }
                }
            }
            return ids;
        }

        /// <summary>
        /// Resolves Facebook friend ids to app friends (uid + name + record) via
        /// the <c>resolveFacebookFriends</c> Cloud Function.
        /// </summary>
        public static async Task<List<Friend>> ResolveFacebookFriendsAsync(IReadOnlyList<string> facebookIds)
        {
            List<Friend> friends = new();
            if (facebookIds.Count == 0)
            {
                return friends;
            }

            Dictionary<string, object> payload = new()
            {
                ["facebookIds"] = new List<object>(facebookIds),
            };
            HttpsCallableResult result = await FirebaseFunctions.DefaultInstance
                .GetHttpsCallable("resolveFacebookFriends").CallAsync(payload);

            if (result.Data is IDictionary data
                && data["friends"] is IEnumerable<object> arr)
            {
                foreach (object item in arr)
                {
                    if (item is IDictionary f)
                    {
                        friends.Add(new Friend(
                            AsString(f["uid"]),
                            AsString(f["name"]),
                            AsString(f["photoURL"]),
                            AsInt(f["wins"]),
                            AsInt(f["matchesPlayed"])));
                    }
                }
            }
            return friends;
        }

        /// <summary>
        /// Reads a ranked leaderboard via the <c>getLeaderboard</c> Cloud Function.
        /// </summary>
        /// <param name="scope">"global" or "friends".</param>
        /// <param name="metric">"wins" or "points".</param>
        /// <param name="friendUids">App uids to rank for the "friends" scope.</param>
        public static async Task<List<LeaderRow>> GetLeaderboardAsync(
            string scope, string metric, IReadOnlyList<string>? friendUids = null)
        {
            List<LeaderRow> rows = new();

            Dictionary<string, object> payload = new()
            {
                ["scope"] = scope,
                ["metric"] = metric,
            };
            if (friendUids != null)
            {
                payload["friendUids"] = new List<object>(friendUids);
            }

            HttpsCallableResult result = await FirebaseFunctions.DefaultInstance
                .GetHttpsCallable("getLeaderboard").CallAsync(payload);

            if (result.Data is IDictionary data
                && data["rows"] is IEnumerable<object> arr)
            {
                foreach (object item in arr)
                {
                    if (item is IDictionary r)
                    {
                        rows.Add(new LeaderRow(
                            AsString(r["uid"]),
                            AsString(r["name"]),
                            AsString(r["photoURL"]),
                            AsInt(r["wins"]),
                            AsInt(r["points"]),
                            AsInt(r["matchesPlayed"]),
                            AsInt(r["rank"]),
                            AsBool(r["isSelf"])));
                    }
                }
            }
            return rows;
        }

        // Firebase Functions returns JSON numbers as long/double and may omit a
        // key (null). These coerce defensively.
        private static int AsInt(object? value) => value switch
        {
            long l => (int)l,
            int i => i,
            double d => (int)d,
            string s when int.TryParse(s, out int parsed) => parsed,
            _ => 0,
        };

        private static float AsFloat(object? value) => value switch
        {
            double d => (float)d,
            long l => l,
            int i => i,
            float f => f,
            string s when float.TryParse(s, out float parsed) => parsed,
            _ => 0f,
        };

        private static string AsString(object? value) => value as string ?? string.Empty;

        private static bool AsBool(object? value) => value is bool b && b;
    }
}
