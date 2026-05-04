#nullable enable
using System.Collections.Generic;

namespace Pose.Core
{
    /// <summary>
    /// Determines who leads the opening turn and which tile they must play.
    /// The standard rule across every variant Pose supports: the player holding
    /// the highest double leads with that tile (so [6|6] beats [5|5] beats … beats
    /// [0|0]). If no player holds any double, the player holding the single tile
    /// with the highest pip total leads with that tile, ties broken by the order
    /// players appear in the players list.
    /// </summary>
    public static class StartingPlayerRule
    {
        public readonly struct Lead
        {
            public PlayerId Player { get; }
            public Tile Tile { get; }

            public Lead(PlayerId player, Tile tile)
            {
                Player = player;
                Tile = tile;
            }
        }

        /// <summary>
        /// Finds the starting player and their leading tile. <paramref name="maxPip"/>
        /// is the maximum pip value present in the variant's tile set (6 for double-six,
        /// 9 for double-nine, etc.) and bounds the double-search range.
        /// </summary>
        public static Lead FindLead(
            IReadOnlyList<PlayerId> players,
            IReadOnlyDictionary<PlayerId, Hand> hands,
            byte maxPip)
        {
            for (int d = maxPip; d >= 0; d--)
            {
                Tile target = new((byte)d, (byte)d);
                for (int i = 0; i < players.Count; i++)
                {
                    PlayerId p = players[i];
                    if (hands[p].Contains(target))
                    {
                        return new Lead(p, target);
                    }
                }
            }

            // No player holds any double; fall back to the highest single tile.
            PlayerId bestPlayer = players[0];
            Tile bestTile = default;
            int bestPips = -1;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerId p = players[i];
                foreach (Tile t in hands[p])
                {
                    if (t.Pips > bestPips)
                    {
                        bestPips = t.Pips;
                        bestPlayer = p;
                        bestTile = t;
                    }
                }
            }

            return new Lead(bestPlayer, bestTile);
        }
    }
}
