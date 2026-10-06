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
    /// Unlike -bsCapture it never feeds actions to the simulation directly. A second pass plays 1-1
    /// again on a virtual gamepad (D-pad and stick, RB aim, LT focus, A borrow) and drives the
    /// menus with it: Select for the hint, Start to pause, D-pad + A into Watch solution, B out.
    /// </summary>
    public sealed partial class GameRoot
    {
        Keyboard botKb;
        Mouse botMouse;
        Gamepad botPad;
        /// <summary>Lets the input bot reach the in-level flow (pause, hint, restart) that scripted runs skip.</summary>
        bool botDrivesFlow;
        Vector2 botPointer;

        IEnumerator InputBot(string dir)
        {
            Directory.CreateDirectory(dir);
            var log = new List<string>();
            // the window's real devices would compete for Keyboard.current / Mouse.current
            foreach (var dev in new List<InputDevice>(InputSystem.devices))
                if (dev is Pointer || dev is Keyboard || dev is Gamepad) InputSystem.DisableDevice(dev);
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
            log.Add(ok ? $"PASS 1-1 played through virtual keyboard + mouse, won at tick {Session.Cur.Tick}" : "FAIL keyboard + mouse");
            bool kbOk = ok;

            bool padOk = false;
            yield return PadPass(dir, log, r => padOk = r);
            log.Add($"info frames {Time.frameCount} over {Time.realtimeSinceStartup:0.0}s real");
            log.Add(kbOk && padOk ? "RESULT PASS keyboard + mouse and gamepad" : "RESULT FAIL");
            File.WriteAllLines(Path.Combine(dir, "inputbot.log"), log);
            Debug.Log("[InputBot] " + string.Join(" | ", log));
            Application.Quit(kbOk && padOk ? 0 : 1);
        }

        // ---------------------------------------------------------------- gamepad pass

        GamepadState padHeld; // buttons/axes held across taps (e.g. LT while pressing A)

        void PadSet(GamepadState st) => InputSystem.QueueStateEvent(botPad, st);

        IEnumerator PadTap(GamepadButton b)
        {
            var st = padHeld;
            PadSet(st.WithButton(b));
            yield return null;
            PadSet(padHeld);
            yield return null;
            yield return null;
        }

        /// <summary>One step per tap, from the D-pad or (stick != 0) the left stick.</summary>
        IEnumerator PadSteps(GamepadButton dpad, Vector2 stick, int n, List<string> log, System.Action<bool> result)
        {
            for (int i = 0; i < n; i++)
            {
                int before = Session.Cur.P;
                var st = padHeld;
                if (stick != Vector2.zero) st.leftStick = stick; else st = st.WithButton(dpad);
                PadSet(st);
                yield return null;
                PadSet(padHeld);
                float deadline = Time.realtimeSinceStartup + 2f;
                while ((Session.Cur.P == before || Session.Cur.Moving) && !Session.Cur.Dead && Time.realtimeSinceStartup < deadline) yield return null;
                if (Session.Cur.P == before || Session.Cur.Dead)
                {
                    log.Add($"FAIL pad step {(stick != Vector2.zero ? "stick" : dpad.ToString())} #{i + 1}: stuck at {Session.Def.X(before)},{Session.Def.Y(before)}");
                    result(false);
                    yield break;
                }
            }
            result(true);
        }

        IEnumerator PadPass(string dir, List<string> log, System.Action<bool> result)
        {
            botDrivesFlow = true;
            InputSystem.DisableDevice(botKb);
            InputSystem.DisableDevice(botMouse);
            botPad = InputSystem.AddDevice<Gamepad>("BotGamepad");
            botPad.MakeCurrent();
            padHeld = new GamepadState();
            PadSet(padHeld);
            complete.Hide();
            StartLevel(0, false);
            float deadline = Time.realtimeSinceStartup + 6f;
            while (Session.State != LevelSession.Mode.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            var def = Session.Def;
            bool ok = true;
            void Fail(string why) { if (ok) log.Add("FAIL pad " + why); ok = false; }

            // Select folds and unfolds the tip; the first pad input switches the key hints (and 1-1's
            // tip, which names controls) to the pad
            yield return new WaitForSecondsRealtime(2.6f); // the tip slides in after the title intro
            string kbTip = Hud.TipText;
            yield return PadTap(GamepadButton.Select);
            yield return PadTap(GamepadButton.Select);
            string open = Hud.TipText;
            bool padTip = open != kbTip && open.Contains("<b>A</b>");
            yield return new WaitForSecondsRealtime(0.4f);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "pad_0_tip.png"));
            yield return null;
            if (!padTip) Fail("1-1's tip still names mouse controls on the gamepad: " + open);
            yield return PadTap(GamepadButton.Select);
            bool padHints = Input.UsingGamepad && (Hud.HintsText ?? "").Contains("<b>Stick</b>") && Hud.HintsText.Contains("<b>Select</b> hint");
            bool folded = Hud.TipText != open && Hud.TipText.Contains("Select");
            yield return new WaitForSecondsRealtime(0.4f);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "pad_1_hint-folded.png"));
            yield return null;
            yield return PadTap(GamepadButton.Select);
            bool reopened = Hud.TipText == open;
            if (!padHints) Fail("key hints did not switch to the gamepad: " + Hud.HintsText);
            if (!folded || !reopened) Fail($"Select hint toggle: folded={folded} reopened={reopened}");
            if (ok) log.Add("ok   pad: key hints and 1-1's tip switched to the gamepad; Select folded and unfolded the tip");

            // walk to the lane's edge: one D-pad tap and one stick flick
            if (ok) yield return PadSteps(GamepadButton.DpadDown, Vector2.zero, 1, log, r => ok &= r);
            if (ok) yield return PadSteps(GamepadButton.DpadDown, new Vector2(0, -1), 1, log, r => ok &= r);
            if (ok && Session.Cur.P != def.Idx(3, 3)) Fail($"walk: player at {def.X(Session.Cur.P)},{def.Y(Session.Cur.P)}");

            // RB aims, LT held focuses, A borrows once the slider is in its far alcove
            if (ok) yield return PadTap(GamepadButton.RightShoulder);
            if (ok && Session.Aim != 0) Fail("RB did not aim at the slider (aim " + Session.Aim + ")");
            padHeld.leftTrigger = 1f;
            PadSet(padHeld);
            int loans = Session.Cur.Loans;
            deadline = Time.realtimeSinceStartup + 20f;
            float focusSeen = 0f;
            while (ok && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                focusSeen = Mathf.Max(focusSeen, Session.FocusBlend);
                var s = Session.Cur;
                if (Session.Aim == 0 && (s.SIdx[0] >= 13 || (s.SIdx[0] == 12 && s.SDir[0] > 0)))
                {
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, "pad_2_aim-focus.png"));
                    yield return PadTap(GamepadButton.South);
                    break;
                }
            }
            padHeld.leftTrigger = 0f;
            PadSet(padHeld);
            deadline = Time.realtimeSinceStartup + 2f;
            while (ok && Session.Cur.Loans == loans && Time.realtimeSinceStartup < deadline) yield return null;
            if (ok && focusSeen < 0.9f) Fail($"LT focus never engaged (blend {focusSeen:0.00})");
            if (ok && Session.Cur.Loans == loans) Fail("A did not borrow");
            if (ok) log.Add($"ok   pad: RB aimed, LT focused (blend {focusSeen:0.00}), A borrowed at tick {Session.Cur.Tick}");

            // across the frozen lane on the stick (held), then down on the D-pad to the exit
            if (ok) yield return PadSteps(GamepadButton.DpadDown, Vector2.zero, 1, log, r => ok &= r);
            if (ok)
            {
                padHeld.leftStick = new Vector2(1, 0);
                PadSet(padHeld);
                deadline = Time.realtimeSinceStartup + 6f;
                while (!(def.X(Session.Cur.P) >= 11 && !Session.Cur.Moving) && !Session.Cur.Dead && Time.realtimeSinceStartup < deadline) yield return null;
                padHeld.leftStick = Vector2.zero;
                PadSet(padHeld);
                yield return null;
                if (def.X(Session.Cur.P) < 11 || Session.Cur.Dead) Fail($"stick hold: player at {def.X(Session.Cur.P)},{def.Y(Session.Cur.P)}");
            }
            if (ok) yield return PadSteps(GamepadButton.DpadDown, Vector2.zero, 3, log, r => ok &= r);
            deadline = Time.realtimeSinceStartup + 10f;
            while (ok && Session.State != LevelSession.Mode.Won && !Session.Cur.Dead && Time.realtimeSinceStartup < deadline) yield return null;
            if (ok && Session.State != LevelSession.Mode.Won) Fail($"no win (dead={Session.Cur.Dead}, tick {Session.Cur.Tick})");
            if (ok) log.Add($"ok   pad: won 1-1 at tick {Session.Cur.Tick}");

            // menus: Start pauses, B resumes; Start, D-pad down twice and A open Watch solution; B stops it
            yield return new WaitForSecondsRealtime(0.5f);
            complete.Hide();
            pendingComplete = null;
            StartLevel(0, false);
            yield return new WaitForSecondsRealtime(1f);
            yield return PadTap(GamepadButton.Start);
            bool paused = State == Flow.Paused;
            yield return new WaitForSecondsRealtime(0.6f);
            yield return PadTap(GamepadButton.East);
            yield return new WaitForSecondsRealtime(0.3f);
            bool resumed = State == Flow.Playing;
            if (!paused || !resumed) Fail($"Start/B pause: paused={paused} resumed={resumed}");
            yield return PadTap(GamepadButton.Start);
            yield return new WaitForSecondsRealtime(0.8f);
            yield return PadTap(GamepadButton.DpadDown);
            yield return new WaitForSecondsRealtime(0.15f);
            yield return PadTap(GamepadButton.DpadDown);
            yield return new WaitForSecondsRealtime(0.7f);
            string row = pause.Menu.Items[pause.Menu.Selected].Label;
            // the brass selector plate must have followed the D-pad to that row
            float plateY = pause.Menu.SelectorY, rowY = pause.Menu.Items[pause.Menu.Selected].Rt.anchoredPosition.y - (pause.Menu.RowHeight - 10) * 0.5f;
            if (Mathf.Abs(plateY - rowY) > 4f) Fail($"menu selector plate at y={plateY:0} did not follow the selection (row y={rowY:0})");
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "pad_3_pause-watch.png"));
            yield return null;
            yield return PadTap(GamepadButton.South);
            deadline = Time.realtimeSinceStartup + 3f;
            while (State != Flow.Watching && Time.realtimeSinceStartup < deadline) yield return null;
            bool watching = State == Flow.Watching && (Hud.HintsText ?? "").Contains("<b>B</b> stop watching");
            yield return new WaitForSecondsRealtime(1.5f);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "pad_4_watching.png"));
            yield return null;
            yield return PadTap(GamepadButton.East);
            deadline = Time.realtimeSinceStartup + 3f;
            while (State == Flow.Watching && Time.realtimeSinceStartup < deadline) yield return null;
            bool stopped = State == Flow.Playing && Session.Autoplay == null;
            if (row != "Watch solution" || !watching || !stopped) Fail($"pause menu > Watch solution: selected '{row}', watching={watching}, B stopped={stopped}");
            if (ok) log.Add("ok   pad: Start paused and B resumed; D-pad + A chose Watch solution; B stopped it");
            log.Add(ok ? "PASS 1-1 and the menus played through a virtual gamepad" : "FAIL gamepad");
            botDrivesFlow = false;
            result(ok);
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
