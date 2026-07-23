#nullable enable

namespace Pose.Core
{
    /// <summary>
    /// One tile's computed placement in the rendered chain, in logical layout
    /// units (Unity-free so the geometry can be unit-tested). The origin is the
    /// top-center of the chain area; <see cref="CenterX"/> grows rightward and
    /// <see cref="CenterY"/> grows <em>downward</em>. A renderer maps these to
    /// its own coordinate space.
    ///
    /// A tile is <see cref="Landscape"/> when it is rendered rotated across the
    /// chain direction — that is true for doubles and for the "bridge" tile at a
    /// bend. Portrait tiles are <see cref="ChainLayout.ShortDim"/> wide by
    /// <see cref="ChainLayout.LongDim"/> tall; landscape tiles are the transpose.
    ///
    /// <see cref="FirstPip"/> / <see cref="SecondPip"/> are the pip values to
    /// draw on the tile's two halves in reading order (top→bottom for portrait,
    /// left→right for landscape), already resolved for the tile's orientation at
    /// this point in the chain.
    /// </summary>
    public readonly struct ChainSlot
    {
        public readonly float CenterX;
        public readonly float CenterY;
        public readonly bool Landscape;
        public readonly byte FirstPip;
        public readonly byte SecondPip;

        public ChainSlot(float centerX, float centerY, bool landscape, byte firstPip, byte secondPip)
        {
            CenterX = centerX;
            CenterY = centerY;
            Landscape = landscape;
            FirstPip = firstPip;
            SecondPip = secondPip;
        }

        /// <summary>Rendered width in layout units.</summary>
        public float Width => Landscape ? ChainLayout.LongDim : ChainLayout.ShortDim;

        /// <summary>Rendered height in layout units.</summary>
        public float Height => Landscape ? ChainLayout.ShortDim : ChainLayout.LongDim;
    }
}
