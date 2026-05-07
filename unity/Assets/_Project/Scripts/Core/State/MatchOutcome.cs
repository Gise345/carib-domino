#nullable enable
using System.Collections.Generic;

namespace Pose.Core
{
    /// <summary>
    /// The result of a finished single-round match. <see cref="WinnerId"/> identifies
    /// the *triggering* player (the one who dominoed, or the lone holder of the
    /// lowest pip count); <see cref="WinningTeamId"/> identifies which team scores —
    /// the same as the winning player's team. Both are null only on a draw (a tied
    /// block end). <see cref="WinnerScore"/> is the sum of pips remaining in every
    /// losing player's hand; for a draw it is 0. Partner variants (introduced later
    /// in M1 step 3) may exclude teammates' pips from the score; the field's contract
    /// is just "the points the winning team earned this round".
    /// </summary>
    public sealed class MatchOutcome
    {
        public MatchEndReason Reason { get; }
        public PlayerId? WinnerId { get; }
        public TeamId? WinningTeamId { get; }
        public int WinnerScore { get; }
        public IReadOnlyDictionary<PlayerId, int> RemainingPips { get; }

        public bool IsDraw => WinnerId == null;

        public MatchOutcome(
            MatchEndReason reason,
            PlayerId? winnerId,
            TeamId? winningTeamId,
            int winnerScore,
            IReadOnlyDictionary<PlayerId, int> remainingPips)
        {
            Reason = reason;
            WinnerId = winnerId;
            WinningTeamId = winningTeamId;
            WinnerScore = winnerScore;
            RemainingPips = remainingPips;
        }
    }
}
