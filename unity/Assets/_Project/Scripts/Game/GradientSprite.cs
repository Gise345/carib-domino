#nullable enable
using UnityEngine;

namespace Pose.Game
{
    /// <summary>
    /// Builds gradient <see cref="Sprite"/>s at runtime so the lobby can paint
    /// country-colour blocks and cinematic scrims without shipping image assets.
    /// Supports multi-stop gradients in three directions and optional rounded
    /// corners baked into the alpha channel. Sprites are generated once at build
    /// time and reused; keep references rather than regenerating per frame.
    /// </summary>
    public static class GradientSprite
    {
        public enum Direction
        {
            Vertical,   // stops[0] at top → stops[last] at bottom
            Horizontal, // stops[0] at left → stops[last] at right
            Diagonal,   // stops[0] top-left → stops[last] bottom-right
        }

        private const int DefaultSize = 256;

        /// <summary>Top→bottom gradient through the given colour stops.</summary>
        public static Sprite Vertical(params Color[] stops) =>
            Create(Direction.Vertical, 0f, stops);

        /// <summary>Corner-to-corner gradient — reads as a lit, cinematic block.</summary>
        public static Sprite Diagonal(params Color[] stops) =>
            Create(Direction.Diagonal, 0f, stops);

        /// <summary>Diagonal gradient with rounded corners baked into the alpha.</summary>
        public static Sprite RoundedDiagonal(float cornerRadius01, params Color[] stops) =>
            Create(Direction.Diagonal, cornerRadius01, stops);

