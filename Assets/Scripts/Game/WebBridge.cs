using System.Runtime.InteropServices;
using UnityEngine;

namespace BorrowedSeconds.Game
{
    /// <summary>
    /// The browser build's link to its page (Assets/Plugins/WebGL/BorrowedSecondsWeb.jslib): the on-screen touch
    /// controls the page draws over the game on phones and tablets, and what the page knows about the device.
    /// Everywhere else (desktop builds, the editor) every call is a no-op and touch is never active.
    /// </summary>
    public static class WebBridge
    {
        /// <summary>The on-screen buttons, one bit each, as the page numbers them.</summary>
        public static class Button
        {
            public const int Up = 1, Right = 2, Down = 4, Left = 8, Borrow = 16, Focus = 32, Rewind = 64, Restart = 128,
                Hint = 256, Pause = 512, Back = 1024, Confirm = 2048, AimNext = 4096;
        }

        /// <summary>Which controls the page shows (BSWeb_SetMode).</summary>
        public enum Mode { None = 0, Menu = 1, Title = 2, Level = 3, Watching = 4, Ledger = 5 }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int BSWeb_Init();
        [DllImport("__Internal")] static extern int BSWeb_Flags();
        [DllImport("__Internal")] static extern int BSWeb_TouchActive();
        [DllImport("__Internal")] static extern int BSWeb_Held();
        [DllImport("__Internal")] static extern int BSWeb_Pressed();
        [DllImport("__Internal")] static extern int BSWeb_TakeTap();
        [DllImport("__Internal")] static extern void BSWeb_SetMode(int mode);
        [DllImport("__Internal")] static extern void BSWeb_OtherInput();
        [DllImport("__Internal")] static extern float BSWeb_Zone(int i);
        static int flags = -1;
        static int Flags => flags >= 0 ? flags : flags = BSWeb_Init();
        public static bool TouchActive => BSWeb_TouchActive() != 0;
        public static int Held => BSWeb_Held();
        public static int Pressed => BSWeb_Pressed();
        static int TakeTapRaw() => BSWeb_TakeTap();
        static Mode mode = (Mode)(-1);
        public static void SetMode(Mode m) { if (m != mode) { mode = m; BSWeb_SetMode((int)m); } }
        public static void OtherInput() => BSWeb_OtherInput();
        /// <summary>The controls' corners as fractions of the page: 0, 1 the d-pad (bottom left), 2, 3 the buttons
        /// (bottom right), 4, 5 pause and hint (top right): width, height.</summary>
        public static float Zone(int i) => BSWeb_Zone(i);
#else
        static int Flags => 0;
        public static bool TouchActive => false;
        public static int Held => 0;
        public static int Pressed => 0;
        static int TakeTapRaw() => -1;
        public static void SetMode(Mode m) { }
        public static void OtherInput() { }
        public static float Zone(int i) => 0f;
#endif

        /// <summary>A phone or tablet: the page's pointer is coarse and there is no fine one (mouse, trackpad).</summary>
        public static bool TouchFirst => (Flags & 1) != 0;
        /// <summary>A phone-sized screen (its short side under 500 CSS pixels).</summary>
        public static bool Phone => (Flags & 2) != 0;
        /// <summary>The previous session in this tab ended without the page closing: the browser most likely
        /// killed it for memory, so this one starts lighter.</summary>
        public static bool Recovered => (Flags & 4) != 0;

        /// <summary>A tap on the game since the last call, in screen pixels (origin bottom left).</summary>
        public static bool TakeTap(out Vector2 pos)
        {
            int t = TakeTapRaw();
            pos = t < 0 ? Vector2.zero : new Vector2(t >> 16, t & 0xFFFF);
            return t >= 0;
        }
    }
}
