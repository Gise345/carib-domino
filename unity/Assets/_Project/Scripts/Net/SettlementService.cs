#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Functions;

namespace Pose.Net
{
    /// <summary>
    /// Submits a finished online round to the <c>submitRoundLog</c> Cloud
    /// Function. The client sends only raw inputs — the match id, seat order,
    /// per-seat uids and the move log — never a claimed outcome. The server
    /// replays the round from the seed it issued and writes the recomputed
    /// result (see <c>docs/DECISIONS/0007-settlement-replay-validation.md</c>).
    ///
    /// Only the host submits (it's the one seat bound to the match server-side).
    /// Static — it only needs the initialised Firebase Functions SDK.
    /// </summary>
    public static class SettlementService
    {
        /// <summary>
        /// Submits the round for settlement. Throws on SDK/network/validation
        /// failure; callers fire-and-forget and log.
        /// </summary>
        /// <param name="matchId">The server-issued id the seed was recorded under.</param>
        /// <param name="players">Display names in seat order.</param>
        /// <param name="seatUids">Firebase uid per seat (empty string where unknown).</param>
        /// <param name="moves">The round's move log, in order.</param>
        public static async Task SubmitRoundLog(
            string matchId,
            IReadOnlyList<string> players,
            IReadOnlyList<string> seatUids,
            IReadOnlyList<NetworkedMove> moves)
        {
            FirebaseFunctions functions = FirebaseFunctions.DefaultInstance
                ?? throw new InvalidOperationException("Firebase Functions SDK not initialised.");

            List<object> moveList = new(moves.Count);
            foreach (NetworkedMove m in moves)
            {
                Dictionary<string, object> encoded = new()
                {
                    ["playerIndex"] = (int)m.PlayerIndex,
                    ["kind"] = KindString(m.Kind),
                };
                if (m.Kind == NetworkedMove.KindPlace)
                {
                    encoded["low"] = (int)m.LowPip;
                    encoded["high"] = (int)m.HighPip;
                    encoded["end"] = m.EndSide == 0 ? "left" : "right";
                }
                moveList.Add(encoded);
            }

            Dictionary<string, object> payload = new()
            {
                ["matchId"] = matchId,
                ["players"] = new List<object>(players),
                ["seatUids"] = new List<object>(seatUids),
                ["moves"] = moveList,
            };

            HttpsCallableReference fn = functions.GetHttpsCallable("submitRoundLog");
            await fn.CallAsync(payload);
        }

        private static string KindString(byte kind) => kind switch
        {
            NetworkedMove.KindPlace => "place",
            NetworkedMove.KindPass => "pass",
            NetworkedMove.KindResign => "resign",
            _ => "pass",
        };
    }
}
