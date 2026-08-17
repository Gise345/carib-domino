#nullable enable
using System;
using NUnit.Framework;

namespace Pose.Core.Tests
{
    public class JoinRetryPolicyTests
    {
        private const float Resend = 1f;
        private const float GiveUp = 10f;

        private static JoinRetryPolicy New() => new(Resend, GiveUp);

        /// <summary>Advances in small steps, as a real frame loop would.</summary>
        private static JoinAttempt Run(JoinRetryPolicy p, float seconds, float step = 0.1f)
        {
            JoinAttempt last = JoinAttempt.Wait;
            for (float t = 0f; t < seconds; t += step)
            {
                JoinAttempt now = p.Advance(step);
                if (now != JoinAttempt.Wait)
                {
                    last = now;
                }
            }

            return last;
        }

        // ---- Asking again ---------------------------------------------------

        [Test]
        public void Waits_Before_Asking_Again()
        {
            JoinRetryPolicy p = New();

            Assert.That(p.Advance(Resend * 0.5f), Is.EqualTo(JoinAttempt.Wait));
            Assert.That(p.Resends, Is.Zero);
        }

        [Test]
        public void Asks_Again_On_The_Cadence()
        {
            JoinRetryPolicy p = New();

            Run(p, Resend + 0.05f);

            Assert.That(p.Resends, Is.EqualTo(1));
        }

        [Test]
        public void Keeps_Asking_While_Unseated()
        {
            JoinRetryPolicy p = New();

            Run(p, Resend * 5f);

            Assert.That(p.Resends, Is.GreaterThanOrEqualTo(4));
        }

        [Test]
        public void One_Huge_Tick_Asks_Once_Not_In_A_Burst()
        {
            // A phone resuming from the background hands us one enormous delta.
            JoinRetryPolicy p = New();

            JoinAttempt result = p.Advance(Resend * 5f);

            Assert.That(result, Is.EqualTo(JoinAttempt.Resend));
            Assert.That(p.Resends, Is.EqualTo(1));
        }

        // ---- Being seated ---------------------------------------------------

        [Test]
        public void Stops_Asking_Once_Seated()
        {
            JoinRetryPolicy p = New();
            Run(p, Resend * 2f);
            int before = p.Resends;

            p.Seated();
            Run(p, Resend * 5f);

            Assert.That(p.Resends, Is.EqualTo(before));
            Assert.That(p.IsSeated, Is.True);
        }

        [Test]
        public void A_Seat_That_Lands_Late_Still_Counts()
        {
            // The table confirmed us just after we stopped asking — that is a
            // seat, not a failure, and the UI must not be left showing an error.
            JoinRetryPolicy p = New();
            Run(p, GiveUp + Resend);
            Assert.That(p.HasGivenUp, Is.True);

            p.Seated();

            Assert.That(p.HasGivenUp, Is.False);
            Assert.That(p.Advance(Resend), Is.EqualTo(JoinAttempt.Wait));
        }

        [Test]
        public void Seated_Is_Idempotent()
        {
            JoinRetryPolicy p = New();
            p.Seated();

            Assert.That(() => p.Seated(), Throws.Nothing);
            Assert.That(p.Advance(GiveUp * 2f), Is.EqualTo(JoinAttempt.Wait));
        }

        // ---- Giving up ------------------------------------------------------

        [Test]
        public void Gives_Up_Rather_Than_Waiting_Forever()
        {
            JoinRetryPolicy p = New();

            JoinAttempt result = Run(p, GiveUp + Resend);

            Assert.That(result, Is.EqualTo(JoinAttempt.GiveUp));
            Assert.That(p.HasGivenUp, Is.True);
        }

        [Test]
        public void Giving_Up_Is_Terminal()
        {
            JoinRetryPolicy p = New();
            Run(p, GiveUp + Resend);
            int asked = p.Resends;

            Assert.That(p.Advance(Resend * 3f), Is.EqualTo(JoinAttempt.GiveUp));
            Assert.That(p.Resends, Is.EqualTo(asked), "must not keep asking after giving up");
        }

        [Test]
        public void One_Huge_Tick_Cannot_Skip_Past_Giving_Up()
        {
            JoinRetryPolicy p = New();

            Assert.That(p.Advance(GiveUp * 10f), Is.EqualTo(JoinAttempt.GiveUp));
        }

        // ---- Guards ---------------------------------------------------------

        [Test]
        public void Rejects_A_Non_Positive_Cadence()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new JoinRetryPolicy(0f, GiveUp));
        }

        [Test]
        public void Rejects_Giving_Up_Before_A_Single_Retry()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new JoinRetryPolicy(Resend, Resend));
        }

        [Test]
        public void Rejects_A_Negative_Delta()
        {
            Assert.That(() => New().Advance(-1f),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }
}
