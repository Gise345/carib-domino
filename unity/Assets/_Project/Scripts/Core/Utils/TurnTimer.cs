#nullable enable
using System;

namespace Pose.Core
{
    /// <summary>
    /// Tracks how long the current player has been sitting on their turn, and
    /// reports the two moments the game reacts to: the nudge (a haptic prod at
    /// <see cref="NudgeAfterSeconds"/>) and the expiry (an auto-play at
    /// <see cref="ExpireAfterSeconds"/>).
    ///
    /// Pure C# with no Unity dependency and no internal clock — the caller feeds
    /// it elapsed time via <see cref="Advance"/>. That keeps it unit-testable and
    /// lets the two drivers share it: offline practice ticks it from
    /// <c>BoardBootstrap.Update</c>, and an online table ticks it on the seat's
    /// authority so a backgrounded client can't stall the table by simply not
    /// running.
    ///
    /// Each transition is reported exactly once. <see cref="Advance"/> returns
    /// <see cref="TurnTimerEvent.Nudged"/> on the tick that crosses the nudge
    /// threshold and <see cref="TurnTimerEvent.Expired"/> on the tick that
    /// crosses expiry — never again for the same turn, so callers can fire an
    /// effect directly off the return value without tracking edges themselves.
    /// </summary>
    public sealed class TurnTimer
    {
        /// <summary>Seconds of inactivity before the player is nudged.</summary>
        public const float NudgeAfterSeconds = 15f;

        /// <summary>Seconds of inactivity before the turn is auto-played.</summary>
        public const float ExpireAfterSeconds = 30f;

        private readonly float _nudgeAfter;
        private readonly float _expireAfter;

        private bool _running;
        private float _elapsed;
        private bool _nudged;
        private bool _expired;

        /// <param name="nudgeAfterSeconds">
        /// Override for the nudge threshold. Defaults to
        /// <see cref="NudgeAfterSeconds"/>; tests use a shorter value.
        /// </param>
        /// <param name="expireAfterSeconds">
        /// Override for the expiry threshold. Defaults to
        /// <see cref="ExpireAfterSeconds"/>.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when either threshold is non-positive, or when the nudge does
        /// not come strictly before expiry.
        /// </exception>
        public TurnTimer(
            float nudgeAfterSeconds = NudgeAfterSeconds,
            float expireAfterSeconds = ExpireAfterSeconds)
        {
            if (nudgeAfterSeconds <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(nudgeAfterSeconds),
                    "Nudge threshold must be positive.");
            }

            if (expireAfterSeconds <= nudgeAfterSeconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(expireAfterSeconds),
                    "Expiry must come strictly after the nudge.");
            }

            _nudgeAfter = nudgeAfterSeconds;
            _expireAfter = expireAfterSeconds;
        }

        /// <summary>True while the timer is counting a live turn.</summary>
        public bool IsRunning => _running;

        /// <summary>Seconds counted on the current turn.</summary>
        public float Elapsed => _elapsed;

        /// <summary>
        /// Seconds left before auto-play, clamped at zero. Zero whenever the
        /// timer is stopped, so a paused turn reads as no pressure.
        /// </summary>
        public float Remaining => _running ? Math.Max(0f, _expireAfter - _elapsed) : 0f;

        /// <summary>
        /// Fraction of the turn consumed, 0..1. Drives the countdown ring.
        /// </summary>
        public float Progress => _running ? Math.Min(1f, _elapsed / _expireAfter) : 0f;

        /// <summary>True once the nudge has fired for the current turn.</summary>
        public bool HasNudged => _nudged;

        /// <summary>
        /// Begins timing a fresh turn, clearing any nudge/expiry already fired.
        /// Safe to call on every turn change, including repeats.
        /// </summary>
        public void Restart()
        {
            _running = true;
            _elapsed = 0f;
            _nudged = false;
            _expired = false;
        }

        /// <summary>
        /// Stops timing — a round ended, the turn passed to someone this client
        /// isn't timing, or the table paused. <see cref="Advance"/> is inert
        /// until the next <see cref="Restart"/>.
        /// </summary>
        public void Stop()
        {
            _running = false;
            _elapsed = 0f;
            _nudged = false;
            _expired = false;
        }

        /// <summary>
        /// Adds <paramref name="deltaSeconds"/> to the current turn and reports
        /// any threshold crossed by this tick.
        /// </summary>
        /// <param name="deltaSeconds">
        /// Elapsed time since the last call, normally <c>Time.deltaTime</c>.
        /// Negative values are rejected; zero is a no-op.
        /// </param>
        /// <returns>
        /// <see cref="TurnTimerEvent.Expired"/> on the tick that reaches expiry,
        /// <see cref="TurnTimerEvent.Nudged"/> on the tick that reaches the nudge
        /// threshold, otherwise <see cref="TurnTimerEvent.None"/>. A single tick
        /// large enough to cross both reports only <c>Expired</c> — the auto-play
        /// is imminent, so prodding the player first would be noise.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="deltaSeconds"/> is negative.
        /// </exception>
        public TurnTimerEvent Advance(float deltaSeconds)
        {
            if (deltaSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(deltaSeconds),
                    "Time cannot advance backwards.");
            }

            if (!_running || _expired)
            {
                return TurnTimerEvent.None;
            }

            _elapsed += deltaSeconds;

            if (_elapsed >= _expireAfter)
            {
                _expired = true;
                // Suppress a late nudge: the auto-play speaks for itself.
                _nudged = true;
                return TurnTimerEvent.Expired;
            }

            if (!_nudged && _elapsed >= _nudgeAfter)
            {
                _nudged = true;
                return TurnTimerEvent.Nudged;
            }

            return TurnTimerEvent.None;
        }
    }

    /// <summary>
    /// A threshold crossed by a single <see cref="TurnTimer.Advance"/> call.
    /// </summary>
    public enum TurnTimerEvent
    {
        /// <summary>Nothing crossed on this tick.</summary>
        None = 0,

        /// <summary>The player has stalled long enough to be prodded.</summary>
        Nudged = 1,

        /// <summary>The turn is out of time and must be auto-played.</summary>
        Expired = 2,
    }
}
