#nullable enable
using System;
using System.Collections.Generic;

namespace Pose.Core
{
    /// <summary>
    /// The running state of a Cut-Throat match SERIES — the layer above a single
    /// round. Tracks each player's games won and cumulative points (1000 per win),
    /// decides when the match is over, and models the cut-throat "battle": whenever
    /// two or more players are tied for the lead on games won, the next round is a
    /// battle (double-six poses) and stays so until one of the tied players wins —
    /// at which point the other tied player(s) are wiped back to LOVE (points and
    /// games won reset to 0). Immutable: <see cref="ApplyRound"/> returns a new
    /// instance. Cut-Throat only for now.
    /// </summary>
    public sealed class SeriesState
    {
        public IReadOnlyList<PlayerId> Players { get; }
        public MatchFormat Format { get; }
        public IReadOnlyDictionary<PlayerId, int> Points { get; }
        public IReadOnlyDictionary<PlayerId, int> GamesWon { get; }
        public int RoundsPlayed { get; }

        /// <summary>True when the NEXT round is a cut-throat battle (a lead tie).</summary>
        public bool PendingBattle { get; }

        /// <summary>The tied leaders who fight the pending battle (empty when none).</summary>
        public IReadOnlyList<PlayerId> BattlePlayers { get; }

        private SeriesState(
            IReadOnlyList<PlayerId> players,
            MatchFormat format,
            IReadOnlyDictionary<PlayerId, int> points,
            IReadOnlyDictionary<PlayerId, int> gamesWon,
            int roundsPlayed,
            bool pendingBattle,
            IReadOnlyList<PlayerId> battlePlayers)
        {
            Players = players;
            Format = format;
            Points = points;
            GamesWon = gamesWon;
            RoundsPlayed = roundsPlayed;
            PendingBattle = pendingBattle;
            BattlePlayers = battlePlayers;
        }

        /// <summary>A fresh series: everyone on love, no rounds played.</summary>
        public static SeriesState New(IReadOnlyList<PlayerId> players, MatchFormat format)
        {
            if (players == null || players.Count < 2)
            {
                throw new ArgumentException("A series needs at least two players.", nameof(players));
            }
            Dictionary<PlayerId, int> points = new(players.Count);
            Dictionary<PlayerId, int> games = new(players.Count);
            foreach (PlayerId p in players)
            {
                points[p] = 0;
                games[p] = 0;
            }
            return new SeriesState(players, format, points, games, 0, pendingBattle: false, Array.Empty<PlayerId>());
        }

        /// <summary>
        /// Folds a finished round's outcome into the series. The winner gains a game
        /// and 1000 points (2000 for a key). If the round just played was a battle
        /// and the winner is one of the battlers, the other battler(s) are reset to
        /// love. Then the battle for the NEXT round is recomputed.
        /// </summary>
        public SeriesState ApplyRound(MatchOutcome outcome)
        {
            if (outcome == null)
            {
                throw new ArgumentNullException(nameof(outcome));
            }

            Dictionary<PlayerId, int> points = Copy(Points);
            Dictionary<PlayerId, int> games = Copy(GamesWon);

            if (outcome.WinnerId is PlayerId winner && points.ContainsKey(winner))
            {
                // Battle resolution: a battler won → wipe the other battlers to love.
                if (PendingBattle && Contains(BattlePlayers, winner))
                {
                    foreach (PlayerId b in BattlePlayers)
                    {
                        if (!b.Equals(winner))
                        {
                            points[b] = 0;
                            games[b] = 0;
                        }
                    }
                }

                // Keys (+2000) land with the rules pass; flat 1000 per win for now.
                games[winner] += 1;
                points[winner] += MatchFormatRules.PointsPerRoundWin;
            }

            (bool nextBattle, IReadOnlyList<PlayerId> battlers) = ComputeBattle(games);
            return new SeriesState(
                Players, Format, points, games, RoundsPlayed + 1, nextBattle, battlers);
        }

        /// <summary>Points for a player (0 if unknown).</summary>
        public int PointsOf(PlayerId player) => Points.TryGetValue(player, out int p) ? p : 0;

        /// <summary>Games won by a player (0 if unknown).</summary>
        public int GamesWonBy(PlayerId player) => GamesWon.TryGetValue(player, out int g) ? g : 0;

        /// <summary>Whether a player has reached the format's target ("love") total.</summary>
        public bool IsOver => MaxPoints() >= MatchFormatRules.For(Format).TargetPoints;

        /// <summary>The match winner once <see cref="IsOver"/> (the highest scorer); null otherwise.</summary>
        public PlayerId? Winner => IsOver ? HighestScorer() : null;

        // The set of players tied for the top games-won total (>0). A tie of two or
        // more means the next round is a battle.
        private (bool, IReadOnlyList<PlayerId>) ComputeBattle(IReadOnlyDictionary<PlayerId, int> games)
        {
            int max = 0;
            foreach (int v in games.Values)
            {
                if (v > max)
                {
                    max = v;
                }
            }
            if (max <= 0)
            {
                return (false, Array.Empty<PlayerId>());
            }
            List<PlayerId> leaders = new();
            foreach (PlayerId p in Players)
            {
                if (GamesOf(games, p) == max)
                {
                    leaders.Add(p);
                }
            }
            return (leaders.Count >= 2, leaders.Count >= 2 ? leaders : Array.Empty<PlayerId>());
        }

        private int MaxPoints()
        {
            int max = 0;
            foreach (int v in Points.Values)
            {
                if (v > max)
                {
                    max = v;
                }
            }
            return max;
        }

        private PlayerId? HighestScorer()
        {
            int max = MaxPoints();
            foreach (PlayerId p in Players)
            {
                if (PointsOf(p) == max)
                {
                    return p;
                }
            }
            return null;
        }

        private static Dictionary<PlayerId, int> Copy(IReadOnlyDictionary<PlayerId, int> src)
        {
            Dictionary<PlayerId, int> dst = new(src.Count);
            foreach (KeyValuePair<PlayerId, int> kv in src)
            {
                dst[kv.Key] = kv.Value;
            }
            return dst;
        }

        private static int GamesOf(IReadOnlyDictionary<PlayerId, int> games, PlayerId p) =>
            games.TryGetValue(p, out int g) ? g : 0;

        private static bool Contains(IReadOnlyList<PlayerId> list, PlayerId p)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Equals(p))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
