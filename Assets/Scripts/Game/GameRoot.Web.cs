using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BorrowedSeconds.UI;
using BorrowedSeconds.View;
using UnityEngine;
using UnityEngine.Profiling;

namespace BorrowedSeconds.Game
{
    /// <summary>
    /// The browser build on phones and tablets: which on-screen controls the page shows (WebBridge), where they
    /// sit (so the board is framed clear of them), lighter settings after the browser killed the tab, and the
    /// console lines Tools/check-mobile.mjs reads. Nothing here runs outside the browser build.
    /// </summary>
    public sealed partial class GameRoot
    {
        /// <summary>Before the first settings apply: says what the page found, and a tab whose last session the
        /// browser ended (most likely for memory) starts at Graphics fidelity Low and a 70 % render resolution.</summary>
        void WebAwake()
        {
            if (!DisplayOptions.Web) return;
            Debug.Log($"[Web] device: {(WebBridge.TouchFirst ? "touch-first" : "mouse and keyboard")}{(WebBridge.Phone ? ", phone-sized" : "")}; "
                + $"default Graphics fidelity {GraphicsFidelity.For(GraphicsFidelity.Default).Name}, HUD size {Mathf.RoundToInt(SaveData.DefaultHudScale * 100)}%");
            if (WebBridge.Recovered && !capturing)
            {
                Save.fidelity = 0;
                Save.renderScale = Mathf.Min(Save.renderScale, 0.7f);
                Save.Save();
                Debug.Log("[Web] the last session in this tab ended without closing: starting at Graphics fidelity Low, render resolution 70%");
            }
        }

        /// <summary>
        /// On a phone or tablet, before the first text: the game's dynamic fonts skip their OpenType features.
        /// TextMeshPro otherwise reads a font's whole kerning and mark tables when a text first uses it, which in the
        /// browser build kept about 144 MB of the wasm heap for good (measured with Tools/check-mobile.mjs), a
        /// large share of an iPhone tab's memory. Text loses kerning there; desktop browsers keep it.
        /// </summary>
        static void WebLightFonts()
        {
            if (!DisplayOptions.Web || !WebBridge.TouchFirst) return;
            foreach (var name in new[] { "FiraSans-Regular", "FiraSans-SemiBold", "FiraSans-ExtraBold", "FiraSans-Light" })
            {
                var f = Resources.Load<TMPro.TMP_FontAsset>("Fonts/" + name + " SDF");
                if (f != null) f.getFontFeatures = false;
            }
        }

        /// <summary>The wasm heap as Unity sees it, for the browser checks.</summary>
        public static void LogWebMemory(string when)
        {
            if (!DisplayOptions.Web) return;
            const float MB = 1f / 1048576f;
            Debug.Log($"[Web] memory {when}: Unity {Profiler.GetTotalAllocatedMemoryLong() * MB:0} MB allocated of {Profiler.GetTotalReservedMemoryLong() * MB:0} MB reserved, "
                + $"managed {Profiler.GetMonoUsedSizeLong() * MB:0} of {Profiler.GetMonoHeapSizeLong() * MB:0} MB");
        }

        WebBridge.Mode webMode = WebBridge.Mode.None;
        MenuScreen webTop;
        float webTargetsAt;
        string webTargets;
        readonly List<(string name, RectTransform rt)> webTargetList = new List<(string, RectTransform)>();

