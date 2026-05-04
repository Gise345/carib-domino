#nullable enable
using System.Collections.Generic;

namespace Pose.Core
{
    /// <summary>
    /// The result of a finished single-round match. <see cref="WinnerId"/> is null
    /// only in the case of a Blocked tie (every winning candidate has the same
    /// lowest pip total). <see cref="WinnerScore"/> is the sum of pips remaining in
    /// every losing player's hand at the moment the match ended; for a draw it is 0.
    /// </summary>
    public sealed class MatchOutcome
    {
        public MatchEndReason Reason { get; }
        public PlayerId? WinnerId { get; }
        public int WinnerScore { get; }
        public IReadOnlyDictionary<PlayerId, int> RemainingPips { get; }

        public bool IsDraw => WinnerId == null;

        public MatchOutcome(
            MatchEndReason reason,
            PlayerId? winnerId,
            int winnerScore,
            IReadOnlyDictionary<PlayerId, int> remainingPips)
        {
            Reason = reason;
            WinnerId = winnerId;
            WinnerScore = winnerScore;
            RemainingPips = remainingPips;
        }
    }
}
