using BorrowedSeconds.View;
using UnityEngine;

namespace BorrowedSeconds.UI
{
    /// <summary>
    /// Generated hardware cursors in the game's style: a brass clock-hand arrow for menus and an
    /// ice targeting ring while a loan is aimed at an obstacle.
    /// </summary>
    public static class Cursors
    {
        public enum Kind { None, Arrow, Aim }
        static Texture2D arrow, aim;
        static Kind current = Kind.None;

        public static void Set(Kind k)
        {
            if (k == current) return;
            current = k;
            if (arrow == null) Build();
            if (k == Kind.Aim) Cursor.SetCursor(aim, new Vector2(aim.width * 0.5f, aim.height * 0.5f), CursorMode.Auto);
            else Cursor.SetCursor(arrow, new Vector2(3, 3), CursorMode.Auto);
        }

        /// <summary>Writes the cursor images to PNG (screenshots don't include hardware cursors).</summary>
        public static void Export(string dir)
        {
            if (arrow == null) Build();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "cursor_arrow.png"), arrow.EncodeToPNG());
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "cursor_aim.png"), aim.EncodeToPNG());
        }

        static void Build()
        {
            const int n = 48;
            arrow = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "CursorArrow", filterMode = FilterMode.Bilinear };
            aim = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "CursorAim", filterMode = FilterMode.Bilinear };
            var a = new Color[n * n];
            var b = new Color[n * n];
            Color brass = Palette.Gold, ink = new Color(0.04f, 0.05f, 0.1f, 1f), ice = Palette.Ice;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                // texture rows run bottom-up; cursor space has y pointing down from the hotspot
                float px = x + 0.5f, py = n - 1 - y + 0.5f;
                // arrow: a slim clock-hand pointing up-left from the hotspot at (3,3)
                float dArrow = ArrowSdf(new Vector2(px - 3f, py - 3f));
                float fillA = Mathf.Clamp01(0.5f - dArrow);
                float edgeA = Mathf.Clamp01(0.5f - (dArrow - 1.8f));
                Color c = Color.Lerp(ink, Color.Lerp(brass, Color.white, Mathf.Clamp01((24f - px - py) / 40f) * 0.5f), fillA);
                c.a = edgeA;
                a[y * n + x] = c;
                // aim: ice ring with four ticks and a centre dot
                float dx = px - n * 0.5f, dy = py - n * 0.5f, r = Mathf.Sqrt(dx * dx + dy * dy);
                float ring = Mathf.Clamp01(1.4f - Mathf.Abs(r - 15f));
                float tick = (Mathf.Abs(dx) < 1.3f || Mathf.Abs(dy) < 1.3f) && r > 17f && r < 23f ? 1f : 0f;
                float dot = Mathf.Clamp01(2.6f - r);
                float v = Mathf.Max(ring, Mathf.Max(tick, dot));
                float outline = Mathf.Max(Mathf.Clamp01(2.6f - Mathf.Abs(r - 15f)), (Mathf.Abs(dx) < 2.4f || Mathf.Abs(dy) < 2.4f) && r > 16f && r < 24f ? 1f : 0f);
                outline = Mathf.Max(outline, Mathf.Clamp01(3.8f - r));
                var ca = Color.Lerp(ink, ice, v);
                ca.a = Mathf.Max(v, outline * 0.8f);
                b[y * n + x] = ca;
            }
            arrow.SetPixels(a);
            arrow.Apply();
            aim.SetPixels(b);
            aim.Apply();
        }

        static float ArrowSdf(Vector2 p)
        {
            // triangle head + shaft along the diagonal (down-right from the tip)
            var dir = new Vector2(0.7071f, 0.7071f);
            float along = Vector2.Dot(p, dir);
            float across = Mathf.Abs(p.x * dir.y - p.y * dir.x);
            float head = along < 0 ? -along * 3f + across : Mathf.Max(across - along * 0.45f, along - 18f);
            float shaft = Mathf.Max(across - 2.2f, Mathf.Max(-along + 10f, along - 34f));
            float pivot = Vector2.Distance(p, dir * 30f) - 4.5f;
            return Mathf.Min(Mathf.Min(head, shaft), pivot);
        }
    }
}
