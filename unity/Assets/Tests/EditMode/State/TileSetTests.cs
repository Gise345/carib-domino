#nullable enable
using System.Collections.Generic;
using NUnit.Framework;

namespace Pose.Core.Tests
{
    public class TileSetTests
    {
        [Test]
        public void DoubleSix_Has_28_Tiles()
        {
            int count = TileSet.DoubleSix.Count;

            Assert.That(count, Is.EqualTo(28));
        }

        [Test]
        public void DoubleNine_Has_55_Tiles()
        {
            int count = TileSet.DoubleNine.Count;

            Assert.That(count, Is.EqualTo(55));
        }

        [Test]
        public void DoubleTwelve_Has_91_Tiles()
        {
            int count = TileSet.DoubleTwelve.Count;

            Assert.That(count, Is.EqualTo(91));
        }

        [Test]
        public void DoubleSix_Has_All_Unique_Tiles()
        {
            HashSet<Tile> unique = new(TileSet.DoubleSix);

            Assert.That(unique.Count, Is.EqualTo(28));
        }

        [Test]
        public void DoubleSix_Includes_Every_Double_From_Zero_To_Six()
        {
            HashSet<Tile> tiles = new(TileSet.DoubleSix);

            for (byte i = 0; i <= 6; i++)
            {
                Assert.That(tiles.Contains(new Tile(i, i)), Is.True, $"Missing double [{i}|{i}].");
            }
        }

        [Test]
        public void DoubleSix_Total_Pips_Equals_168()
        {
            // Sum of all pips on a standard double-six set is the well-known 168.
            int total = 0;
            foreach (Tile t in TileSet.DoubleSix)
            {
                total += t.Pips;
            }

            Assert.That(total, Is.EqualTo(168));
        }

        [Test]
        public void DoubleSix_Returns_The_Same_Cached_Reference_On_Repeated_Access()
        {
            // The set should be generated once. Multiple property accesses must return
            // the identical instance — important so callers can safely use it as a
            // deterministic shuffle source without surprises.
            IReadOnlyList<Tile> first = TileSet.DoubleSix;
            IReadOnlyList<Tile> second = TileSet.DoubleSix;

            Assert.That(ReferenceEquals(first, second), Is.True);
        }
    }
}
