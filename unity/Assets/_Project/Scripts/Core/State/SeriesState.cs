#nullable enable
using System;
using System.Collections.Generic;

namespace Pose.Core
{
    /// <summary>
    /// The running state of a match SERIES — the layer above a single round.
    /// Keyed on <see cref="TeamId"/> so it serves every mode uniformly: in
    /// Cut-Throat each player is their own solo team (see
    /// <see cref="Partnership.CutThroat"/>), so a team series is a per-player
    /// series; in Jamaican Partner the two real teams accumulate together. Tracks
    /// each team's games won and cumulative points (1000 per win, 2000 for a key),
    /// decides when the match is over (a team reaches the format target), and —
    /// when <see cref="BattlesEnabled"/> (Cut-Throat only) — models the cut-throat
    /// "battle": whenever two or more teams are tied for the lead on games won, the
    /// next round is a battle (double-six poses) and stays so until one of the tied
    /// teams wins, wiping the other tied team(s) back to LOVE (points and games
    /// reset to 0). Partner disables battles and simply races to the target.
    /// Immutable: <see cref="ApplyRound"/> returns a new instance.
    /// </summary>
    public sealed class SeriesState
    {
        public IReadOnlyList<TeamId> Teams { get; }
        public MatchFormat Format { get; }

        /// <summary>True when lead-tie "battles" apply (Cut-Throat); false for Partner.</summary>
        public bool BattlesEnabled { get; }

        public IReadOnlyDictionary<TeamId, int> Points { get; }
        public IReadOnlyDictionary<TeamId, int> GamesWon { get; }
        public int RoundsPlayed { get; }

        /// <summary>True when the NEXT round is a cut-throat battle (a lead tie).</summary>
        public bool PendingBattle { get; }

        /// <summary>The tied leaders who fight the pending battle (empty when none).</summary>
        public IReadOnlyList<TeamId> BattleTeams { get; }

        private SeriesState(
            IReadOnlyList<TeamId> teams,
            MatchFormat format,
            bool battlesEnabled,
            IReadOnlyDictionary<TeamId, int> points,
            IReadOnlyDictionary<TeamId, int> gamesWon,
            int roundsPlayed,
            bool pendingBattle,
            IReadOnlyList<TeamId> battleTeams)
        {
            Teams = teams;
            Format = format;
            BattlesEnabled = battlesEnabled;
            Points = points;
            GamesWon = gamesWon;
            RoundsPlayed = roundsPlayed;
            PendingBattle = pendingBattle;
            BattleTeams = battleTeams;
        }

        /// <summary>A fresh series: every team on love, no rounds played.</summary>
        public static SeriesState New(
            IReadOnlyList<TeamId> teams, MatchFormat format, bool battlesEnabled)
        {
            if (teams == null || teams.Count < 2)
            {
                throw new ArgumentException("A series needs at least two teams.", nameof(teams));
            }
            Dictionary<TeamId, int> points = new(teams.Count);
            Dictionary<TeamId, int> games = new(teams.Count);
            foreach (TeamId t in teams)
            {
                points[t] = 0;
                games[t] = 0;
            }
            return new SeriesState(
                teams, format, battlesEnabled, points, games, 0,
                pendingBattle: false, Array.Empty<TeamId>());
        }

        /// <summary>
        /// Folds a finished round's outcome into the series. The winning team gains
        /// a game and 1000 points (2000 for a key). If the round just played was a
        /// battle and the winner is one of the battlers, the other battler(s) are
        /// reset to love. Then the battle for the NEXT round is recomputed (only
        /// when <see cref="BattlesEnabled"/>).
        /// </summary>
        public SeriesState ApplyRound(MatchOutcome outcome)
        {
            if (outcome == null)
            {
                throw new ArgumentNullException(nameof(outcome));
            }

            Dictionary<TeamId, int> points = Copy(Points);
            Dictionary<TeamId, int> games = Copy(GamesWon);

            if (outcome.WinningTeamId is TeamId winner && points.ContainsKey(winner))
            {
                // Battle resolution: a battler won → wipe the other battlers to love.
                if (BattlesEnabled && PendingBattle && Contains(BattleTeams, winner))
                {
                    foreach (TeamId b in BattleTeams)
                    {
                        if (!b.Equals(winner))
                        {
                            points[b] = 0;
                            games[b] = 0;
                        }
                    }
                }

                // A key scores the bonus (2000) instead of the flat win (1000).
                games[winner] += 1;
                points[winner] += outcome.IsKey ? MatchFormatRules.KeyPoints : MatchFormatRules.PointsPerRoundWin;
            }

            (bool nextBattle, IReadOnlyList<TeamId> battlers) = ComputeBattle(games);
            return new SeriesState(
                Teams, Format, BattlesEnabled, points, games, RoundsPlayed + 1, nextBattle, battlers);
        }

        /// <summary>Points for a team (0 if unknown).</summary>
        public int PointsOf(TeamId team) => Points.TryGetValue(team, out int p) ? p : 0;

        /// <summary>Games won by a team (0 if unknown).</summary>
        public int GamesWonBy(TeamId team) => GamesWon.TryGetValue(team, out int g) ? g : 0;

        /// <summary>Whether a team has reached the format's target ("love") total.</summary>
        public bool IsOver => MaxPoints() >= MatchFormatRules.For(Format).TargetPoints;

        /// <summary>The winning team once <see cref="IsOver"/> (the highest scorer); null otherwise.</summary>
        public TeamId? WinnerTeam => IsOver ? HighestScorer() : null;

        // The set of teams tied for the top games-won total (>0). A tie of two or
        // more means the next round is a battle. Only when battles are enabled.
        private (bool, IReadOnlyList<TeamId>) ComputeBattle(IReadOnlyDictionary<TeamId, int> games)
        {
            if (!BattlesEnabled)
            {
                return (false, Array.Empty<TeamId>());
            }
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
                return (false, Array.Empty<TeamId>());
            }
            List<TeamId> leaders = new();
            foreach (TeamId t in Teams)
            {
                if (GamesOf(games, t) == max)
                {
                    leaders.Add(t);
                }
            }
            return (leaders.Count >= 2, leaders.Count >= 2 ? leaders : Array.Empty<TeamId>());
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

        private TeamId? HighestScorer()
        {
            int max = MaxPoints();
            foreach (TeamId t in Teams)
            {
                if (PointsOf(t) == max)
                {
                    return t;
                }
            }
            return null;
        }

        private static Dictionary<TeamId, int> Copy(IReadOnlyDictionary<TeamId, int> src)
        {
            Dictionary<TeamId, int> dst = new(src.Count);
            foreach (KeyValuePair<TeamId, int> kv in src)
            {
                dst[kv.Key] = kv.Value;
            }
            return dst;
        }

        private static int GamesOf(IReadOnlyDictionary<TeamId, int> games, TeamId t) =>
            games.TryGetValue(t, out int g) ? g : 0;

        private static bool Contains(IReadOnlyList<TeamId> list, TeamId t)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Equals(t))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
