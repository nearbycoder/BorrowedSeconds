using System.Collections;
using System.Collections.Generic;
using System.IO;
using BorrowedSeconds.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace BorrowedSeconds.Game
{
    /// <summary>
    /// -bsInputBot DIR: a controls smoke test. Plays 1-1 through *virtual* keyboard and mouse
    /// devices (key taps, a mouse move onto the slider, a left click), so the whole real input path
    /// is exercised: polling, move buffering, hover picking, click-to-borrow, the debt and the win.
    /// Unlike -bsCapture it never feeds actions to the simulation directly.
    /// </summary>
    public sealed partial class GameRoot
    {
        Keyboard botKb;
        Mouse botMouse;
        Vector2 botPointer;

        IEnumerator InputBot(string dir)
        {
            Directory.CreateDirectory(dir);
            var log = new List<string>();
            // the window's real devices would compete for Keyboard.current / Mouse.current
            foreach (var dev in new List<InputDevice>(InputSystem.devices))
                if (dev is Pointer || dev is Keyboard) InputSystem.DisableDevice(dev);
            botKb = InputSystem.AddDevice<Keyboard>("BotKeyboard");
            botMouse = InputSystem.AddDevice<Mouse>("BotMouse");
            botKb.MakeCurrent();
            botMouse.MakeCurrent();
            botPointer = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            yield return BotMouse(botPointer, false);

            StartLevel(0, false);
            float deadline = Time.realtimeSinceStartup + 6f;
            while (Session.State != LevelSession.Mode.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            var def = Session.Def;
            bool ok = def.Id == "1-1";
            if (!ok) log.Add("FAIL first level is not 1-1");

            // walk down to the lane's edge, one tapped step at a time
            if (ok) yield return BotSteps(Key.S, 2, log, r => ok &= r);
            ok &= Session.Cur.P == def.Idx(3, 3);
            if (!ok) log.Add($"FAIL walk: player at {def.X(Session.Cur.P)},{def.Y(Session.Cur.P)}");

            // hold Shift (focus slows time), track the slider with the pointer, click once it is
            // both under the cursor and in the far alcove
            int borrowsBefore = Session.Cur.Loans;
            deadline = Time.realtimeSinceStartup + 20f;
            bool hovered = false;
            InputSystem.QueueStateEvent(botKb, new KeyboardState(Key.LeftShift));
            while (ok && Time.realtimeSinceStartup < deadline)
            {
                var target = (Vector2)Cam.WorldToScreenPoint(Session.Board.ObstacleCenter(0));
                botPointer = target;
                InputSystem.QueueStateEvent(botMouse, new MouseState { position = target });
                yield return null;
                hovered |= Session.Aim == 0;
                var s = Session.Cur;
                bool far = s.SIdx[0] >= 13 || (s.SIdx[0] == 12 && s.SDir[0] > 0);
                if (Session.Aim == 0 && far)
                {
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, "bot_1_hover.png"));
                    log.Add($"info click at tick {s.Tick}: slider index {s.SIdx[0]}, focus={Session.FocusBlend:0.00}");
                    InputSystem.QueueStateEvent(botMouse, new MouseState { position = target, buttons = 1 });
                    yield return null;
                    InputSystem.QueueStateEvent(botMouse, new MouseState { position = target });
                    yield return null;
                    break;
                }
            }
            InputSystem.QueueStateEvent(botKb, new KeyboardState());
            if (ok && !hovered) { ok = false; log.Add("FAIL hover: aim never landed on the slider"); }
            deadline = Time.realtimeSinceStartup + 2f;
            while (ok && Session.Cur.Loans == borrowsBefore && Time.realtimeSinceStartup < deadline) yield return null;
            if (ok && Session.Cur.Loans == borrowsBefore) { ok = false; log.Add("FAIL click: no loan was taken"); }
            if (ok) log.Add($"ok   borrowed at tick {Session.Cur.Tick}, slider frozen={Session.Cur.SFrozen[0] > 0}");

            // across the lane (frozen slider), then down to the exit
            if (ok) yield return BotSteps(Key.S, 1, log, r => ok &= r);
            if (ok) yield return BotHold(Key.D, () => Session.Def.X(Session.Cur.P) >= 11 && !Session.Cur.Moving, log, r => ok &= r);
            if (ok) ScreenCapture.CaptureScreenshot(Path.Combine(dir, "bot_2_crossing.png"));
            if (ok) yield return BotSteps(Key.S, 3, log, r => ok &= r);

            deadline = Time.realtimeSinceStartup + 10f;
            bool froze = false;
            while (ok && Session.State != LevelSession.Mode.Won && !Session.Cur.Dead && Time.realtimeSinceStartup < deadline)
            {
                if (!froze && Session.Cur.PFrozen > 0)
                {
                    froze = true;
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, "bot_3_repaying.png"));
                }
                yield return null;
            }
            bool won = Session.State == LevelSession.Mode.Won;
            if (ok && !froze) log.Add("FAIL the debt never froze the player");
            if (ok && !won) log.Add($"FAIL no win (dead={Session.Cur.Dead}, tick {Session.Cur.Tick})");
            ok &= froze && won;
            yield return new WaitForSecondsRealtime(1.5f);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "bot_4_win.png"));
            yield return null;
            yield return null;
            log.Add($"info frames {Time.frameCount} over {Time.realtimeSinceStartup:0.0}s real");
            log.Add(ok ? $"PASS 1-1 played through virtual keyboard + mouse, won at tick {Session.Cur.Tick}" : "FAIL");
            File.WriteAllLines(Path.Combine(dir, "inputbot.log"), log);
            Debug.Log("[InputBot] " + string.Join(" | ", log));
            Application.Quit(ok ? 0 : 1);
        }

        /// <summary>-bsFps FILE: plays the busiest levels from their replays and logs frame times.</summary>
        IEnumerator FpsProbe(string file)
        {
            var lines = new List<string>();
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-bsNoVsync") >= 0)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
            }
            foreach (var id in new[] { "4-5", "3-5", "2-4" })
            {
                int idx = Catalog.Levels.FindIndex(l => l.Id == id);
                LoadLevel(idx);
                State = Flow.Playing;
                Hud.SetVisible(true);
                Session.Autoplay = Catalog.SolutionFor(Catalog.Levels[idx]).Actions;
                Session.IntroTime = 0.2f;
                yield return new WaitForSecondsRealtime(1.5f);   // warm-up
                var dts = new List<float>();
                float end = Time.realtimeSinceStartup + 6f;
                while (Time.realtimeSinceStartup < end)
                {
                    yield return null;
                    dts.Add(Time.unscaledDeltaTime);
                }
                dts.Sort();
                float total = 0f;
                foreach (var d in dts) total += d;
                float p99 = dts[Mathf.Clamp((int)(dts.Count * 0.99f), 0, dts.Count - 1)];
                lines.Add($"{id}: {dts.Count / total:0.0} fps avg, median {1f / dts[dts.Count / 2]:0.0} fps, worst 1% {p99 * 1000f:0.0} ms ({dts.Count} frames, {Screen.width}x{Screen.height}, vsync {QualitySettings.vSyncCount})");
            }
            File.WriteAllLines(file, lines);
            Debug.Log("[Fps] " + string.Join(" | ", lines));
            Application.Quit(0);
        }

        /// <summary>Taps a key n times, each tap waiting for the step to land.</summary>
        IEnumerator BotSteps(Key key, int n, List<string> log, System.Action<bool> result)
        {
            for (int i = 0; i < n; i++)
            {
                int before = Session.Cur.P;
                // a quick tap: one frame down (a held key auto-repeats once the step lands)
                InputSystem.QueueStateEvent(botKb, new KeyboardState(key));
                yield return null;
                InputSystem.QueueStateEvent(botKb, new KeyboardState());
                float deadline = Time.realtimeSinceStartup + 2f;
                while ((Session.Cur.P == before || Session.Cur.Moving) && !Session.Cur.Dead && Time.realtimeSinceStartup < deadline) yield return null;
                log.Add($"info tap {key}: ({Session.Def.X(before)},{Session.Def.Y(before)}) -> ({Session.Def.X(Session.Cur.P)},{Session.Def.Y(Session.Cur.P)}) tick {Session.Cur.Tick} frame {Time.frameCount}");
                if (Session.Cur.P == before || Session.Cur.Dead)
                {
                    log.Add($"FAIL step {key} #{i + 1}: player stuck at {Session.Def.X(before)},{Session.Def.Y(before)} dead={Session.Cur.Dead}");
                    result(false);
                    yield break;
                }
            }
            result(true);
        }

        /// <summary>Holds a key (auto-repeating steps, as a player would) until the condition holds.</summary>
        IEnumerator BotHold(Key key, System.Func<bool> until, List<string> log, System.Action<bool> result)
        {
            InputSystem.QueueStateEvent(botKb, new KeyboardState(key));
            float deadline = Time.realtimeSinceStartup + 6f;
            while (!until() && !Session.Cur.Dead && Time.realtimeSinceStartup < deadline) yield return null;
            InputSystem.QueueStateEvent(botKb, new KeyboardState());
            yield return null;
            bool ok = until() && !Session.Cur.Dead;
            if (!ok) log.Add($"FAIL hold {key}: player at {Session.Def.X(Session.Cur.P)},{Session.Def.Y(Session.Cur.P)} dead={Session.Cur.Dead}");
            result(ok);
        }

        IEnumerator BotMouse(Vector2 to, bool down)
        {
            // glide so the reader sees real pointer motion
            for (int k = 0; k < 3; k++)
            {
                botPointer = Vector2.Lerp(botPointer, to, 0.6f);
                InputSystem.QueueStateEvent(botMouse, new MouseState { position = botPointer, buttons = (ushort)(down ? 1 : 0) });
                yield return null;
            }
            botPointer = to;
            InputSystem.QueueStateEvent(botMouse, new MouseState { position = to, buttons = (ushort)(down ? 1 : 0) });
            yield return null;
        }
    }
}
