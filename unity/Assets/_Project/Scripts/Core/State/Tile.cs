#nullable enable
using System;

namespace Pose.Core
{
    /// <summary>
    /// A single domino tile, identified by two pip values. Tiles are symmetric:
    /// the order of the two pips does not affect identity, so <c>[3,5]</c> equals
    /// <c>[5,3]</c>. Internally stored canonical form (smaller pip in <see cref="A"/>)
    /// to make equality and hashing trivial.
    /// </summary>
    public readonly struct Tile : IEquatable<Tile>
    {
        public byte A { get; }
        public byte B { get; }

        public Tile(byte a, byte b)
        {
            if (a <= b)
            {
                A = a;
                B = b;
            }
            else
            {
                A = b;
                B = a;
            }
        }

        /// <summary>Total pip value on this tile (A + B).</summary>
        public int Pips => A + B;

        /// <summary>True when both pips are equal (a "double").</summary>
        public bool IsDouble => A == B;

        /// <summary>
        /// Returns the pip value on the opposite end of the tile from
        /// <paramref name="pip"/>. For doubles, returns the same pip.
        /// Throws if the tile does not contain <paramref name="pip"/>.
        /// </summary>
        public byte GetOther(byte pip)
        {
            if (A == pip)
            {
                return B;
            }

            if (B == pip)
            {
                return A;
            }

            throw new ArgumentException(
                $"Tile {this} does not contain pip {pip}.",
                nameof(pip));
        }

        /// <summary>True if either pip on the tile equals <paramref name="pip"/>.</summary>
        public bool Matches(byte pip) => A == pip || B == pip;

        public bool Equals(Tile other) => A == other.A && B == other.B;
        public override bool Equals(object? obj) => obj is Tile t && Equals(t);
        public override int GetHashCode() => HashCode.Combine(A, B);
        public override string ToString() => $"[{A}|{B}]";

        public static bool operator ==(Tile l, Tile r) => l.Equals(r);
        public static bool operator !=(Tile l, Tile r) => !l.Equals(r);
    }
}
