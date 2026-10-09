using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BorrowedSeconds.Game
{
    /// <summary>
    /// Settings > Display and Render resolution. A window gets a real size instead of keeping the
    /// full-screen resolution (which made the "window" as big as the screen), and the 3D scene can
    /// render at a fraction of the screen for weaker GPUs; menus and the HUD stay at full resolution.
    /// </summary>
    public static class DisplayOptions
    {
        /// <summary>The browser build: a page can't be resized or quit, and fullscreen is the
        /// browser's, granted only from a click or key press (Tools/build-pages.sh).</summary>
#if UNITY_WEBGL && !UNITY_EDITOR
        public static readonly bool Web = true;
#else
        public static readonly bool Web = false;
#endif

        /// <summary>Window sizes on offer (16:9); only those that fit the desktop are listed.</summary>
        static readonly Vector2Int[] Windows =
        {
            new Vector2Int(1280, 720), new Vector2Int(1600, 900), new Vector2Int(1920, 1080),
            new Vector2Int(2560, 1440), new Vector2Int(3200, 1800),
        };

        public static readonly float[] RenderScales = { 0.5f, 0.7f, 0.85f, 1f };

        public static Vector2Int Desktop
        {
            get
            {
                var r = Screen.currentResolution;
                return new Vector2Int(Mathf.Max(r.width, 640), Mathf.Max(r.height, 360));
            }
        }

        /// <summary>Window sizes that fit <paramref name="desktop"/> with room for the title bar and panels.</summary>
        public static List<Vector2Int> Fitting(Vector2Int desktop)
        {
            var list = new List<Vector2Int>();
            foreach (var w in Windows)
                if (w.x <= desktop.x * 0.95f && w.y <= desktop.y * 0.9f) list.Add(w);
            if (list.Count == 0) list.Add(Windows[0]);
            return list;
        }

        /// <summary>The saved window size if it still fits, else the largest that is at most 3/4 of the desktop width.</summary>
        public static Vector2Int WindowSize(SaveData save, Vector2Int desktop)
        {
            var fit = Fitting(desktop);
            foreach (var w in fit) if (w.x == save.windowW && w.y == save.windowH) return w;
            var pick = fit[0];
            foreach (var w in fit) if (w.x <= desktop.x * 0.75f) pick = w;
            return pick;
        }

        /// <summary>The Display row's choices: 0 is fullscreen, then each window size that fits.</summary>
        public static int Choice(SaveData save, Vector2Int desktop)
        {
            if (Web) return Screen.fullScreen ? 0 : 1;
            if (save.fullscreen) return 0;
            return Fitting(desktop).IndexOf(WindowSize(save, desktop)) + 1;
        }

        public static void Choose(SaveData save, Vector2Int desktop, int choice)
        {
            // in a browser the choices are fullscreen and the page; the save keeps the last choice
            // but a reload always starts in the page (fullscreen needs a click or key press)
            if (Web) { save.fullscreen = choice % 2 == 0; return; }
            var fit = Fitting(desktop);
            choice = Mathf.Clamp(choice, 0, fit.Count);
            save.fullscreen = choice == 0;
            if (choice > 0) { save.windowW = fit[choice - 1].x; save.windowH = fit[choice - 1].y; }
        }

        public static string Label(SaveData save, Vector2Int desktop)
        {
            if (Web) return Screen.fullScreen ? "Fullscreen" : "Browser window"; // Esc leaves fullscreen without asking the game
            if (save.fullscreen) return "Fullscreen";
            var w = WindowSize(save, desktop);
            return $"Window {w.x}×{w.y}";
        }

        /// <summary>
        /// Puts the window in the saved mode. <paramref name="chosen"/>: the player just picked a size,
        /// so apply it even if already windowed; otherwise a window keeps whatever size it has (the
        /// player may have dragged it), and only leaving fullscreen sets the saved size.
        /// </summary>
        public static void Apply(SaveData save, bool chosen)
        {
            if (Web)
            {
                // the page fills the browser window; only a choice made by a key press or click may go fullscreen
                if (chosen) Screen.fullScreen = save.fullscreen;
                return;
            }
            var desktop = Desktop;
            if (save.fullscreen)
            {
                if (Screen.fullScreenMode != FullScreenMode.FullScreenWindow)
                    Screen.SetResolution(desktop.x, desktop.y, FullScreenMode.FullScreenWindow);
                return;
            }
            var size = WindowSize(save, desktop);
            if (chosen || Screen.fullScreenMode != FullScreenMode.Windowed)
                Screen.SetResolution(size.x, size.y, FullScreenMode.Windowed);
        }

        public static int ScaleStep(float scale)
        {
            int k = 0;
            for (int i = 0; i < RenderScales.Length; i++)
                if (Mathf.Abs(RenderScales[i] - scale) < Mathf.Abs(RenderScales[k] - scale)) k = i;
            return k;
        }

        /// <summary>
        /// The 3D scene's render scale (URP; screen-space menus and the HUD aren't affected). Not
        /// applied in the editor, where it would write into the pipeline asset on disk.
        /// </summary>
        public static void ApplyRenderScale(float scale)
        {
            if (Application.isEditor) return;
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)) return;
            urp.renderScale = RenderScales[ScaleStep(scale)];
            // FSR's shader doesn't run on WebGL, and URP drops all post-processing when it's missing
            urp.upscalingFilter = Web ? UpscalingFilterSelection.Linear : UpscalingFilterSelection.FSR;
        }

        public static float CurrentRenderScale =>
            GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp ? urp.renderScale : 1f;
    }
}
