#nullable enable

namespace Pose.Core
{
    /// <summary>
    /// What a single <see cref="MatchSignalTracker.Observe"/> call concluded
    /// about the replicated match fields since the previous observation.
    /// </summary>
    public readonly struct MatchSignal
    {
        /// <summary>No transition occurred.</summary>
        public static readonly MatchSignal None = default;

        /// <summary>The very first deal became available on this client.</summary>
        public bool DealStarted { get; }

        /// <summary>A subsequent round (a rematch) began on this client.</summary>
        public bool RoundStarted { get; }

        /// <summary>Inclusive lower bound of newly-appended moves.</summary>
        public int MovesFrom { get; }

        /// <summary>Exclusive upper bound of newly-appended moves.</summary>
        public int MovesTo { get; }

        /// <summary>True when <see cref="MovesTo"/> exceeds <see cref="MovesFrom"/>.</summary>
        public bool HasMoves => MovesTo > MovesFrom;

        internal MatchSignal(bool dealStarted, bool roundStarted, int movesFrom, int movesTo)
        {
            DealStarted = dealStarted;
            RoundStarted = roundStarted;
            MovesFrom = movesFrom;
            MovesTo = movesTo;
        }
    }

    /// <summary>
    /// Edge-detects the replicated fields of a networked match — "has the deal
    /// landed", "has a rematch begun", "which move indices are new" — from a
    /// stream of point-in-time observations. Pure and Unity-free so the
    /// sequencing can be unit-tested without standing up Fusion; the networked
    /// behaviour merely feeds it the current field values once per render.
    ///
    /// The subtlety this type exists to contain: a rematch resets the move
    /// count to zero. A naive <c>moveCount &gt; _lastMoveCount</c> detector
    /// therefore goes deaf after a rematch — it sits at the previous round's
    /// high-water mark and silently swallows the new round's opening moves,
    /// desyncing the clients. Observing the round number alongside the move
    /// count lets us rebase the move cursor exactly when (and only when) the
    /// round actually advanced.
    /// </summary>
    public sealed class MatchSignalTracker
    {
        private bool _dealt;
        private int _lastRoundNumber;
        private int _lastMoveCount;

        /// <summary>
        /// Folds the current replicated values into the tracker and reports
        /// what changed. Safe to call every frame; returns
        /// <see cref="MatchSignal.None"/> when nothing advanced.
        /// </summary>
        /// <param name="dealReady">
        /// Latched true once both players have registered. Never returns to
        /// false for the lifetime of the match.
        /// </param>
        /// <param name="roundNumber">
        /// 0 before the first deal, 1 for the first round, incrementing once
        /// per rematch.
        /// </param>
        /// <param name="moveCount">
        /// Length of the authoritative move log for the <em>current</em> round.
        /// Resets to 0 whenever <paramref name="roundNumber"/> advances.
        /// </param>
        /// <returns>The transitions detected by this observation.</returns>
        public MatchSignal Observe(bool dealReady, int roundNumber, int moveCount)
        {
            if (!dealReady && !_dealt)
            {
                return MatchSignal.None;
            }

            bool dealStarted = false;
            bool roundStarted = false;

            if (!_dealt)
            {
                // First observation with the deal latched. Adopt the round we
                // landed in rather than assuming 1 — a client that joins on a
                // later snapshot must not replay a phantom rematch.
                _dealt = true;
                _lastRoundNumber = roundNumber;
                _lastMoveCount = 0;
                dealStarted = true;
            }
            else if (roundNumber > _lastRoundNumber)
            {
                // Rematch. Rebase the move cursor so the new round's log
                // replays from its own index 0.
                _lastRoundNumber = roundNumber;
                _lastMoveCount = 0;
                roundStarted = true;
            }

            // A deal or a rematch can arrive in the same snapshot as moves that
            // were already appended to the new round (late-joining client, or a
            // snapshot coalescing several ticks). Falling through to the move
            // check rather than returning early replays them in one range.
            if (moveCount > _lastMoveCount)
            {
                int from = _lastMoveCount;
                _lastMoveCount = moveCount;
                return new MatchSignal(dealStarted, roundStarted, from, moveCount);
            }

            // moveCount < _lastMoveCount without a round advance means the log
            // rewound without our seeing why. Refuse to rewind the cursor: a
            // stale snapshot must not cause us to re-apply moves we already
            // folded into local state.
            return new MatchSignal(dealStarted, roundStarted, 0, 0);
        }
    }
}
