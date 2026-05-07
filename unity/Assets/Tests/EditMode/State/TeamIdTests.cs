#nullable enable
using System;
using NUnit.Framework;

namespace Pose.Core.Tests
{
    public class TeamIdTests
    {
        [Test]
        public void Equal_TeamIds_Are_Equal()
        {
            TeamId a = new("team_a");
            TeamId b = new("team_a");

            Assert.That(a, Is.EqualTo(b));
            Assert.That(a == b, Is.True);
            Assert.That(a != b, Is.False);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        }

        [Test]
        public void Different_TeamIds_Are_Not_Equal()
        {
            TeamId a = new("team_a");
            TeamId b = new("team_b");

            Assert.That(a, Is.Not.EqualTo(b));
            Assert.That(a == b, Is.False);
            Assert.That(a != b, Is.True);
        }

        [Test]
        public void Equality_Is_Case_Sensitive_Ordinal()
        {
            // Mirrors PlayerId: ordinal comparison so "Team_A" and "team_a" are
            // distinct identifiers, not casefold-equivalent.
            TeamId lower = new("team_a");
            TeamId mixed = new("Team_A");

            Assert.That(lower, Is.Not.EqualTo(mixed));
        }

        [Test]
        public void Constructor_Rejects_Null_Or_Empty()
        {
            Assert.Throws<ArgumentException>(() => new TeamId(null!));
            Assert.Throws<ArgumentException>(() => new TeamId(string.Empty));
        }

        [Test]
        public void ToString_Returns_The_Underlying_Value()
        {
            Assert.That(new TeamId("team:alice").ToString(), Is.EqualTo("team:alice"));
        }
    }
}
