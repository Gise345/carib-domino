#nullable enable
using System;
using NUnit.Framework;

namespace Pose.Core.Tests
{
    public class ChainTests
    {
        [Test]
        public void Empty_Chain_Has_No_Tiles()
        {
            Chain c = Chain.Empty;

            Assert.That(c.IsEmpty, Is.True);
            Assert.That(c.Count, Is.EqualTo(0));
        }

        [Test]
        public void Empty_Chain_LeftEnd_Throws()
        {
            Chain c = Chain.Empty;

            Assert.Throws<InvalidOperationException>(() => { _ = c.LeftEnd; });
        }

        [Test]
        public void Place_On_Empty_Chain_Lays_Tile_As_Is()
        {
            Chain after = Chain.Empty.Place(new Tile(3, 5), ChainEnd.Left);

            Assert.That(after.Count, Is.EqualTo(1));
            Assert.That(after.LeftEnd, Is.EqualTo((byte)3));
            Assert.That(after.RightEnd, Is.EqualTo((byte)5));
        }

        [Test]
        public void Place_On_Empty_Chain_Ignores_Requested_End()
        {
            // For the very first tile both ends are open; the requested end has no
            // effect — the canonical orientation (A-Left, B-Right) is used.
            Chain left = Chain.Empty.Place(new Tile(3, 5), ChainEnd.Left);
            Chain right = Chain.Empty.Place(new Tile(3, 5), ChainEnd.Right);

            Assert.That(left.LeftEnd, Is.EqualTo(right.LeftEnd));
            Assert.That(left.RightEnd, Is.EqualTo(right.RightEnd));
        }

        [Test]
        public void Place_On_Right_Updates_RightEnd_To_Other_Pip()
        {
            Chain c = Chain.Empty.Place(new Tile(3, 5), ChainEnd.Left);

            Chain after = c.Place(new Tile(5, 2), ChainEnd.Right);

            Assert.That(after.LeftEnd, Is.EqualTo((byte)3));
            Assert.That(after.RightEnd, Is.EqualTo((byte)2));
            Assert.That(after.Count, Is.EqualTo(2));
        }

        [Test]
        public void Place_On_Left_Updates_LeftEnd_To_Other_Pip()
        {
            Chain c = Chain.Empty.Place(new Tile(3, 5), ChainEnd.Left);

            Chain after = c.Place(new Tile(3, 6), ChainEnd.Left);

            Assert.That(after.LeftEnd, Is.EqualTo((byte)6));
            Assert.That(after.RightEnd, Is.EqualTo((byte)5));
        }

        [Test]
        public void Place_Throws_When_Tile_Does_Not_Match_End()
        {
            Chain c = Chain.Empty.Place(new Tile(3, 5), ChainEnd.Left);

            Assert.Throws<ArgumentException>(() => c.Place(new Tile(0, 1), ChainEnd.Right));
            Assert.Throws<ArgumentException>(() => c.Place(new Tile(0, 1), ChainEnd.Left));
        }

        [Test]
        public void Place_Of_Double_Keeps_End_Pip_Same()
        {
            Chain c = Chain.Empty.Place(new Tile(3, 5), ChainEnd.Left);

            Chain after = c.Place(new Tile(5, 5), ChainEnd.Right);

            Assert.That(after.RightEnd, Is.EqualTo((byte)5));
            Assert.That(after.Count, Is.EqualTo(2));
        }

        [Test]
        public void Original_Chain_Is_Not_Mutated_By_Place()
        {
            Chain original = Chain.Empty.Place(new Tile(3, 5), ChainEnd.Left);
            int before = original.Count;

            Chain _ = original.Place(new Tile(5, 2), ChainEnd.Right);

            Assert.That(original.Count, Is.EqualTo(before));
            Assert.That(original.RightEnd, Is.EqualTo((byte)5));
        }
    }
}
