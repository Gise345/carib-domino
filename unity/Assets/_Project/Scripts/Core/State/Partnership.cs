#nullable enable
using System;
using System.Collections.Generic;

namespace Pose.Core
{
    /// <summary>
    /// The team configuration for a single round. Every player belongs to exactly one
    /// team; a team holds one or more players and is the unit that wins or loses a
    /// round in partnership variants. Cut-Throat is unified into the same model by
    /// assigning each player to their own solo team — see
    /// <see cref="CutThroat(IReadOnlyList{PlayerId})"/>. Partnerships are immutable
    /// for the lifetime of a round; the rule engine reads but never mutates them.
    /// </summary>
    public sealed class Partnership
    {
        public IReadOnlyList<Team> Teams { get; }

        // Built once in the constructor and cached for O(1) lookups by GetTeamOf.
        private readonly Dictionary<PlayerId, TeamId> _teamByPlayer;

        public Partnership(IReadOnlyList<Team> teams)
        {
            if (teams == null)
            {
                throw new ArgumentNullException(nameof(teams));
            }

            if (teams.Count == 0)
            {
                throw new ArgumentException(
                    "Partnership must contain at least one team.",
                    nameof(teams));
            }

            HashSet<TeamId> seenTeamIds = new();
            Dictionary<PlayerId, TeamId> teamByPlayer = new();

            for (int i = 0; i < teams.Count; i++)
            {
                Team t = teams[i] ?? throw new ArgumentException(
                    $"Team at index {i} is null.",
                    nameof(teams));

                if (!seenTeamIds.Add(t.Id))
                {
                    throw new ArgumentException(
                        $"Duplicate TeamId '{t.Id}' in partnership.",
                        nameof(teams));
                }

                for (int j = 0; j < t.Members.Count; j++)
                {
                    PlayerId p = t.Members[j];
                    if (teamByPlayer.ContainsKey(p))
                    {
                        throw new ArgumentException(
                            $"Player {p} appears in more than one team.",
                            nameof(teams));
                    }
                    teamByPlayer[p] = t.Id;
                }
            }

            Teams = teams;
            _teamByPlayer = teamByPlayer;
        }

        /// <summary>
        /// Returns the <see cref="TeamId"/> the given player belongs to. Throws when
        /// the player is not a member of any team in this partnership.
        /// </summary>
        public TeamId GetTeamOf(PlayerId player)
        {
            if (_teamByPlayer.TryGetValue(player, out TeamId teamId))
            {
                return teamId;
            }
            throw new InvalidOperationException(
                $"Player {player} is not a member of any team in this partnership.");
        }

        /// <summary>
        /// Cut-Throat partnership: each player gets their own solo team. The engine
        /// treats Cut-Throat and Partner identically by walking
        /// <see cref="MatchOutcome.WinningTeamId"/>; for Cut-Throat the winning team
        /// just happens to have one member. Team IDs are deterministically named
        /// <c>"team:{playerValue}"</c> so logs are debuggable.
        /// </summary>
        public static Partnership CutThroat(IReadOnlyList<PlayerId> players)
        {
            if (players == null)
            {
                throw new ArgumentNullException(nameof(players));
            }

            if (players.Count == 0)
            {
                throw new ArgumentException(
                    "Cut-Throat partnership requires at least one player.",
                    nameof(players));
            }

            HashSet<PlayerId> seen = new();
            List<Team> teams = new(players.Count);
            for (int i = 0; i < players.Count; i++)
            {
                PlayerId p = players[i];
                if (!seen.Add(p))
                {
                    throw new ArgumentException(
                        $"Duplicate player {p} in Cut-Throat partnership.",
                        nameof(players));
                }
                teams.Add(new Team(
                    new TeamId($"team:{p.Value}"),
                    new[] { p }));
            }
            return new Partnership(teams);
        }

        /// <summary>
        /// Jamaican Partner alternating-pairs partnership: positions 0+2 form team_a,
        /// positions 1+3 form team_b. Mirrors how players are seated at a table for
        /// partner play (partners across the table from each other). All four player
        /// IDs must be distinct.
        /// </summary>
        public static Partnership AlternatingPairs(
            PlayerId p1,
            PlayerId p2,
            PlayerId p3,
            PlayerId p4)
        {
            // Distinctness check across the four positional arguments. Using a set
            // guards against any pair colliding rather than enumerating each pair
            // explicitly (six pair checks is a maintenance trap as we add positions).
            HashSet<PlayerId> seen = new() { p1 };
            if (!seen.Add(p2) || !seen.Add(p3) || !seen.Add(p4))
            {
                throw new ArgumentException(
                    "AlternatingPairs requires four distinct players.");
            }

            Team teamA = new(new TeamId("team_a"), new[] { p1, p3 });
            Team teamB = new(new TeamId("team_b"), new[] { p2, p4 });
            return new Partnership(new[] { teamA, teamB });
        }
    }
}
