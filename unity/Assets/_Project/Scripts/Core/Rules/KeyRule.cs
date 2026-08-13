#nullable enable

namespace Pose.Core
{
    /// <summary>
    /// Detects a "key" — a both-ends lock-out win. A player who empties their hand
    /// scores a key when the winning tile was playable on BOTH open ends (a
    /// capicúa) AND no opponent still holds either of those two end numbers (so the
    /// board was truly locked). Worth <see cref="MatchFormatRules.KeyPoints"/>
    /// instead of the flat win. Shared by the Cut-Throat and Partner engines and
    /// mirrored in <c>functions/src/rules/keyRule.ts</c>.
    /// </summary>
    public static class KeyRule
    {
        /// <summary>
        /// True if <paramref name="winner"/>'s hand-emptying win is a key. The
        /// caller has already established a domino end (winner has zero tiles).
        /// </summary>
        public static bool IsKey(MatchState state, PlayerId winner)
        {
            if (state.History.Count == 0
                || state.History[state.History.Count - 1] is not PlaceMove last)
            {
                return false;
            }

            // The end NOT played on is unchanged by the winning move; if the
            // winning tile also matches it, the tile was playable on both ends.
            byte otherEnd = last.End == ChainEnd.Left ? state.Chain.RightEnd : state.Chain.LeftEnd;
            if (!last.Tile.Matches(otherEnd))
            {
                return false;
            }

            // Lock-out: no opponent holds either of the winning tile's pips
            // (i.e. no one could have played on either of the two locked ends).
            byte a = last.Tile.A;
            byte b = last.Tile.B;
            for (int i = 0; i < state.Players.Count; i++)
            {
                PlayerId p = state.Players[i];
                if (p.Equals(winner))
                {
                    continue;
                }
                foreach (Tile t in state.Hands[p])
                {
                    if (t.Matches(a) || t.Matches(b))
                    {
                        return false;
                    }
                }
            }
            return true;
        }
    }
}
