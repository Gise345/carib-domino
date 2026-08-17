#nullable enable
using System;
using NUnit.Framework;

namespace Pose.Core.Tests
{
    public class TurnTimerTests
    {
        // Short thresholds keep the arithmetic in the tests readable; the
        // shipping values live on TurnTimer as constants.
        private const float Nudge = 3f;
        private const float Expire = 6f;

        private static TurnTimer Started()
        {
            TurnTimer timer = new(Nudge, Expire);
            timer.Restart();
            return timer;
        }

        // ---- Lifecycle -------------------------------------------------------

        [Test]
        public void Is_Not_Running_Until_Restarted()
        {
            TurnTimer timer = new(Nudge, Expire);

            Assert.That(timer.IsRunning, Is.False);
            Assert.That(timer.Advance(Expire * 2f), Is.EqualTo(TurnTimerEvent.None));
        }

        [Test]
        public void Restart_Clears_A_Fired_Nudge_And_Expiry()
        {
            TurnTimer timer = Started();
            timer.Advance(Expire);

            timer.Restart();

            Assert.That(timer.HasNudged, Is.False);
            Assert.That(timer.Elapsed, Is.EqualTo(0f));
            Assert.That(timer.Advance(Nudge), Is.EqualTo(TurnTimerEvent.Nudged));
        }

        [Test]
        public void Stop_Halts_Counting_Until_The_Next_Restart()
        {
            TurnTimer timer = Started();
            timer.Advance(1f);

            timer.Stop();

            Assert.That(timer.IsRunning, Is.False);
            Assert.That(timer.Advance(Expire * 2f), Is.EqualTo(TurnTimerEvent.None));
            Assert.That(timer.Remaining, Is.EqualTo(0f));
        }

        // ---- Thresholds ------------------------------------------------------

        [Test]
        public void Reports_Nothing_Before_The_Nudge_Threshold()
        {
            TurnTimer timer = Started();

            Assert.That(timer.Advance(1f), Is.EqualTo(TurnTimerEvent.None));
            Assert.That(timer.Advance(1f), Is.EqualTo(TurnTimerEvent.None));
        }

        [Test]
        public void Nudges_On_The_Tick_That_Crosses_The_Threshold()
        {
            TurnTimer timer = Started();
            timer.Advance(Nudge - 0.5f);

            TurnTimerEvent crossing = timer.Advance(0.5f);

            Assert.That(crossing, Is.EqualTo(TurnTimerEvent.Nudged));
        }

        [Test]
        public void Nudges_Only_Once_Per_Turn()
        {
            TurnTimer timer = Started();
            timer.Advance(Nudge);

            TurnTimerEvent next = timer.Advance(0.5f);

            Assert.That(next, Is.EqualTo(TurnTimerEvent.None));
        }

        [Test]
        public void Expires_On_The_Tick_That_Reaches_The_Limit()
        {
            TurnTimer timer = Started();
            timer.Advance(Nudge);

            TurnTimerEvent crossing = timer.Advance(Expire - Nudge);

            Assert.That(crossing, Is.EqualTo(TurnTimerEvent.Expired));
        }

        [Test]
        public void Expires_Only_Once_So_Auto_Play_Cannot_Double_Fire()
        {
            // The driver submits a move on Expired. A second Expired would
            // submit a second move for a seat whose turn has already passed.
            TurnTimer timer = Started();
            timer.Advance(Expire);

            Assert.That(timer.Advance(1f), Is.EqualTo(TurnTimerEvent.None));
            Assert.That(timer.Advance(60f), Is.EqualTo(TurnTimerEvent.None));
        }

        [Test]
        public void A_Single_Long_Tick_Reports_Expiry_And_Suppresses_The_Nudge()
        {
            // A hitch or a resumed-from-background frame can hand us one huge
            // delta. Prodding the player at the same instant we auto-play for
            // them would be noise.
            TurnTimer timer = Started();

            TurnTimerEvent crossing = timer.Advance(Expire + 10f);

            Assert.That(crossing, Is.EqualTo(TurnTimerEvent.Expired));
        }

        // ---- Readouts --------------------------------------------------------

