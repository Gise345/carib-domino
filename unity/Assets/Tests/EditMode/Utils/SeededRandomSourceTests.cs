#nullable enable
using System;
using NUnit.Framework;

namespace Pose.Core.Tests
{
    public class SeededRandomSourceTests
    {
        [Test]
        public void Same_Seed_Produces_Identical_Sequence()
        {
            SeededRandomSource a = new(seed: 0xABCDEF1234567890UL);
            SeededRandomSource b = new(seed: 0xABCDEF1234567890UL);

            for (int i = 0; i < 1000; i++)
            {
                Assert.That(a.NextUInt64(), Is.EqualTo(b.NextUInt64()), $"Diverged at draw {i}.");
            }
        }

        [Test]
        public void Different_Seeds_Produce_Different_Sequences()
        {
            SeededRandomSource a = new(seed: 1UL);
            SeededRandomSource b = new(seed: 2UL);

            // Almost certain to diverge by the 100th draw with overwhelming probability.
            bool sawDifference = false;
            for (int i = 0; i < 100 && !sawDifference; i++)
            {
                if (a.NextUInt64() != b.NextUInt64())
                {
                    sawDifference = true;
                }
            }

            Assert.That(sawDifference, Is.True);
        }

        [Test]
        public void NextInt_Stays_Within_Bounds()
        {
            SeededRandomSource r = new(seed: 42UL);

            for (int i = 0; i < 10_000; i++)
            {
                int v = r.NextInt(28);
                Assert.That(v, Is.InRange(0, 27));
            }
        }

        [Test]
        public void NextInt_Rejects_Non_Positive_Bound()
        {
            SeededRandomSource r = new(seed: 1UL);

            Assert.Throws<ArgumentOutOfRangeException>(() => r.NextInt(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => r.NextInt(-1));
        }

        [Test]
        public void NextInt_Distribution_Is_Approximately_Uniform()
        {
            // Sanity check that the rejection-sampling implementation is not biased.
            // We don't run a chi-squared test here (overkill); we just verify each
            // bucket is hit roughly equally over a large sample.
            const int Buckets = 10;
            const int Samples = 100_000;
            int[] counts = new int[Buckets];

            SeededRandomSource r = new(seed: 99UL);
            for (int i = 0; i < Samples; i++)
            {
                counts[r.NextInt(Buckets)]++;
            }

            int expected = Samples / Buckets;
            int tolerance = expected / 5; // ±20% — generous to avoid spurious failures
            for (int i = 0; i < Buckets; i++)
            {
                Assert.That(counts[i], Is.InRange(expected - tolerance, expected + tolerance),
                    $"Bucket {i} count {counts[i]} far outside expected {expected} ±{tolerance}.");
            }
        }

        // NOTE: a reference-values test pinning the exact SplitMix64 sequence for a
        // known seed will be added when the TypeScript port lands at M4. Both
        // implementations must produce the same byte sequence for the same seed —
        // see docs/ARCHITECTURE.md section 7.2 (rule-engine parity).
    }
}
