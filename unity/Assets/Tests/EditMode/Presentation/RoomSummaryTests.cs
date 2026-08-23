#nullable enable
using NUnit.Framework;

namespace Pose.Core.Tests
{
    /// <summary>
    /// A room screen makes promises about money and length. These pin the ones
    /// players will notice if they are wrong: the pot moves with table size,
    /// Partner splits, and the series length and clock are read from the rules
    /// rather than restated.
    /// </summary>
    public class RoomSummaryTests
    {
        [Test]
        public void CutThroat_ThreeHandClassic_StatesTheTableAndThePot()
        {
            RoomSummary s = RoomSummary.For(GameMode.CutThroat, 3, MatchFormat.ClassicSixLove);

            Assert.That(s.Seats, Is.EqualTo(3));
            Assert.That(s.Entry, Is.EqualTo(1000));
            Assert.That(s.Pot, Is.EqualTo(3000));
            Assert.That(s.WinningSideTakes, Is.EqualTo(3000));
            Assert.That(s.ShareEach, Is.EqualTo(3000), "one winner takes it all");
            Assert.That(s.KeyBonus, Is.EqualTo(2000));
            Assert.That(s.Loves, Is.EqualTo(6));
            Assert.That(s.TurnSeconds, Is.EqualTo(30));
            Assert.That(s.IsTeamGame, Is.False);
        }

        [Test]
        public void CutThroat_PotGrowsWithTheTable([Values(2, 3, 4)] int seats)
        {
            RoomSummary s = RoomSummary.For(GameMode.CutThroat, seats, MatchFormat.ClassicSixLove);

            Assert.That(s.Pot, Is.EqualTo(1000 * seats));
            Assert.That(s.ShareEach, Is.EqualTo(1000 * seats));
        }

        [Test]
        public void QuickLove_IsThreeLoves_NotSix()
        {
            RoomSummary quick = RoomSummary.For(GameMode.CutThroat, 2, MatchFormat.QuickLove);
            RoomSummary classic = RoomSummary.For(GameMode.CutThroat, 2, MatchFormat.ClassicSixLove);

            Assert.That(quick.Loves, Is.EqualTo(3));
            Assert.That(classic.Loves, Is.EqualTo(6));
        }

        [Test]
        public void Format_DoesNotChangeTheMoney()
        {
            RoomSummary quick = RoomSummary.For(GameMode.CutThroat, 4, MatchFormat.QuickLove);
            RoomSummary classic = RoomSummary.For(GameMode.CutThroat, 4, MatchFormat.ClassicSixLove);

            Assert.That(quick.Pot, Is.EqualTo(classic.Pot));
            Assert.That(quick.KeyBonus, Is.EqualTo(classic.KeyBonus));
        }

        [Test]
        public void Partner_IsAlwaysFourSeats_WhateverIsAskedFor([Values(2, 3, 4)] int asked)
        {
            RoomSummary s = RoomSummary.For(GameMode.Partner, asked, MatchFormat.ClassicSixLove);

            Assert.That(s.Seats, Is.EqualTo(4));
            Assert.That(s.Pot, Is.EqualTo(4000));
        }

        [Test]
        public void Partner_TeamTakesThePot_AndSplitsIt()
        {
            RoomSummary s = RoomSummary.For(GameMode.Partner, 4, MatchFormat.ClassicSixLove);

            Assert.That(s.Winners, Is.EqualTo(2));
            Assert.That(s.WinningSideTakes, Is.EqualTo(4000), "the team's take");
            Assert.That(s.ShareEach, Is.EqualTo(2000), "each partner's half");
            Assert.That(s.IsTeamGame, Is.True);
        }

        [Test]
        public void QuickPartner_IsThreeLoves_AtTheSameStake()
        {
            RoomSummary quick = RoomSummary.For(GameMode.Partner, 4, MatchFormat.QuickLove);

            Assert.That(quick.Loves, Is.EqualTo(3));
            Assert.That(quick.WinningSideTakes, Is.EqualTo(4000));
            Assert.That(quick.ShareEach, Is.EqualTo(2000));
        }

        [Test]
        public void Loves_ComeFromTheRules_NotFromThisClass([Values(
            MatchFormat.ClassicSixLove, MatchFormat.QuickLove)] MatchFormat format)
        {
            RoomSummary s = RoomSummary.For(GameMode.CutThroat, 2, format);

            Assert.That(s.Loves, Is.EqualTo(MatchFormatRules.For(format).Loves));
        }

        [Test]
        public void TurnClock_ComesFromTheTurnTimer()
        {
            RoomSummary s = RoomSummary.For(GameMode.CutThroat, 2, MatchFormat.ClassicSixLove);

            Assert.That(s.TurnSeconds, Is.EqualTo((int)TurnTimer.ExpireAfterSeconds));
        }
    }
}