        /// <summary>
        /// Radial gradient from <paramref name="center"/> out to <paramref name="edge"/>
        /// — used as a cinematic vignette (transparent centre, dark corners). The
        /// inner <paramref name="clearFraction"/> of the radius stays fully at the
        /// centre colour before the fall-off begins.
        /// </summary>
        public static Sprite Radial(Color center, Color edge, float clearFraction = 0.45f)
        {
            int size = DefaultSize;
            Texture2D tex = new(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "RadialGradientSprite",
            };

            Color[] pixels = new Color[size * size];
            float half = size * 0.5f;
            float maxDist = Mathf.Sqrt(2f) * half; // centre → corner
            float clear = Mathf.Clamp01(clearFraction);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) - half;
                    float dy = (y + 0.5f) - half;
                    float d = Mathf.Sqrt((dx * dx) + (dy * dy)) / maxDist;
                    float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(clear, 1f, d));
                    pixels[(y * size) + x] = Color.Lerp(center, edge, t);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(updateMipmaps: false);
            return Sprite.Create(
                tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                pixelsPerUnit: 100f, extrude: 0, meshType: SpriteMeshType.FullRect);
        }

        /// <summary>
        /// A rounded-rect outline with a soft outward bloom and a hollow centre —
        /// the "lit" ring that marks a chosen option.
        ///
        /// Baked rather than layered from two tinted rects because a glow needs a
        /// falloff, and a hard second rectangle behind the first reads as a
        /// mistake. The centre is left fully transparent so whatever the option
        /// sits on shows through.
        ///
        /// 9-sliced: the border is set past the corner radius and the bloom, so
        /// corners stay crisp at any box size while the straight runs stretch.
        /// </summary>
        /// <param name="color">Ring colour; its alpha scales the whole glow.</param>
        /// <param name="cornerFraction">Corner radius, as a fraction of half the sprite.</param>
        /// <param name="glowFraction">Bloom width, as a fraction of half the sprite.</param>
        /// <param name="lineFraction">Crisp ring width, as a fraction of half the sprite.</param>
        public static Sprite RoundedGlow(
            Color color, float cornerFraction = 0.30f, float glowFraction = 0.17f, float lineFraction = 0.035f)
        {
            const int size = 128;
            float half = size * 0.5f;
            float glow = Mathf.Max(1f, glowFraction * half);
            float line = Mathf.Max(1f, lineFraction * half);
            float radius = Mathf.Clamp(cornerFraction * half, 0f, half - glow - 1f);

            // The crisp box is inset by the bloom, so the bloom has room to fall
            // off inside the sprite rather than being clipped at its edge.
            float inset = glow;
            Texture2D tex = new(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "RoundedGlowSprite",
            };

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Signed distance to the rounded rect: negative inside.
                    float px = x + 0.5f;
                    float py = y + 0.5f;
                    float qx = Mathf.Abs(px - half) - ((half - inset) - radius);
                    float qy = Mathf.Abs(py - half) - ((half - inset) - radius);
                    float outside = Mathf.Sqrt(
                        (Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f)) + (Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f)));
                    float d = outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;

                    float a;
                    if (d > 0f)
                    {
                        // Outside the box: bloom, fading to nothing.
                        float t = Mathf.Clamp01(d / glow);
                        a = (1f - t) * (1f - t) * 0.85f;
                    }
                    else if (d > -line)
                    {
                        a = 1f;                                   // the ring itself
                    }
                    else
                    {
                        // Inside: a faint inner lip, then clear.
                        float t = Mathf.Clamp01((-d - line) / (glow * 0.8f));
                        a = (1f - t) * (1f - t) * 0.35f;
                    }

                    pixels[(y * size) + x] = new Color(color.r, color.g, color.b, a * color.a);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(updateMipmaps: false);
            float border = inset + radius + 2f;
            return Sprite.Create(
                tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                pixelsPerUnit: 100f, extrude: 0, meshType: SpriteMeshType.FullRect,
                border: new Vector4(border, border, border, border));
        }

        /// <summary>
        /// Renders a gradient into a fresh texture and wraps it in a sprite.
        /// </summary>
        /// <param name="direction">Axis the gradient runs along.</param>
        /// <param name="cornerRadius01">Corner radius as a fraction of the size
        /// (0 = square). Antialiased into the alpha channel.</param>
        /// <param name="stops">Two or more colour stops, evenly spaced.</param>
        public static Sprite Create(Direction direction, float cornerRadius01, params Color[] stops)
        {
            if (stops == null || stops.Length == 0)
            {
                stops = new[] { Color.magenta };
            }

            int size = DefaultSize;
            Texture2D tex = new(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "GradientSprite",
            };

            float radiusPx = Mathf.Clamp01(cornerRadius01) * size * 0.5f;
            Color[] pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float t = direction switch
                    {
                        Direction.Vertical => 1f - (y / (float)(size - 1)),
                        Direction.Horizontal => x / (float)(size - 1),
                        _ => ((x / (float)(size - 1)) + (1f - (y / (float)(size - 1)))) * 0.5f,
                    };

                    Color c = Sample(stops, t);
                    if (radiusPx > 0f)
                    {
                        c.a *= CornerAlpha(x, y, size, radiusPx);
                    }
                    pixels[(y * size) + x] = c;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(updateMipmaps: false);

            return Sprite.Create(
                tex,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit: 100f,
                extrude: 0,
                meshType: SpriteMeshType.FullRect);
        }

        private static Color Sample(Color[] stops, float t)
        {
            if (stops.Length == 1)
            {
                return stops[0];
            }
            t = Mathf.Clamp01(t);
            float scaled = t * (stops.Length - 1);
            int i = Mathf.Min((int)scaled, stops.Length - 2);
            float frac = scaled - i;
            return Color.Lerp(stops[i], stops[i + 1], frac);
        }

        // Antialiased rounded-corner mask: 1 inside, 0 outside, ~1px feather.
        private static float CornerAlpha(int x, int y, int size, float radiusPx)
        {
            float cx = Mathf.Min(x + 0.5f, size - (x + 0.5f));
            float cy = Mathf.Min(y + 0.5f, size - (y + 0.5f));
            // Only the corner quadrants can be clipped.
            if (cx >= radiusPx || cy >= radiusPx)
            {
                return 1f;
            }
            float dx = radiusPx - cx;
            float dy = radiusPx - cy;
            float dist = Mathf.Sqrt((dx * dx) + (dy * dy));
            return Mathf.Clamp01(radiusPx - dist + 0.5f);
        }
    }
}
