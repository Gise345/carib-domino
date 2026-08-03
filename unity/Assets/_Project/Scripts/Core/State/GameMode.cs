#nullable enable

namespace Pose.Core
{
    /// <summary>
    /// Which ruleset a round is played under. <see cref="CutThroat"/> is
    /// every-player-for-themselves (2–4 players, solo teams); <see cref="Partner"/>
    /// is Jamaican Partner (exactly 4 players, 2 teams of 2). Selects both the
    /// rule engine and the partnership. Mirrors the server's <c>GameMode</c>
    /// (see <c>functions/src/rules/gameMode.ts</c>); the wire string is
    /// <c>"cutthroat"</c> / <c>"partner"</c>.
    /// </summary>
    public enum GameMode
    {
        CutThroat,
        Partner,
    }
}
