#nullable enable
using System;
using System.Collections.Generic;

namespace Pose.Core
{
    /// <summary>
    /// A team of one or more players sharing a single score in partnership variants.
    /// In Jamaican Cut-Throat each team has exactly one member (the player plays for
    /// themselves); in Jamaican Partner each team has exactly two members. The engine
    /// itself doesn't care about team size — it just iterates <see cref="Members"/>.
    /// </summary>
    public sealed class Team
    {
        public TeamId Id { get; }
        public IReadOnlyList<PlayerId> Members { get; }

        public Team(TeamId id, IReadOnlyList<PlayerId> members)
        {
            if (id == default)
            {
                throw new ArgumentException(
                    "Team requires a non-default TeamId.",
                    nameof(id));
            }

            if (members == null)
            {
                throw new ArgumentNullException(nameof(members));
            }

            if (members.Count == 0)
            {
                throw new ArgumentException(
                    "Team must have at least one member.",
                    nameof(members));
            }

            HashSet<PlayerId> seen = new();
            for (int i = 0; i < members.Count; i++)
            {
                if (!seen.Add(members[i]))
                {
                    throw new ArgumentException(
                        $"Team {id} contains duplicate member {members[i]}.",
                        nameof(members));
                }
            }

            Id = id;
            Members = members;
        }

        public bool Contains(PlayerId player)
        {
            for (int i = 0; i < Members.Count; i++)
            {
                if (Members[i] == player)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
