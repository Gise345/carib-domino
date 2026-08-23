#nullable enable
using System;
using System.Threading.Tasks;
using Unity.Services.Vivox;
using UnityEngine;

namespace Pose.Net.Voice
{
    /// <summary>
    /// Supplies Vivox with server-minted access tokens (ADR 0024 §4).
    ///
    /// Registered once via <c>VivoxService.Instance.SetTokenProvider(...)</c>, after
    /// which the SDK calls <see cref="GetTokenAsync"/> whenever it needs a
    /// credential — once to log in, once to join the channel. Each call goes to the
    /// <c>mintVivoxToken</c> Cloud Function, which re-runs the full entitlement
    /// chain, so a player banned or muted mid-series loses voice at their next
    /// request rather than at the end of the session.
    ///
    /// <b>Every URI argument here is deliberately ignored.</b> Vivox hands this
    /// method the client's own idea of who it is (<c>fromUserUri</c>) and where it
    /// wants to go (<c>channelUri</c>, <c>targetUserUri</c>). Forwarding those for
    /// signature would turn the Cloud Function into an oracle that mints a
    /// credential to join any channel as any user. The server builds both URIs
    /// itself, from the uid on the verified token and the channel recorded against
    /// a room the caller is a proven member of. The only thing this class
    /// contributes is <em>which room</em> is in play, and that is set by our own
    /// code — never by the SDK.
    ///
    /// <c>expiration</c> is ignored for the same reason: token lifetime is the
    /// server's call, not a client request.
    /// </summary>
    public sealed class VivoxTokenProvider : IVivoxTokenProvider
    {
        /// <summary>Vivox's action name for connecting as a player.</summary>
        private const string LoginAction = "login";

        /// <summary>Vivox's action name for entering a channel.</summary>
        private const string JoinAction = "join";

        private string? _roomId;

        /// <summary>
        /// Points the provider at the match currently in play. Called by the voice
        /// controller when a room is joined, and cleared when it is left, so a
        /// stale room can never be used to mint a token for a table the player has
        /// already walked away from.
        /// </summary>
        /// <param name="roomId">The Photon session name, or null when out of a room.</param>
        public void SetRoom(string? roomId) => _roomId = roomId;

        /// <summary>
        /// Fetches a token for one Vivox action.
        /// </summary>
        /// <param name="issuer">Ignored — the server knows its own issuer.</param>
        /// <param name="expiration">Ignored — lifetime is the server's decision.</param>
        /// <param name="targetUserUri">Ignored — see the class remarks.</param>
        /// <param name="action">The Vivox action, e.g. <c>login</c> or <c>join</c>.</param>
        /// <param name="channelUri">Ignored — see the class remarks.</param>
        /// <param name="fromUserUri">Ignored — see the class remarks.</param>
        /// <param name="realm">Ignored — the server knows its own domain.</param>
        /// <returns>The signed token.</returns>
        /// <exception cref="InvalidOperationException">
        /// When the server refuses. Thrown rather than returned as null so the
        /// failure surfaces through Vivox's own error path instead of becoming a
        /// null-reference inside the SDK.
        /// </exception>
        public async Task<string> GetTokenAsync(
            string? issuer = null,
            TimeSpan? expiration = null,
            string? targetUserUri = null,
            string? action = null,
            string? channelUri = null,
            string? fromUserUri = null,
            string? realm = null)
        {
            string requested = string.IsNullOrEmpty(action) ? LoginAction : action!;

            // Only a join is scoped to a room; a login names no destination.
            string? roomId = string.Equals(requested, JoinAction, StringComparison.Ordinal)
                ? _roomId
                : null;

            if (roomId is null && string.Equals(requested, JoinAction, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Vivox asked for a join token with no room set. Call SetRoom first.");
            }

            string? token = await VoiceService.MintTokenAsync(requested, roomId);
            if (token is null)
            {
                Debug.LogWarning($"[VivoxTokenProvider] no token issued for '{requested}'.");
                throw new InvalidOperationException($"Voice token refused for '{requested}'.");
            }

            return token;
        }
    }
}
