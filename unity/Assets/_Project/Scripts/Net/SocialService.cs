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
            public int Wins { get; }
            public int MatchesPlayed { get; }

            public Friend(string uid, string name, int wins, int matchesPlayed)
            {
                Uid = uid;
                Name = name;
                Wins = wins;
                MatchesPlayed = matchesPlayed;
            }
        }

        /// <summary>One ranked leaderboard row.</summary>
        public readonly struct LeaderRow
        {
            public string Uid { get; }
            public string Name { get; }
            public int Wins { get; }
            public int Points { get; }
            public int MatchesPlayed { get; }
            public int Rank { get; }
            public bool IsSelf { get; }

            public LeaderRow(string uid, string name, int wins, int points, int matchesPlayed, int rank, bool isSelf)
            {
                Uid = uid;
                Name = name;
                Wins = wins;
                Points = points;
                MatchesPlayed = matchesPlayed;
                Rank = rank;
                IsSelf = isSelf;
            }
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

        private static string AsString(object? value) => value as string ?? string.Empty;

        private static bool AsBool(object? value) => value is bool b && b;
    }
}
