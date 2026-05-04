#nullable enable
using System.Collections.Generic;

namespace Pose.Core
{
    /// <summary>
    /// Variant-specific gameplay rules. Implementations are pure: every method is a
    /// function of its inputs only — no internal mutable state. This is what allows
    /// the eventual server-side validator (TypeScript port, see
    /// <c>docs/ARCHITECTURE.md</c> section 5 phase 4) to replay an entire match
    /// from a server-issued seed and verify every move was legal.
    /// </summary>
    public interface IRuleEngine
    {
        /// <summary>
        /// Returns every legal move available to the current player in this state.
        /// If the player has no playable tile, returns a single <see cref="PassMove"/>.
        /// Returns an empty list iff the match has already ended.
        /// </summary>
        IReadOnlyList<Move> GetLegalMoves(MatchState state);

        /// <summary>
        /// Validates that <paramref name="move"/> is legal in <paramref name="state"/>.
        /// </summary>
        bool IsLegal(MatchState state, Move move);

        /// <summary>
        /// Applies <paramref name="move"/> to <paramref name="state"/> and returns the
        /// resulting state. Throws <see cref="System.InvalidOperationException"/> if
        /// the move is not legal — callers wishing to validate without throwing should
        /// call <see cref="IsLegal"/> first.
        /// </summary>
        MatchState Apply(MatchState state, Move move);

        /// <summary>
        /// If the match has ended in <paramref name="state"/>, returns the outcome.
        /// Returns null otherwise.
        /// </summary>
        MatchOutcome? GetOutcome(MatchState state);
    }
}
