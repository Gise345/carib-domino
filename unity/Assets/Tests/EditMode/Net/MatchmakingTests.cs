#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Pose.Core.Tests
{
    /// <summary>
    /// The random-matchmaking property set is what Photon groups strangers by:
    /// a creator and a joiner that build different keys or values silently never
    /// match. These tests pin the keys ("mode"/"size"), the Cut-Throat value,
    /// the stringified size, and the range so a drift breaks the build, not a
    /// player's session.
    /// </summary>
    public class MatchmakingTests
    {
        [Test]
        public void CutThroatProperties_CarriesModeAndSize([Values(2, 3, 4)] int size)
        {
            IReadOnlyDictionary<string, string> props = Matchmaking.CutThroatProperties(size);

            Assert.That(props[Matchmaking.PropMode], Is.EqualTo(Matchmaking.ModeCutThroat));
            Assert.That(props[Matchmaking.PropSize], Is.EqualTo(size.ToString()));
        }

        [Test]
        public void CutThroatProperties_KeysAreExactlyModeAndSize()
        {
            IReadOnlyDictionary<string, string> props = Matchmaking.CutThroatProperties(4);

            Assert.That(props.Keys, Is.EquivalentTo(new[] { "mode", "size" }));
        }

        [Test]
        public void ModeValue_MatchesServerWireString()
        {
            // Must equal the "cutthroat" string the server records (MatchService /
            // startMatch); a mismatch would split the client and server view of
            // the ruleset.
            Assert.That(Matchmaking.ModeCutThroat, Is.EqualTo("cutthroat"));
        }

        [Test]
        public void CutThroatProperties_SameSizeProducesEqualSets()
        {
            // Two independent callers (creator + joiner) must produce identical
            // property sets, or Photon won't group them.
            IReadOnlyDictionary<string, string> a = Matchmaking.CutThroatProperties(4);
            IReadOnlyDictionary<string, string> b = Matchmaking.CutThroatProperties(4);

            Assert.That(a, Is.EquivalentTo(b));
        }

        [Test]
        public void CutThroatProperties_DifferentSizesDoNotMatch()
        {
            string threeP = Matchmaking.CutThroatProperties(3)[Matchmaking.PropSize];
            string fourP = Matchmaking.CutThroatProperties(4)[Matchmaking.PropSize];

            Assert.That(threeP, Is.Not.EqualTo(fourP));
        }

        [Test]
        public void CutThroatProperties_RejectsOutOfRange([Values(0, 1, 5, -1)] int size)
        {
            Assert.That(
                () => Matchmaking.CutThroatProperties(size),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }
}
