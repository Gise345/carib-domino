#nullable enable
using System;
using UnityEngine;

namespace Pose.Game
{
    /// <summary>
    /// Draws flat white UI icons at runtime (house, person, gear, …) so the shell
    /// doesn't depend on an emoji font or imported art. Each icon is a white shape
    /// on transparent — tint it with the <c>Image.color</c>. Same texture-baking
    /// approach as <see cref="GradientSprite"/>; generate once and reuse.
    /// </summary>
    public static class IconFactory
    {
        private const int S = 72;

        public static Sprite House() => Make(b =>
        {
            Tri(b, 10, 38, 62, 38, 36, 66);          // roof
            Rect(b, 18, 8, 54, 40);                   // body
            ClearRect(b, 31, 8, 41, 28);              // door cutout
        });

        public static Sprite Person() => Make(b =>
        {
            Circle(b, 36, 50, 13);                    // head
            Circle(b, 36, 18, 22);                    // shoulders
            ClearRect(b, 0, 0, S, 12);                // flat bottom
        });

        public static Sprite People() => Make(b =>
        {
            Circle(b, 26, 48, 11); Circle(b, 26, 20, 18);
            Circle(b, 48, 50, 11); Circle(b, 48, 22, 18);
            ClearRect(b, 0, 0, S, 12);
        });

        public static Sprite Gear() => Make(b =>
        {
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4d;
                float x = 36 + (float)Math.Cos(a) * 30f;
                float y = 36 + (float)Math.Sin(a) * 30f;
                Circle(b, x, y, 9);
            }
            Circle(b, 36, 36, 24);
            ClearCircle(b, 36, 36, 11);
        });

        public static Sprite Bag() => Make(b =>
        {
            Ring(b, 36, 44, 15, 10);                  // handle
            ClearRect(b, 0, 44, S, S);                // keep only the top arc of the handle
            RoundRect(b, 16, 8, 56, 48, 8);           // bag body
        });

        public static Sprite Film() => Make(b =>
        {
            RoundRect(b, 12, 14, 60, 58, 6);
            ClearRect(b, 22, 22, 50, 50);             // window
            for (int i = 0; i < 3; i++)
            {
                ClearCircle(b, 18, 22 + i * 14, 3);   // left sprockets
                ClearCircle(b, 54, 22 + i * 14, 3);   // right sprockets
            }
        });

        public static Sprite Coin() => Make(b =>
        {
            Circle(b, 36, 36, 28);
            ClearCircle(b, 36, 36, 22);
            Circle(b, 36, 36, 17);
            ClearRect(b, 33, 22, 39, 50);             // rough "1"/bar detail
            Rect(b, 33, 22, 39, 50);
        });

        public static Sprite Trophy() => Make(b =>
        {
            Ring(b, 22, 46, 12, 7); Ring(b, 50, 46, 12, 7);   // handles
            RoundRect(b, 24, 30, 48, 60, 8);          // cup
            Rect(b, 33, 18, 39, 32);                  // stem
            Rect(b, 24, 10, 48, 20);                  // base
        });

        public static Sprite Chart() => Make(b =>
        {
            Rect(b, 14, 12, 26, 36);
            Rect(b, 30, 12, 42, 50);
            Rect(b, 46, 12, 58, 62);
        });

        /// <summary>A chevron: down (selector) or left (back).</summary>
        public static Sprite Chevron(bool down) => Make(b =>
        {
            if (down)
            {
                ThickLine(b, 18, 44, 36, 26, 5f);
                ThickLine(b, 54, 44, 36, 26, 5f);
            }
            else
            {
                ThickLine(b, 44, 18, 26, 36, 5f);
                ThickLine(b, 44, 54, 26, 36, 5f);
            }
        });

        /// <summary>A hamburger menu — three stacked bars.</summary>
        public static Sprite Menu() => Make(b =>
        {
            RoundRect(b, 14, 46, 58, 52, 3);
            RoundRect(b, 14, 33, 58, 39, 3);
            RoundRect(b, 14, 20, 58, 26, 3);
        });

        /// <summary>A speech bubble (chat).</summary>
        /// <summary>Chevron pointing left — the back control.</summary>
        public static Sprite ChevronLeft() => Make(b =>
        {
            ThickLine(b, 46, 12, 28, 36, 5f);
            ThickLine(b, 46, 60, 28, 36, 5f);
        });

