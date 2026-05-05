#nullable enable
using System;
using System.Collections.Generic;

namespace Pose.Core
{
    /// <summary>
    /// An immutable snapshot of an in-progress single round of dominoes. Pure data;
    /// the rule engine returns a new <see cref="MatchState"/> for every move applied
    /// (see <see cref="IRuleEngine.Apply"/>). Multi-round target-to-N scoring wraps
    /// this type at a higher layer and is intentionally out of scope here.
    /// </summary>
    public sealed class MatchState
    {
        /// <summary>
        /// The participants in **turn order**. Index 0 plays first (after opening
        /// rules pick the leader), index 1 next, and so on, wrapping. The engine is
        /// direction-agnostic — it simply iterates this list — but the canonical
        /// Jamaican variants (Cut-Throat, Partner) describe play as anticlockwise
        /// around a physical table, so when seating is rendered, list order
        /// corresponds to anticlockwise seating from the dealer/leader.
        /// </summary>
        public IReadOnlyList<PlayerId> Players { get; }
        public int CurrentPlayerIndex { get; }
        public IReadOnlyDictionary<PlayerId, Hand> Hands { get; }
        public Chain Chain { get; }
        public int TurnNumber { get; }
        public int ConsecutivePassCount { get; }
        public IReadOnlyList<Move> History { get; }
        public bool IsOver { get; }

        public PlayerId CurrentPlayer => Players[CurrentPlayerIndex];

        public MatchState(
            IReadOnlyList<PlayerId> players,
            int currentPlayerIndex,
            IReadOnlyDictionary<PlayerId, Hand> hands,
            Chain chain,
            int turnNumber,
            int consecutivePassCount,
            IReadOnlyList<Move> history,
            bool isOver)
        {
            if (players == null)
            {
                throw new ArgumentNullException(nameof(players));
            }

            if (players.Count < 2)
            {
                throw new ArgumentException(
                    "A match requires at least two players.",
                    nameof(players));
            }

            if (currentPlayerIndex < 0 || currentPlayerIndex >= players.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(currentPlayerIndex),
                    "Current player index out of range.");
            }

            Players = players;
            CurrentPlayerIndex = currentPlayerIndex;
            Hands = hands ?? throw new ArgumentNullException(nameof(hands));
            Chain = chain ?? throw new ArgumentNullException(nameof(chain));
            TurnNumber = turnNumber;
            ConsecutivePassCount = consecutivePassCount;
            History = history ?? throw new ArgumentNullException(nameof(history));
            IsOver = isOver;
        }

        /// <summary>
        /// Returns a copy of this state with the listed fields replaced.
        /// All other fields are preserved. Players list is not mutable through With().
        /// </summary>
        public MatchState With(
            int? currentPlayerIndex = null,
            IReadOnlyDictionary<PlayerId, Hand>? hands = null,
            Chain? chain = null,
            int? turnNumber = null,
            int? consecutivePassCount = null,
            IReadOnlyList<Move>? history = null,
            bool? isOver = null)
        {
            return new MatchState(
                Players,
                currentPlayerIndex ?? CurrentPlayerIndex,
                hands ?? Hands,
                chain ?? Chain,
                turnNumber ?? TurnNumber,
                consecutivePassCount ?? ConsecutivePassCount,
                history ?? History,
                isOver ?? IsOver);
        }
    }
}
