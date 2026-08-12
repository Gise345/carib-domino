#nullable enable
using System;
using System.Collections.Generic;

namespace Pose.Core
{
    /// <summary>
    /// The running state of a Cut-Throat match SERIES — the layer above a single
    /// round. Tracks each player's cumulative points (a flat
    /// <see cref="MatchFormatRules.PointsPerRoundWin"/> per round won) and how many
    /// rounds have been played, and decides when the match is over and who won.
    /// Immutable: <see cref="ApplyRound"/> returns a new instance.
    ///
    /// Cut-Throat only for now (points are per player). Partner series (points per
    /// team) is a later extension.
    /// </summary>
    public sealed class SeriesState
    {
        public IReadOnlyList<PlayerId> Players { get; }
        public MatchFormat Format { get; }
        public IReadOnlyDictionary<PlayerId, int> Points { get; }
        public int RoundsPlayed { get; }

        private SeriesState(
            IReadOnlyList<PlayerId> players,
            MatchFormat format,
            IReadOnlyDictionary<PlayerId, int> points,
            int roundsPlayed)
        {
            Players = players;
            Format = format;
            Points = points;
            RoundsPlayed = roundsPlayed;
        }

        /// <summary>A fresh series: everyone on zero, no rounds played.</summary>
        public static SeriesState New(IReadOnlyList<PlayerId> players, MatchFormat format)
        {
            if (players == null || players.Count < 2)
            {
                throw new ArgumentException("A series needs at least two players.", nameof(players));
            }
            Dictionary<PlayerId, int> points = new(players.Count);
            foreach (PlayerId p in players)
            {
                points[p] = 0;
            }
            return new SeriesState(players, format, points, roundsPlayed: 0);
        }

        /// <summary>
        /// Folds a finished round's outcome into the series: the round winner gains
        /// <see cref="MatchFormatRules.PointsPerRoundWin"/> (a draw awards nobody),
        /// and the round count advances.
        /// </summary>
        public SeriesState ApplyRound(MatchOutcome outcome)
        {
            if (outcome == null)
            {
                throw new ArgumentNullException(nameof(outcome));
            }

            Dictionary<PlayerId, int> next = new(Points.Count);
            foreach (KeyValuePair<PlayerId, int> kv in Points)
            {
                next[kv.Key] = kv.Value;
            }

            if (outcome.WinnerId != null && next.ContainsKey(outcome.WinnerId.Value))
            {
                next[outcome.WinnerId.Value] += MatchFormatRules.PointsPerRoundWin;
            }

            return new SeriesState(Players, Format, next, RoundsPlayed + 1);
        }

        /// <summary>Points for a player (0 if unknown).</summary>
        public int PointsOf(PlayerId player) => Points.TryGetValue(player, out int p) ? p : 0;

        /// <summary>
        /// Whether the match has been decided. Classic: someone reached the target.
        /// Quick: the round limit is reached AND there is a sole leader (a tie keeps
        /// the match alive for sudden-death rounds).
        /// </summary>
        public bool IsOver
        {
            get
            {
                MatchFormatRules rules = MatchFormatRules.For(Format);
                if (rules.TargetPoints is int target)
                {
                    return MaxPoints() >= target;
                }
                if (rules.RoundLimit is int limit)
                {
                    return RoundsPlayed >= limit && HasSoleLeader();
                }
                return false;
            }
        }

        /// <summary>
        /// The match winner once <see cref="IsOver"/>; null while the match is still
        /// running (including a tie awaiting sudden death).
        /// </summary>
        public PlayerId? Winner
        {
            get
            {
                if (!IsOver)
                {
                    return null;
                }
                return LeaderOrNull();
            }
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

        private bool HasSoleLeader() => LeaderOrNull() != null;

        // The unique highest-scoring player, or null if two or more share the top.
        private PlayerId? LeaderOrNull()
        {
            int max = MaxPoints();
            PlayerId? leader = null;
            int leaders = 0;
            foreach (PlayerId p in Players)
            {
                if (PointsOf(p) == max)
                {
                    leaders++;
                    leader = p;
                }
            }
            return leaders == 1 ? leader : null;
        }
    }
}
