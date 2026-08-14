#nullable enable
using System;
using System.Collections.Generic;

namespace Pose.Core
{
    /// <summary>
    /// Produces an initial <see cref="MatchState"/> by shuffling the configured tile
    /// set with a deterministic PRNG and dealing fixed-size hands to each player.
    /// The starting player is chosen by <see cref="StartingPlayerRule"/>.
    /// </summary>
    public static class Dealer
    {
        /// <summary>
        /// Deals a new round. Given the same <paramref name="random"/> sequence,
        /// the same <paramref name="players"/> in the same order, and the same
        /// <paramref name="partnership"/>, this function is deterministic — the
        /// foundational property the eventual server-side validator (see
        /// <c>docs/ARCHITECTURE.md</c> §5) will rely on.
        /// </summary>
        /// <param name="openerIndex">
        /// Seat that must open the round. -1 (the default) means "use the
        /// standard opening rule" — the highest-double holder leads (round 1 of
        /// a game, or a cut-throat battle). A seat &gt;= 0 forces that seat to
        /// lead: the pose rule seats the previous round's winner here.
        /// </param>
        /// <param name="freeOpening">
        /// When true the opener may lead with ANY tile (a "free pose"), not the
        /// forced highest double. Ignored when <paramref name="openerIndex"/> is
        /// -1. See the pose rule (ADR 0015).
        /// </param>
        public static MatchState Deal(
            DealConfig config,
            IReadOnlyList<PlayerId> players,
            Partnership partnership,
            IRandomSource random,
            int openerIndex = -1,
            bool freeOpening = false)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (players == null)
            {
                throw new ArgumentNullException(nameof(players));
            }

            if (partnership == null)
            {
                throw new ArgumentNullException(nameof(partnership));
            }

            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            if (players.Count < 2)
            {
                throw new ArgumentException(
                    "A round requires at least two players.",
                    nameof(players));
            }

            int tilesNeeded = players.Count * config.TilesPerHand;
            if (tilesNeeded > config.TileSet.Count)
            {
                throw new ArgumentException(
                    $"Cannot deal {config.TilesPerHand} tiles to {players.Count} players from a " +
                    $"tile set of {config.TileSet.Count} tiles.",
                    nameof(players));
            }

            List<Tile> shuffled = ShuffleFisherYates(config.TileSet, random);

            Dictionary<PlayerId, Hand> hands = new(players.Count);
            for (int i = 0; i < players.Count; i++)
            {
                List<Tile> handTiles = shuffled.GetRange(i * config.TilesPerHand, config.TilesPerHand);
                hands[players[i]] = new Hand(handTiles);
            }

            // Pose rule: a valid openerIndex forces that seat to lead (the
            // previous round's winner). Otherwise the standard rule applies —
            // the highest-double holder leads with that tile.
            bool free;
            int startingIndex;
            if (openerIndex >= 0 && openerIndex < players.Count)
            {
                startingIndex = openerIndex;
                free = freeOpening;
            }
            else
            {
                StartingPlayerRule.Lead lead = StartingPlayerRule.FindLead(players, hands, config.MaxPip);
                startingIndex = IndexOfPlayer(players, lead.Player);
                free = false;
            }

            return new MatchState(
                players: players,
                partnership: partnership,
                currentPlayerIndex: startingIndex,
                hands: hands,
                chain: Chain.Empty,
                turnNumber: 0,
                consecutivePassCount: 0,
                history: Array.Empty<Move>(),
                isOver: false,
                freeOpening: free);
        }

        private static List<Tile> ShuffleFisherYates(IReadOnlyList<Tile> source, IRandomSource random)
        {
            List<Tile> shuffled = new(source);
            for (int i = shuffled.Count - 1; i > 0; i--)
            {
                int j = random.NextInt(i + 1);
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }
            return shuffled;
        }

        private static int IndexOfPlayer(IReadOnlyList<PlayerId> players, PlayerId target)
        {
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] == target)
                {
                    return i;
                }
            }
            throw new InvalidOperationException(
                $"Starting player {target} not found in players list.");
        }
    }
}
