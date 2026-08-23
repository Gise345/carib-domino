#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Pose.Core.Voice
{
    /// <summary>
    /// The game's whole view of in-match voice.
    ///
    /// This interface exists to keep the Vivox SDK out of <c>Assembly-CSharp</c>.
    /// The implementation lives in the separately-compiled <c>Pose.Net.Voice</c>
    /// assembly, which is the only code that names a Vivox type; the board, the
    /// HUD and the bootstrap only ever see this. If that assembly fails to build —
    /// a package that did not resolve, an SDK upgrade that moved an API — voice
    /// simply never registers itself and the game runs without it, instead of the
    /// entire project failing to compile.
    ///
    /// Reach it through <see cref="VoiceRuntime.Session"/>, which is null whenever
    /// voice is unavailable. Callers must treat that as normal, not exceptional.
    /// </summary>
    public interface IVoiceSession
    {
        /// <summary>True once connected to a channel and exchanging audio.</summary>
        bool IsConnected { get; }

        /// <summary>True when this player is entitled to transmit.</summary>
        bool CanSpeak { get; }

        /// <summary>What the server last said about this player's voice.</summary>
        VoiceJoinOutcome LastOutcome { get; }

        /// <summary>
        /// What the OS says about the microphone. Exposed here because the
        /// permission API lives in this assembly — the HUD cannot reach it
        /// directly and should not need to know that.
        /// </summary>
        MicPermissionState MicPermission { get; }

        /// <summary>
        /// Raised whenever anything the mic control renders has changed —
        /// connection, entitlement, mute or permission. The HUD redraws on this
        /// rather than polling every frame.
        /// </summary>
        event Action? StateChanged;

        /// <summary>
        /// Raised when a seat starts or stops speaking, so the HUD can light a
        /// ring. The int is the seat index, the bool whether they are speaking.
        /// </summary>
        event Action<int, bool>? SeatSpeakingChanged;

        /// <summary>
        /// Supplies the uid-to-seat mapping used to attribute speech to a seat.
        ///
        /// Needed because Vivox identifies a participant by their uid and knows
        /// nothing about the table. Until this is set, speech is still detected
        /// but cannot be attributed, so no seat lights up.
        /// </summary>
        /// <param name="uidToSeat">Room roster, uid to seat index.</param>
        void SetSeatMap(IReadOnlyDictionary<string, int> uidToSeat);

        /// <summary>
        /// Joins the voice channel for a match, if the player is entitled to it.
        ///
        /// Safe to call when voice is switched off or the player is refused — the
        /// outcome says what happened and no exception is thrown.
        /// </summary>
        /// <param name="roomId">The Photon session name; one channel per series.</param>
        /// <param name="displayName">Name shown beside this player.</param>
        /// <param name="seat">Table seat index, or -1 when not yet seated.</param>
        /// <param name="matchId">Server-issued match id, for moderation context.</param>
        /// <param name="mode">Ruleset being played.</param>
        /// <param name="origin">How this table was reached.</param>
        /// <returns>What the server decided.</returns>
        Task<VoiceJoinOutcome> BeginAsync(
            string roomId,
            string displayName,
            int seat,
            string? matchId,
            string? mode,
            VoiceRoomOrigin origin);

        /// <summary>Leaves the channel and releases the microphone.</summary>
        /// <returns>A task that completes once the channel is left.</returns>
        Task EndAsync();

        /// <summary>
        /// Stops or resumes this player's own transmission. Deliberately does not
        /// persist: a forgotten self-mute reads as a broken feature three matches
        /// later. The Settings toggle is the one that survives a restart.
        /// </summary>
        /// <param name="muted">True to stop transmitting.</param>
        void SetSelfMuted(bool muted);

        /// <summary>True while this player has muted their own microphone.</summary>
        bool IsSelfMuted { get; }

        /// <summary>
        /// Silences another seat for this listener only. Everyone else at the
        /// table still hears them. Local and unpersisted for v1 (ADR 0024 §6).
        /// </summary>
        /// <param name="seat">The seat to silence.</param>
        /// <param name="muted">True to stop hearing them.</param>
        void SetSeatMuted(int seat, bool muted);

        /// <summary>Whether this listener has muted a seat.</summary>
        /// <param name="seat">The seat to check.</param>
        /// <returns>True when muted for this listener.</returns>
        bool IsSeatMuted(int seat);
    }
}
