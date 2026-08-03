#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Functions;

namespace Pose.Net
{
    /// <summary>
    /// Fetches a server-issued deal seed from the <c>startMatch</c> Cloud
    /// Function. The client never chooses the seed — that is what stops a
    /// malicious host from searching for a seed that deals it a loaded hand
    /// (see <c>docs/DECISIONS/0007-settlement-replay-validation.md</c>). The
    /// server also stores the seed under the returned match id so M4.3's
    /// settlement can replay against it.
    ///
    /// Static (no MonoBehaviour) — it only needs the already-initialised
    /// Firebase Functions SDK. Call from the host once per round (initial deal
    /// and each rematch).
    /// </summary>
    public static class MatchService
    {
        /// <summary>A server-issued seed and the match id it was recorded under.</summary>
        public readonly struct IssuedSeed
        {
            public string MatchId { get; }
            public ulong Seed { get; }

            public IssuedSeed(string matchId, ulong seed)
            {
                MatchId = matchId;
                Seed = seed;
            }
        }

        /// <summary>
        /// Requests a fresh server seed for a round of the given size. Throws if
        /// the SDK is unavailable, the call fails, or the response is malformed —
        /// the caller decides how to degrade (the online controller falls back to
        /// a local seed during the pre-deploy gap).
        /// </summary>
        public static async Task<IssuedSeed> StartMatch(int playerCount)
        {
            FirebaseFunctions functions = FirebaseFunctions.DefaultInstance
                ?? throw new InvalidOperationException("Firebase Functions SDK not initialised.");

            Dictionary<string, object> payload = new()
            {
                ["playerCount"] = playerCount,
            };

            HttpsCallableReference fn = functions.GetHttpsCallable("startMatch");
            HttpsCallableResult result = await fn.CallAsync(payload);

            if (result.Data is not IDictionary<object, object> data)
            {
                throw new InvalidOperationException("startMatch returned an unexpected payload shape.");
            }

            string matchId = data.TryGetValue("matchId", out object? idObj) && idObj is string id
                ? id
                : throw new InvalidOperationException("startMatch response missing matchId.");

            string seedStr = data.TryGetValue("seed", out object? seedObj) && seedObj is string s
                ? s
                : throw new InvalidOperationException("startMatch response missing seed.");

            if (!ulong.TryParse(seedStr, out ulong seed))
            {
                throw new InvalidOperationException($"startMatch returned an unparseable seed: \"{seedStr}\".");
            }

            return new IssuedSeed(matchId, seed);
        }
    }
}
