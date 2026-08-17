#nullable enable
using System.Collections.Generic;
using NUnit.Framework;

namespace Pose.Core.Tests
{
    /// <summary>
    /// Geometry invariants for the chain layout walker — the checks that kept
    /// regressing when this lived in a MonoBehaviour. Orientation rules under
    /// test: regular tiles portrait; doubles landscape (crosswise) EXCEPT the
    /// first tile of a column after a bend, which is portrait; and at each bend
    /// the outgoing tile, bridge, and new column's first tile go flush at a
    /// common edge (top at a top bend, bottom at a bottom bend).
    /// </summary>
    public class ChainLayoutTests
    {
        private const float Eps = 0.01f;
        private const float TouchTolerance = 0.5f;

        private static readonly ChainLayout.Config Cramped =
            new(tileSpacing: 2f, virtualHeight: 480f, dropZoneHalfHeight: 70f);

        private static ChainLayout.Config Default => ChainLayout.Config.Default;

        private readonly struct Aabb
        {
            public readonly float MinX, MaxX, MinY, MaxY;

            public Aabb(ChainSlot s)
            {
                MinX = s.CenterX - s.Width / 2f;
                MaxX = s.CenterX + s.Width / 2f;
                MinY = s.CenterY - s.Height / 2f;
                MaxY = s.CenterY + s.Height / 2f;
            }

            public bool OverlapsBeyond(Aabb o, float eps) =>
                MinX < o.MaxX - eps && MaxX > o.MinX + eps &&
                MinY < o.MaxY - eps && MaxY > o.MinY + eps;

            public float GapTo(Aabb o)
            {
                float dx = Max(0f, Max(o.MinX - MaxX, MinX - o.MaxX));
                float dy = Max(0f, Max(o.MinY - MaxY, MinY - o.MaxY));
                return Max(dx, dy);
            }

            private static float Max(float a, float b) => a > b ? a : b;
        }

        // ---- Chain builders ----------------------------------------------

        /// <summary>Alternating regular tiles and doubles: [0|1][1|1][1|2][2|2]…</summary>
        private static Chain AlternatingDoublesChain(int count)
        {
            Chain chain = Chain.Empty.Place(new Tile(0, 1), ChainEnd.Right);
            bool placeDouble = true;
            for (int i = 1; i < count; i++)
            {
                byte open = chain.RightEnd;
                Tile t = placeDouble ? new Tile(open, open) : new Tile(open, (byte)(open + 1));
                chain = chain.Place(t, ChainEnd.Right);
                placeDouble = !placeDouble;
            }
            return chain;
        }

        /// <summary>Doubles-free line — ping-pongs [1|2][2|1]….</summary>
        private static Chain PortraitOnlyChain(int count)
        {
            Chain chain = Chain.Empty.Place(new Tile(0, 1), ChainEnd.Right);
            for (int i = 1; i < count; i++)
            {
                byte open = chain.RightEnd;
                byte other = (byte)(open == 1 ? 2 : 1);
                chain = chain.Place(new Tile(open, other), ChainEnd.Right);
            }
            return chain;
        }

