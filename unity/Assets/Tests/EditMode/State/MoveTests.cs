#nullable enable
using NUnit.Framework;

namespace Pose.Core.Tests
{
    public class MoveTests
    {
        [Test]
        public void PlaceMove_Captures_Player_Tile_And_End()
        {
            PlayerId p = new("alice");

            PlaceMove m = new(p, new Tile(3, 5), ChainEnd.Right);

            Assert.That(m.Player, Is.EqualTo(p));
            Assert.That(m.Tile, Is.EqualTo(new Tile(3, 5)));
            Assert.That(m.End, Is.EqualTo(ChainEnd.Right));
        }

        [Test]
        public void PassMove_Captures_Player()
        {
            PlayerId p = new("bob");

            PassMove m = new(p);

            Assert.That(m.Player, Is.EqualTo(p));
        }

        [Test]
        public void PlaceMove_ToString_Is_Human_Readable()
        {
            PlaceMove m = new(new PlayerId("alice"), new Tile(3, 5), ChainEnd.Right);

            string s = m.ToString();

            Assert.That(s, Does.Contain("alice"));
            Assert.That(s, Does.Contain("PLACE"));
            Assert.That(s, Does.Contain("Right"));
        }
    }
}
