#nullable enable
using System;

namespace Pose.Core
{
    /// <summary>
    /// Strongly-typed identifier for a partnership team. Mirrors <see cref="PlayerId"/>:
    /// wraps a non-empty string so we cannot accidentally pass a player ID, score, or
    /// arbitrary string where a team ID is expected. Use <see cref="Value"/> to read
    /// the underlying string. For Cut-Throat, each player is on a solo team whose
    /// <see cref="TeamId"/> is conventionally <c>"team:{playerId}"</c>; for partner
    /// variants, teams use synthetic labels (<c>"team_a"</c>, <c>"team_b"</c>).
    /// </summary>
    public readonly struct TeamId : IEquatable<TeamId>
    {
        private readonly string? _value;

        public string Value => _value ?? string.Empty;

        public TeamId(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException(
                    "TeamId cannot be null or empty.",
                    nameof(value));
            }

            _value = value;
        }

        public bool Equals(TeamId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is TeamId t && Equals(t);

        public override int GetHashCode() => Value.GetHashCode();

        public override string ToString() => Value;

        public static bool operator ==(TeamId l, TeamId r) => l.Equals(r);
        public static bool operator !=(TeamId l, TeamId r) => !l.Equals(r);
    }
}
