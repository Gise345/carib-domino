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
        public Partnership Partnership { get; }
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
            Partnership partnership,
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

            if (partnership == null)
            {
                throw new ArgumentNullException(nameof(partnership));
            }

            // Integrity invariant: the partnership must cover exactly the same set of
            // players that are playing this round. A mismatch would mean either an
            // un-partnered player can play moves that have no team to score, or a
            // partnership references a player who isn't in the round at all.
            ValidatePartnershipMatchesPlayers(players, partnership);

            if (currentPlayerIndex < 0 || currentPlayerIndex >= players.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(currentPlayerIndex),
                    "Current player index out of range.");
            }

            Players = players;
            Partnership = partnership;
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
        /// All other fields are preserved. Players and Partnership are not mutable
        /// through With() — they are fixed for the lifetime of a round.
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
                Partnership,
                currentPlayerIndex ?? CurrentPlayerIndex,
                hands ?? Hands,
                chain ?? Chain,
                turnNumber ?? TurnNumber,
                consecutivePassCount ?? ConsecutivePassCount,
                history ?? History,
                isOver ?? IsOver);
        }

        private static void ValidatePartnershipMatchesPlayers(
            IReadOnlyList<PlayerId> players,
            Partnership partnership)
        {
            HashSet<PlayerId> playerSet = new(players);
            HashSet<PlayerId> partnershipPlayers = new();
            for (int i = 0; i < partnership.Teams.Count; i++)
            {
                Team t = partnership.Teams[i];
                for (int j = 0; j < t.Members.Count; j++)
                {
                    partnershipPlayers.Add(t.Members[j]);
                }
            }

            if (!playerSet.SetEquals(partnershipPlayers))
            {
                throw new ArgumentException(
                    "Partnership must contain exactly the players in the match " +
                    "(no extras, no missing).",
                    nameof(partnership));
            }
        }
    }
}
