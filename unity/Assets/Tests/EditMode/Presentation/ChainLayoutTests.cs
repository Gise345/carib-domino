#nullable enable
using System.Collections.Generic;
using NUnit.Framework;

namespace Pose.Core.Tests
{
    /// <summary>
    /// Geometry invariants for the chain layout walker. These are the checks
    /// that kept regressing while the walker lived in a MonoBehaviour and could
    /// only be eyeballed on a device: a bend must connect whether the outgoing
    /// column ended on a portrait tile (half-width 30) or a landscape double
    /// (half-width 60), and no two tiles may overlap. A doubles-free chain lands
    /// every bend on a portrait tile; the alternating-doubles chain lands them
    /// on doubles — so exercising both covers both corner cases.
    /// </summary>
    public class ChainLayoutTests
    {
        private const float Eps = 0.01f;
        private const float TouchTolerance = 0.5f;

        // Force several bends without needing 14-tile columns.
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

            /// <summary>Nearest-edge gap between two AABBs; 0 if they touch or overlap.</summary>
            public float GapTo(Aabb o)
            {
                float dx = Max(0f, Max(o.MinX - MaxX, MinX - o.MaxX));
                float dy = Max(0f, Max(o.MinY - MaxY, MinY - o.MaxY));
                return Max(dx, dy);
            }

            private static float Max(float a, float b) => a > b ? a : b;
        }

        // ---- Chain builders ----------------------------------------------

        /// <summary>
        /// Right-extending line alternating regular tiles and doubles —
        /// [0|1][1|1][1|2][2|2]… — so the snake wraps with doubles at the column
        /// ends. Opening is index 0. Chain.Place only checks pip matching, not
        /// set uniqueness, so reused tiles are fine for geometry.
        /// </summary>
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

        /// <summary>
        /// Right-extending line with no doubles — ping-pongs [1|2][2|1]… — so
        /// every bend lands on a portrait tile.
        /// </summary>
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

        private static List<int> BridgeIndices(Chain chain, ChainSlot[] slots)
        {
            List<int> bridges = new();
            for (int i = 0; i < slots.Length; i++)
            {
                // A bridge is a non-double rendered landscape (a forced elbow).
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

        private static void AssertBridgesAligned(Chain chain, ChainSlot[] slots)
        {
            // Each bridge must sit at the SAME center-Y as the outgoing column's
            // last tile (the tile immediately before it), regardless of that
            // tile's orientation — the "double at a bend doesn't line up" bug.
            foreach (int i in BridgeIndices(chain, slots))
            {
                Assert.That(
                    slots[i].CenterY, Is.EqualTo(slots[i - 1].CenterY).Within(Eps),
                    $"bridge {i} not aligned with outgoing tile {i - 1}");
            }
        }

        // ---- Tests --------------------------------------------------------

        [Test]
        public void AlternatingDoubles_Cramped_HoldsInvariants()
        {
            Chain chain = AlternatingDoublesChain(24);
            ChainSlot[] slots = ChainLayout.Compute(chain, 0, Cramped).Slots;

            AssertNoOverlap(slots);
            AssertConsecutiveTouch(slots, Cramped);
            AssertBridgesAligned(chain, slots);
        }

        [Test]
        public void PortraitOnly_Cramped_HoldsInvariants()
        {
            Chain chain = PortraitOnlyChain(24);
            ChainSlot[] slots = ChainLayout.Compute(chain, 0, Cramped).Slots;

            AssertNoOverlap(slots);
            AssertConsecutiveTouch(slots, Cramped);
            AssertBridgesAligned(chain, slots);
        }

        [Test]
        public void DefaultConfig_FullLengthChains_HoldInvariants()
        {
            foreach (Chain chain in new[] { AlternatingDoublesChain(28), PortraitOnlyChain(28) })
            {
                ChainSlot[] slots = ChainLayout.Compute(chain, 0, Default).Slots;
                AssertNoOverlap(slots);
                AssertConsecutiveTouch(slots, Default);
                AssertBridgesAligned(chain, slots);
            }
        }

        [Test]
        public void PortraitOnlyChain_ProducesPortraitEndedBends()
        {
            // Guards AssertBridgesAligned above from passing vacuously for the
            // portrait case.
            Chain chain = PortraitOnlyChain(24);
            ChainSlot[] slots = ChainLayout.Compute(chain, 0, Cramped).Slots;

            List<int> bridges = BridgeIndices(chain, slots);
            Assert.That(bridges, Is.Not.Empty, "no bends were produced");
            foreach (int i in bridges)
            {
                Assert.That(
                    slots[i - 1].Landscape, Is.False,
                    $"expected a portrait tile before bridge {i}");
            }
        }

        [Test]
        public void AlternatingDoublesChain_ProducesDoubleEndedBends()
        {
            // Guards the double case — the misalignment regression lived here.
            Chain chain = AlternatingDoublesChain(24);
            ChainSlot[] slots = ChainLayout.Compute(chain, 0, Cramped).Slots;

            List<int> bridges = BridgeIndices(chain, slots);
            Assert.That(bridges, Is.Not.Empty, "no bends were produced");

            bool anyDoubleEnded = false;
            foreach (int i in bridges)
            {
                if (slots[i - 1].Landscape)
                {
                    anyDoubleEnded = true;
                }
            }
            Assert.That(anyDoubleEnded, Is.True, "no bend landed on a double");
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
