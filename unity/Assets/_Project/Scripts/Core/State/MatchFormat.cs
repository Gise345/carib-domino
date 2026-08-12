#nullable enable
using System;

namespace Pose.Core
{
    /// <summary>
    /// How a Cut-Throat match series ends. Both award a flat
    /// <see cref="MatchFormatRules.PointsPerRoundWin"/> to each round's winner.
    /// <see cref="ClassicSixLove"/> plays until someone reaches six "loves"
    /// (6000 points); <see cref="QuickSixRounds"/> plays a fixed six rounds and
    /// the highest total wins (sudden-death rounds break a tie). Mirrors the
    /// wire strings <c>"classic"</c> / <c>"quick"</c>.
    /// </summary>
    public enum MatchFormat
    {
        ClassicSixLove,
        QuickSixRounds,
    }

    /// <summary>
    /// The concrete numbers behind a <see cref="MatchFormat"/>. Kept as data so
    /// the series logic (<see cref="SeriesState"/>) and the UI read one source.
    /// </summary>
    public sealed class MatchFormatRules
    {
        /// <summary>Points awarded to a round's winner (one "love"/mark).</summary>
        public const int PointsPerRoundWin = 1000;

        /// <summary>Classic target: six loves.</summary>
        public const int ClassicTargetPoints = 6000;

        /// <summary>Quick fixed length.</summary>
        public const int QuickRoundLimit = 6;

        /// <summary>The format this rule-set describes.</summary>
        public MatchFormat Format { get; }

        /// <summary>Points that end the match when a player reaches them, or null if the format is round-limited.</summary>
        public int? TargetPoints { get; }

        /// <summary>Fixed number of rounds after which the leader wins, or null if the format is target-based.</summary>
        public int? RoundLimit { get; }

        private MatchFormatRules(MatchFormat format, int? targetPoints, int? roundLimit)
        {
            Format = format;
            TargetPoints = targetPoints;
            RoundLimit = roundLimit;
        }

        /// <summary>The rules for the given format.</summary>
        public static MatchFormatRules For(MatchFormat format) => format switch
        {
            MatchFormat.ClassicSixLove => new MatchFormatRules(format, ClassicTargetPoints, roundLimit: null),
            MatchFormat.QuickSixRounds => new MatchFormatRules(format, targetPoints: null, QuickRoundLimit),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown match format."),
        };

        /// <summary>The wire string for this format (<c>"classic"</c> / <c>"quick"</c>).</summary>
        public static string ToWire(MatchFormat format) => format switch
        {
            MatchFormat.ClassicSixLove => "classic",
            MatchFormat.QuickSixRounds => "quick",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown match format."),
        };

        /// <summary>Parses a wire string; defaults to <see cref="MatchFormat.ClassicSixLove"/>.</summary>
        public static MatchFormat FromWire(string? wire) =>
            wire == "quick" ? MatchFormat.QuickSixRounds : MatchFormat.ClassicSixLove;
    }
}
