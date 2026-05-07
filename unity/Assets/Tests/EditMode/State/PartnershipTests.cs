#nullable enable
using System;
using NUnit.Framework;

namespace Pose.Core.Tests
{
    public class PartnershipTests
    {
        private static readonly PlayerId Alice = new("alice");
        private static readonly PlayerId Bob = new("bob");
        private static readonly PlayerId Cara = new("cara");
        private static readonly PlayerId Dan = new("dan");

        // ---- CutThroat factory --------------------------------------------

        [Test]
        public void CutThroat_Factory_Creates_One_Solo_Team_Per_Player()
        {
            Partnership p = Partnership.CutThroat(new[] { Alice, Bob, Cara });

            Assert.That(p.Teams.Count, Is.EqualTo(3));
            foreach (Team t in p.Teams)
            {
                Assert.That(t.Members.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public void CutThroat_Factory_Produces_Distinct_Team_Ids()
        {
            Partnership p = Partnership.CutThroat(new[] { Alice, Bob, Cara, Dan });

            // Walking the four teams, every TeamId should be unique. (The constructor
            // also enforces this; this test confirms the *factory* respects the
            // contract before passing into the ctor.)
            System.Collections.Generic.HashSet<TeamId> ids = new();
            foreach (Team t in p.Teams)
            {
                Assert.That(ids.Add(t.Id), Is.True, $"Duplicate TeamId {t.Id} from CutThroat factory.");
            }
        }

        [Test]
        public void CutThroat_Factory_GetTeamOf_Returns_The_Players_Solo_Team()
        {
            Partnership p = Partnership.CutThroat(new[] { Alice, Bob });

            TeamId aliceTeam = p.GetTeamOf(Alice);
            TeamId bobTeam = p.GetTeamOf(Bob);

            Assert.That(aliceTeam, Is.Not.EqualTo(bobTeam));
        }

        [Test]
        public void CutThroat_Factory_Rejects_Empty_Or_Null_Players()
        {
            Assert.Throws<ArgumentNullException>(() => Partnership.CutThroat(null!));
            Assert.Throws<ArgumentException>(() => Partnership.CutThroat(Array.Empty<PlayerId>()));
        }

        [Test]
        public void CutThroat_Factory_Rejects_Duplicate_Players()
        {
            Assert.Throws<ArgumentException>(() =>
                Partnership.CutThroat(new[] { Alice, Bob, Alice }));
        }

        // ---- AlternatingPairs factory --------------------------------------

        [Test]
        public void AlternatingPairs_Pairs_Positions_0_And_2_Versus_1_And_3()
        {
            Partnership p = Partnership.AlternatingPairs(Alice, Bob, Cara, Dan);

            // Alice (pos 0) and Cara (pos 2) share a team; Bob (pos 1) and Dan (pos 3)
            // share the other. Mirrors physical seating where partners sit across.
            Assert.That(p.GetTeamOf(Alice), Is.EqualTo(p.GetTeamOf(Cara)));
            Assert.That(p.GetTeamOf(Bob), Is.EqualTo(p.GetTeamOf(Dan)));
            Assert.That(p.GetTeamOf(Alice), Is.Not.EqualTo(p.GetTeamOf(Bob)));
        }

        [Test]
        public void AlternatingPairs_Builds_Two_Teams_Of_Two()
        {
            Partnership p = Partnership.AlternatingPairs(Alice, Bob, Cara, Dan);

            Assert.That(p.Teams.Count, Is.EqualTo(2));
            foreach (Team t in p.Teams)
            {
                Assert.That(t.Members.Count, Is.EqualTo(2));
            }
        }

        [Test]
        public void AlternatingPairs_Rejects_Duplicate_Players()
        {
            // Any pair of equal arguments — including "partner-of-self" (p1 == p3) —
            // must throw. Set-based detection catches every collision uniformly.
            Assert.Throws<ArgumentException>(() =>
                Partnership.AlternatingPairs(Alice, Bob, Alice, Dan));   // p1 == p3
            Assert.Throws<ArgumentException>(() =>
                Partnership.AlternatingPairs(Alice, Alice, Cara, Dan));  // p1 == p2
            Assert.Throws<ArgumentException>(() =>
                Partnership.AlternatingPairs(Alice, Bob, Cara, Alice));  // p1 == p4
        }

        // ---- Direct construction validation --------------------------------

        [Test]
        public void Constructor_Rejects_Empty_Teams_List()
        {
            Assert.Throws<ArgumentException>(() => new Partnership(Array.Empty<Team>()));
        }

        [Test]
        public void Constructor_Rejects_Player_On_Two_Teams()
        {
            Team t1 = new(new TeamId("team_a"), new[] { Alice, Bob });
            Team t2 = new(new TeamId("team_b"), new[] { Bob, Cara }); // Bob duplicated

            Assert.Throws<ArgumentException>(() => new Partnership(new[] { t1, t2 }));
        }

        [Test]
        public void Constructor_Rejects_Duplicate_Team_Ids()
        {
            Team t1 = new(new TeamId("team_x"), new[] { Alice });
            Team t2 = new(new TeamId("team_x"), new[] { Bob });

            Assert.Throws<ArgumentException>(() => new Partnership(new[] { t1, t2 }));
        }

        // ---- GetTeamOf -----------------------------------------------------

        [Test]
        public void GetTeamOf_Throws_For_Unknown_Player()
        {
            Partnership p = Partnership.CutThroat(new[] { Alice, Bob });
            PlayerId stranger = new("eve");

            Assert.Throws<InvalidOperationException>(() => p.GetTeamOf(stranger));
        }
    }
}
