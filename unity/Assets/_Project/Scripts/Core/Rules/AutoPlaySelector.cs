#nullable enable
using System;
using System.Collections.Generic;

namespace Pose.Core
{
    /// <summary>
    /// Picks the move played on a player's behalf when their turn timer expires.
    /// Pure C# and fully deterministic — the same legal-move list always yields
    /// the same move, with no <see cref="IRandomSource"/> involved. That matters
    /// because a timed-out turn emits an ordinary move into the round log, which
    /// settlement replays and re-validates like any other (ADR 0007); a
    /// non-deterministic pick would still validate, but a deterministic one keeps
    /// timeouts reproducible when diagnosing a disputed round.
    ///
    /// Priority, highest first:
    /// <list type="number">
    ///   <item>Any double, heaviest first — a double left in hand is the tile
    ///         most likely to strand its holder, so it goes down first.</item>
    ///   <item>Otherwise the highest pip total (6-5 beats 6-1).</item>
    ///   <item>Ties break on the higher single pip, then tile order, then the
    ///         chain end (Left before Right).</item>
    /// </list>
    /// Falls back to the pass when no placement is legal.
    /// </summary>
    public static class AutoPlaySelector
    {
        /// <summary>
        /// Chooses the auto-play move from a rule-engine legal-move list.
        /// </summary>
        /// <param name="legalMoves">
        /// The legal moves for the current player, as returned by
        /// <see cref="IRuleEngine.GetLegalMoves"/>.
        /// </param>
        /// <returns>
        /// The highest-priority <see cref="PlaceMove"/>, or the
        /// <see cref="PassMove"/> when the player has nothing playable. Never
        /// returns a <see cref="ResignMove"/> — a timeout must never surrender a
        /// player's stake.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="legalMoves"/> is empty, or contains no
        /// move this selector is willing to play. The rule engine guarantees a
        /// non-empty list whenever the match is not over, so callers should only
        /// invoke this on a live turn.
        /// </exception>
        public static Move Pick(IReadOnlyList<Move> legalMoves)
        {
            if (legalMoves == null)
            {
                throw new ArgumentNullException(nameof(legalMoves));
            }

            if (legalMoves.Count == 0)
            {
                throw new ArgumentException(
                    "Cannot auto-play from an empty legal-moves list.",
                    nameof(legalMoves));
            }

            PlaceMove? best = null;
            PassMove? pass = null;

            for (int i = 0; i < legalMoves.Count; i++)
            {
                switch (legalMoves[i])
                {
                    case PlaceMove candidate:
                        if (best == null || OutranksBest(candidate, best))
                        {
                            best = candidate;
                        }
                        break;

                    // Kept as the fallback rather than returned immediately: the
                    // engine lists the pass alongside placements in variants
                    // where passing is optional, and a placement always wins.
                    case PassMove p:
                        pass = p;
                        break;

                    // ResignMove and anything future is deliberately ignored.
                    default:
                        break;
                }
            }

            if (best != null)
            {
                return best;
            }

            if (pass != null)
            {
                return pass;
            }

            throw new ArgumentException(
                "Legal-moves list contained no placement or pass to auto-play.",
                nameof(legalMoves));
        }

        /// <summary>
        /// True when <paramref name="candidate"/> should be auto-played ahead of
        /// <paramref name="incumbent"/>. Strict — an exact tie leaves the
        /// incumbent in place, so the first-listed move wins and the result does
        /// not depend on the engine's enumeration order beyond that.
        /// </summary>
        private static bool OutranksBest(PlaceMove candidate, PlaceMove incumbent)
        {
            // 1. Doubles outrank every non-double regardless of weight.
            if (candidate.Tile.IsDouble != incumbent.Tile.IsDouble)
            {
                return candidate.Tile.IsDouble;
            }

            // 2. Heavier tile wins (among doubles this is the highest double).
            if (candidate.Tile.Pips != incumbent.Tile.Pips)
            {
                return candidate.Tile.Pips > incumbent.Tile.Pips;
            }

            // 3. Higher single pip: 6-2 beats 5-3 at equal weight.
            int candidateHigh = candidate.Tile.B;
            int incumbentHigh = incumbent.Tile.B;
            if (candidateHigh != incumbentHigh)
            {
                return candidateHigh > incumbentHigh;
            }

            // 4. Tile order. Equal weight and equal high pip already implies the
            //    same tile, so this only guards against a future tile shape.
            if (candidate.Tile.A != incumbent.Tile.A)
            {
                return candidate.Tile.A > incumbent.Tile.A;
            }

            // 5. Same tile, two open ends — settle on Left so the choice is total.
            return candidate.End == ChainEnd.Left && incumbent.End != ChainEnd.Left;
        }
    }
}
