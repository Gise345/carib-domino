#nullable enable
using System;
using System.Linq;
using NUnit.Framework;

namespace Pose.Core.Tests
{
    public class HandTests
    {
        [Test]
        public void Hand_Empty_Has_Zero_Count()
        {
            Hand h = Hand.Empty;

            Assert.That(h.Count, Is.EqualTo(0));
            Assert.That(h.PipTotal, Is.EqualTo(0));
        }

        [Test]
        public void Hand_Count_Reflects_Number_Of_Tiles()
        {
            Hand h = new(new[] { new Tile(1, 2), new Tile(3, 4), new Tile(0, 0) });

            int count = h.Count;

            Assert.That(count, Is.EqualTo(3));
        }

        [Test]
        public void Hand_PipTotal_Sums_All_Tile_Pips()
        {
            Hand h = new(new[] { new Tile(1, 2), new Tile(3, 4), new Tile(0, 6) });

            int total = h.PipTotal;

            Assert.That(total, Is.EqualTo(1 + 2 + 3 + 4 + 0 + 6));
        }

        [Test]
        public void Hand_Contains_True_When_Tile_Present()
        {
            Hand h = new(new[] { new Tile(1, 2), new Tile(3, 4) });

            Assert.That(h.Contains(new Tile(1, 2)), Is.True);
            Assert.That(h.Contains(new Tile(2, 1)), Is.True, "Tile equality is symmetric.");
            Assert.That(h.Contains(new Tile(5, 5)), Is.False);
        }

        [Test]
        public void Hand_Without_Returns_New_Hand_With_Tile_Removed()
        {
            Hand original = new(new[] { new Tile(1, 2), new Tile(3, 4), new Tile(5, 6) });

            Hand reduced = original.Without(new Tile(3, 4));

            Assert.That(reduced.Count, Is.EqualTo(2));
            Assert.That(reduced.Contains(new Tile(3, 4)), Is.False);
            Assert.That(original.Count, Is.EqualTo(3), "Original hand must not mutate.");
        }

        [Test]
        public void Hand_Without_Throws_When_Tile_Not_Present()
        {
            Hand h = new(new[] { new Tile(1, 2) });

            Assert.Throws<InvalidOperationException>(() => h.Without(new Tile(5, 5)));
        }

        [Test]
        public void Hand_Preserves_Insertion_Order_When_Enumerated()
        {
            Tile[] inOrder = { new(1, 2), new(3, 4), new(5, 6) };
            Hand h = new(inOrder);

            Tile[] enumerated = h.ToArray();

            CollectionAssert.AreEqual(inOrder, enumerated);
        }
    }
}
