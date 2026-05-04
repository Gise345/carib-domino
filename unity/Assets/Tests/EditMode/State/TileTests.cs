#nullable enable
using System;
using NUnit.Framework;

namespace Pose.Core.Tests
{
    public class TileTests
    {
        [Test]
        public void Tile_Pips_Sums_The_Two_Faces()
        {
            Tile t = new(3, 5);

            int pips = t.Pips;

            Assert.That(pips, Is.EqualTo(8));
        }

        [Test]
        public void Tile_IsDouble_True_When_Both_Pips_Equal()
        {
            Tile t = new(4, 4);

            bool isDouble = t.IsDouble;

            Assert.That(isDouble, Is.True);
        }

        [Test]
        public void Tile_IsDouble_False_When_Pips_Differ()
        {
            Tile t = new(2, 5);

            bool isDouble = t.IsDouble;

            Assert.That(isDouble, Is.False);
        }

        [Test]
        public void Tile_Equality_Is_Symmetric_Across_Construction_Order()
        {
            Tile a = new(3, 5);
            Tile b = new(5, 3);

            bool equal = a == b;

            Assert.That(equal, Is.True);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        }

        [Test]
        public void Tile_Matches_True_When_Either_Pip_Equals_Target()
        {
            Tile t = new(2, 6);

            Assert.That(t.Matches(2), Is.True);
            Assert.That(t.Matches(6), Is.True);
            Assert.That(t.Matches(4), Is.False);
        }

        [Test]
        public void Tile_GetOther_Returns_Opposite_Pip()
        {
            Tile t = new(2, 6);

            Assert.That(t.GetOther(2), Is.EqualTo((byte)6));
            Assert.That(t.GetOther(6), Is.EqualTo((byte)2));
        }

        [Test]
        public void Tile_GetOther_On_Double_Returns_Same_Pip()
        {
            Tile t = new(4, 4);

            byte other = t.GetOther(4);

            Assert.That(other, Is.EqualTo((byte)4));
        }

        [Test]
        public void Tile_GetOther_Throws_When_Pip_Not_On_Tile()
        {
            Tile t = new(2, 6);

            Assert.Throws<ArgumentException>(() => t.GetOther(3));
        }

        [Test]
        public void Tile_ToString_Renders_Smaller_Pip_First()
        {
            Tile t = new(5, 3);

            string s = t.ToString();

            Assert.That(s, Is.EqualTo("[3|5]"));
        }
    }
}
