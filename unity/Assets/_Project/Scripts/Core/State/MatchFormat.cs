#nullable enable
using System;

namespace Pose.Core
{
    /// <summary>
    /// A Cut-Throat match series format. Both award a flat
    /// <see cref="MatchFormatRules.PointsPerRoundWin"/> per round won and end when
    /// a player reaches the format's target ("love") total:
    /// <see cref="ClassicSixLove"/> = 6000 (six love), <see cref="QuickLove"/> =
    /// 3000. Mirrors the wire strings <c>"classic"</c> / <c>"quick"</c>.
    /// </summary>
    public enum MatchFormat
    {
        ClassicSixLove,
        QuickLove,
    }

    /// <summary>
    /// The concrete numbers behind a <see cref="MatchFormat"/>, kept as data so
    /// the series logic (<see cref="SeriesState"/>) and the UI read one source.
    /// </summary>
    public sealed class MatchFormatRules
    {
        /// <summary>Points a round's winner scores (one "love"/mark).</summary>
        public const int PointsPerRoundWin = 1000;

        /// <summary>Bonus a "key" (both-ends lock-out win) scores instead of the flat win.</summary>
        public const int KeyPoints = 2000;

        /// <summary>Classic target: six loves.</summary>
        public const int ClassicTargetPoints = 6000;

        /// <summary>Quick target: three loves.</summary>
        public const int QuickTargetPoints = 3000;

        /// <summary>The format this rule-set describes.</summary>
        public MatchFormat Format { get; }

        /// <summary>Points that end the match when a player reaches (or passes) them.</summary>
        public int TargetPoints { get; }

        private MatchFormatRules(MatchFormat format, int targetPoints)
        {
            Format = format;
            TargetPoints = targetPoints;
        }

        /// <summary>The rules for the given format.</summary>
        public static MatchFormatRules For(MatchFormat format) => format switch
        {
            MatchFormat.ClassicSixLove => new MatchFormatRules(format, ClassicTargetPoints),
            MatchFormat.QuickLove => new MatchFormatRules(format, QuickTargetPoints),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown match format."),
        };

        /// <summary>The "loves" a target represents (target ÷ 1000).</summary>
        public int Loves => TargetPoints / PointsPerRoundWin;

        /// <summary>The wire string for this format (<c>"classic"</c> / <c>"quick"</c>).</summary>
        public static string ToWire(MatchFormat format) => format switch
        {
            MatchFormat.ClassicSixLove => "classic",
            MatchFormat.QuickLove => "quick",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown match format."),
        };

        /// <summary>Parses a wire string; defaults to <see cref="MatchFormat.ClassicSixLove"/>.</summary>
        public static MatchFormat FromWire(string? wire) =>
            wire == "quick" ? MatchFormat.QuickLove : MatchFormat.ClassicSixLove;
    }
}
