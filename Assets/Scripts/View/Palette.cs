using UnityEngine;

namespace BorrowedSeconds.View
{
    /// <summary>The art-direction palette (docs/PLAN.md section 8).</summary>
    public static class Palette
    {
        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        public static readonly Color VoidTop = Hex("#1C2142");
        public static readonly Color VoidBottom = Hex("#0D1020");
        public static readonly Color FloorA = Hex("#ECE6DA");
        public static readonly Color FloorB = Hex("#E1D9CB");
        public static readonly Color FloorSide = Hex("#B9AF9F");
        public static readonly Color Wall = Hex("#323A5C");
        public static readonly Color WallTop = Hex("#4A5582");
        public static readonly Color Plinth = Hex("#232845");
        public static readonly Color PlinthLit = Hex("#3A4373"); // the plinth's top edge; it fades to dark below
        public static readonly Color PlinthGlow = Hex("#232A52");
        public static readonly Color Brass = Hex("#C9A15A");
        public static readonly Color Porcelain = Hex("#F4EFE6");
        public static readonly Color Amber = Hex("#FFB547");
        public static readonly Color Coral = Hex("#FF4F64");
        public static readonly Color Graphite = Hex("#20243A");
        public static readonly Color Ice = Hex("#7CF4FF");
        public static readonly Color IceDeep = Hex("#2E9BC4");
        public static readonly Color Mint = Hex("#55E0AE");
        public static readonly Color Gold = Hex("#FFD27A");
        public static readonly Color Danger = Hex("#FF3048");
        public static readonly Color Ink = Hex("#151A33");
        public static readonly Color Paper = Hex("#F6F1E7");

        // plate/gate channels a-f: mint first, so single-channel levels keep their look; each
        // channel also carries channel+1 pips, so pairs read without colour
        static readonly Color[] channels = { Mint, Hex("#B58CFF"), Hex("#F2E66B"), Hex("#FF8FC8"), Hex("#FFA25C"), Hex("#9FE36A") };
        public static Color Channel(int c) => channels[Mathf.Clamp(c, 0, channels.Length - 1)];
    }
}
