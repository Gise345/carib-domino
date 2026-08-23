#nullable enable
using System.Collections.Generic;

namespace Pose.Core.Chat
{
    /// <summary>
    /// The one-tap phrases above the composer.
    ///
    /// Typing mid-hand is the problem they solve: a player with a tile half
    /// dragged is not going to open a keyboard, so the table goes quiet exactly
    /// when it should be loudest. They are ordinary messages on the wire — the
    /// server filters and rate-limits them like anything else — so they add no
    /// new trust surface.
    ///
    /// The wording is deliberately plain for now. Because this is a list of
    /// localization KEYS rather than strings, giving each locale its own idiom
    /// later — Caribbean English, Cuban Spanish, Haitian French — is a change to
    /// the string tables alone, with nothing to rebuild.
    /// </summary>
    public static class ChatQuickPhrases
    {
        /// <summary>
        /// Localization keys, in the order they appear in the strip: roughly the
        /// order a hand uses them, opening to sign-off.
        /// </summary>
        public static readonly IReadOnlyList<string> Keys = new[]
        {
            "quick_phrase_luck",       // "Good luck!"
            "quick_phrase_nice_play",  // "Nice play!"
            "quick_phrase_your_turn",  // "Your turn"
            "quick_phrase_close_one",  // "Close one!"
            "quick_phrase_good_game",  // "Good game!"
            "quick_phrase_rematch",    // "One more?"
        };
    }
}
