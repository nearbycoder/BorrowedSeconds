using UnityEngine;

namespace BorrowedSeconds.View
{
    /// <summary>
    /// The HUD's size (Settings > HUD size) and the room it takes, shared by the HUD, which scales
    /// its groups, and the camera, which frames the board clear of them. Canvas units: the canvas
    /// is at least 1920x1080 of them. At 100 % this is the layout the game always had.
    /// </summary>
    public static class HudLayout
    {
        public static readonly float[] Sizes = { 1f, 1.25f, 1.5f };
        /// <summary>The level title, clock, tip, key hints, aim tag, tags and the line under the
        /// watch scale by this.</summary>
        public static float Scale = 1f;
        /// <summary>The watch is big already and sits over the middle of the board: it grows a
        /// quarter as much.</summary>
        public static float WatchScale => 1f + (Scale - 1f) * 0.25f;
        /// <summary>The line under the watch grows, so the watch rises by as much.</summary>
        public static float WatchLift => 26f * (Scale - 1f);
        /// <summary>Above 100 % the key hints take two rows, so they stay clear of the watch.</summary>
        public static bool TwoRows => Scale > 1.01f;
        public const float Margin = 56f;
        const float HintRow = 44f;

        /// <summary>The browser's on-screen touch controls in a level (Game.WebBridge), in canvas units: the d-pad
        /// from the bottom-left corner and the buttons from the bottom-right one (width, height), and the pause and
        /// hint buttons from the top-right one. Zero while they aren't showing. The camera frames the board clear of
        /// them, and the tip sits above the d-pad (the key hints give way to the buttons).</summary>
        public static Vector2 TouchLeft, TouchRight, TouchTop;
        public static bool Touch => TouchLeft.y > 0f;

        public static int Step(float scale)
        {
            int k = 0;
            for (int i = 0; i < Sizes.Length; i++) if (Mathf.Abs(Sizes[i] - scale) < Mathf.Abs(Sizes[k] - scale)) k = i;
            return k;
        }

        /// <summary>How far the bottom-left HUD (hints and tip) may reach from the left margin before
        /// it meets the watch.</summary>
        public static float Room(float canvasWidth) => canvasWidth * 0.5f - 150f * WatchScale - Margin;

        /// <summary>Bottom of a key-hint row (row 0 is the lower one).</summary>
        public static float HintY(int row) => 34f + row * HintRow * Scale;

        /// <summary>Bottom of the tip, above the key hints.</summary>
        public static float TipY => Touch ? TouchLeft.y + 16f : HintY(TwoRows ? 2 : 1) + 6f * Scale;

        /// <summary>The tip's text width in its own (scaled) units: 620 at 100 %, narrower above so
        /// the panel ends short of the watch.</summary>
        public static float TipWidth(float canvasWidth) => TwoRows ? Mathf.Min(620f, (Room(canvasWidth) - 40f) / Scale - 40f) : 620f;

        /// <summary>Keep-out boxes from the left edge (x0, y0, x1, y1; y below 0 counts from the top):
        /// the level title, and the key hints with a tip of up to two lines (three above 100 %, where
        /// it wraps narrower).</summary>
        public static Vector4 TitleBox => new Vector4(0f, -170f * Scale, 720f * Scale, 0f);
        public static Vector4 BottomLeftBox(float canvasWidth) => Touch
            ? new Vector4(0f, 0f, Mathf.Max(TouchLeft.x, Mathf.Min(760f * Scale, Margin + Room(canvasWidth))), TipY + (2 * 34f + 28f) * Scale)
            : TwoRows
            ? new Vector4(0f, 0f, Margin + Room(canvasWidth), TipY + (3 * 34f + 28f) * Scale)
            : new Vector4(0f, 0f, 760f, 180f);
        /// <summary>The clock, from the top-right corner.</summary>
        public static Vector4 ClockBox => new Vector4(-420f * Scale, -150f * Scale, 0f, 0f);
        /// <summary>The watch, from the bottom centre up to its bow (it scales about its base, 46 up).</summary>
        public static Vector4 WatchBox => new Vector4(-130f * WatchScale, 0f, 130f * WatchScale, 46f + WatchLift + (285f - 46f) * WatchScale);
    }
}
