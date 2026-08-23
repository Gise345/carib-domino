#nullable enable
using System;
using Pose.Core.Chat;

namespace Pose.Core.Voice
{
    /// <summary>
    /// What the local player may do with the microphone right now. Pure and
    /// immutable: the HUD asks this rather than scattering guest/mute/permission
    /// checks through the view.
    ///
    /// It is built <em>from</em> <see cref="ChatEntitlement"/> rather than
    /// alongside it, so the gates chat and voice share — signed out, guest, muted,
    /// no room — are evaluated exactly once and can never drift apart. That is the
    /// promise ADR 0023 §3 made when it decided the voice rule up front.
    ///
    /// Presentation only. The server re-checks every gate when it mints a Vivox
    /// token (ADR 0024 §4), so an unlocked mic on a tampered client still gets no
    /// token.
    /// </summary>
    public readonly struct VoiceEntitlement
    {
        /// <summary>Whether the player may transmit.</summary>
        public bool CanSpeak { get; }

        /// <summary>
        /// Whether the player may hear the table — i.e. whether to connect to the
        /// voice channel at all.
        ///
        /// True only when the block is something other than an account problem:
        /// a moderator mute or a refused microphone still lets you listen. A
        /// <b>guest does not listen</b>, which is where voice deliberately parts
        /// company with chat: a guest may read chat because text you can see is
        /// text you can report, whereas a voice you have no participant handle
        /// for cannot be reported at all. Never connecting a guest is also the
        /// cheaper answer — a connected listener burns a Vivox concurrent user.
        /// </summary>
        public bool CanListen { get; }

        /// <summary>Why speaking is unavailable, or <see cref="VoiceLockReason.None"/>.</summary>
        public VoiceLockReason LockReason { get; }

        private VoiceEntitlement(bool canSpeak, bool canListen, VoiceLockReason lockReason)
        {
            CanSpeak = canSpeak;
            CanListen = canListen;
            LockReason = lockReason;
        }

        /// <summary>
        /// Works out what this session may do with the microphone.
        /// </summary>
        /// <param name="chat">
        /// The already-computed chat entitlement, which carries the shared gates.
        /// </param>
        /// <param name="roomAllowsVoice">
        /// <see cref="VoiceRoomPolicy.IsAllowed"/> for this table.
        /// </param>
        /// <param name="micPermission">What the OS has said about the microphone.</param>
        /// <returns>The entitlement the mic control should render.</returns>
        public static VoiceEntitlement For(
            ChatEntitlement chat,
            bool roomAllowsVoice,
            MicPermissionState micPermission)
        {
            // Scope is checked before the account gates so a guest at a
            // voice-less table is told the table has no voice, rather than being
            // sold an account that would not help them here.
            if (!roomAllowsVoice)
            {
                return new VoiceEntitlement(false, false, VoiceLockReason.NotAllowedInThisRoom);
            }

            if (!chat.CanUseVoice)
            {
                VoiceLockReason reason = Map(chat.LockReason);

                // A moderator mute takes your voice, not your ears. Every other
                // account-level block means no channel connection at all.
                return new VoiceEntitlement(false, reason == VoiceLockReason.Muted, reason);
            }

            // Only an outright refusal locks. Unknown means we have not asked yet,
            // and the prompt belongs at first use, not at boot.
            if (micPermission == MicPermissionState.Denied)
            {
                return new VoiceEntitlement(false, true, VoiceLockReason.MicPermissionDenied);
            }

            return new VoiceEntitlement(true, true, VoiceLockReason.None);
        }

        /// <summary>
        /// Translates a shared chat gate into its voice equivalent.
        /// </summary>
        /// <param name="reason">The chat lock reason.</param>
        /// <returns>The matching voice lock reason.</returns>
        /// <exception cref="ArgumentOutOfRangeException">If chat grows a reason voice has not mapped.</exception>
        private static VoiceLockReason Map(ChatLockReason reason) => reason switch
        {
            ChatLockReason.None => VoiceLockReason.None,
            ChatLockReason.SignedOut => VoiceLockReason.SignedOut,
            ChatLockReason.Guest => VoiceLockReason.Guest,
            ChatLockReason.Muted => VoiceLockReason.Muted,
            ChatLockReason.NoRoom => VoiceLockReason.NoRoom,
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unmapped chat lock reason."),
        };
    }
}
