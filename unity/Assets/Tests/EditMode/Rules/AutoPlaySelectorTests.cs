#nullable enable
using System;
using NUnit.Framework;

namespace Pose.Core.Tests
{
    public class AutoPlaySelectorTests
    {
        private static readonly PlayerId Alice = new("alice");

        private static PlaceMove Place(byte a, byte b, ChainEnd end = ChainEnd.Left)
        {
            return new PlaceMove(Alice, new Tile(a, b), end);
        }

        // ---- Doubles outrank everything -------------------------------------

        [Test]
        public void Plays_A_Double_Ahead_Of_A_Heavier_Non_Double()
        {
            // 6-5 is the heavier tile (11 pips vs 4), but the double goes first.
            PlaceMove doubleTwo = Place(2, 2);
            Move[] legal = { Place(6, 5), doubleTwo };

            Move picked = AutoPlaySelector.Pick(legal);

            Assert.That(picked, Is.SameAs(doubleTwo));
        }

        [Test]
        public void Plays_The_Heaviest_Double_When_Several_Are_Legal()
        {
            PlaceMove doubleSix = Place(6, 6);
            Move[] legal = { Place(1, 1), doubleSix, Place(4, 4) };

            Move picked = AutoPlaySelector.Pick(legal);

            Assert.That(picked, Is.SameAs(doubleSix));
        }

        // ---- Weight, then higher single pip ---------------------------------

        [Test]
        public void Plays_The_Highest_Pip_Total_When_No_Double_Is_Legal()
        {
            PlaceMove sixFive = Place(6, 5);
            Move[] legal = { Place(6, 1), sixFive, Place(3, 2) };

            Move picked = AutoPlaySelector.Pick(legal);

            Assert.That(picked, Is.SameAs(sixFive));
        }

        [Test]
        public void Breaks_An_Equal_Weight_Tie_On_The_Higher_Single_Pip()
        {
            // Both total 8; 6-2 carries the higher single pip, so it goes down.
            PlaceMove sixTwo = Place(6, 2);
            Move[] legal = { Place(5, 3), sixTwo, Place(4, 4, ChainEnd.Right) };

            // 4-4 is a double, so it outranks both regardless of the tie rule.
            Assert.That(
                ((PlaceMove)AutoPlaySelector.Pick(legal)).Tile,
                Is.EqualTo(new Tile(4, 4)));

            Move picked = AutoPlaySelector.Pick(new Move[] { Place(5, 3), sixTwo });

            Assert.That(picked, Is.SameAs(sixTwo));
        }

        // ---- Determinism -----------------------------------------------------

        [Test]
        public void Same_Legal_List_Always_Yields_The_Same_Move()
        {
            Move[] legal = { Place(6, 5), Place(3, 3), Place(6, 1), Place(2, 2) };

            Move first = AutoPlaySelector.Pick(legal);
            Move second = AutoPlaySelector.Pick(legal);
            Move third = AutoPlaySelector.Pick(legal);

            Assert.That(second, Is.SameAs(first));
            Assert.That(third, Is.SameAs(first));
        }

        [Test]
        public void Same_Tile_On_Both_Ends_Settles_On_Left()
        {
            // A tile playable at either end must resolve to one deterministic
            // end, whichever order the engine enumerated them in.
            Move[] rightFirst = { Place(6, 4, ChainEnd.Right), Place(6, 4, ChainEnd.Left) };
            Move[] leftFirst = { Place(6, 4, ChainEnd.Left), Place(6, 4, ChainEnd.Right) };

            Move fromRightFirst = AutoPlaySelector.Pick(rightFirst);
            Move fromLeftFirst = AutoPlaySelector.Pick(leftFirst);

            Assert.That(((PlaceMove)fromRightFirst).End, Is.EqualTo(ChainEnd.Left));
            Assert.That(((PlaceMove)fromLeftFirst).End, Is.EqualTo(ChainEnd.Left));
        }

        // ---- Pass fallback ---------------------------------------------------

        [Test]
        public void Passes_When_The_Pass_Is_The_Only_Legal_Move()
        {
            PassMove pass = new(Alice);

            Move picked = AutoPlaySelector.Pick(new Move[] { pass });

            Assert.That(picked, Is.SameAs(pass));
        }

        [Test]
        public void Prefers_Any_Placement_Over_An_Available_Pass()
        {
            PlaceMove place = Place(0, 1);
            Move[] legal = { new PassMove(Alice), place };

            Move picked = AutoPlaySelector.Pick(legal);

            Assert.That(picked, Is.SameAs(place));
        }

        // ---- A timeout must never resign -------------------------------------

        [Test]
        public void Never_Resigns_Even_When_Resigning_Is_Legal()
        {
            // Resigning forfeits the player's staked coins (ADR 0016). Running
            // out of time must never cost someone their stake.
            PlaceMove place = Place(0, 1);
            Move[] legal = { new ResignMove(Alice), place };

            Move picked = AutoPlaySelector.Pick(legal);

            Assert.That(picked, Is.SameAs(place));
        }

        [Test]
        public void Throws_When_Resigning_Is_The_Only_Legal_Move()
        {
            Move[] legal = { new ResignMove(Alice) };

            Assert.That(
                () => AutoPlaySelector.Pick(legal),
                Throws.TypeOf<ArgumentException>());
        }

        // ---- Guard rails -----------------------------------------------------

        [Test]
        public void Throws_On_An_Empty_Legal_List()
        {
            Assert.That(
                () => AutoPlaySelector.Pick(Array.Empty<Move>()),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void Throws_On_A_Null_Legal_List()
        {
            Assert.That(
                () => AutoPlaySelector.Pick(null!),
                Throws.TypeOf<ArgumentNullException>());
        }
    }
}
