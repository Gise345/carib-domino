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
        /// Deals a new round. Given the same <paramref name="random"/> sequence and
        /// the same <paramref name="players"/> in the same order, this function is
        /// deterministic — the foundational property the eventual server-side
        /// validator (see <c>docs/ARCHITECTURE.md</c> section 5) will rely on.
        /// </summary>
        public static MatchState Deal(
            DealConfig config,
            IReadOnlyList<PlayerId> players,
            IRandomSource random)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (players == null)
            {
                throw new ArgumentNullException(nameof(players));
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

            StartingPlayerRule.Lead lead = StartingPlayerRule.FindLead(players, hands, config.MaxPip);
            int startingIndex = IndexOfPlayer(players, lead.Player);

            return new MatchState(
                players: players,
                currentPlayerIndex: startingIndex,
                hands: hands,
                chain: Chain.Empty,
                turnNumber: 0,
                consecutivePassCount: 0,
                history: Array.Empty<Move>(),
                isOver: false);
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
