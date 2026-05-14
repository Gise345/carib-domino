#nullable enable
using System;
using System.Collections.Generic;

namespace Pose.Core
{
    /// <summary>
    /// Jamaican Cut-Throat dominoes — every player for themselves, no teams. Standard
    /// double-six tile set, 2–4 players, no boneyard draws: a player who cannot match
    /// either chain end must pass. The round ends either when a player empties their
    /// hand (<see cref="MatchEndReason.Domino"/>) or when every player passes in
    /// succession (<see cref="MatchEndReason.Blocked"/>). Round winner scores the sum
    /// of every other player's remaining pips; a tied block ends in a draw with no
    /// score. Multi-round mechanics ("Six-Love", tied-block replay for 2 points) are
    /// out of scope at this layer — the engine produces a single-round outcome that a
    /// higher tournament/scoring layer wraps. The "Block / Draw (Anglo)" variant in
    /// <c>docs/ARCHITECTURE.md</c> §8.1 is a separate future ruleset; this type
    /// implements the Jamaican Cut-Throat row of that table.
    /// </summary>
    public sealed class CutThroatRules : IRuleEngine
    {
        private readonly byte _maxPip;

        /// <summary>
        /// Creates a Cut-Throat rule engine for a tile set with the given maximum pip
        /// value (6 for standard double-six). The max pip is needed only by the
        /// opening-turn rule to identify the leading double.
        /// </summary>
        public CutThroatRules(byte maxPip = 6)
        {
            _maxPip = maxPip;
        }

        public IReadOnlyList<Move> GetLegalMoves(MatchState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (state.IsOver)
            {
                return Array.Empty<Move>();
            }

            PlayerId player = state.CurrentPlayer;
            Hand hand = state.Hands[player];
            List<Move> moves = new();

            if (state.Chain.IsEmpty)
            {
                // Opening turn — only the leading tile (highest double, or highest
                // single tile if no doubles exist anywhere) may be played, by its
                // holder. The Dealer set CurrentPlayer to that holder; here we
                // re-derive the tile so the rule engine remains a pure function of
                // state with no Dealer-private knowledge.
                StartingPlayerRule.Lead lead = StartingPlayerRule.FindLead(
                    state.Players,
                    state.Hands,
                    _maxPip);

                moves.Add(new PlaceMove(player, lead.Tile, ChainEnd.Left));
                return moves;
            }

            byte left = state.Chain.LeftEnd;
            byte right = state.Chain.RightEnd;

            foreach (Tile tile in hand)
            {
                if (tile.Matches(left))
                {
                    moves.Add(new PlaceMove(player, tile, ChainEnd.Left));
                }

                if (tile.Matches(right))
                {
                    moves.Add(new PlaceMove(player, tile, ChainEnd.Right));
                }
            }

            if (moves.Count == 0)
            {
                moves.Add(new PassMove(player));
            }

            return moves;
        }

        public bool IsLegal(MatchState state, Move move)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (move == null)
            {
                throw new ArgumentNullException(nameof(move));
            }

            // Identity check: the move's player must be the current player. A move
            // submitted by anyone else is rejected as a structural violation.
            if (move.Player != state.CurrentPlayer)
            {
                return false;
            }

            IReadOnlyList<Move> legal = GetLegalMoves(state);
            for (int i = 0; i < legal.Count; i++)
            {
                if (MovesEqual(legal[i], move))
                {
                    return true;
                }
            }
            return false;
        }

        public MatchState Apply(MatchState state, Move move)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (move == null)
            {
                throw new ArgumentNullException(nameof(move));
            }

            if (state.IsOver)
            {
                throw new InvalidOperationException(
                    "Cannot apply a move to a finished match.");
            }

            if (!IsLegal(state, move))
            {
                throw new InvalidOperationException($"Illegal move: {move}");
            }

            List<Move> newHistory = new(state.History) { move };