        /// <summary>A hollow circle, used as a badge behind another glyph.</summary>
        public static Sprite Ring() => Make(b => Ring(b, S / 2f, S / 2f, (S / 2f) - 2f, (S / 2f) - 7f));

        /// <summary>Envelope — the email sign-in route.</summary>
        public static Sprite Envelope() => Make(b =>
        {
            RoundRect(b, 10, 20, 74, 64, 6);          // body
            ClearRect(b, 15, 25, 69, 59);             // hollow it out
            ThickLine(b, 15, 59, 42, 40, 2.4f);       // flap, left half
            ThickLine(b, 69, 59, 42, 40, 2.4f);       // flap, right half
        });

        public static Sprite Chat() => Make(b =>
        {
            RoundRect(b, 12, 24, 60, 58, 9);          // bubble
            Tri(b, 20, 26, 34, 26, 18, 12);           // tail
            ClearCircle(b, 27, 41, 3);                // three dots
            ClearCircle(b, 36, 41, 3);
            ClearCircle(b, 45, 41, 3);
        });

        /// <summary>A microphone (voice).</summary>
        public static Sprite Mic() => Make(b =>
        {
            RoundRect(b, 30, 30, 42, 60, 6);          // capsule head
            Ring(b, 36, 34, 17, 13);                  // pickup arc
            ClearRect(b, 12, 34, 60, 60);             // keep only lower arc
            Rect(b, 34, 12, 38, 24);                  // stem
            Rect(b, 27, 10, 45, 14);                  // base
        });

        /// <summary>A send arrow (paper-plane-ish triangle).</summary>
        public static Sprite Send() => Make(b =>
        {
            Tri(b, 18, 18, 18, 54, 58, 36);
        });

        /// <summary>A struck-through microphone — muted, or voice unavailable.</summary>
        public static Sprite MicOff() => Make(b =>
        {
            RoundRect(b, 30, 30, 42, 60, 6);          // capsule head
            Ring(b, 36, 34, 17, 13);                  // pickup arc
            ClearRect(b, 12, 34, 60, 60);             // keep only lower arc
            Rect(b, 34, 12, 38, 24);                  // stem
            Rect(b, 27, 10, 45, 14);                  // base
            ThickLine(b, 14, 58, 58, 14, 3.2f);       // the strike
        });

        /// <summary>An X — dismisses a modal.</summary>
        public static Sprite Close() => Make(b =>
        {
            ThickLine(b, 20, 20, 52, 52, 3.4f);
            ThickLine(b, 52, 20, 20, 52, 3.4f);
        });

        /// <summary>A pennant on a pole — report a message.</summary>
        public static Sprite Flag() => Make(b =>
        {
            Rect(b, 18, 8, 23, 62);                   // pole
            Tri(b, 23, 62, 23, 36, 56, 49);           // pennant
        });

        /// <summary>A padlock — a feature an account is needed for.</summary>
        public static Sprite Lock() => Make(b =>
        {
            RoundRect(b, 16, 8, 56, 40, 6);           // body
            Ring(b, 36, 44, 16, 12);                  // shackle
            ClearRect(b, 12, 8, 60, 44);              // keep only the arc above the body
            ClearCircle(b, 36, 26, 5);                // keyhole
        });

        // ---- Baking + primitives ------------------------------------------

        private static Sprite Make(Action<Color[]> draw)
        {
            Color[] buf = new Color[S * S]; // transparent
            draw(buf);
            Texture2D tex = new(S, S, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "Icon",
            };
            tex.SetPixels(buf);
            tex.Apply(updateMipmaps: false);
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        }

        private static void Put(Color[] b, int x, int y, float a)
        {
            if (x < 0 || y < 0 || x >= S || y >= S || a <= 0f)
            {
                return;
            }
            int i = (y * S) + x;
            float na = Mathf.Clamp01(Mathf.Max(b[i].a, a));
            b[i] = new Color(1f, 1f, 1f, na);
        }

        private static void Clear(Color[] b, int x, int y)
        {
            if (x < 0 || y < 0 || x >= S || y >= S)
            {
                return;
            }
            b[(y * S) + x] = new Color(0f, 0f, 0f, 0f);
        }

