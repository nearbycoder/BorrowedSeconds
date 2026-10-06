using System.Collections;
using System.Collections.Generic;
using System.IO;
using BorrowedSeconds.Sim;
using BorrowedSeconds.UI;
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

            bool padOk = false, rebindOk = false;
            yield return PadPass(dir, log, r => padOk = r);
            yield return RebindPass(dir, log, r => rebindOk = r);
            bool restartOk = false;
            yield return RestartPass(dir, log, r => restartOk = r);
            rebindOk &= restartOk;
            log.Add($"info frames {Time.frameCount} over {Time.realtimeSinceStartup:0.0}s real");
            bool all = kbOk && padOk && rebindOk;
            log.Add(all ? "RESULT PASS keyboard + mouse, gamepad, rebound keyboard, restart" : "RESULT FAIL");
            File.WriteAllLines(Path.Combine(dir, "inputbot.log"), log);
            Debug.Log("[InputBot] " + string.Join(" | ", log));
            Application.Quit(all ? 0 : 1);
        }

        // ---------------------------------------------------------------- rebound keyboard pass

        IEnumerator KeyTap(Key k, int n = 1, float gap = 0.12f)
        {
            for (int i = 0; i < n; i++)
            {
                InputSystem.QueueStateEvent(botKb, new KeyboardState(k));
                yield return null;
                InputSystem.QueueStateEvent(botKb, new KeyboardState());
                yield return new WaitForSecondsRealtime(gap);
            }
        }

        /// <summary>
        /// Rebinds keys through the real menus (Esc, pause > Settings > Controls, choose a row, Enter,
        /// press the new key), then plays 1-1 keyboard-only on the new keys: J/L to move, Tab to aim,
        /// F held to focus, K to borrow. The old keys must no longer act, and the hints must follow.
        /// </summary>
        IEnumerator RebindPass(string dir, List<string> log, System.Action<bool> result)
        {
            botDrivesFlow = true;
            InputSystem.DisableDevice(botPad);
            InputSystem.EnableDevice(botKb);
            botKb.MakeCurrent();
            InputSystem.QueueStateEvent(botKb, new KeyboardState());
            complete.Hide();
            StartLevel(0, false);
            yield return new WaitForSecondsRealtime(2.6f);
            bool ok = true;
            void Fail(string why) { if (ok) log.Add("FAIL rebind " + why); ok = false; }

            yield return KeyTap(Key.Escape);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return KeyTap(Key.DownArrow, 4);              // Resume, Restart, Watch solution, Levels, Settings
            yield return KeyTap(Key.Enter);
            yield return new WaitForSecondsRealtime(0.7f);
            yield return KeyTap(Key.DownArrow, SettingsScreen.ControlsRow);
            yield return KeyTap(Key.Enter);
            yield return new WaitForSecondsRealtime(0.8f);
            if (!controls.Visible) Fail("pause > Settings > Controls did not open the Controls page");
            var binds = new (KeyAction action, Key key)[] { (KeyAction.Down, Key.J), (KeyAction.Right, Key.L), (KeyAction.Borrow, Key.K), (KeyAction.Focus, Key.F), (KeyAction.Hint, Key.U) };
            int row = 0;
            foreach (var (action, key) in binds)
            {
                if (!ok) break;
                int to = (int)action;
                yield return KeyTap(to > row ? Key.DownArrow : Key.UpArrow, Mathf.Abs(to - row));
                row = to;
                yield return KeyTap(Key.Enter);
                yield return new WaitForSecondsRealtime(0.2f);
                if (action == KeyAction.Focus)
                {
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, "rebind_1_listening.png"));
                    yield return null;
                }
                yield return KeyTap(key);
                if (Input.Keys[to] != key) Fail($"{action} -> {key}: bound to {Input.Keys[to]}");
            }
            yield return new WaitForSecondsRealtime(0.6f);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "rebind_2_controls.png"));
            yield return null;
            bool saved = ok && KeyBindings.Load(Save.keys)[(int)KeyAction.Borrow] == Key.K;
            if (!saved) Fail("the bindings did not reach the save");
            yield return KeyTap(Key.Escape);                     // Controls -> Settings
            yield return new WaitForSecondsRealtime(0.5f);
            yield return KeyTap(Key.Escape);                     // Settings -> Pause
            yield return new WaitForSecondsRealtime(0.5f);
            yield return KeyTap(Key.Escape);                     // resume
            yield return new WaitForSecondsRealtime(0.5f);
            if (State != Flow.Playing) Fail($"Esc did not back out to play (state {State})");
            string hints = Hud.HintsText ?? "";
            bool hintRow = hints.Contains("<b>F</b> focus") && hints.Contains("<b>U</b> hint") && hints.Contains("<b>WAJL</b> move");
            if (ok && !hintRow) Fail("key hints did not follow the bindings: " + hints);
            if (ok) log.Add("ok   rebind: Down J, Right L, Borrow K, Focus F, Hint U via pause > Settings > Controls; saved; hints follow");

            // the old keys are dead, the new ones play 1-1
            var def = Session.Def;
            int p0 = Session.Cur.P;
            yield return KeyTap(Key.S);
            yield return new WaitForSecondsRealtime(0.3f);
            if (ok && Session.Cur.P != p0) Fail("S still moves after Down was rebound to J");
            if (ok) yield return BotSteps(Key.J, 2, log, r => ok &= r);
            if (ok && Session.Cur.P != def.Idx(3, 3)) Fail($"walk: player at {def.X(Session.Cur.P)},{def.Y(Session.Cur.P)}");
            if (ok) yield return KeyTap(Key.Tab);
            if (ok && Session.Aim != 0) Fail("Tab did not aim at the slider");
            InputSystem.QueueStateEvent(botKb, new KeyboardState(Key.F));
            int loans = Session.Cur.Loans;
            float deadline = Time.realtimeSinceStartup + 20f, focus = 0f;
            while (ok && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                focus = Mathf.Max(focus, Session.FocusBlend);
                var st = Session.Cur;
                if (focus >= 0.9f && Session.Aim == 0 && (st.SIdx[0] >= 13 || (st.SIdx[0] == 12 && st.SDir[0] > 0)))
                {
                    InputSystem.QueueStateEvent(botKb, new KeyboardState(Key.F, Key.K));
                    yield return null;
                    InputSystem.QueueStateEvent(botKb, new KeyboardState(Key.F));
                    yield return null;
                    break;
                }
            }
            InputSystem.QueueStateEvent(botKb, new KeyboardState());
            deadline = Time.realtimeSinceStartup + 2f;
            while (ok && Session.Cur.Loans == loans && Time.realtimeSinceStartup < deadline) yield return null;
            if (ok && focus < 0.9f) Fail($"F never focused (blend {focus:0.00})");
            if (ok && Session.Cur.Loans == loans) Fail("K did not borrow");
            if (ok) yield return BotSteps(Key.J, 1, log, r => ok &= r);
            // counted taps, not a hold: a held key can start the next step on the tick the last one lands
            if (ok) yield return BotSteps(Key.L, 11 - def.X(Session.Cur.P), log, r => ok &= r);
            if (ok) ScreenCapture.CaptureScreenshot(Path.Combine(dir, "rebind_3_hud.png"));
            if (ok) yield return BotSteps(Key.J, 3, log, r => ok &= r);
            deadline = Time.realtimeSinceStartup + 10f;
            while (ok && Session.State != LevelSession.Mode.Won && !Session.Cur.Dead && Time.realtimeSinceStartup < deadline) yield return null;
            if (ok && Session.State != LevelSession.Mode.Won) Fail($"no win (dead={Session.Cur.Dead})");
            log.Add(ok ? $"PASS 1-1 played keyboard-only on rebound keys (J/L move, Tab aim, F focus, K borrow), won at tick {Session.Cur.Tick}" : "FAIL rebound keyboard");
            ResetKeys();
            botDrivesFlow = false;
            result(ok);
        }

        // ---------------------------------------------------------------- restart pass

        /// <summary>
        /// R restarts at once in a level's first three seconds; later a tap does nothing (the HOLD R
        /// tag appears) and only a held R restarts.
        /// </summary>
        IEnumerator RestartPass(string dir, List<string> log, System.Action<bool> result)
        {
            botDrivesFlow = true;
            bool ok = true;
            void Fail(string why) { if (ok) log.Add("FAIL restart " + why); ok = false; }
            complete.Hide();
            StartLevel(0, false);
            float deadline = Time.realtimeSinceStartup + 6f;
            while ((Session.State != LevelSession.Mode.Playing || Session.Tick < 20) && Time.realtimeSinceStartup < deadline) yield return null;
            var first = Session;
            yield return KeyTap(Key.R);
            if (Session == first) Fail("an early tap did not restart");
            deadline = Time.realtimeSinceStartup + 8f;
            while ((Session.State != LevelSession.Mode.Playing || Session.Tick < 80) && Time.realtimeSinceStartup < deadline) yield return null;
            var late = Session;
            int tick = Session.Tick;
            InputSystem.QueueStateEvent(botKb, new KeyboardState(Key.R));
            yield return new WaitForSecondsRealtime(0.2f);
            float shown = Hud.RestartHold;
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "restart_hold.png"));
            yield return null;
            InputSystem.QueueStateEvent(botKb, new KeyboardState());
            yield return new WaitForSecondsRealtime(0.5f);
            if (Session != late) Fail($"a short tap at tick {tick} restarted the level");
            if (shown <= 0f) Fail("no HOLD R TO RESTART progress while R was down");
            InputSystem.QueueStateEvent(botKb, new KeyboardState(Key.R));
            float peak = 0f;
            deadline = Time.realtimeSinceStartup + 0.9f;
            while (Time.realtimeSinceStartup < deadline && Session == late) { peak = Mathf.Max(peak, Hud.RestartHold); yield return null; }
            InputSystem.QueueStateEvent(botKb, new KeyboardState());
            yield return null;
            if (Session == late) Fail($"holding R for 0.9 s did not restart (hold peaked at {peak:0.00}, state {State}, held={Input.RestartHeld})");
            log.Add(ok ? $"PASS restart: early tap restarts; at tick {tick} a tap only shows the hold tag ({shown:0.00}); a hold restarts" : "FAIL restart");
            botDrivesFlow = false;
            result(ok);
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
                if (focusSeen >= 0.9f && Session.Aim == 0 && (s.SIdx[0] >= 13 || (s.SIdx[0] == 12 && s.SDir[0] > 0)))
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
