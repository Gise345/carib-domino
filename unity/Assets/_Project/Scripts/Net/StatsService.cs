#nullable enable
using System;
using System.Collections.Generic;
using Firebase.Functions;
using Pose.Core;
using UnityEngine;

namespace Pose.Net
{
    /// <summary>
    /// Calls the <c>submitMatchResult</c> Cloud Function when a round ends.
    /// Stats writes go through Cloud Functions, not direct client writes, per
    /// the trust model in <c>docs/ARCHITECTURE.md</c> §4 — Firestore rules
    /// deny client writes to <c>/stats/{uid}</c>, so this is the only path
    /// through which stats ever update.
    ///
    /// For M2.3 the Cloud Function trusts the payload (no replay validation
    /// yet). M4's settlement pipeline adds the canonical TS rule engine that
    /// recomputes the outcome from the seed + move log and rejects mismatches.
    /// </summary>
    public sealed class StatsService : MonoBehaviour
    {
        public static StatsService? Instance { get; private set; }

        private FirebaseFunctions? _functions;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            _functions = FirebaseFunctions.DefaultInstance;
        }

        /// <summary>
        /// Submits the round outcome to the settlement Cloud Function from
        /// the supplied <paramref name="humanPlayer"/>'s perspective. Fire-
        /// and-forget; failures are logged but don't block the game loop.
        /// </summary>
        public async void SubmitRoundResult(MatchOutcome outcome, PlayerId humanPlayer)
        {
            if (_functions == null)
            {
                Debug.LogWarning("[StatsService] Functions SDK not initialised — skipping submit.");
                return;
            }

            try
            {
                string outcomeStr = ResolveOutcomeFor(humanPlayer, outcome);
                string endReasonStr = outcome.Reason switch
                {
                    MatchEndReason.Domino => "domino",
                    MatchEndReason.Blocked => "blocked",
                    _ => "draw",
                };
                // Score is only meaningful for the winner; pass 0 otherwise so
                // the server-side schema's min(0) check passes uniformly.
                int score = outcomeStr == "won" ? outcome.WinnerScore : 0;

                Dictionary<string, object> payload = new()
                {
                    ["outcome"] = outcomeStr,
                    ["endReason"] = endReasonStr,
                    ["score"] = score,
                };

                HttpsCallableReference fn = _functions.GetHttpsCallable("submitMatchResult");
                HttpsCallableResult result = await fn.CallAsync(payload);

                Debug.Log(
                    $"[StatsService] Submitted result: {outcomeStr} " +
                    $"(reason={endReasonStr}, score={score})");
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[StatsService] submitMatchResult failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static string ResolveOutcomeFor(PlayerId humanPlayer, MatchOutcome outcome)
        {
            if (outcome.IsDraw)
            {
                return "draw";
            }
            return outcome.WinnerId == humanPlayer ? "won" : "lost";
        }
    }
}
