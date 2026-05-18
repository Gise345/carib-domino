#nullable enable
using System;

namespace Pose.Game
{
    /// <summary>
    /// Generates short, human-friendly room codes for the lobby — 6 characters
    /// from a 32-symbol alphabet (uppercase letters + digits, with
    /// <c>O / 0 / I / 1</c> excluded to avoid visual ambiguity when read aloud
    /// or typed). 32⁶ ≈ 1.07 billion combinations — collision-free for any
    /// concurrent-room count we'll plausibly hit pre-launch.
    /// </summary>
    public static class RoomCodeGenerator
    {
        // 24 letters (A-Z minus O, I) + 8 digits (2-9) = 32 symbols.
        private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        private const int CodeLength = 6;

        // System.Random instead of UnityEngine.Random so this stays
        // pure C# (no UnityEngine dep), reusable from tests if needed.
        private static readonly Random Rng = new();
        private static readonly object RngLock = new();

        public static string Generate()
        {
            char[] chars = new char[CodeLength];
            lock (RngLock)
            {
                for (int i = 0; i < CodeLength; i++)
                {
                    chars[i] = Alphabet[Rng.Next(Alphabet.Length)];
                }
            }
            return new string(chars);
        }
    }
}
