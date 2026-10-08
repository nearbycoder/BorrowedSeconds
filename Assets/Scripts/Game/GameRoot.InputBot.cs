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

            // hold Shift (focus slows time), rest the pointer on the middle of the lane (the
            // slider's track aims it, so there's no chasing the block), click once the block is in
            // the far alcove, well away from the pointer
            int borrowsBefore = Session.Cur.Loans;
            deadline = Time.realtimeSinceStartup + 20f;
            bool hovered = false;
            int parkedFrames = 0, parkedAimed = 0;
            InputSystem.QueueStateEvent(botKb, new KeyboardState(Key.LeftShift));
            while (ok && Time.realtimeSinceStartup < deadline)
            {
                var target = (Vector2)Cam.WorldToScreenPoint(Session.Board.At(def.Idx(7, 4)));
                botPointer = target;
                InputSystem.QueueStateEvent(botMouse, new MouseState { position = target });
                yield return null;
                hovered |= Session.Aim == 0;
                if (hovered && Session.State == LevelSession.Mode.Playing) { parkedFrames++; if (Session.Aim == 0) parkedAimed++; }
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
            if (ok && parkedAimed < parkedFrames) { ok = false; log.Add($"FAIL parked pointer: aimed at the slider on {parkedAimed} of {parkedFrames} frames"); }
            if (ok) log.Add($"ok   pointer parked mid-lane: aimed at the slider on {parkedAimed}/{parkedFrames} frames");
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
            bool wheelOk = false;
            yield return WheelAimPass(dir, log, r => wheelOk = r);
            kbOk &= wheelOk;

            bool padOk = false, rebindOk = false;
            yield return PadPass(dir, log, r => padOk = r);
            yield return RebindPass(dir, log, r => rebindOk = r);
            bool restartOk = false;
            yield return RestartPass(dir, log, r => restartOk = r);
            rebindOk &= restartOk;
            bool readyOk = false;
            yield return ReadyHoldPass(dir, log, r => readyOk = r);
            kbOk &= readyOk;
            bool toggleOk = false;
            yield return FocusTogglePass(dir, log, r => toggleOk = r);
            bool namesOk = false;
            yield return PadNamesPass(dir, log, r => namesOk = r);
            log.Add($"info frames {Time.frameCount} over {Time.realtimeSinceStartup:0.0}s real");
            bool all = kbOk && padOk && rebindOk && toggleOk && namesOk;
            log.Add(all ? "RESULT PASS keyboard + mouse, gamepad, rebound keyboard, restart, focus toggle, controller names and unplugging" : "RESULT FAIL");
            File.WriteAllLines(Path.Combine(dir, "inputbot.log"), log);
            Debug.Log("[InputBot] " + string.Join(" | ", log));
            Application.Quit(all ? 0 : 1);
        }

        /// <summary>
        /// The mouse wheel steps the aim on 1-3 (two sliders): down to the next, up to the previous,
        /// starting from the obstacle the pointer aims at; moving the pointer goes back to hover aim.
        /// </summary>
        IEnumerator WheelAimPass(string dir, List<string> log, System.Action<bool> result)
        {
            complete.Hide();
            pendingComplete = null;
            StartLevel(Catalog.Levels.FindIndex(l => l.Id == "1-3"), false);
            float deadline = Time.realtimeSinceStartup + 6f;
            while (Session.State != LevelSession.Mode.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            var steps = new List<string>();
            IEnumerator Point(Vector2 at)
            {
                botPointer = at;
                InputSystem.QueueStateEvent(botMouse, new MouseState { position = at });
                yield return null;
                yield return null;
            }
            IEnumerator Wheel(float y)
            {
                InputSystem.QueueStateEvent(botMouse, new MouseState { position = botPointer, scroll = new Vector2(0, y) });
                yield return null;
                InputSystem.QueueStateEvent(botMouse, new MouseState { position = botPointer });
                yield return null;
                yield return null;
                steps.Add($"{(y < 0 ? "down" : "up")}->{Session.Aim}");
            }
            bool ok = Session.Def.ObstacleCount == 2;
            yield return Point(new Vector2(12, 12)); // a corner: nothing under the pointer
            bool none = Session.Aim < 0;
            yield return Wheel(-120);
            int first = Session.Aim;
            yield return Wheel(-120);
            int second = Session.Aim;
            yield return Wheel(120);
            int back = Session.Aim;
            ok &= none && first >= 0 && second == 1 - first && back == first;
            // hover the other slider (following it, it moves), then wheel down from there
            int other = 1 - first;
            for (int i = 0; i < 4; i++) yield return Point(Cam.WorldToScreenPoint(Session.Board.ObstacleCenter(other)));
            int hovered = Session.Aim;
            yield return Wheel(-120);
            int fromHover = Session.Aim;
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "bot_wheel.png"));
            yield return null;
            yield return Point(Cam.WorldToScreenPoint(Session.Board.ObstacleCenter(other)) + new Vector3(6, 0, 0));
            int moved = Session.Aim;
            ok &= hovered == other && fromHover == first && moved == other;
            string detail = $"pointer on nothing aims {(none ? "nothing" : Session.Aim.ToString())}; {string.Join(", ", steps)}; hovering {hovered} then down -> {fromHover}; moving the pointer -> {moved}";
            log.Add(ok ? "ok   wheel aim on 1-3: " + detail : "FAIL wheel aim on 1-3: " + detail);
            result(ok);
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

        // ---------------------------------------------------------------- time waits pass

        /// <summary>
        /// Time waits for the first move (the hold a player gets, switched on for this pass): 1-1
        /// stays at tick 0 with its tag up while the pointer aims the slider and the aim tag reads a
        /// verdict; a tap of S starts the clock; a death's rewind doesn't hold again; a held R
        /// restarts into a fresh hold; and a click as the first action borrows on tick 0.
        /// </summary>
        IEnumerator ReadyHoldPass(string dir, List<string> log, System.Action<bool> result)
        {
            botDrivesFlow = true;
            forceReadyHold = true;
            bool ok = true;
            var notes = new List<string>();
            void Fail(string why) { if (ok) log.Add("FAIL time waits: " + why); ok = false; }
            // earlier passes leave the bot mouse disabled, and Down may be rebound (the rebind pass)
            InputSystem.EnableDevice(botMouse);
            botMouse.MakeCurrent();
            InputSystem.QueueStateEvent(botKb, new KeyboardState());
            var down = Input.Keys[(int)KeyAction.Down];
            complete.Hide();
            pendingComplete = null;
            StartLevel(0, false);
            var def = Session.Def;
            float deadline = Time.realtimeSinceStartup + 6f;
            while (Session.State != LevelSession.Mode.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            // the camera glides into place as a level starts: aim once it has settled
            yield return new WaitForSecondsRealtime(1.2f);
            var lane = (Vector2)Cam.WorldToScreenPoint(Session.Board.At(def.Idx(7, 4)));
            yield return BotMouse(lane, false);
            yield return new WaitForSecondsRealtime(0.8f);
            int aim = Session.Aim;
            string aimText = Hud.AimText;
            bool verdict = aimText.Contains("SAFE") || aimText.Contains("LETHAL") || aimText.Contains("HIT");
            if (Session.Tick != 0) Fail($"the clock ran with no input (tick {Session.Tick} after 2 s)");
            if (!Hud.ReadyShown || !Hud.ReadyText.Contains("TIME WAITS") || !Hud.ReadyText.Contains("click")) Fail($"no waiting tag (\"{Hud.ReadyText}\", shown={Hud.ReadyShown})");
            if (aim != 0 || !verdict) Fail($"the pointer on the lane did not aim with a forecast during the hold (aim {aim}, pointer {Input.Pointer} for {lane}, pick {Session.PickAt(lane)}, pad {Input.UsingGamepad})");
            notes.Add($"held at tick 0 for 2 s with the tag up; pointer aims {aim}, tag \"{aimText.Replace("\n", " / ")}\"");
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "ready_hold.png"));
            yield return null;
            yield return null;

            // a step starts the clock
            if (ok) yield return BotSteps(down, 1, log, r => { if (!r) Fail("the first step did not move"); });
            yield return new WaitForSecondsRealtime(0.5f);
            int afterStep = Session.Tick;
            if (afterStep < 5) Fail($"the clock did not start after a step (tick {afterStep})");
            if (Hud.ReadyShown) Fail("the waiting tag stayed up after the clock started");
            notes.Add($"a tap of {down} started it (tick {afterStep} 0.5 s later)");

            // walk into the lane and let the block take you: the rewind after a death doesn't hold
            if (ok) yield return BotSteps(down, 1, log, r => { if (!r) Fail("could not reach the lane's edge"); });
            InputSystem.QueueStateEvent(botKb, new KeyboardState(down)); // held: in as soon as the block has passed
            deadline = Time.realtimeSinceStartup + 8f;
            while (ok && Session.Deaths == 0 && Time.realtimeSinceStartup < deadline) yield return null;
            InputSystem.QueueStateEvent(botKb, new KeyboardState());
            while (ok && Session.State != LevelSession.Mode.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            int rewoundTo = Session.Tick;
            yield return new WaitForSecondsRealtime(0.6f);
            if (Session.Deaths == 0) Fail("standing in the lane never died");
            else if (Session.Tick <= rewoundTo) Fail($"after the rewind the clock held (tick {rewoundTo} -> {Session.Tick})");
            else notes.Add($"died, rewound to tick {rewoundTo}, ran on to {Session.Tick} with no input");

            // hold R: the fresh start waits again
            var before = Session;
            InputSystem.QueueStateEvent(botKb, new KeyboardState(Input.Keys[(int)KeyAction.Restart]));
            deadline = Time.realtimeSinceStartup + 1.5f;
            while (Session == before && Time.realtimeSinceStartup < deadline) yield return null;
            InputSystem.QueueStateEvent(botKb, new KeyboardState());
            if (Session == before) Fail("holding R did not restart");
            deadline = Time.realtimeSinceStartup + 6f;
            while (Session.State != LevelSession.Mode.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            yield return new WaitForSecondsRealtime(1f);
            if (Session.Tick != 0 || !Hud.ReadyShown) Fail($"the restart did not wait (tick {Session.Tick}, tag {Hud.ReadyShown})");
            else notes.Add("a held R restarted into a fresh hold");

            // a click as the first action borrows on tick 0
            int borrowedOn = -1;
            Session.Events += (st, evs) => { foreach (var e in evs) if (e.Type == Ev.Borrow && borrowedOn < 0) borrowedOn = st.Tick - 1; };
            lane = Cam.WorldToScreenPoint(Session.Board.At(def.Idx(7, 4)));
            yield return BotMouse(lane, false);
            yield return null;
            int clickAim = Session.Aim;
            InputSystem.QueueStateEvent(botMouse, new MouseState { position = lane, buttons = 1 });
            yield return null;
            InputSystem.QueueStateEvent(botMouse, new MouseState { position = lane });
            deadline = Time.realtimeSinceStartup + 2f;
            while (borrowedOn < 0 && Time.realtimeSinceStartup < deadline) yield return null;
            if (borrowedOn != 0) Fail($"a click as the first action did not borrow on tick 0 (aim {clickAim}, borrowed on {borrowedOn})");
            else notes.Add("a click as the first action borrowed on tick 0");

            log.Add(ok ? "PASS time waits on 1-1: " + string.Join("; ", notes) : "info time waits: " + string.Join("; ", notes));
            forceReadyHold = false;
            botDrivesFlow = false;
            result(ok);
        }

        // ---------------------------------------------------------------- focus toggle pass

        /// <summary>
        /// Settings > Toggle Focus: switched on through its menu row, a tap of the Focus key keeps time
        /// slowed after release and a second tap ends it; so do the right mouse button and LT. Pausing
        /// switches it off. Then the row is switched back and a tap only lasts as long as it's held.
        /// </summary>
        IEnumerator FocusTogglePass(string dir, List<string> log, System.Action<bool> result)
        {
            bool ok = true;
            var notes = new List<string>();
            void Fail(string why) { if (ok) log.Add("FAIL focus toggle " + why); ok = false; }
            complete.Hide();
            StartLevel(0, false);
            float deadline = Time.realtimeSinceStartup + 6f;
            while (Session.State != LevelSession.Mode.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            // the real Settings row (scripted runs ignore the saved option, so apply it by hand)
            Pause();
            OpenSettings(Flow.Paused);
            var row = settings.Menu.Items.Find(i => i.Label.StartsWith("Toggle Focus"));
            if (row == null) { Fail("no Toggle Focus row in Settings"); result(false); yield break; }
            row.Adjust(1);
            bool saved = Save.focusToggle && JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(Save)).focusToggle;
            if (!saved) Fail("the row did not set the saved option");
            Input.FocusToggle = Save.focusToggle;
            settings.Menu.Selected = settings.Menu.Items.IndexOf(row);
            yield return new WaitForSecondsRealtime(2f); // the window's intro, even at a low frame rate
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "focus_toggle_setting.png"));
            yield return null;
            CloseSettings();
            Resume();
            ShowControlHints(true);
            if (!Hud.HintsText.Contains("focus on/off")) Fail($"key hints don't say the focus key toggles: {Hud.HintsText}");
            Key fk = Input.Keys[(int)KeyAction.Focus];
            yield return new WaitForSecondsRealtime(2.5f); // the level-title intro, so the key hints are up

            // one source at a time: tap, wait a second (blend should hold at 1), tap, wait (back to 0)
            IEnumerator Tap(string name, System.Action down, System.Action up)
            {
                down(); yield return null; yield return null; up();
                yield return new WaitForSecondsRealtime(1f);
                float on = Session.FocusBlend;
                if (name == "key") { ScreenCapture.CaptureScreenshot(Path.Combine(dir, "focus_toggle_on.png")); yield return null; }
                down(); yield return null; yield return null; up();
                yield return new WaitForSecondsRealtime(0.6f);
                float off = Session.FocusBlend;
                notes.Add($"{name} {on:0.00}->{off:0.00}");
                if (on < 0.9f || off > 0.1f) Fail($"{name}: blend {on:0.00} a second after a tap, {off:0.00} after the second tap");
            }
            yield return Tap("key", () => InputSystem.QueueStateEvent(botKb, new KeyboardState(fk)), () => InputSystem.QueueStateEvent(botKb, new KeyboardState()));
            // earlier passes leave the bot mouse and pad disabled
            InputSystem.EnableDevice(botMouse);
            botMouse.MakeCurrent();
            yield return Tap("right-mouse", () => InputSystem.QueueStateEvent(botMouse, new MouseState { position = botPointer, buttons = 2 }),
                () => InputSystem.QueueStateEvent(botMouse, new MouseState { position = botPointer }));
            if (botPad != null)
            {
                InputSystem.EnableDevice(botPad);
                botPad.MakeCurrent();
                PadSet(new GamepadState());
                yield return Tap("LT", () => PadSet(new GamepadState { leftTrigger = 1f }), () => PadSet(new GamepadState()));
                InputSystem.DisableDevice(botPad);
            }

            // a pause switches it off
            yield return KeyTap(fk);
            yield return new WaitForSecondsRealtime(0.5f);
            Pause();
            yield return new WaitForSecondsRealtime(0.2f);
            Resume();
            yield return new WaitForSecondsRealtime(0.8f);
            if (Session.FocusBlend > 0.1f) Fail($"still focused after pause and resume (blend {Session.FocusBlend:0.00})");

            // back to hold: the same tap is gone once released
            Pause();
            OpenSettings(Flow.Paused);
            row.Adjust(1);
            Input.FocusToggle = Save.focusToggle;
            CloseSettings();
            Resume();
            yield return KeyTap(fk);
            yield return new WaitForSecondsRealtime(0.8f);
            if (Save.focusToggle || Session.FocusBlend > 0.1f) Fail($"hold mode: option={Save.focusToggle}, blend {Session.FocusBlend:0.00} after a released tap");
            ShowControlHints(true);
            log.Add(ok ? $"PASS focus toggle via Settings: {string.Join(", ", notes)}; pause ends it; hold mode restored" : "FAIL focus toggle");
            result(ok);
        }

        // ---------------------------------------------------------------- gamepad pass

        GamepadState padHeld; // buttons/axes held across taps (e.g. LT while pressing A)

        void PadSet(GamepadState st) => InputSystem.QueueStateEvent(botPad, st);

        /// <summary>Records every motor command sent to <paramref name="pad"/> (time, low, high): a
        /// virtual pad has no motors, so the bot reads the commands on their way to the device.</summary>
        static unsafe InputDeviceCommandDelegate MotorSpy(InputDevice pad, List<(float t, float low, float high)> sent)
        {
            // the Input System's rumble command (internal): 'RMBL', then the low and high motor speeds
            var rumble = new UnityEngine.InputSystem.Utilities.FourCC('R', 'M', 'B', 'L');
            return (device, command) =>
            {
                if (device == pad && command->type == rumble && command->payloadSizeInBytes >= 8)
                {
                    var speeds = (float*)((byte*)command + InputDeviceCommand.BaseCommandSize);
                    sent.Add((Time.realtimeSinceStartup, speeds[0], speeds[1]));
                }
                return null; // not handled: it still goes on to the device
            };
        }

        static bool HasPulse(List<(float t, float low, float high)> sent, int from, float low, float high)
        {
            for (int i = from; i < sent.Count; i++)
                if (Mathf.Approximately(sent[i].low, low) && Mathf.Approximately(sent[i].high, high)) return true;
            return false;
        }

        static string Pulses(List<(float t, float low, float high)> sent, int from)
        {
            var parts = new List<string>();
            for (int i = from; i < sent.Count; i++) parts.Add($"{sent[i].low:0.##}/{sent[i].high:0.##}");
            return parts.Count == 0 ? "none" : string.Join(" ", parts);
        }

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
            var motors = new List<(float t, float low, float high)>();
            var spy = MotorSpy(botPad, motors);
            InputSystem.onDeviceCommand += spy;
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
            int motorsAtStart = motors.Count;
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

            // Controller vibration: the borrow, the debt and the win each sent a pulse, and each
            // stopped by itself; with the setting off a borrow sends nothing; a pause stops the motors
            if (ok)
            {
                yield return new WaitForSecondsRealtime(0.5f);
                string played = Pulses(motors, motorsAtStart);
                bool pulses = HasPulse(motors, motorsAtStart, 0.2f, 0.5f) && HasPulse(motors, motorsAtStart, 0.5f, 0.25f) && HasPulse(motors, motorsAtStart, 0.35f, 0.7f);
                bool quiet = motors.Count > motorsAtStart && motors[motors.Count - 1].low == 0f && motors[motors.Count - 1].high == 0f && rumble.Active == null;
                complete.Hide();
                pendingComplete = null;
                Save.vibration = false;
                ApplySettings();
                StartLevel(0, false);
                deadline = Time.realtimeSinceStartup + 6f;
                while (Session.State != LevelSession.Mode.Playing && Time.realtimeSinceStartup < deadline) yield return null;
                int offFrom = motors.Count;
                yield return PadTap(GamepadButton.RightShoulder);
                yield return PadTap(GamepadButton.South);
                deadline = Time.realtimeSinceStartup + 2f;
                while (Session.Cur.Loans == 0 && Time.realtimeSinceStartup < deadline) yield return null;
                yield return new WaitForSecondsRealtime(0.2f);
                bool offBorrowed = Session.Cur.Loans > 0, offSilent = motors.Count == offFrom;
                Save.vibration = true;
                ApplySettings(); // switching it back on gives one short pulse
                bool sample = HasPulse(motors, offFrom, 0.35f, 0.35f);
                yield return new WaitForSecondsRealtime(0.3f);
                StartLevel(0, false);
                deadline = Time.realtimeSinceStartup + 6f;
                while (Session.State != LevelSession.Mode.Playing && Time.realtimeSinceStartup < deadline) yield return null;
                Buzz(0.9f, 0.9f, 5f); // a long pulse (a default's strength), then Start mid-pulse
                int pauseFrom = motors.Count;
                yield return PadTap(GamepadButton.Start);
                bool pauseStops = State == Flow.Paused && rumble.Active == null && motors.Count > pauseFrom
                    && motors[motors.Count - 1].low == 0f && motors[motors.Count - 1].high == 0f;
                yield return new WaitForSecondsRealtime(0.6f);
                yield return PadTap(GamepadButton.East);
                yield return new WaitForSecondsRealtime(0.3f);
                string detail = $"1-1 sent {played}; quiet after={quiet}; with vibration off a borrow (taken={offBorrowed}) sent nothing={offSilent}; "
                    + $"switching it on pulsed={sample}; Start mid-pulse paused and stopped the motors={pauseStops}";
                if (pulses && quiet && offBorrowed && offSilent && sample && pauseStops) log.Add("ok   pad: vibration: " + detail);
                else Fail("vibration: " + detail);
            }

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

            // the replay at the viewer's pace: LT slows it, X scrubs it back, and once let go it
            // carries on from there and still wins at par
            if (ok)
            {
                yield return new WaitForSecondsRealtime(0.8f);
                yield return PadTap(GamepadButton.Start);
                yield return new WaitForSecondsRealtime(0.8f);
                yield return PadTap(GamepadButton.DpadDown);
                yield return new WaitForSecondsRealtime(0.15f);
                yield return PadTap(GamepadButton.DpadDown);
                yield return new WaitForSecondsRealtime(0.5f);
                yield return PadTap(GamepadButton.South);
                deadline = Time.realtimeSinceStartup + 3f;
                while (State != Flow.Watching && Time.realtimeSinceStartup < deadline) yield return null;
                var watched = Session;
                while (State == Flow.Watching && Session.State != LevelSession.Mode.Playing && Time.realtimeSinceStartup < deadline + 3f) yield return null;
                yield return new WaitForSecondsRealtime(0.5f);
                float Rate(int t0, float r0) => (Session.Tick - t0) / Mathf.Max(0.01f, Time.realtimeSinceStartup - r0);
                int t = Session.Tick; float r = Time.realtimeSinceStartup;
                yield return new WaitForSecondsRealtime(1f);
                float normal = Rate(t, r);
                padHeld.leftTrigger = 1f;
                PadSet(padHeld);
                yield return new WaitForSecondsRealtime(0.4f); // Focus blends in
                t = Session.Tick; r = Time.realtimeSinceStartup;
                yield return new WaitForSecondsRealtime(1.5f);
                float slow = Rate(t, r);
                padHeld.leftTrigger = 0f;
                PadSet(padHeld);
                yield return new WaitForSecondsRealtime(0.3f);
                int beforeRewind = Session.Tick;
                padHeld = padHeld.WithButton(GamepadButton.West);
                PadSet(padHeld);
                yield return new WaitForSecondsRealtime(0.25f);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, "pad_5_watch-rewind.png"));
                yield return new WaitForSecondsRealtime(0.4f);
                padHeld = new GamepadState();
                PadSet(padHeld);
                yield return null;
                yield return null;
                int afterRewind = Session.Tick;
                int par = Catalog.SolutionFor(def)?.Par ?? -1;
                deadline = Time.realtimeSinceStartup + 30f;
                while (Session == watched && Session.State != LevelSession.Mode.Won && Time.realtimeSinceStartup < deadline) yield return null;
                int won = Session == watched && Session.State == LevelSession.Mode.Won ? Session.Tick : -1;
                deadline = Time.realtimeSinceStartup + 5f;
                while (State == Flow.Watching && Time.realtimeSinceStartup < deadline) yield return null;
                bool handedBack = State == Flow.Playing && Session.Autoplay == null;
                string pace = $"{normal:0.0} ticks/s, {slow:0.0} with LT; X rewound tick {beforeRewind} -> {afterRewind}; won at {won} (par {par}); handed back={handedBack}";
                if (slow > normal * 0.5f || afterRewind > beforeRewind - 5 || won != par || !handedBack) Fail("watching at your own pace: " + pace);
                else log.Add("ok   pad: Watch solution at your own pace: " + pace);
            }
            InputSystem.onDeviceCommand -= spy;
            log.Add(ok ? "PASS 1-1 and the menus played through a virtual gamepad" : "FAIL gamepad");
            botDrivesFlow = false;
            result(ok);
        }

        // ---------------------------------------------------------------- controller names pass

        /// <summary>Sets one control on any pad, in the pad's own state format (a DualSense's report
        /// isn't a GamepadState, and its layout drops events in other formats).</summary>
        static void PadWrite(Gamepad pad, InputControl<float> control, float value)
        {
            using (StateEvent.From(pad, out var ptr))
            {
                control.WriteValueIntoEvent(value, ptr);
                InputSystem.QueueEvent(ptr);
            }
        }

        /// <summary>
        /// A virtual DualSense, DualShock 4, Switch Pro controller and plain gamepad in turn: every
        /// place that names a pad button (key hints, 1-1's tip folded and open, the restart tag, the
        /// onboarding prompts, the title, Ledger and Settings footers) must use that pad's names.
        /// Then the pad is unplugged mid-level, which must pause it and hand the hints to the keyboard.
        /// </summary>
        IEnumerator PadNamesPass(string dir, List<string> log, System.Action<bool> result)
        {
            botDrivesFlow = true;
            bool ok = true;
            var notes = new List<string>();
            InputSystem.DisableDevice(botKb);
            InputSystem.DisableDevice(botMouse);
            if (botPad != null) InputSystem.DisableDevice(botPad);
            var kinds = new (string name, System.Func<Gamepad> add, PadNames want)[]
            {
                ("DualSense", () => InputSystem.AddDevice<UnityEngine.InputSystem.DualShock.DualSenseGamepadHID>("BotDualSense"), PadNames.DualSense),
                ("DualShock 4", () => InputSystem.AddDevice<UnityEngine.InputSystem.DualShock.DualShock4GamepadHID>("BotDualShock4"), PadNames.DualShock4),
                ("Switch Pro", () => InputSystem.AddDevice<UnityEngine.InputSystem.Switch.SwitchProControllerHID>("BotSwitchPro"), PadNames.Nintendo),
                ("plain gamepad", () => InputSystem.AddDevice<Gamepad>("BotPlainPad"), PadNames.Xbox),
            };
            int learned = Save.learned;
            Gamepad pad = null;
            foreach (var (name, add, w) in kinds)
            {
                bool kindOk = true;
                void Fail(string why) { kindOk = false; log.Add($"FAIL pad names ({name}): {why}"); }
                pad = add();
                pad.MakeCurrent();
                complete.Hide();
                pendingComplete = null;
                Save.learned = 0;
                promptDemo = true; // the onboarding pills, which scripted runs normally hide
                StartLevel(0, false);
                yield return new WaitForSecondsRealtime(2.6f); // the title intro, then the tip slides in
                PadWrite(pad, pad.rightTrigger, 1f); // an unused trigger: switches the hints to the pad
                yield return null;
                PadWrite(pad, pad.rightTrigger, 0f);
                yield return null;
                yield return null;
                if (!Input.UsingGamepad || Input.Pad != w) Fail($"pad not recognised: using pad={Input.UsingGamepad}, names={Input.Pad.Family}/{Input.Pad.Select}");

                string hints = Hud.HintsText ?? "";
                foreach (var want in new[] { $"<b>{w.Shoulders}</b> aim", $"<b>{w.South}</b> borrow", $"<b>{w.LeftTrigger}</b> focus", $"<b>{w.West}</b> rewind",
                    $"<b>{w.North}</b> restart", $"<b>{w.Select}</b> hint", $"<b>{w.Start}</b> pause" })
                    if (!hints.Contains(want)) Fail($"key hints lack '{want}': {hints}");
                string tip = Hud.TipText;
                if (!tip.Contains($"<b>{w.Shoulders}</b> aims") || !tip.Contains($"<b>{w.South}</b> freezes") || tip.Contains("{")) Fail("1-1's tip: " + tip);
                if (!Hud.RestartLabel.Contains($"HOLD {w.North.ToUpperInvariant()} TO")) Fail("restart tag: " + Hud.RestartLabel);
                string movePrompt = Prompts.PlayerText;
                if (!movePrompt.Contains($">{w.Stick}<")) Fail("move prompt: " + movePrompt);
                Save.learned = UI.Prompts.Move;
                yield return new WaitForSecondsRealtime(0.5f);
                string borrowPrompt = Prompts.ObstacleText;
                if (!borrowPrompt.Contains($">{w.South}</color>  freeze it") || !borrowPrompt.Contains($">{w.Shoulders}</color>  aim")) Fail("borrow prompt: " + borrowPrompt);
                if (name == "DualSense") { ScreenCapture.CaptureScreenshot(Path.Combine(dir, "names_dualsense_level.png")); yield return null; }
                Save.learned = UI.Prompts.Move | UI.Prompts.Borrow;
                yield return new WaitForSecondsRealtime(0.3f);
                string focusPrompt = Prompts.PlayerText;
                if (!focusPrompt.Contains($">{w.LeftTrigger}</color>  slow time")) Fail("focus prompt: " + focusPrompt);
                PadWrite(pad, pad.selectButton, 1f); // fold the tip
                yield return null;
                PadWrite(pad, pad.selectButton, 0f);
                yield return null;
                yield return null;
                if (!Hud.TipText.Contains($">{w.Select}<")) Fail("folded tip: " + Hud.TipText);
                promptDemo = false;

                // menus: the title, the Ledger and Settings footers name the pad's buttons
                ShowTitle();
                yield return new WaitForSecondsRealtime(2.8f);
                if (title.Footer != $"<b>{w.Dpad}</b> choose <b>{w.South}</b> confirm") Fail("title footer: " + title.Footer);
                ShowLevels(4);
                yield return new WaitForSecondsRealtime(name == "DualSense" ? 2.5f : 1.2f); // the screenshot waits out the intro
                string ledger = levels.Footer ?? "";
                if (!ledger.Contains($"<b>{w.East}</b> back") || (Catalog.Levels.Count > 20 && !ledger.Contains($"<b>{w.Shoulders}</b> page"))) Fail("Ledger footer: " + ledger);
                if (name == "DualSense") { ScreenCapture.CaptureScreenshot(Path.Combine(dir, "names_dualsense_ledger.png")); yield return null; }
                levels.Hide();
                OpenSettings(Flow.Title);
                yield return new WaitForSecondsRealtime(1.4f);
                if (settings.Footer != $"<b>{w.Dpad}</b> choose and adjust <b>{w.East}</b> back") Fail("Settings footer: " + settings.Footer);
                settings.Hide();
                // How to play lists the pad's buttons
                OpenHowTo(Flow.Title);
                yield return new WaitForSecondsRealtime(1.4f);
                string howKeys = howto.ControlsText ?? "";
                foreach (var want in new[] { $"<b>{w.Stick}</b> or <b>{w.Dpad}</b> move", $"<b>{w.Shoulders}</b> aim", $"<b>{w.South}</b> borrow", $"<b>{w.LeftTrigger}</b> hold",
                    $"<b>{w.West}</b> hold: rewind", $"<b>{w.North}</b> restart", $"<b>{w.Select}</b> show", $"<b>{w.Start}</b> pause" })
                    if (!howKeys.Contains(want)) Fail($"How to play lacks '{want}': {howKeys}");
                if (howto.Footer != $"<b>{w.East}</b> back") Fail("How to play footer: " + howto.Footer);
                if (name == "DualSense") { ScreenCapture.CaptureScreenshot(Path.Combine(dir, "names_dualsense_howto.png")); yield return null; }
                howto.Hide();
                title.Hide();
                notes.Add($"{name} {(kindOk ? "ok" : "FAIL")}");
                ok &= kindOk;
                if (name != "plain gamepad") InputSystem.RemoveDevice(pad);
            }

            // unplugging the pad mid-level pauses it, and the hints go back to the keyboard
            StartLevel(0, false);
            yield return new WaitForSecondsRealtime(1.5f);
            PadWrite(pad, pad.rightTrigger, 1f);
            yield return null;
            PadWrite(pad, pad.rightTrigger, 0f);
            yield return new WaitForSecondsRealtime(0.3f);
            bool before = State == Flow.Playing && Input.UsingGamepad;
            InputSystem.RemoveDevice(pad);
            yield return null;
            yield return null;
            int tick = Session.Tick;
            yield return new WaitForSecondsRealtime(1f);
            bool paused = State == Flow.Paused && Session.Tick == tick;
            bool kbHints = !Input.UsingGamepad && (Hud.HintsText ?? "").Contains("<b>Esc</b> pause");
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "names_unplugged.png"));
            yield return null;
            if (!before || !paused || !kbHints)
            {
                ok = false;
                log.Add($"FAIL unplug: playing on the pad before={before}, paused={State == Flow.Paused}, tick {tick} -> {Session.Tick}, keyboard hints={kbHints}");
            }
            else notes.Add($"unplugged at tick {tick}: paused, tick held 1 s, hints back on the keyboard");
            pause.Hide();
            Resume();
            Save.learned = learned;
            InputSystem.EnableDevice(botKb);
            InputSystem.EnableDevice(botMouse);
            botDrivesFlow = false;
            log.Add(ok ? $"PASS controller names: {string.Join("; ", notes)}" : "FAIL controller names");
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
