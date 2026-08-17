#nullable enable
using System;

namespace Pose.Core
{
    /// <summary>What a client waiting for a seat should do this frame.</summary>
    public enum JoinAttempt
    {
        /// <summary>Keep waiting — the last request is still in flight.</summary>
        Wait = 0,

        /// <summary>Send the seat request again.</summary>
        Resend = 1,

        /// <summary>Stop asking and tell the player.</summary>
        GiveUp = 2,
    }

    /// <summary>
    /// Paces a joining client's attempts to claim a seat at a table.
    ///
    /// Seat registration is a single unacknowledged message to whichever client
    /// holds the table. On a long link — a player in Cayman joining a table
    /// hosted the other side of the world — that message can be lost, and one
    /// lost message used to strand the player in the waiting room forever while
    /// everyone else saw them "connected". The seat request is idempotent by
    /// construction (a client that already holds a seat is ignored), so the fix
    /// is simply to keep asking until the table confirms, and to give up loudly
    /// rather than silently.
    ///
    /// Pure C# with no clock of its own — the caller feeds it elapsed time, the
    /// same shape as <see cref="TurnTimer"/> and <see cref="ShuffleSequence"/>,
    /// which keeps the policy testable away from the network.
    /// </summary>
    public sealed class JoinRetryPolicy
    {
        /// <summary>
        /// Gap between attempts. Comfortably longer than a round trip to the
        /// far side of the world, so a slow link isn't mistaken for a lost
        /// message and spammed.
        /// </summary>
        public const float ResendEverySeconds = 1.25f;

        /// <summary>
        /// How long to keep asking before telling the player it failed. Sits
        /// below the table's own 60s auto-start deadline, so a client that is
        /// never going to be seated finds out while the table is still waiting
        /// rather than after it has dealt without them.
        /// </summary>
        public const float GiveUpAfterSeconds = 40f;

        private readonly float _resendEvery;
        private readonly float _giveUpAfter;

        private float _elapsed;
        private float _sinceLastSend;
        private bool _seated;
        private bool _gaveUp;

        /// <param name="resendEverySeconds">Override for <see cref="ResendEverySeconds"/>.</param>
        /// <param name="giveUpAfterSeconds">Override for <see cref="GiveUpAfterSeconds"/>.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when a duration is not positive, or when the client would give
        /// up before it ever got to ask a second time.
        /// </exception>
        public JoinRetryPolicy(
            float resendEverySeconds = ResendEverySeconds,
            float giveUpAfterSeconds = GiveUpAfterSeconds)
        {
            if (resendEverySeconds <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(resendEverySeconds), "The gap between attempts must be positive.");
            }

            if (giveUpAfterSeconds <= resendEverySeconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(giveUpAfterSeconds),
                    "Giving up this early would allow no retry at all.");
            }

            _resendEvery = resendEverySeconds;
            _giveUpAfter = giveUpAfterSeconds;
        }

        /// <summary>True once the table has confirmed a seat; nothing more is sent.</summary>
        public bool IsSeated => _seated;

        /// <summary>True once the client has stopped asking without a seat.</summary>
        public bool HasGivenUp => _gaveUp;

        /// <summary>How long this client has been trying to take a seat.</summary>
        public float Elapsed => _elapsed;

        /// <summary>How many repeat requests have been asked for so far.</summary>
        public int Resends { get; private set; }

        /// <summary>
        /// The table confirmed our seat. Terminal — later calls to
        /// <see cref="Advance"/> only ever return <see cref="JoinAttempt.Wait"/>.
        /// Safe to call more than once, and safe to call after giving up (a late
        /// confirmation is still a seat).
        /// </summary>
        public void Seated()
        {
            _seated = true;
            _gaveUp = false;
        }

        /// <summary>
        /// Advances the wait and says what to do now.
        /// </summary>
        /// <param name="deltaSeconds">
        /// Elapsed time since the last call, normally <c>Time.deltaTime</c>.
        /// </param>
        /// <returns>Whether to wait, ask again, or stop.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="deltaSeconds"/> is negative.
        /// </exception>
        public JoinAttempt Advance(float deltaSeconds)
        {
            if (deltaSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(deltaSeconds), "Time cannot advance backwards.");
            }

            if (_seated || _gaveUp)
            {
                return _seated ? JoinAttempt.Wait : JoinAttempt.GiveUp;
            }

            _elapsed += deltaSeconds;
            _sinceLastSend += deltaSeconds;

            if (_elapsed >= _giveUpAfter)
            {
                _gaveUp = true;
                return JoinAttempt.GiveUp;
            }

            if (_sinceLastSend < _resendEvery)
            {
                return JoinAttempt.Wait;
            }

            // One resend per call however long the tick was: a device resuming
            // from the background shouldn't fire off a burst of stale requests.
            _sinceLastSend = 0f;
            Resends++;
            return JoinAttempt.Resend;
        }
    }
}
