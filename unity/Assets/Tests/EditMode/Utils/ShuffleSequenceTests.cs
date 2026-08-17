#nullable enable
using System;
using NUnit.Framework;

namespace Pose.Core.Tests
{
    public class ShuffleSequenceTests
    {
        // Short, readable durations; the shipping values live on the class.
        private const float Gather = 1f;
        private const float Cycle = 2f;
        private const float Stack = 1f;
        private const float Deal = 2f;
        private const float MaxSwirl = 10f;

        private static ShuffleSequence New() =>
            new(Gather, Cycle, Stack, Deal, MaxSwirl);

        /// <summary>Advances in small steps, as a real frame loop would.</summary>
        private static void Run(ShuffleSequence s, float seconds, float step = 0.1f)
        {
            for (float t = 0f; t < seconds; t += step)
            {
                s.Advance(step);
            }
        }

        // ---- Order ----------------------------------------------------------

        [Test]
        public void Starts_By_Gathering()
        {
            Assert.That(New().Phase, Is.EqualTo(ShufflePhase.Gather));
        }

        [Test]
        public void Gather_Gives_Way_To_Swirl()
        {
            ShuffleSequence s = New();

            Run(s, Gather + 0.1f);

            Assert.That(s.Phase, Is.EqualTo(ShufflePhase.Swirl));
        }

        [Test]
        public void Runs_Stack_Then_Deal_Then_Done()
        {
            ShuffleSequence s = New();
            Run(s, Gather + 0.1f);
            s.RequestFinish();

            Run(s, Cycle);
            Assert.That(s.Phase, Is.EqualTo(ShufflePhase.Stack));

            Run(s, Stack);
            Assert.That(s.Phase, Is.EqualTo(ShufflePhase.Deal));

            Run(s, Deal);
            Assert.That(s.Phase, Is.EqualTo(ShufflePhase.Done));
        }

        // ---- The shuffle must not gate the deal ------------------------------

        [Test]
        public void Swirls_Indefinitely_While_The_Deal_Has_Not_Landed()
        {
            // The whole point: a slow startMatch round-trip must not cut the
            // animation short, and must not be cut short BY it either.
            ShuffleSequence s = New();

            Run(s, Gather + (Cycle * 4f));

            Assert.That(s.Phase, Is.EqualTo(ShufflePhase.Swirl));
            Assert.That(s.SwirlCyclesCompleted, Is.GreaterThanOrEqualTo(3));
        }

        [Test]
        public void Leaves_Swirl_Only_On_A_Cycle_Boundary()
        {
            ShuffleSequence s = New();
            Run(s, Gather + 0.1f);

            // Ask to finish a fraction into a cycle.
            Run(s, Cycle * 0.25f);
            s.RequestFinish();
            Run(s, Cycle * 0.25f);

            Assert.That(s.Phase, Is.EqualTo(ShufflePhase.Swirl),
                "should still be swirling mid-cycle");

            // Just past the boundary — enough to end the cycle, not enough to
            // run through Stack as well.
            Run(s, Cycle * 0.5f);

            Assert.That(s.Phase, Is.EqualTo(ShufflePhase.Stack));
        }

        [Test]
        public void A_Deal_That_Is_Ready_Immediately_Still_Gets_One_Full_Cycle()
        {
            // Otherwise a cached or instant deal makes the shuffle flicker.
            ShuffleSequence s = New();
            s.RequestFinish();

            Run(s, Gather + (Cycle * 0.5f));

            Assert.That(s.Phase, Is.EqualTo(ShufflePhase.Swirl));
            Assert.That(s.SwirlCyclesCompleted, Is.Zero);
        }

        [Test]
        public void Gives_Up_Swirling_After_The_Ceiling()
        {
            // A dropped RequestFinish must not leave the player watching tiles
            // spin forever.
            ShuffleSequence s = New();

            Run(s, Gather + MaxSwirl + Cycle);

            Assert.That(s.Phase, Is.Not.EqualTo(ShufflePhase.Swirl));
        }

        // ---- Progress --------------------------------------------------------

        [Test]
        public void Phase_Progress_Runs_Zero_To_One()
        {
            ShuffleSequence s = New();

            Assert.That(s.PhaseProgress, Is.EqualTo(0f).Within(0.001f));

            s.Advance(Gather / 2f);

            Assert.That(s.PhaseProgress, Is.EqualTo(0.5f).Within(0.001f));
        }

        [Test]
        public void Swirl_Progress_Resets_Each_Cycle()
        {
            // The renderer drives a repeating motion from this, so it has to
            // wrap rather than climb.
            ShuffleSequence s = New();
            Run(s, Gather + 0.1f);
            Run(s, Cycle * 0.5f);
            float mid = s.PhaseProgress;

            Run(s, Cycle);

            Assert.That(mid, Is.GreaterThan(0.3f));
            Assert.That(s.PhaseProgress, Is.LessThan(1f));
        }

        // ---- Edges -----------------------------------------------------------

        [Test]
        public void One_Huge_Tick_Cannot_Skip_Past_Done()
        {
            // A resumed-from-background frame hands us one enormous delta.
            ShuffleSequence s = New();
            s.RequestFinish();

            s.Advance(1000f);

            Assert.That(s.Phase, Is.EqualTo(ShufflePhase.Done));
        }

        [Test]
        public void Done_Is_Terminal()
        {
            ShuffleSequence s = New();
            s.RequestFinish();
            s.Advance(1000f);

            Assert.That(s.Advance(10f), Is.EqualTo(ShufflePhase.Done));
            Assert.That(s.IsDone, Is.True);
        }

        [Test]
        public void RequestFinish_Is_Idempotent()
        {
            ShuffleSequence s = New();
            s.RequestFinish();
            s.RequestFinish();

            Assert.That(s.FinishRequested, Is.True);
            Assert.That(() => s.RequestFinish(), Throws.Nothing);
        }

        [Test]
        public void Rejects_A_Negative_Delta()
        {
            Assert.That(() => New().Advance(-1f),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Rejects_A_Non_Positive_Duration()
        {
            Assert.That(() => new ShuffleSequence(gatherSeconds: 0f),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Shipping_Durations_Are_Sane()
        {
            Assert.That(ShuffleSequence.GatherSeconds, Is.GreaterThan(0f));
            Assert.That(ShuffleSequence.SwirlCycleSeconds, Is.GreaterThan(0f));
            Assert.That(ShuffleSequence.MaxSwirlSeconds,
                Is.GreaterThan(ShuffleSequence.SwirlCycleSeconds));
        }
    }
}
