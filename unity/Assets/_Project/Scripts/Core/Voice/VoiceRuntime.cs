#nullable enable

namespace Pose.Core.Voice
{
    /// <summary>
    /// Where the game finds voice, if voice is there at all.
    ///
    /// A one-service locator rather than a singleton: nothing here constructs an
    /// <see cref="IVoiceSession"/> or knows how one is built. The
    /// <c>Pose.Net.Voice</c> assembly registers its implementation at startup, and
    /// everything else reads <see cref="Session"/>.
    ///
    /// The indirection is the point. <c>Pose.Net.Voice</c> is the only assembly
    /// that names a Vivox type and is compiled separately with
    /// <c>autoReferenced: false</c>, so when the Vivox package is missing or its
    /// API moves, that assembly alone fails to build. It then never registers,
    /// <see cref="Session"/> stays null, and the game runs — silently without
    /// voice — instead of the whole project refusing to compile. That is exactly
    /// what happened the first time the package failed to resolve, and it should
    /// not be able to happen twice.
    ///
    /// So <see cref="Session"/> being null is a NORMAL state, not an error: it
    /// covers voice being unbuilt, unprovisioned, or simply not started yet.
    /// Callers null-check; they never assume.
    /// </summary>
    public static class VoiceRuntime
    {
        /// <summary>
        /// The live voice session, or null when voice is unavailable for any
        /// reason. Always null-check before use.
        /// </summary>
        public static IVoiceSession? Session { get; private set; }

        /// <summary>True when a voice implementation is registered.</summary>
        public static bool IsAvailable => Session != null;

        /// <summary>
        /// Registers the voice implementation. Called by <c>Pose.Net.Voice</c> as
        /// it starts up; nothing else should call it.
        /// </summary>
        /// <param name="session">The implementation to publish.</param>
        public static void Register(IVoiceSession session) => Session = session;

        /// <summary>
        /// Withdraws the current implementation, so a torn-down session cannot be
        /// used by mistake. Idempotent.
        /// </summary>
        public static void Clear() => Session = null;
    }
}