        /// <summary>
        /// Non-double bridges — a bridge is a tile forced landscape at a bend.
        /// Doubles are also landscape, so we exclude them; the tile immediately
        /// after a bridge is the first tile of the new column.
        /// </summary>
        private static List<int> BridgeIndices(Chain chain, ChainSlot[] slots)
        {
            List<int> bridges = new();
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].Landscape && !chain.Tiles[i].Tile.IsDouble)
                {
                    bridges.Add(i);
                }
            }
            return bridges;
        }

        private static void AssertNoOverlap(ChainSlot[] slots)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                for (int j = i + 1; j < slots.Length; j++)
                {
                    Assert.That(
                        new Aabb(slots[i]).OverlapsBeyond(new Aabb(slots[j]), Eps), Is.False,
                        $"tiles {i} and {j} overlap");
                }
            }
        }

        private static void AssertConsecutiveTouch(ChainSlot[] slots, ChainLayout.Config cfg)
        {
            for (int i = 0; i < slots.Length - 1; i++)
            {
                float gap = new Aabb(slots[i]).GapTo(new Aabb(slots[i + 1]));
                Assert.That(
                    gap, Is.LessThanOrEqualTo(cfg.TileSpacing + TouchTolerance),
                    $"tiles {i} and {i + 1} are not connected (gap {gap})");
            }
        }

        /// <summary>
        /// At each bend the outgoing tile and the bridge are flush at a common
        /// horizontal edge — both TOP edges equal (a top bend) or both BOTTOM
        /// edges equal (a bottom bend) — so the U-turn reads cleanly.
        /// </summary>
        private static void AssertBendsFlush(Chain chain, ChainSlot[] slots)
        {
            foreach (int i in BridgeIndices(chain, slots))
            {
                Aabb outgoing = new(slots[i - 1]);
                Aabb bridge = new(slots[i]);

                bool topFlush = Near(outgoing.MinY, bridge.MinY);
                bool bottomFlush = Near(outgoing.MaxY, bridge.MaxY);

                Assert.That(
                    topFlush || bottomFlush, Is.True,
                    $"bend {i}: outgoing tile and bridge are not flush at a common edge");
            }
        }

        /// <summary>
        /// The new column folds back over the bridge rather than stepping past
        /// it: its first tile is centred on the bridge's far half, and sits
        /// clear of the bridge on the side the column is now heading.
        /// </summary>
        private static void AssertColumnTucksUnderBridge(Chain chain, ChainSlot[] slots)
        {
            foreach (int i in BridgeIndices(chain, slots))
            {
                if (i + 1 >= slots.Length)
                {
                    continue;
                }
                Aabb bridge = new(slots[i]);
                ChainSlot firstSlot = slots[i + 1];
                Aabb first = new(firstSlot);

                // Horizontally centred on one half of the bridge.
                float halfOffset = System.Math.Abs(firstSlot.CenterX - slots[i].CenterX);
                Assert.That(
                    halfOffset, Is.EqualTo(ChainLayout.ShortDim / 2f).Within(Eps),
                    $"bend {i}: new column is not centred on the bridge's far half");

                // Horizontally INSIDE the bridge's footprint, not past its edge.
                Assert.That(
                    first.MinX, Is.GreaterThanOrEqualTo(bridge.MinX - Eps),
                    $"bend {i}: new column runs past the bridge's left edge");
                Assert.That(
                    first.MaxX, Is.LessThanOrEqualTo(bridge.MaxX + Eps),
                    $"bend {i}: new column runs past the bridge's right edge");

                // Vertically clear of it, above or below.
                bool above = first.MaxY <= bridge.MinY + Eps;
                bool below = first.MinY >= bridge.MaxY - Eps;
                Assert.That(
                    above || below, Is.True,
                    $"bend {i}: new column's first tile is not stacked clear of the bridge");
            }
        }

        private static bool Near(float a, float b) => System.Math.Abs(a - b) < Eps;

        // ---- Tests --------------------------------------------------------

        [Test]
        public void AlternatingDoubles_Cramped_HoldsInvariants()
        {
            Chain chain = AlternatingDoublesChain(24);
            ChainSlot[] slots = ChainLayout.Compute(chain, 0, Cramped).Slots;

            AssertNoOverlap(slots);
            AssertConsecutiveTouch(slots, Cramped);
            AssertBendsFlush(chain, slots);
            AssertColumnTucksUnderBridge(chain, slots);
        }

        [Test]
        public void PortraitOnly_Cramped_HoldsInvariants()
        {
            Chain chain = PortraitOnlyChain(24);
            ChainSlot[] slots = ChainLayout.Compute(chain, 0, Cramped).Slots;

            AssertNoOverlap(slots);
            AssertConsecutiveTouch(slots, Cramped);
            AssertBendsFlush(chain, slots);
            AssertColumnTucksUnderBridge(chain, slots);
        }

        [Test]
        public void DefaultConfig_FullLengthChains_HoldInvariants()
        {
            foreach (Chain chain in new[] { AlternatingDoublesChain(28), PortraitOnlyChain(28) })
            {
                ChainSlot[] slots = ChainLayout.Compute(chain, 0, Default).Slots;
                AssertNoOverlap(slots);
                AssertConsecutiveTouch(slots, Default);
                AssertBendsFlush(chain, slots);
                AssertColumnTucksUnderBridge(chain, slots);
            AssertColumnTucksUnderBridge(chain, slots);
            }
        }

        [Test]
        public void Doubles_LieLandscape_ExceptFirstTileOfAColumn()
        {
            Chain chain = AlternatingDoublesChain(24);
            ChainSlot[] slots = ChainLayout.Compute(chain, 0, Cramped).Slots;

            HashSet<int> firstOfColumn = new();
            foreach (int b in BridgeIndices(chain, slots))
            {
                firstOfColumn.Add(b + 1); // tile after a bridge starts the new column
            }

            bool sawMidDouble = false;
            bool sawFirstOfColumnDouble = false;
            for (int i = 0; i < slots.Length; i++)
            {
                if (!chain.Tiles[i].Tile.IsDouble)
                {
                    continue;
                }
                if (firstOfColumn.Contains(i))
                {
                    sawFirstOfColumnDouble = true;
                    Assert.That(slots[i].Landscape, Is.False, $"first-of-column double {i} should be portrait");
                }
                else
                {
                    sawMidDouble = true;
                    Assert.That(slots[i].Landscape, Is.True, $"in-run double {i} should be landscape");
                }
            }

            Assert.That(sawMidDouble, Is.True, "test saw no in-run double to check");
            Assert.That(sawFirstOfColumnDouble, Is.True, "test saw no first-of-column double to check");
        }

        [Test]
        public void OpeningDouble_LiesLandscape()
        {
            // The opening is column 1's first tile (not a post-bend column), so a
            // double opener lies crosswise.
            Chain chain = Chain.Empty.Place(new Tile(6, 6), ChainEnd.Right);
            chain = chain.Place(new Tile(6, 5), ChainEnd.Right);
            ChainSlot[] slots = ChainLayout.Compute(chain, 0, Default).Slots;

            Assert.That(slots[0].Landscape, Is.True, "opening double should lie landscape");
        }

        [Test]
        public void Compute_OpeningAnchoredAtVerticalCenter()
        {
            Chain chain = AlternatingDoublesChain(6);
            ChainSlot[] slots = ChainLayout.Compute(chain, 0, Default).Slots;

            Assert.That(slots[0].CenterY, Is.EqualTo(Default.VirtualHeight / 2f).Within(Eps));
            Assert.That(slots[0].CenterX, Is.EqualTo(0f).Within(Eps));
        }
    }
}