        [Test]
        public void Remaining_Counts_Down_And_Clamps_At_Zero()
        {
            TurnTimer timer = Started();

            Assert.That(timer.Remaining, Is.EqualTo(Expire).Within(0.001f));

            timer.Advance(2f);
            Assert.That(timer.Remaining, Is.EqualTo(Expire - 2f).Within(0.001f));

            timer.Advance(Expire * 2f);
            Assert.That(timer.Remaining, Is.EqualTo(0f));
        }

        [Test]
        public void Progress_Runs_From_Zero_To_One()
        {
            TurnTimer timer = Started();

            Assert.That(timer.Progress, Is.EqualTo(0f).Within(0.001f));

            timer.Advance(Expire / 2f);
            Assert.That(timer.Progress, Is.EqualTo(0.5f).Within(0.001f));

            timer.Advance(Expire);
            Assert.That(timer.Progress, Is.EqualTo(1f).Within(0.001f));
        }

        // ---- Guard rails -----------------------------------------------------

        [Test]
        public void Rejects_A_Negative_Delta()
        {
            TurnTimer timer = Started();

            Assert.That(
                () => timer.Advance(-1f),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Rejects_A_Nudge_That_Does_Not_Precede_Expiry()
        {
            Assert.That(
                () => new TurnTimer(nudgeAfterSeconds: 10f, expireAfterSeconds: 10f),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Shipping_Defaults_Are_Fifteen_And_Thirty_Seconds()
        {
            Assert.That(TurnTimer.NudgeAfterSeconds, Is.EqualTo(15f));
            Assert.That(TurnTimer.ExpireAfterSeconds, Is.EqualTo(30f));
        }

        // ---- Per-turn window (forced pass) -----------------------------------

        [Test]
        public void A_Short_Window_Expires_At_Its_Own_Limit()
        {
            // A forced pass runs a 3-second clock, not the full turn.
            TurnTimer timer = new(Nudge, Expire);
            timer.Restart(expireAfterSeconds: 3f);

            Assert.That(timer.Advance(2.9f), Is.EqualTo(TurnTimerEvent.None));
            Assert.That(timer.Advance(0.2f), Is.EqualTo(TurnTimerEvent.Expired));
        }

        [Test]
        public void A_Window_Shorter_Than_The_Nudge_Never_Nudges()
        {
            // Three seconds is not stalling, so prodding the player would be
            // noise — and the nudge threshold sits past this window anyway.
            TurnTimer timer = new(Nudge, Expire);
            timer.Restart(expireAfterSeconds: 3f);

            TurnTimerEvent first = timer.Advance(2.0f);
            TurnTimerEvent second = timer.Advance(0.5f);

            Assert.That(first, Is.EqualTo(TurnTimerEvent.None));
            Assert.That(second, Is.EqualTo(TurnTimerEvent.None));
            Assert.That(timer.HasNudged, Is.False);
        }

        [Test]
        public void A_Short_Window_Drives_Remaining_And_Progress()
        {
            TurnTimer timer = new(Nudge, Expire);
            timer.Restart(expireAfterSeconds: 4f);

            Assert.That(timer.Remaining, Is.EqualTo(4f).Within(0.001f));

            timer.Advance(2f);

            Assert.That(timer.Remaining, Is.EqualTo(2f).Within(0.001f));
            Assert.That(timer.Progress, Is.EqualTo(0.5f).Within(0.001f));
        }

        [Test]
        public void Restarting_Without_A_Window_Returns_To_The_Defaults()
        {
            TurnTimer timer = new(Nudge, Expire);
            timer.Restart(expireAfterSeconds: 3f);
            timer.Advance(3f);

            timer.Restart();

            Assert.That(timer.Remaining, Is.EqualTo(Expire).Within(0.001f));
            Assert.That(timer.Advance(Nudge), Is.EqualTo(TurnTimerEvent.Nudged));
        }

        [Test]
        public void Rejects_A_Non_Positive_Window()
        {
            TurnTimer timer = new(Nudge, Expire);

            Assert.That(
                () => timer.Restart(expireAfterSeconds: 0f),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }
}