        /// <summary>Each frame: the controls for what's on screen, and where they sit while a level is played.</summary>
        void WebUpdate()
        {
            if (!DisplayOptions.Web) return;
            var top = TopScreen();
            // the title's menu sits left, so its d-pad goes right; the level select's cards fill the screen and
            // are big enough to tap, so it only gets Back; windows get the d-pad left and OK right
            var mode = top == title ? WebBridge.Mode.Title
                : top == levels ? WebBridge.Mode.Ledger
                : top != null ? WebBridge.Mode.Menu
                : State == Flow.Playing ? WebBridge.Mode.Level
                : State == Flow.Watching ? WebBridge.Mode.Watching
                : WebBridge.Mode.None;
            if (Wipe != null && Wipe.Busy) mode = WebBridge.Mode.None;
            if (mode != webMode && DisplayOptions.Web) Debug.Log($"[Web] controls: {mode}");
            webMode = mode;
            WebBridge.SetMode(mode);

            // the buttons' corners while they're up in a level: the camera frames the board clear of them and the tip
            // sits above the d-pad; menus over a level keep the level's framing
            if (!Input.UsingTouch) SetTouchZones(Vector2.zero, Vector2.zero, Vector2.zero);
            else if (mode == WebBridge.Mode.Level || mode == WebBridge.Mode.Watching)
            {
                float sw = Mathf.Max(1, Screen.width), sh = Mathf.Max(1, Screen.height), s = Mathf.Min(sw / 1920f, sh / 1080f);
                float cw = sw / s, ch = sh / s;
                SetTouchZones(new Vector2(WebBridge.Zone(0) * cw, WebBridge.Zone(1) * ch), new Vector2(WebBridge.Zone(2) * cw, WebBridge.Zone(3) * ch),
                    new Vector2(WebBridge.Zone(4) * cw, WebBridge.Zone(5) * ch));
            }

            // what can be tapped, for Tools/check-mobile.mjs: once a screen has settled, and again if it changes
            if (!WebBridge.TouchFirst) return;
            if (top != webTop) { webTop = top; webTargets = null; webTargetsAt = 0f; }
            if (top == null || top.ShownFor < 0.9f || Clock.Now < webTargetsAt) return;
            webTargetsAt = Clock.Now + 1f;
            webTargetList.Clear();
            top.Targets(webTargetList);
            var sb = new StringBuilder();
            foreach (var (name, rt) in webTargetList)
            {
                if (rt == null || !rt.gameObject.activeInHierarchy) continue;
                var c = rt.TransformPoint(rt.rect.center);
                if (sb.Length > 0) sb.Append(" | ");
                sb.Append(name).Append(" @ ").Append((c.x / Screen.width).ToString("0.000", CultureInfo.InvariantCulture))
                  .Append(',').Append((1f - c.y / Screen.height).ToString("0.000", CultureInfo.InvariantCulture));
            }
            string line = sb.ToString();
            if (line == webTargets) return;
            webTargets = line;
            Debug.Log($"[Web] targets {top.Root.name}: {line}");
        }

        void SetTouchZones(Vector2 left, Vector2 right, Vector2 topRight)
        {
            bool Moved(Vector2 a, Vector2 b) => Mathf.Abs(a.x - b.x) > 8f || Mathf.Abs(a.y - b.y) > 8f;
            if (!Moved(left, HudLayout.TouchLeft) && !Moved(right, HudLayout.TouchRight) && !Moved(topRight, HudLayout.TouchTop)) return;
            HudLayout.TouchLeft = left;
            HudLayout.TouchRight = right;
            HudLayout.TouchTop = topRight;
            if (Session != null) Rig.Frame(Session.Board.Bounds, false, Session.Board.TileTops);
        }

        /// <summary>The page asks (unityInstance.SendMessage("Game", "WebReport")); logs the level as a "[Web] state"
        /// line: tick, the player's tile, the aim, Focus, what's frozen, and where each obstacle is on screen.</summary>
        public void WebReport(string unused)
        {
            if (Session == null || Session.Def == null) { Debug.Log("[Web] state {}"); return; }
            var s = Session.Cur;
            var d = Session.Def;
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("{\"tick\":").Append(s.Tick).Append(",\"x\":").Append(d.X(s.P)).Append(",\"y\":").Append(d.Y(s.P))
              .Append(",\"p\":[").Append(d.X(s.P)).Append(',').Append(d.Y(s.P)).Append(']')
              .Append(",\"aim\":").Append(Session.Aim).Append(",\"focus\":").Append(Session.Focusing ? "true" : "false")
              .Append(",\"state\":\"").Append(Session.State).Append("\",\"touch\":").Append(Input.UsingTouch ? "true" : "false")
              .Append(",\"frozen\":[");
            bool first = true;
            for (int i = 0; i < d.ObstacleCount; i++)
                if (s.IsObstacleFrozen(d, i)) { if (!first) sb.Append(','); sb.Append(i); first = false; }
            sb.Append("],\"obstacles\":[");
            for (int i = 0; i < d.ObstacleCount; i++)
            {
                var p = Cam.WorldToScreenPoint(Session.Board.ObstacleCenter(i));
                if (i > 0) sb.Append(',');
                sb.Append("{\"fx\":").Append((p.x / Screen.width).ToString("0.000", inv)).Append(",\"fy\":").Append((1f - p.y / Screen.height).ToString("0.000", inv)).Append('}');
            }
            sb.Append("]}");
            Debug.Log("[Web] state " + sb);
        }
    }
}
