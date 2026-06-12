#nullable enable
using NUnit.Framework;

namespace Pose.Core.Tests
{
    /// <summary>
    /// Resign semantics added in M3.5 — a player can forfeit at any time during
    /// the round; the round ends and the non-resigner wins with score equal to
    /// the resigner's remaining pip total. These tests pin down the rule edges
    /// so the network layer (which encodes resign as a special move kind) can
    /// rely on them.
    /// </summary>
    public class CutThroatRulesResignTests
    {
        private static readonly PlayerId Alice = new("alice");
        private static readonly PlayerId Bob = new("bob");

        private static MatchState DealFresh(ulong seed = 0xDEADBEEFUL)
        {
            return Dealer.Deal(
                DealConfig.CutThroatDoubleSix(2),
                new[] { Alice, Bob },
                Partnership.CutThroat(new[] { Alice, Bob }),
                new SeededRandomSource(seed));
        }

        [Test]
        public void Resign_IsLegal_FromCurrentPlayer()
        {
            CutThroatRules rules = new();
            MatchState s = DealFresh();
            ResignMove resign = new(s.CurrentPlayer);

            Assert.That(rules.IsLegal(s, resign), Is.True);
        }

        [Test]
        public void Resign_IsLegal_FromNonCurrentPlayer()
        {
            // Unlike Place/Pass, resign is legal from a participant who isn't
            // currently on-turn. This is intentional: a player should be able
            // to forfeit while their opponent is still thinking.
            CutThroatRules rules = new();
            MatchState s = DealFresh();
            PlayerId nonCurrent = s.CurrentPlayer == Alice ? Bob : Alice;
            ResignMove resign = new(nonCurrent);

            Assert.That(rules.IsLegal(s, resign), Is.True);
        }

        [Test]
        public void Resign_IsLegal_RejectsUnknownPlayer()
        {
            CutThroatRules rules = new();
            MatchState s = DealFresh();
            ResignMove resign = new(new PlayerId("not-a-participant"));

            Assert.That(rules.IsLegal(s, resign), Is.False);
        }

        [Test]
        public void Resign_EndsRound_AndAppendsToHistory()
        {
            CutThroatRules rules = new();
            MatchState s = DealFresh();
            int historyBefore = s.History.Count;

            MatchState after = rules.Apply(s, new ResignMove(s.CurrentPlayer));

            Assert.That(after.IsOver, Is.True);
            Assert.That(after.History.Count, Is.EqualTo(historyBefore + 1));
            Assert.That(after.History[after.History.Count - 1], Is.InstanceOf<ResignMove>());
        }

        [Test]
        public void Outcome_AfterResign_NonResignerWins_ScoreEqualsResignerPips()
        {
            CutThroatRules rules = new();
            MatchState s = DealFresh();
            PlayerId resigner = s.CurrentPlayer;
            PlayerId other = resigner == Alice ? Bob : Alice;
            int resignerPips = s.Hands[resigner].PipTotal;

            MatchState after = rules.Apply(s, new ResignMove(resigner));
            MatchOutcome? outcome = rules.GetOutcome(after);

            Assert.That(outcome, Is.Not.Null);
            Assert.That(outcome!.Reason, Is.EqualTo(MatchEndReason.Resigned));
            Assert.That(outcome.WinnerId, Is.EqualTo(other));
            Assert.That(outcome.WinnerScore, Is.EqualTo(resignerPips));
            Assert.That(outcome.IsDraw, Is.False);
        }

        [Test]
        public void Resign_Apply_RejectsWhenRoundAlreadyOver()
        {
            // Defensive: after the round ends, no further moves should land —
            // including a second resign. CutThroatRules.Apply checks IsOver up
            // front and throws.
            CutThroatRules rules = new();
            MatchState s = DealFresh();
            MatchState ended = rules.Apply(s, new ResignMove(s.CurrentPlayer));

            Assert.Throws<System.InvalidOperationException>(() =>
                rules.Apply(ended, new ResignMove(s.CurrentPlayer)));
        }
    }
}