        private static void Rect(Color[] b, int x0, int y0, int x1, int y1)
        {
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    Put(b, x, y, 1f);
                }
            }
        }

        private static void ClearRect(Color[] b, int x0, int y0, int x1, int y1)
        {
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    Clear(b, x, y);
                }
            }
        }

        private static void RoundRect(Color[] b, int x0, int y0, int x1, int y1, float r)
        {
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    float dx = Mathf.Min(x - x0, x1 - 1 - x);
                    float dy = Mathf.Min(y - y0, y1 - 1 - y);
                    if (dx >= r || dy >= r)
                    {
                        Put(b, x, y, 1f);
                        continue;
                    }
                    float d = Mathf.Sqrt(((r - dx) * (r - dx)) + ((r - dy) * (r - dy)));
                    Put(b, x, y, Mathf.Clamp01(r - d + 0.5f));
                }
            }
        }

        private static void Circle(Color[] b, float cx, float cy, float r)
        {
            for (int y = (int)(cy - r) - 1; y <= cy + r + 1; y++)
            {
                for (int x = (int)(cx - r) - 1; x <= cx + r + 1; x++)
                {
                    float d = Mathf.Sqrt(((x - cx) * (x - cx)) + ((y - cy) * (y - cy)));
                    Put(b, x, y, Mathf.Clamp01(r - d + 0.5f));
                }
            }
        }

        private static void ClearCircle(Color[] b, float cx, float cy, float r)
        {
            for (int y = (int)(cy - r) - 1; y <= cy + r + 1; y++)
            {
                for (int x = (int)(cx - r) - 1; x <= cx + r + 1; x++)
                {
                    if (Mathf.Sqrt(((x - cx) * (x - cx)) + ((y - cy) * (y - cy))) <= r)
                    {
                        Clear(b, x, y);
                    }
                }
            }
        }

        private static void Ring(Color[] b, float cx, float cy, float rOut, float rIn)
        {
            Circle(b, cx, cy, rOut);
            ClearCircle(b, cx, cy, rIn);
        }

        private static void Tri(Color[] b, float ax, float ay, float bx, float by, float cx, float cy)
        {
            int minX = (int)Mathf.Min(ax, bx, cx);
            int maxX = (int)Mathf.Max(ax, bx, cx);
            int minY = (int)Mathf.Min(ay, by, cy);
            int maxY = (int)Mathf.Max(ay, by, cy);
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (InTri(x + 0.5f, y + 0.5f, ax, ay, bx, by, cx, cy))
                    {
                        Put(b, x, y, 1f);
                    }
                }
            }
        }

        private static bool InTri(float px, float py, float ax, float ay, float bx, float by, float cx, float cy)
        {
            float d1 = Sign(px, py, ax, ay, bx, by);
            float d2 = Sign(px, py, bx, by, cx, cy);
            float d3 = Sign(px, py, cx, cy, ax, ay);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0;
            bool pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        private static float Sign(float px, float py, float ax, float ay, float bx, float by) =>
            ((px - bx) * (ay - by)) - ((ax - bx) * (py - by));

        private static void ThickLine(Color[] b, float x0, float y0, float x1, float y1, float half)
        {
            int minX = (int)(Mathf.Min(x0, x1) - half - 1);
            int maxX = (int)(Mathf.Max(x0, x1) + half + 1);
            int minY = (int)(Mathf.Min(y0, y1) - half - 1);
            int maxY = (int)(Mathf.Max(y0, y1) + half + 1);
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (DistToSeg(x + 0.5f, y + 0.5f, x0, y0, x1, y1) <= half)
                    {
                        Put(b, x, y, 1f);
                    }
                }
            }
        }

        private static float DistToSeg(float px, float py, float x0, float y0, float x1, float y1)
        {
            float dx = x1 - x0;
            float dy = y1 - y0;
            float len2 = (dx * dx) + (dy * dy);
            float t = len2 <= 0f ? 0f : Mathf.Clamp01((((px - x0) * dx) + ((py - y0) * dy)) / len2);
            float qx = x0 + (t * dx);
            float qy = y0 + (t * dy);
            return Mathf.Sqrt(((px - qx) * (px - qx)) + ((py - qy) * (py - qy)));
        }
    }
}
