#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Pose.Core.Tests
{
    public class ShuffleScatterTests
    {
        // A double-six set on a field roughly the shape the board gives it.
        private const int Tiles = 28;
        private const int Columns = 5;
        private const float FieldW = 384f;
        private const float FieldH = 648f;
        private const float AngleSpread = 22f;
        private const float Jitter = 0.26f;

        private static ShuffleScatter New() =>
            new(Tiles, Columns, FieldW, FieldH, AngleSpread, Jitter);

        // ---- Spread ---------------------------------------------------------

        [Test]
        public void Every_Tile_Holds_Its_Own_Cell()
        {
            ShuffleScatter s = New();

            for (int cycle = 0; cycle < 8; cycle++)
            {
                HashSet<int> cells = new();
                for (int i = 0; i < Tiles; i++)
                {
                    cells.Add(s.CellOf(i, cycle));
                }

                Assert.That(cells.Count, Is.EqualTo(Tiles), $"cycle {cycle} doubled up a cell");
            }
        }

        [Test]
        public void Cells_Come_From_The_Grid()
        {
            ShuffleScatter s = New();

            Assert.That(s.CellCount, Is.EqualTo(Columns * s.Rows));

            for (int i = 0; i < Tiles; i++)
            {
                Assert.That(s.CellOf(i, 3), Is.InRange(0, s.CellCount - 1));
            }
        }

        [Test]
        public void The_Gaps_In_A_Part_Filled_Row_Move_Around()
        {
            ShuffleScatter s = New();

            Assert.That(s.CellCount, Is.GreaterThan(Tiles), "no gaps to move");

            HashSet<int> first = Occupied(s, 0);
            HashSet<int> later = Occupied(s, 1);

            Assert.That(later, Is.Not.EquivalentTo(first));
        }

        [Test]
        public void No_Tile_Leaves_The_Field()
        {
            ShuffleScatter s = New();

            for (int cycle = 0; cycle < 8; cycle++)
            {
                for (int i = 0; i < Tiles; i++)
                {
                    ScatterPlacement p = s.Placement(i, cycle);

                    Assert.That(Math.Abs(p.X), Is.LessThanOrEqualTo(FieldW * 0.5f));
                    Assert.That(Math.Abs(p.Y), Is.LessThanOrEqualTo(FieldH * 0.5f));
                }
            }
        }

        [Test]
        public void Tiles_Never_Heap_On_One_Another()
        {
            ShuffleScatter s = New();
            float floor = s.MinimumSeparation;

            Assert.That(floor, Is.GreaterThan(0f));

            for (int cycle = 0; cycle < 8; cycle++)
            {
                for (int a = 0; a < Tiles; a++)
                {
                    for (int b = a + 1; b < Tiles; b++)
                    {
                        ScatterPlacement pa = s.Placement(a, cycle);
                        ScatterPlacement pb = s.Placement(b, cycle);
                        double gap = Math.Sqrt(
                            ((pa.X - pb.X) * (pa.X - pb.X)) + ((pa.Y - pb.Y) * (pa.Y - pb.Y)));

                        Assert.That(gap, Is.GreaterThanOrEqualTo(floor - 0.001f),
                            $"tiles {a} and {b} piled up in cycle {cycle}");
                    }
                }
            }
        }

        [Test]
        public void Tiles_Do_Not_Line_Up_In_Rows_Or_Columns()
        {
            ShuffleScatter s = New();

            HashSet<float> xs = new();
            HashSet<float> ys = new();
            for (int i = 0; i < Tiles; i++)
            {
                ScatterPlacement p = s.Placement(i, 0);
                xs.Add(p.X);
                ys.Add(p.Y);
            }

            // A grid would collapse to `Columns` distinct x values and `Rows`
            // distinct y values. Scattered placement shares none.
            Assert.That(xs.Count, Is.EqualTo(Tiles));
            Assert.That(ys.Count, Is.EqualTo(Tiles));
        }

        [Test]
        public void Rows_Are_Staggered_So_Columns_Never_Form()
        {
            ShuffleScatter s = New();

            // Jitter alone can only ever pull two tiles of the same column this
            // far apart. Anything wider is the row stagger doing its job.
            float jitterOnly = 2f * Jitter * s.CellWidth;
            int pulledApart = 0;

            for (int cycle = 0; cycle < 8; cycle++)
            {
                for (int a = 0; a < Tiles; a++)
                {
                    for (int b = a + 1; b < Tiles; b++)
                    {
                        int cellA = s.CellOf(a, cycle);
                        int cellB = s.CellOf(b, cycle);
                        bool sameColumn = (cellA % Columns) == (cellB % Columns);
                        bool sameRow = (cellA / Columns) == (cellB / Columns);
                        if (!sameColumn || sameRow)
                        {
                            continue;
                        }

                        float gap = Math.Abs(s.Placement(a, cycle).X - s.Placement(b, cycle).X);
                        if (gap > jitterOnly)
                        {
                            pulledApart++;
                        }
                    }
                }
            }

            Assert.That(pulledApart, Is.GreaterThan(0));
        }

        // ---- Churn ----------------------------------------------------------

        [Test]
        public void Next_Cycle_Moves_Nearly_Every_Tile()
        {
            ShuffleScatter s = New();

            for (int cycle = 0; cycle < 6; cycle++)
            {
                int moved = 0;
                for (int i = 0; i < Tiles; i++)
                {
                    if (s.CellOf(i, cycle) != s.CellOf(i, cycle + 1))
                    {
                        moved++;
                    }
                }

                Assert.That(moved, Is.GreaterThanOrEqualTo((int)(Tiles * 0.8f)),
                    $"cycle {cycle} to {cycle + 1} barely churned");
            }
        }

        [Test]
        public void Same_Tile_And_Cycle_Always_Gives_The_Same_Place()
        {
            ShuffleScatter s = New();

            ScatterPlacement first = s.Placement(11, 4);
            s.Placement(11, 9);          // move the cache off cycle 4
            ScatterPlacement again = s.Placement(11, 4);

            Assert.That(again.X, Is.EqualTo(first.X));
            Assert.That(again.Y, Is.EqualTo(first.Y));
            Assert.That(again.AngleDegrees, Is.EqualTo(first.AngleDegrees));
        }

        // ---- Patterns -------------------------------------------------------

        [Test]
        public void No_Pattern_Plays_Twice_Running()
        {
            for (int cycle = 1; cycle < 40; cycle++)
            {
                Assert.That(
                    ShuffleScatter.PatternOf(cycle),
                    Is.Not.EqualTo(ShuffleScatter.PatternOf(cycle - 1)),
                    $"cycle {cycle} repeated the previous pattern");
            }
        }

        [Test]
        public void Every_Pattern_Comes_Round()
        {
            HashSet<ShufflePattern> seen = new();
            for (int cycle = 0; cycle < 16; cycle++)
            {
                seen.Add(ShuffleScatter.PatternOf(cycle));
            }

            Assert.That(seen.Count, Is.EqualTo(4));
        }

        [Test]
        public void The_Pattern_For_A_Cycle_Is_Stable()
        {
            Assert.That(ShuffleScatter.PatternOf(7), Is.EqualTo(ShuffleScatter.PatternOf(7)));
        }

        [Test]
        public void Rejects_A_Negative_Cycle_For_A_Pattern()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ShuffleScatter.PatternOf(-1));
        }

        // ---- Tilt -----------------------------------------------------------

        [Test]
        public void Tilt_Stays_Within_The_Spread()
        {
            ShuffleScatter s = New();

            for (int cycle = 0; cycle < 4; cycle++)
            {
                for (int i = 0; i < Tiles; i++)
                {
                    Assert.That(
                        Math.Abs(s.Placement(i, cycle).AngleDegrees),
                        Is.LessThanOrEqualTo(AngleSpread));
                }
            }
        }

        [Test]
        public void No_Tile_Is_Left_Upright()
        {
            ShuffleScatter s = New();

            // An upright tile reads as a laid-out tile, so none may be near it.
            float floor = AngleSpread * 0.34f;

            for (int cycle = 0; cycle < 4; cycle++)
            {
                for (int i = 0; i < Tiles; i++)
                {
                    Assert.That(
                        Math.Abs(s.Placement(i, cycle).AngleDegrees),
                        Is.GreaterThanOrEqualTo(floor),
                        $"tile {i} sat upright in cycle {cycle}");
                }
            }
        }

        [Test]
        public void Tilt_Falls_Both_Ways()
        {
            ShuffleScatter s = New();

            int left = 0;
            int right = 0;
            for (int i = 0; i < Tiles; i++)
            {
                if (s.Placement(i, 0).AngleDegrees < 0f)
                {
                    left++;
                }
                else
                {
                    right++;
                }
            }

            Assert.That(left, Is.GreaterThan(0));
            Assert.That(right, Is.GreaterThan(0));
        }

        // ---- Guards ---------------------------------------------------------

        [Test]
        public void Rejects_An_Empty_Set()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ShuffleScatter(0, Columns, FieldW, FieldH, AngleSpread, Jitter));
        }

        [Test]
        public void Rejects_A_Field_With_No_Columns()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ShuffleScatter(Tiles, 0, FieldW, FieldH, AngleSpread, Jitter));
        }

        [Test]
        public void Rejects_A_Field_With_No_Size()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ShuffleScatter(Tiles, Columns, 0f, FieldH, AngleSpread, Jitter));

            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ShuffleScatter(Tiles, Columns, FieldW, 0f, AngleSpread, Jitter));
        }

        [Test]
        public void Rejects_Jitter_That_Would_Let_Tiles_Heap()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ShuffleScatter(Tiles, Columns, FieldW, FieldH, AngleSpread, 0.5f));

            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ShuffleScatter(Tiles, Columns, FieldW, FieldH, AngleSpread, -0.1f));
        }

        [Test]
        public void Rejects_A_Tile_Outside_The_Set()
        {
            ShuffleScatter s = New();

            Assert.Throws<ArgumentOutOfRangeException>(() => s.Placement(Tiles, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Placement(-1, 0));
        }

        [Test]
        public void Rejects_A_Negative_Cycle()
        {
            ShuffleScatter s = New();

            Assert.Throws<ArgumentOutOfRangeException>(() => s.Placement(0, -1));
        }

        private static HashSet<int> Occupied(ShuffleScatter s, int cycle)
        {
            HashSet<int> cells = new();
            for (int i = 0; i < Tiles; i++)
            {
                cells.Add(s.CellOf(i, cycle));
            }

            return cells;
        }
    }
}