            if (move is PlaceMove pm)
            {
                Hand newHand = state.Hands[pm.Player].Without(pm.Tile);
                Dictionary<PlayerId, Hand> newHands = new(state.Hands)
                {
                    [pm.Player] = newHand,
                };
                Chain newChain = state.Chain.Place(pm.Tile, pm.End);

                bool wonByDomino = newHand.Count == 0;
                int nextIdx = wonByDomino
                    ? state.CurrentPlayerIndex
                    : NextPlayerIndex(state);

                return state.With(
                    currentPlayerIndex: nextIdx,
                    hands: newHands,
                    chain: newChain,
                    turnNumber: state.TurnNumber + 1,
                    consecutivePassCount: 0,
                    history: newHistory,
                    isOver: wonByDomino);
            }

            if (move is PassMove)
            {
                int newPassCount = state.ConsecutivePassCount + 1;
                bool blocked = newPassCount >= state.Players.Count;
                int nextIdx = blocked
                    ? state.CurrentPlayerIndex
                    : NextPlayerIndex(state);

                return state.With(
                    currentPlayerIndex: nextIdx,
                    turnNumber: state.TurnNumber + 1,
                    consecutivePassCount: newPassCount,
                    history: newHistory,
                    isOver: blocked);
            }

            throw new ArgumentException(
                $"Unsupported move type: {move.GetType().Name}",
                nameof(move));
        }

        public MatchOutcome? GetOutcome(MatchState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (!state.IsOver)
            {
                return null;
            }

            Dictionary<PlayerId, int> remaining = new(state.Players.Count);
            for (int i = 0; i < state.Players.Count; i++)
            {
                PlayerId p = state.Players[i];
                remaining[p] = state.Hands[p].PipTotal;
            }

            // Check for Domino end: someone has zero tiles.
            PlayerId? dominoWinner = null;
            for (int i = 0; i < state.Players.Count; i++)
            {
                PlayerId p = state.Players[i];
                if (state.Hands[p].Count == 0)
                {
                    dominoWinner = p;
                    break;
                }
            }

            if (dominoWinner.HasValue)
            {
                int score = 0;
                for (int i = 0; i < state.Players.Count; i++)
                {
                    PlayerId p = state.Players[i];
                    if (p != dominoWinner.Value)
                    {
                        score += state.Hands[p].PipTotal;
                    }
                }

                return new MatchOutcome(
                    MatchEndReason.Domino,
                    dominoWinner,
                    state.Partnership.GetTeamOf(dominoWinner.Value),
                    score,
                    remaining);
            }

            // Block end: lowest pip total wins, ties produce a draw with no points.
            PlayerId? blockWinner = null;
            int lowest = int.MaxValue;
            bool tiedAtLowest = false;
            for (int i = 0; i < state.Players.Count; i++)
            {
                PlayerId p = state.Players[i];
                int pips = state.Hands[p].PipTotal;
                if (pips < lowest)
                {
                    lowest = pips;
                    blockWinner = p;
                    tiedAtLowest = false;
                }
                else if (pips == lowest)
                {
                    tiedAtLowest = true;
                }
            }

            if (tiedAtLowest)
            {
                return new MatchOutcome(
                    MatchEndReason.Blocked,
                    winnerId: null,
                    winningTeamId: null,
                    winnerScore: 0,
                    remaining);
            }

            int blockScore = 0;
            for (int i = 0; i < state.Players.Count; i++)
            {
                PlayerId p = state.Players[i];
                if (p != blockWinner!.Value)
                {
                    blockScore += state.Hands[p].PipTotal;
                }
            }

            // blockWinner is guaranteed non-null here: the loop above always
            // runs (a match has >= 2 players) and assigns blockWinner on the
            // first iteration, and the tiedAtLowest early-return is the only
            // path that skips this point.
            return new MatchOutcome(
                MatchEndReason.Blocked,
                blockWinner,
                state.Partnership.GetTeamOf(blockWinner!.Value),
                blockScore,
                remaining);
        }

        private static int NextPlayerIndex(MatchState state) =>
            (state.CurrentPlayerIndex + 1) % state.Players.Count;

        private static bool MovesEqual(Move a, Move b)
        {
            if (a.Player != b.Player)
            {
                return false;
            }

            if (a is PlaceMove ap && b is PlaceMove bp)
            {
                return ap.Tile == bp.Tile && ap.End == bp.End;
            }

            if (a is PassMove && b is PassMove)
            {
                return true;
            }

            return false;
        }
    }
}
