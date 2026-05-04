#nullable enable
using System;

namespace Pose.Core
{
    /// <summary>
    /// Strongly-typed identifier for a player. Wraps a non-empty string so we cannot
    /// accidentally pass a tile pip, score, or arbitrary string where a player ID
    /// is expected. Use <see cref="Value"/> to read the underlying string.
    /// </summary>
    public readonly struct PlayerId : IEquatable<PlayerId>
    {
        private readonly string? _value;

        public string Value => _value ?? string.Empty;

        public PlayerId(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException(
                    "PlayerId cannot be null or empty.",
                    nameof(value));
            }

            _value = value;
        }

        public bool Equals(PlayerId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is PlayerId p && Equals(p);

        public override int GetHashCode() => Value.GetHashCode();

        public override string ToString() => Value;

        public static bool operator ==(PlayerId l, PlayerId r) => l.Equals(r);
        public static bool operator !=(PlayerId l, PlayerId r) => !l.Equals(r);
    }
}
