#nullable enable
using System;
using System.Collections.Generic;

namespace Pose.Core
{
    /// <summary>
    /// Trivial bot that picks uniformly at random from the legal moves it's
    /// given. Pure C# (lives in <c>Pose.Core</c>), engine-deterministic given
    /// the same <see cref="IRandomSource"/> sequence — the same property the
    /// dealer relies on for replay validation. Used both client-side as the
    /// M1 offline opponent and server-side by the eventual settlement validator
    /// when reproducing bot moves from a match log.
    /// </summary>
    public sealed class RandomBot
    {
        /// <summary>
        /// Picks one of the supplied legal moves uniformly at random.
        /// <paramref name="state"/> is unused by this strategy but kept in the
        /// signature so smarter future bots (BlockingBot, CounterBot, etc.) can
        /// plug into the same call site without churn at the caller.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="legalMoves"/> is empty — callers should
        /// only invoke the bot when at least one legal move exists (the rule
        /// engine guarantees a non-empty list whenever the match is not over).
        /// </exception>
        public Move PickMove(MatchState state, IReadOnlyList<Move> legalMoves, IRandomSource random)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (legalMoves == null)
            {
                throw new ArgumentNullException(nameof(legalMoves));
            }

            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            if (legalMoves.Count == 0)
            {
                throw new ArgumentException(
                    "Cannot pick a move from an empty legal-moves list.",
                    nameof(legalMoves));
            }

            int index = random.NextInt(legalMoves.Count);
            return legalMoves[index];
        }
    }
}
