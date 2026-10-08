using System.Collections;
using System.Collections.Generic;
using System.IO;
using BorrowedSeconds.Sim;
using UnityEngine;

namespace BorrowedSeconds.Game
{
    /// <summary>
    /// -bsChecks DIR: behaviour checks in the real game loop, for things a solver replay never
    /// exercises (dying, rewinding, menus). Writes checks.log with PASS/FAIL per check and quits.
    /// </summary>
    public sealed partial class GameRoot
    {
        IEnumerator Checks(string dir, string only)
        {
            Directory.CreateDirectory(dir);
            var log = new List<string>();
            int fail = 0;
            float started = Time.realtimeSinceStartup, lastReport = started;
            // each line ends with the wall time since the previous one, so a slow run shows where it went
            void Report(string name, bool ok, string detail)
            {
                if (!ok) fail++;
                float now = Time.realtimeSinceStartup;
                log.Add($"{(ok ? "PASS" : "FAIL")} {name}: {detail} [{now - lastReport:0.0}s]");
                lastReport = now;
                Debug.Log("[Checks] " + log[log.Count - 1]);
            }
            // -bsOnly a,b runs just the checks whose names start with one of these
            bool Want(string name) => only == null || System.Array.Exists(only.Split(','), o => name.StartsWith(o));

            if (Want("default-rewind"))
                foreach (var id in new[] { "1-2", "2-1", "4-5" })
                    yield return CheckDefaultRewind(dir, id, Report);
            if (Want("death-report"))
                foreach (var (id, noun) in new[] { ("1-2", "the block"), ("2-1", "the beam"), ("3-1", "a rotor arm") })
                {
                    yield return CheckDeathReport(dir, id, true, noun, Report);
                    yield return CheckDeathReport(dir, id, false, noun, Report);
                }
            if (Want("ready-hold")) yield return CheckReadyHold(dir, Report);
            if (Want("focus")) yield return CheckFocusPause(dir, Report);
            if (Want("hint")) yield return CheckHint(dir, Report);
            if (Want("ledger-tips")) yield return CheckLedgerTips(dir, Report);
            if (Want("channels"))
            {
                yield return CheckChannels(dir, "6-4", new[] { 1, 1 }, Report);
                yield return CheckChannels(dir, "2-2", new[] { 2 }, Report);
            }
            if (Want("game-speed")) yield return CheckSpeed(dir, Report);
            if (Want("key-bindings")) yield return CheckBindings(dir, Report);
            if (Want("audio")) yield return RecordAudio(dir, Report);
            if (Want("chapter-cards")) yield return CheckChapterCards(dir, Report);
            if (Want("watch")) yield return CheckWatch(dir, Report);
            if (Want("run-watch")) yield return CheckRunWatch(Report);
            if (Want("forecast")) yield return CheckForecast(dir, Report);
            if (Want("aim-reach")) yield return CheckAimReach(dir, Report);
            if (Want("display")) yield return CheckDisplay(dir, Report);
            if (Want("how-to-play")) yield return CheckHowTo(dir, Report);
            if (Want("erase-progress")) yield return CheckErase(dir, Report);
            if (Want("hud-size")) yield return CheckHudSize(dir, Report);
            if (Want("background-mute")) yield return CheckBackgroundMute(dir, Report);
            if (Want("medal-pace")) yield return CheckMedalPace(dir, Report);
            // these need a real compositor: only inside the private one (Tools/nested.sh), never on a shared desktop
            bool nested = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-bsNested") >= 0;
            foreach (var name in new[] { "real-focus", "fullscreen" })
            {
                if (!Want(name)) continue;
                if (!nested) { log.Add($"SKIP {name}: needs the private compositor (Tools/nested.sh)"); continue; }
                yield return name == "real-focus" ? CheckRealFocus(dir, Report) : CheckFullscreen(dir, Report);
            }

            log.Add($"done fail={fail} in {Time.realtimeSinceStartup - started:0}s");
            File.WriteAllLines(Path.Combine(dir, "checks.log"), log);
            yield return new WaitForSecondsRealtime(0.3f);
            Application.Quit(fail == 0 ? 0 : 1);
        }

        /// <summary>
        /// Plays three levels with sound at the default volumes and writes the audio event log
        /// (audio.log) for Tools/audio/balance.py, which checks.sh runs afterwards: the
        /// solutions of 1-5 and 4-5 (borrow, freeze, thaw, dial latches, exit, win) and a thaw
        /// death on 1-2.
        /// </summary>
        IEnumerator RecordAudio(string dir, System.Action<string, bool, string> report)
        {
            var defaults = new SaveData();
            float m0 = Audio.Master, m1 = Audio.Music, m2 = Audio.Effects;
            Audio.Master = defaults.master;
            Audio.Music = defaults.music;
            Audio.Effects = defaults.sfx;
            var writer = new StreamWriter(Path.Combine(dir, "audio.log"));
            BorrowedSeconds.Audio.AudioDirector.Log = writer;
            writer.WriteLine($"V {Clock.Now.ToString(System.Globalization.CultureInfo.InvariantCulture)} 60");
            BorrowedSeconds.Audio.AudioDirector.LogPlaying();
            int played = 0;
            foreach (var id in new[] { "1-5", "4-5", "1-2" })
            {
                int index = Catalog.Levels.FindIndex(l => l.Id == id);
                StartLevel(index, false);
                Hud.SkipIntro();
                bool death = id == "1-2";
                Session.Autoplay = death ? FindThawDeath(Catalog.Levels[index], out _) : Catalog.SolutionFor(Catalog.Levels[index]).Actions;
                var run = new RunWatch(Session);
                while (run.Alive() && (death ? Session.Deaths == 0 : Session.State != LevelSession.Mode.Won)) yield return null;
                yield return new WaitForSecondsRealtime(1.5f); // let the last stinger ring out
                if (death ? Session.Deaths > 0 : Session.State == LevelSession.Mode.Won) played++;
            }
            BorrowedSeconds.Audio.AudioDirector.Log = null;
            writer.Close();
            Audio.Master = m0;
            Audio.Music = m1;
            Audio.Effects = m2;
            report("audio-log", played == 3, $"{played}/3 scripted runs recorded to audio.log (checks.sh runs Tools/audio/balance.py on it)");
        }

        /// <summary>
        /// The binding table's rules: rebinding to a taken key swaps instead of duplicating, reserved
        /// keys are refused, and a damaged save (unknown, reserved or duplicate names) loads as
        /// defaults for those actions; bindings survive a save round trip. Screenshots the Controls page.
        /// </summary>
        IEnumerator CheckBindings(string dir, System.Action<string, bool, string> report)
        {
            var keys = KeyBindings.DefaultKeys();
            int lost = KeyBindings.Assign(keys, KeyAction.Hint, UnityEngine.InputSystem.Key.Space); // Space is Borrow's
            bool swapped = lost == (int)KeyAction.Borrow && keys[(int)KeyAction.Hint] == UnityEngine.InputSystem.Key.Space
                && keys[(int)KeyAction.Borrow] == UnityEngine.InputSystem.Key.H;
            bool reserved = KeyBindings.Reserved(UnityEngine.InputSystem.Key.Escape) && KeyBindings.Reserved(UnityEngine.InputSystem.Key.UpArrow)
                && !KeyBindings.Reserved(UnityEngine.InputSystem.Key.F);
            var damaged = KeyBindings.Load(new[] { "Banana", "Escape", "W", "D", "Space" });
            bool repaired = damaged[0] == UnityEngine.InputSystem.Key.W && damaged[1] == UnityEngine.InputSystem.Key.S && damaged[2] == UnityEngine.InputSystem.Key.A;
            var save = new SaveData { keys = KeyBindings.Save(keys) };
            var back = KeyBindings.Load(JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(save)).keys);
            bool roundTrip = System.Linq.Enumerable.SequenceEqual(back, keys);
            bool unique = System.Linq.Enumerable.Count(System.Linq.Enumerable.Distinct(keys)) == keys.Length;
            StartLevel(Catalog.Levels.FindIndex(l => l.Id == "1-3"), false);
            Pause();
            OpenSettings(Flow.Paused);
            OpenControls();
            controls.ListenFor(KeyAction.Rewind);
            yield return new WaitForSecondsRealtime(1.3f);
            yield return Shot(dir, "controls_listening");
            CloseControls();
            CloseSettings();
            Resume();
            report("key-bindings", swapped && reserved && repaired && roundTrip && unique,
                $"swap={swapped} reserved={reserved} damaged-save repaired={repaired} round-trip={roundTrip} unique={unique}");
        }

        /// <summary>Every chapter's first level opens on its card (screenshot of the newest chapter's).</summary>
        IEnumerator CheckChapterCards(string dir, System.Action<string, bool, string> report)
        {
            int chapters = 0, shown = 0;
            for (int i = 0; i < Catalog.Levels.Count; i++)
            {
                if (i > 0 && Catalog.Levels[i - 1].Chapter == Catalog.Levels[i].Chapter) continue;
                chapters++;
                bool last = Catalog.Levels[i].Chapter == Catalog.Levels[Catalog.Levels.Count - 1].Chapter;
                StartLevel(i, true);
                if (State == Flow.Card) shown++;
                if (last)
                {
                    yield return new WaitForSecondsRealtime(2.9f); // the epigraph has typed out; cards leave at 3.6 s
                    var info = LevelCatalog.Chapters[Catalog.Levels[i].Chapter - 1];
                    yield return Shot(dir, $"chapter-card_{BorrowedSeconds.UI.Hud.Roman(Catalog.Levels[i].Chapter)}_{info.Title}");
                }
                card.Hide();
                yield return null;
            }
            report("chapter-cards", shown == chapters, $"{shown}/{chapters} chapters open on their card");
        }

        /// <summary>Losing window focus mid-level opens the pause menu and stops the clock.</summary>
        IEnumerator CheckFocusPause(string dir, System.Action<string, bool, string> report)
        {
            int index = Catalog.Levels.FindIndex(l => l.Id == "1-3");
            LoadLevel(index);
            State = Flow.Playing;
            Hud.SetVisible(true);
            Session.IntroTime = 0.2f;
            Session.Autoplay = Catalog.SolutionFor(Catalog.Levels[index]).Actions;
            float deadline = Time.realtimeSinceStartup + 10f;
            while (Session.Tick < 40 && Time.realtimeSinceStartup < deadline) yield return null;
            FocusLost(true);
            int tick = Session.Tick;
            yield return new WaitForSecondsRealtime(2f);
            bool ok = State == Flow.Paused && pause.Visible && Session.Tick == tick;
            report("focus-pause", ok, $"paused at tick {tick}; after 2 s state={State}, menu={pause.Visible}, tick={Session.Tick}");
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "focus-pause.png"));
            yield return null;
            yield return null;
            Resume();
            yield return new WaitForSecondsRealtime(0.5f);
            report("focus-resume", State == Flow.Playing && Session.Tick > tick, $"resumed: state={State}, tick={Session.Tick}");
        }

        /// <summary>
        /// A real focus loss, not a call to the handler: mid-level the game opens a second window
        /// (kdialog) in the private compositor, which takes focus as alt-tabbing would. The level
        /// pauses and the mix fades out; closing that window gives focus back, the mix fades in and
        /// the level stays paused until resumed.
        /// </summary>
        IEnumerator CheckRealFocus(string dir, System.Action<string, bool, string> report)
        {
            string saved = JsonUtility.ToJson(Save);
            Save.muteBackground = true;
            forceBackgroundMute = true;
            realFocusCheck = true;
            AudioListener.volume = 1f;
            int index = Catalog.Levels.FindIndex(l => l.Id == "1-3");
            StartLevel(index, false);
            Hud.SkipIntro();
            Session.Autoplay = Catalog.SolutionFor(Catalog.Levels[index]).Actions;
            float deadline = Time.realtimeSinceStartup + 10f;
            while ((Session.Tick < 30 || !appFocused) && Time.realtimeSinceStartup < deadline) yield return null;
            bool focusedAtStart = appFocused;
            int tickBefore = Session.Tick;
            System.Diagnostics.Process other = null;
            string error = "";
            try
            {
                other = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("kdialog",
                    "--title \"Focus check\" --msgbox \"Borrowed Seconds focus check: this window takes focus.\"") { UseShellExecute = false });
            }
            catch (System.Exception e) { error = e.Message; }
            float t0 = Time.realtimeSinceStartup;
            deadline = t0 + 15f;
            while (appFocused && Time.realtimeSinceStartup < deadline) yield return null;
            bool lost = !appFocused;
            float lostAfter = Time.realtimeSinceStartup - t0;
            int tickLost = Session.Tick;
            yield return new WaitForSecondsRealtime(1.5f);
            bool paused = State == Flow.Paused && pause.Visible && Session.Tick == tickLost;
            float volAway = AudioListener.volume;
            yield return Shot(dir, "real-focus_away");
            try { if (other != null && !other.HasExited) other.Kill(); } catch (System.Exception e) { error += " " + e.Message; }
            t0 = Time.realtimeSinceStartup;
            deadline = t0 + 15f;
            while (!appFocused && Time.realtimeSinceStartup < deadline) yield return null;
            bool regained = appFocused;
            float regainedAfter = Time.realtimeSinceStartup - t0;
            yield return new WaitForSecondsRealtime(1.0f);
            float volBack = AudioListener.volume;
            bool stillPaused = State == Flow.Paused && Session.Tick == tickLost;
            if (State == Flow.Paused) Resume();
            yield return new WaitForSecondsRealtime(0.5f);
            bool resumed = State == Flow.Playing && Session.Tick > tickLost;
            other?.Dispose();
            realFocusCheck = false;
            forceBackgroundMute = false;
            AudioListener.volume = 1f;
            JsonUtility.FromJsonOverwrite(saved, Save);
            report("real-focus", focusedAtStart && lost && paused && Mathf.Approximately(volAway, 0f) && regained && Mathf.Approximately(volBack, 1f) && stillPaused && resumed,
                $"focused at start={focusedAtStart} (tick {tickBefore}); a second window took focus after {lostAfter:0.0}s={lost}: paused with the tick held={paused}, volume {volAway:0.00}; "
                + $"closed, focus back after {regainedAfter:0.0}s={regained}: volume {volBack:0.00}, still paused={stillPaused}; resumed={resumed}"
                + (error.Length > 0 ? $"; error: {error}" : ""));
        }

        /// <summary>
        /// Settings > Display in a real compositor: Fullscreen fills the output, and a window size
        /// brings the window back. Logs what the player reports for the desktop, its modes and the
        /// fullscreen size, which shows the resolution it renders at under fractional scaling.
        /// </summary>
        IEnumerator CheckFullscreen(string dir, System.Action<string, bool, string> report)
        {
            var saved = JsonUtility.ToJson(Save);
            int w0 = Screen.width, h0 = Screen.height;
            forceDisplay = true;
            StartLevel(Catalog.Levels.FindIndex(l => l.Id == "4-5"), false);
            Session.Autoplay = Catalog.SolutionFor(Session.Def).Actions;
            Hud.SkipIntro();
            Pause();
            OpenSettings(Flow.Paused);
            var desk = DisplayOptions.Desktop;
            var fit = DisplayOptions.Fitting(desk);
            var row = settings.Menu.Items.Find(i => i.Label == "Display");
            settings.Menu.Selected = settings.Menu.Items.IndexOf(row);
            DisplayOptions.Choose(Save, desk, 1);
            ApplyDisplay(true);
            IEnumerator Until(System.Func<bool> done)
            {
                float t0 = Time.realtimeSinceStartup;
                int frames = 0;
                while (!done() && (Time.realtimeSinceStartup < t0 + 8f || frames < 60)) { frames++; yield return null; }
            }
            yield return Until(() => Screen.width == fit[0].x && Screen.height == fit[0].y);
            bool windowed = Screen.fullScreenMode == FullScreenMode.Windowed && Screen.width == fit[0].x;

            row.Adjust(-1); // one step left of the smallest window: Fullscreen
            float tf = Time.realtimeSinceStartup;
            yield return Until(() => Screen.fullScreenMode == FullScreenMode.FullScreenWindow && Screen.width == desk.x && Screen.height == desk.y);
            float fullAfter = Time.realtimeSinceStartup - tf;
            yield return new WaitForSecondsRealtime(0.5f);
            bool full = Save.fullscreen && Screen.fullScreenMode == FullScreenMode.FullScreenWindow && Screen.width == desk.x && Screen.height == desk.y && row.Value() == "Fullscreen";
            string fullSize = $"{Screen.width}x{Screen.height}";
            var cur = Screen.currentResolution;
            var modes = new List<string>();
            foreach (var r in Screen.resolutions) { string m = $"{r.width}x{r.height}"; if (!modes.Contains(m)) modes.Add(m); }
            string display = $"Display.main system {Display.main.systemWidth}x{Display.main.systemHeight}, rendering {Display.main.renderingWidth}x{Display.main.renderingHeight}, dpi {Screen.dpi:0}";
            CloseSettings();
            Resume();
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Shot(dir, "fullscreen_level");

            Pause();
            OpenSettings(Flow.Paused);
            settings.Menu.Selected = settings.Menu.Items.IndexOf(row);
            row.Adjust(1); // back to the smallest window
            tf = Time.realtimeSinceStartup;
            yield return Until(() => Screen.fullScreenMode == FullScreenMode.Windowed && Screen.width == fit[0].x && Screen.height == fit[0].y);
            float backAfter = Time.realtimeSinceStartup - tf;
            bool back = !Save.fullscreen && Screen.fullScreenMode == FullScreenMode.Windowed && Screen.width == fit[0].x && Screen.height == fit[0].y;
            string backSize = $"{Screen.width}x{Screen.height}";

            Save.fullscreen = false;
            Save.windowW = w0;
            Save.windowH = h0;
            Screen.SetResolution(w0, h0, FullScreenMode.Windowed);
            float until = Time.realtimeSinceStartup + 3f;
            while ((Screen.width != w0 || Screen.height != h0) && Time.realtimeSinceStartup < until) yield return null;
            CloseSettings();
            Resume();
            JsonUtility.FromJsonOverwrite(saved, Save);
            forceDisplay = false;
            report("fullscreen", windowed && full && back,
                $"desktop {desk.x}x{desk.y} (current {cur.width}x{cur.height} @ {cur.refreshRateRatio.value:0} Hz; modes {string.Join(" ", modes)}; {display}); "
                + $"window {fit[0].x}x{fit[0].y}={windowed}; Fullscreen -> {fullSize} in {fullAfter:0.0}s={full}; back to a window -> {backSize} in {backAfter:0.0}s={back}");
        }

        /// <summary>
        /// The thaw forecast is spelled out, not only coloured: every frame the HUD's verdict matches
        /// the ghost's, the term line names it while a debt runs and the aim tag names it while aiming.
        /// Runs a thaw death on 1-2 (lethal) and 1-5's solution (safe, dial latches); screenshots each.
        /// </summary>
        IEnumerator CheckForecast(string dir, System.Action<string, bool, string> report)
        {
            var seen = new HashSet<BorrowedSeconds.View.GhostPreview.Verdict>();
            int frames = 0, mismatch = 0, termShown = 0, aimShown = 0;
            string firstBad = "";
            // 1-2 aiming at its slider for 6 s (no input), a thaw death on 1-2, then 1-5's solution
            foreach (var (id, mode) in new[] { ("1-2", "aim"), ("1-2", "death"), ("1-5", "solution") })
            {
                int index = Catalog.Levels.FindIndex(l => l.Id == id);
                StartLevel(index, false);
                Hud.SkipIntro();
                bool death = mode == "death", aiming = mode == "aim";
                if (aiming) { Session.AllowInput = false; Session.ForcedAim = 0; }
                else Session.Autoplay = death ? FindThawDeath(Catalog.Levels[index], out _) : Catalog.SolutionFor(Catalog.Levels[index]).Actions;
                var run = new RunWatch(Session);
                var shot = new HashSet<BorrowedSeconds.View.GhostPreview.Verdict>();
                float until = Time.realtimeSinceStartup + 7f;
                while (run.Alive() && (aiming ? Time.realtimeSinceStartup < until : death ? Session.Deaths == 0 : Session.State != LevelSession.Mode.Won))
                {
                    yield return new WaitForEndOfFrame(); // after the HUD's LateUpdate
                    if (Session.State != LevelSession.Mode.Playing) continue;
                    var v = Session.Ghost.Result;
                    var cur = Session.Cur;
                    frames++;
                    string words = BorrowedSeconds.UI.Hud.VerdictText(v);
                    bool debt = cur.PFrozen == 0 && (cur.Countdown > 0 || cur.Pending);
                    bool ok = Hud.Forecast == v;
                    if (ok && debt && words.Length > 0) { ok = Hud.TermText.Contains(words); termShown++; }
                    if (ok && !debt && words.Length > 0 && Session.Aim >= 0) { ok = Hud.AimText.Contains(words); aimShown++; }
                    if (!ok && mismatch++ == 0) firstBad = $"{id} tick {cur.Tick}: ghost={v} hud={Hud.Forecast} term=\"{Hud.TermText}\"";
                    if (v == BorrowedSeconds.View.GhostPreview.Verdict.None) continue;
                    seen.Add(v);
                    if ((debt || aiming) && shot.Add(v))
                    {
                        Session.Paused = true;
                        yield return Shot(dir, $"forecast_{id}_{mode}_{v.ToString().ToLowerInvariant()}");
                        Session.Paused = false;
                    }
                }
            }
            bool lethal = seen.Contains(BorrowedSeconds.View.GhostPreview.Verdict.Lethal);
            bool safe = seen.Contains(BorrowedSeconds.View.GhostPreview.Verdict.Safe) || seen.Contains(BorrowedSeconds.View.GhostPreview.Verdict.Charges);
            Session.ForcedAim = -1;
            report("forecast", mismatch == 0 && lethal && safe && termShown > 0 && aimShown > 0,
                $"{frames} frames, verdicts seen {string.Join("/", seen)}, spelled out under the watch on {termShown} and in the aim tag on {aimShown}, mismatches {mismatch} {firstBad}");
        }

        /// <summary>
        /// Pointer aim reaches what it should. On 1-1, a pointer parked on lane tiles stays aimed at
        /// the 10-tiles-a-second slider on every frame of a full cycle (counted both for the piece
        /// alone, which is how aim worked before round 6, and with its track). On every level, a
        /// pointer over each tile that belongs to one obstacle's zone aims at that obstacle, unless
        /// another piece stands in front of it.
        /// </summary>
        IEnumerator CheckAimReach(string dir, System.Action<string, bool, string> report)
        {
            StartLevel(Catalog.Levels.FindIndex(l => l.Id == "1-1"), false);
            Hud.SkipIntro();
            Session.AllowInput = false; // the player stands still at the start, clear of the lane
            float deadline = Time.realtimeSinceStartup + 5f;
            while (Session.State != LevelSession.Mode.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            var def = Session.Def;
            int[] park = { def.Idx(1, 4), def.Idx(7, 4), def.Idx(13, 4) };
            int[] direct = new int[park.Length], zoned = new int[park.Length];
            int frames = 0, t0 = Session.Tick;
            int cycle = 2 * (def.Sliders[0].Path.Length - 1) * def.Sliders[0].Speed + 2 * def.Sliders[0].Dwell;
            while (Session.Tick - t0 <= cycle && Time.realtimeSinceStartup < deadline + 10f)
            {
                yield return null;
                frames++;
                for (int k = 0; k < park.Length; k++)
                {
                    var screen = (Vector2)Cam.WorldToScreenPoint(Session.Board.At(park[k]));
                    if (Session.Board.Pick(Cam.ScreenPointToRay(screen)) == 0) direct[k]++;
                    if (Session.PickAt(screen) == 0) zoned[k]++;
                }
                if (frames == 20) yield return Shot(dir, "aim_1-1_parked");
            }
            string Pct(int n) => (100f * n / Mathf.Max(1, frames)).ToString("0") + "%";
            bool parked = System.Array.TrueForAll(zoned, n => n == frames) && frames > 0;
            report("aim-reach 1-1", parked,
                $"pointer parked on lane tiles (1,4) (7,4) (13,4) over a {cycle}-tick cycle ({frames} frames): aimed {Pct(zoned[0])} {Pct(zoned[1])} {Pct(zoned[2])} "
                + $"(the piece alone: {Pct(direct[0])} {Pct(direct[1])} {Pct(direct[2])})");

            int levelsOk = 0, tilesOk = 0, tilesDirect = 0, covered = 0;
            var misses = new List<string>();
            for (int index = 0; index < Catalog.Levels.Count; index++)
            {
                StartLevel(index, false);
                Session.AllowInput = false;
                Session.Paused = true;
                yield return null;
                yield return null;
                var d = Session.Def;
                var zones = AimZones.Build(d);
                bool levelOk = true;
                for (int t = 0; t < d.Tiles.Length; t++)
                {
                    int owner = -1, owners = 0;
                    for (int o = 0; o < zones.Length; o++) if (zones[o].Has(t)) { owner = o; owners++; }
                    if (owners != 1) continue;
                    var sp = Cam.WorldToScreenPoint(Session.Board.At(t));
                    if (sp.z <= 0 || sp.x < 0 || sp.y < 0 || sp.x >= Screen.width || sp.y >= Screen.height) { levelOk = false; misses.Add($"{d.Id} ({d.X(t)},{d.Y(t)}) off screen"); continue; }
                    int hit = Session.Board.Pick(Cam.ScreenPointToRay(sp));
                    if (hit >= 0 && hit != owner) { covered++; continue; } // another piece stands in front
                    if (hit == owner) tilesDirect++;
                    if (Session.PickAt(sp) == owner) tilesOk++;
                    else { levelOk = false; misses.Add($"{d.Id} ({d.X(t)},{d.Y(t)}) aimed {Session.PickAt(sp)}, not {owner}"); }
                }
                if (levelOk) levelsOk++;
            }
            Session.Paused = false;
            report("aim-reach zones", levelsOk == Catalog.Levels.Count,
                $"{levelsOk}/{Catalog.Levels.Count} levels: {tilesOk} single-owner zone tiles aim at their obstacle (the piece alone at tick 0: {tilesDirect}); "
                + $"{covered} skipped behind another piece" + (misses.Count > 0 ? "; misses: " + string.Join(", ", misses.GetRange(0, Mathf.Min(8, misses.Count))) : ""));
        }

        /// <summary>
        /// Settings > Display picks a real window size: stepping the row through every size that fits
        /// the desktop resizes the window to it. Settings > Render resolution sets URP's render scale
        /// (screenshot at 50 %: the 3D scene softens, the HUD stays sharp). Both survive a save round
        /// trip. Real fullscreen is never entered: on a shared desktop it would cover other windows.
        /// </summary>
        IEnumerator CheckDisplay(string dir, System.Action<string, bool, string> report)
        {
            var saved = JsonUtility.ToJson(Save);
            int w0 = Screen.width, h0 = Screen.height;
            forceDisplay = true;
            StartLevel(Catalog.Levels.FindIndex(l => l.Id == "4-5"), false);
            Session.Autoplay = Catalog.SolutionFor(Session.Def).Actions;
            Hud.SkipIntro();
            Pause();
            OpenSettings(Flow.Paused);
            var desk = DisplayOptions.Desktop;
            var fit = DisplayOptions.Fitting(desk);
            var row = settings.Menu.Items.Find(i => i.Label == "Display");
            var scaleRow = settings.Menu.Items.Find(i => i.Label == "Render resolution");
            bool rows = row != null && scaleRow != null && settings.Menu.Items.IndexOf(row) == 3 && settings.Menu.Items.IndexOf(scaleRow) == 4
                && settings.Menu.Items[UI.SettingsScreen.ControlsRow].Label == "Controls";
            var sizes = new List<string>();
            bool resized = rows;
            if (rows)
            {
                settings.Menu.Selected = 3;
                DisplayOptions.Choose(Save, desk, 1);
                ApplyDisplay(true);
                for (int k = 1; k <= fit.Count; k++)
                {
                    if (k > 1) row.Adjust(1); // the row's own step: one size up, applied at once
                    var want = fit[k - 1];
                    // the compositor answers in its own time: allow 8 s and 60 frames under load
                    float t0 = Time.realtimeSinceStartup, deadline = t0 + 8f;
                    int frames = 0;
                    while ((Screen.width != want.x || Screen.height != want.y) && (Time.realtimeSinceStartup < deadline || frames < 60)) { frames++; yield return null; }
                    bool hit = Screen.width == want.x && Screen.height == want.y && !Save.fullscreen && row.Value() == $"Window {want.x}×{want.y}";
                    sizes.Add($"{want.x}x{want.y} in {Time.realtimeSinceStartup - t0:0.0}s{(hit ? "" : $" (got {Screen.width}x{Screen.height}, row '{row.Value()}')")}");
                    resized &= hit;
                }
            }
            var back = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(Save));
            bool persists = back.windowW == Save.windowW && back.windowH == Save.windowH && !back.fullscreen;

            // render resolution: down to 50 %
            bool scaled = false;
            float scaleSeen = -1f;
            if (scaleRow != null)
            {
                settings.Menu.Selected = 4;
                for (int i = 0; i < 4; i++) scaleRow.Adjust(-1);
                scaleSeen = DisplayOptions.CurrentRenderScale;
                scaled = Mathf.Approximately(Save.renderScale, 0.5f) && Mathf.Approximately(scaleSeen, 0.5f) && scaleRow.Value() == "50%"
                    && Mathf.Approximately(JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(Save)).renderScale, 0.5f);
            }
            // back to the run's own window size before the screenshots
            Save.fullscreen = false;
            Save.windowW = w0;
            Save.windowH = h0;
            Screen.SetResolution(w0, h0, FullScreenMode.Windowed);
            float until = Time.realtimeSinceStartup + 3f;
            while ((Screen.width != w0 || Screen.height != h0) && Time.realtimeSinceStartup < until) yield return null;
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Shot(dir, "display_settings");
            CloseSettings();
            Resume();
            yield return new WaitForSecondsRealtime(2.5f);
            yield return Shot(dir, "display_render-50");
            DisplayOptions.ApplyRenderScale(1f);
            yield return null;
            yield return Shot(dir, "display_render-100");
            float restored = DisplayOptions.CurrentRenderScale;
            JsonUtility.FromJsonOverwrite(saved, Save);
            forceDisplay = false;
            report("display", rows && resized && persists && scaled && Mathf.Approximately(restored, 1f),
                $"desktop {desk.x}x{desk.y}; rows at 3 and 4: {rows}; the Display row stepped the window through {string.Join(", ", sizes)}; "
                + $"saved {persists}; render scale 50% -> URP {scaleSeen:0.00}, saved {scaled}; back to {Screen.width}x{Screen.height} at {restored:0.00}");
        }

        /// <summary>
        /// How to play opens from the pause menu and the title, lists the rules and the keyboard
        /// controls, follows a rebound key, and Esc (Back) returns to the menu it came from.
        /// </summary>
        IEnumerator CheckHowTo(string dir, System.Action<string, bool, string> report)
        {
            StartLevel(Catalog.Levels.FindIndex(l => l.Id == "2-2"), false);
            Hud.SkipIntro();
            Pause();
            bool inPause = pause.Menu.Items.Exists(i => i.Label == "How to play") && title.Menu.Items.Exists(i => i.Label == "How to play");
            pause.Menu.Items.Find(i => i.Label == "How to play").Activate();
            yield return new WaitForSecondsRealtime(1.6f);
            for (int i = 0; i < 60; i++) yield return null; // the window's reveal runs on frames (clamped dt), not seconds
            yield return Shot(dir, "howto_pause");
            string keys = howto.ControlsText ?? "";
            bool rules = howto.RulesText == UI.HowToPlayScreen.Rules && howto.Visible && State == Flow.HowTo;
            bool defaults = keys.Contains("<b>WASD</b> move") && keys.Contains("<b>Click</b> or <b>Space</b> borrow") && keys.Contains("<b>Shift</b> hold")
                && keys.Contains("<b>Z</b> hold: rewind") && keys.Contains("<b>R</b> restart") && keys.Contains("<b>H</b> show") && keys.Contains("<b>Esc</b> pause");
            KeyBindings.Assign(Input.Keys, KeyAction.Borrow, UnityEngine.InputSystem.Key.K);
            yield return null;
            yield return null;
            bool follows = (howto.ControlsText ?? "").Contains("<b>Click</b> or <b>K</b> borrow");
            Input.Keys = KeyBindings.DefaultKeys();
            CloseHowTo(); // what Esc and Back do
            yield return null;
            bool backToPause = State == Flow.Paused && pause.Visible && !howto.Visible;
            pause.Hide();
            ShowTitle();
            yield return new WaitForSecondsRealtime(0.5f);
            title.Menu.Items.Find(i => i.Label == "How to play").Activate();
            yield return new WaitForSecondsRealtime(0.6f);
            bool fromTitle = State == Flow.HowTo && howto.Visible && !title.Visible;
            CloseHowTo();
            yield return null;
            bool backToTitle = State == Flow.Title && title.Visible;
            report("how-to-play", inPause && rules && defaults && follows && backToPause && fromTitle && backToTitle,
                $"in both menus={inPause}; rules={rules}; default keys listed={defaults}; follows a rebound Borrow (K)={follows}; "
                + $"back to pause={backToPause}; from the title={fromTitle}, back={backToTitle}");
        }

        /// <summary>
        /// Settings > Erase progress: one press only arms it, moving off the row or waiting disarms it,
        /// and two presses erase medals, times, the last level, the finished flag and the onboarding
        /// prompts while every setting and key binding stays. Runs on the scripted run's in-memory
        /// save (read-only, so the player's file can't be touched) and restores it afterwards.
        /// </summary>
        /// <summary>
        /// Settings > Mute in background: a focus loss (through the same handler Unity calls) fades
        /// the listener to silence and a focus gain brings it back; with the toggle off nothing
        /// changes. The row flips the setting, it survives a save round trip, and a save written
        /// before the setting existed loads with it on.
        /// </summary>
        IEnumerator CheckBackgroundMute(string dir, System.Action<string, bool, string> report)
        {
            bool keep = Save.muteBackground;
            float vol0 = AudioListener.volume;
            forceBackgroundMute = true;
            ShowTitle();
            OpenSettings(Flow.Title);
            yield return new WaitForSecondsRealtime(1.0f);
            var row = settings.Menu.Items.Find(i => i.Label == "Mute in background");
            bool isRow = row != null && settings.Menu.Items.IndexOf(row) == UI.SettingsScreen.ControlsRow - 1;
            Save.muteBackground = false;
            settings.Menu.Selected = settings.Menu.Items.IndexOf(row);
            row?.Adjust?.Invoke(1); // a toggle row flips on Enter or left/right alike
            yield return null;
            bool rowFlips = Save.muteBackground;
            yield return new WaitForSecondsRealtime(2.5f); // the rows finish their staggered intro
            yield return Shot(dir, "background-mute_row");
            CloseSettings();
            var back = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(new SaveData { muteBackground = false }));
            bool roundTrip = !back.muteBackground && JsonUtility.FromJson<SaveData>("{\"music\":0.5}").muteBackground;

            // time for the listener to reach a level after a focus change (or the cap, if it never does)
            IEnumerator Settle(float target, float[] took)
            {
                float t0 = Time.realtimeSinceStartup;
                while (!Mathf.Approximately(AudioListener.volume, target) && Time.realtimeSinceStartup - t0 < 3f) yield return null;
                took[0] = Time.realtimeSinceStartup - t0;
            }
            var outT = new float[1];
            var inT = new float[1];
            Save.muteBackground = true;
            AudioListener.volume = 1f;
            OnApplicationFocus(false);
            yield return Settle(0f, outT);
            bool muted = Mathf.Approximately(AudioListener.volume, 0f);
            OnApplicationFocus(true);
            yield return Settle(1f, inT);
            bool back1 = Mathf.Approximately(AudioListener.volume, 1f);
            Save.muteBackground = false;
            OnApplicationFocus(false);
            yield return new WaitForSecondsRealtime(0.6f);
            float offVol = AudioListener.volume;
            OnApplicationFocus(true);
            yield return null;

            forceBackgroundMute = false;
            Save.muteBackground = keep;
            AudioListener.volume = vol0;
            report("background-mute", isRow && rowFlips && roundTrip && muted && back1 && Mathf.Approximately(offVol, 1f),
                $"row={isRow} flips={rowFlips}; round trip and old saves default on={roundTrip}; on: focus lost -> silent in {outT[0]:0.00}s={muted}, "
                + $"focus back -> full in {inT[0]:0.00}s={back1}; off: volume {offVol:0.00} after 0.6 s in the background");
        }

        /// <summary>
        /// Medal pace: on a level settled before, the HUD's coin shows gold up to par + 1 s, silver one
        /// tick later and up to par + 4 s, then bronze, and the line under the clock adds the best
        /// time. A level never settled, and Watch solution, show neither. The Ledger names the next
        /// medal's time for every level at silver and bronze, and none at gold.
        /// </summary>
        IEnumerator CheckMedalPace(string dir, System.Action<string, bool, string> report)
        {
            string original = JsonUtility.ToJson(Save);
            var bad = new List<string>();
            string paceLevel = null, steps = "";
            // the live HUD: a level where standing still survives past par + 4 s (the clock runs on)
            for (int index = 0; index < Catalog.Levels.Count && paceLevel == null; index++)
            {
                var d = Catalog.Levels[index];
                int par = Catalog.SolutionFor(d)?.Par ?? 0;
                if (par <= 0) continue;
                Save.ids = new string[0];
                Save.best = new int[0];
                Save.Record(d.Id, par + 50);
                StartLevel(index, false);
                Hud.SkipIntro();
                Session.Autoplay = new List<TimedAction>(); // stand still
                Session.Paused = true;
                yield return null;
                yield return null;
                var got = new List<string> { $"t{Session.Tick}:{Hud.PaceMedal}{(Hud.PaceShown ? "" : "(hidden)")}" };
                bool ok = Hud.PaceShown && Hud.PaceMedal == Medal.Gold && Hud.ParLine.Contains("BEST " + UI.Ui.Secs(par + 50));
                bool survived = true;
                foreach (var (at, expect) in new[] { (par + 20, Medal.Gold), (par + 21, Medal.Silver), (par + 80, Medal.Silver), (par + 81, Medal.Bronze) })
                {
                    Session.Paused = false;
                    Session.Seek(at);
                    Session.Paused = true;
                    if (Session.Cur.Dead || Session.Tick != at) { survived = false; break; }
                    yield return null;
                    yield return null;
                    got.Add($"par+{at - par}:{Hud.PaceMedal}{(Hud.PaceShown ? "" : "(hidden)")}");
                    ok &= Hud.PaceShown && Hud.PaceMedal == expect;
                    if (at == par + 21)
                    {
                        yield return new WaitForSecondsRealtime(0.5f); // the coin's flip settles
                        yield return Shot(dir, "medal-pace_silver");
                    }
                }
                if (!survived) continue;
                paceLevel = d.Id;
                steps = string.Join(" ", got) + $"; line \"{Hud.ParLine}\"";
                if (!ok) bad.Add($"{d.Id} pace {steps}");

                // never settled: no coin, no best
                Save.ids = new string[0];
                Save.best = new int[0];
                StartLevel(index, false);
                Hud.SkipIntro();
                yield return null;
                yield return null;
                if (Hud.PaceShown || Hud.ParLine.Contains("BEST")) bad.Add($"{d.Id} unsettled shows pace (\"{Hud.ParLine}\")");
                // watching the solution: none either
                Save.Record(d.Id, par + 50);
                StartWatch(index);
                yield return new WaitForSecondsRealtime(0.3f);
                if (Hud.PaceShown || Hud.ParLine.Contains("BEST")) bad.Add($"{d.Id} Watch solution shows pace");
                StartLevel(index, false);
            }
            if (paceLevel == null) bad.Add("no level survives standing still past par + 4 s");

            // the Ledger: silver names gold's time, bronze names silver's, gold names none
            int targets = 0;
            foreach (var (delta, medal) in new[] { (50, Medal.Silver), (100, Medal.Bronze), (0, Medal.Gold) })
            {
                Save.ids = new string[0];
                Save.best = new int[0];
                foreach (var d in Catalog.Levels) Save.Record(d.Id, (Catalog.SolutionFor(d)?.Par ?? 100) + delta);
                ShowLevels(0);
                for (int i = 0; i < Catalog.Levels.Count; i++)
                {
                    var d = Catalog.Levels[i];
                    int par = Catalog.SolutionFor(d)?.Par ?? 0;
                    levels.Select(i);
                    yield return null;
                    yield return null;
                    string text = levels.InfoStats;
                    string want = medal == Medal.Silver ? $"gold</color> ≤ {UI.Ui.Secs(par + 20)}s" : medal == Medal.Bronze ? $"silver</color> ≤ {UI.Ui.Secs(par + 80)}s" : null;
                    if (SaveData.MedalFor(par + delta, par) != medal) bad.Add($"{d.Id} par+{delta} isn't {medal}");
                    else if (want != null ? text.Contains(want) : !text.Contains("≤")) targets++;
                    else bad.Add($"{d.Id} {medal}: \"{text.Replace("\n", " / ")}\"");
                    if (i == 2 && medal == Medal.Silver)
                    {
                        yield return new WaitForSecondsRealtime(1.2f);
                        yield return Shot(dir, "medal-pace_ledger");
                    }
                }
                levels.Hide();
            }

            // the Settled screen: the bar's label names gold's and silver's times for the level
            int settled = 0;
            for (int i = 0; i < Catalog.Levels.Count; i++)
            {
                var d = Catalog.Levels[i];
                int par = Catalog.SolutionFor(d)?.Par ?? 0;
                if (i == 0) { StartLevel(i, false); Hud.SkipIntro(); }
                complete.Show(par + 50, par, 0, false);
                string label = complete.BarLabel;
                if (par > 0 && label.Contains($"TIME THIEF</color>  {UI.Ui.Secs(par + 20)}s or better") && label.Contains($"SILVER</color>  {UI.Ui.Secs(par + 80)}s or better")) settled++;
                else bad.Add($"{d.Id} Settled: \"{label}\"");
                if (i == 0)
                {
                    State = Flow.Complete;
                    yield return new WaitForSecondsRealtime(2.8f);
                    yield return Shot(dir, "medal-pace_settled");
                }
                complete.Hide();
            }
            State = Flow.Playing;
            JsonUtility.FromJsonOverwrite(original, Save);
            ShowTitle();
            report("medal-pace", bad.Count == 0,
                $"HUD on {paceLevel}: {steps}; unsettled and watching show none; Ledger targets right on {targets}/{3 * Catalog.Levels.Count} panels; "
                + $"Settled screen names both medal times on {settled}/{Catalog.Levels.Count}"
                + (bad.Count > 0 ? "; " + string.Join("; ", bad.GetRange(0, Mathf.Min(6, bad.Count))) : ""));
        }

        IEnumerator CheckErase(string dir, System.Action<string, bool, string> report)
        {
            string original = JsonUtility.ToJson(Save);
            for (int i = 0; i < 12; i++) Save.Record(Catalog.Levels[i].Id, (Catalog.SolutionFor(Catalog.Levels[i])?.Par ?? 100) + 5);
            Save.lastLevel = 12;
            Save.finished = true;
            Save.learned = 0x3F;
            Save.music = 0.3f;
            Save.speed = 0.7f;
            Save.focusToggle = true;
            Save.keys = new[] { "K", "", "", "", "", "", "", "", "", "", "", "" };
            string Progress() => $"{Save.ids.Length} cleared, last={Save.lastLevel}, finished={Save.finished}, learned={Save.learned}";
            var expect = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(Save));
            expect.EraseProgress();
            string settingsAfter = JsonUtility.ToJson(expect), before = Progress();
            ShowTitle();
            yield return new WaitForSecondsRealtime(0.3f);
            OpenSettings(Flow.Title);
            yield return new WaitForSecondsRealtime(1.0f);
            var row = settings.Menu.Items[UI.SettingsScreen.EraseRow];
            bool isRow = row.Label == "Erase progress" && settings.Menu.Items[UI.SettingsScreen.ControlsRow].Label == "Controls";
            settings.Menu.Selected = UI.SettingsScreen.EraseRow;
            row.Activate();
            yield return null;
            bool armedOnly = settings.EraseArmed && Progress() == before && row.Value().Contains("press again");
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot(dir, "erase_armed");
            settings.Menu.Selected = UI.SettingsScreen.ControlsRow; // moving off the row disarms it
            yield return null;
            yield return null;
            settings.Menu.Selected = UI.SettingsScreen.EraseRow;
            bool movedOff = !settings.EraseArmed;
            row.Activate(); // a fresh first press: arms again, erases nothing
            yield return null;
            bool stillThere = Progress() == before && settings.EraseArmed;
            float t0 = Time.realtimeSinceStartup;
            while (settings.EraseArmed && Time.realtimeSinceStartup - t0 < 15f) yield return null;
            float lapsed = Time.realtimeSinceStartup - t0;
            bool lapses = !settings.EraseArmed && Progress() == before;
            row.Activate();
            yield return null;
            row.Activate(); // the second press
            yield return null;
            string after = Progress();
            bool erased = Save.ids.Length == 0 && Save.best.Length == 0 && Save.lastLevel == 0 && !Save.finished && Save.learned == 0;
            bool kept = JsonUtility.ToJson(Save) == settingsAfter;
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot(dir, "erase_done");
            CloseSettings();
            yield return null;
            yield return null;
            string label = title.Menu.Items[0].Label;
            ShowLevels(0);
            yield return null;
            bool ledger = levels.Unlocked(0) && !levels.Unlocked(1);
            levels.Hide();
            ShowTitle();
            JsonUtility.FromJsonOverwrite(original, Save);
            report("erase-progress", isRow && armedOnly && movedOff && stillThere && lapses && erased && kept && label == "Begin" && ledger,
                $"row={isRow}; one press only arms={armedOnly}; moving off disarms={movedOff}; re-arm erases nothing={stillThere}; "
                + $"arm lapsed after {lapsed:0.0}s={lapses}; two presses: {before} -> {after} (erased={erased}); settings and keys kept={kept}; "
                + $"title reads {label}; Ledger has only 1-1 open={ledger}");
        }

        /// <summary>
        /// Settings > HUD size steps to 150 % and back: the HUD scales (hint rows, text sizes in
        /// pixels), the bottom HUD's pieces don't meet, the camera re-frames 4-5 clear of the
        /// larger HUD, and the choice survives a save round trip. Screenshots at both sizes.
        /// </summary>
        IEnumerator CheckHudSize(string dir, System.Action<string, bool, string> report)
        {
            var saved = JsonUtility.ToJson(Save);
            forceHudScale = true; // scripted runs otherwise keep a 100 % HUD
            StartLevel(Catalog.Levels.FindIndex(l => l.Id == "4-5"), false);
            Session.Autoplay = Catalog.SolutionFor(Session.Def).Actions;
            Hud.SkipIntro();
            Pause();
            OpenSettings(Flow.Paused);
            var row = settings.Menu.Items.Find(i => i.Label == "HUD size");
            bool isRow = row != null && settings.Menu.Items.IndexOf(row) == 5;
            string Measure() => $"{Hud.HintRows} hint row(s), hints {Hud.HintLabelPx:0.0}px, line under the watch {Hud.TermPx:0.0}px, tip {Hud.TipPx:0.0}px, tiles under the HUD {Rig.HudOverlap} ({Hud.HintDebug})";
            yield return new WaitForSecondsRealtime(0.8f);
            if (isRow)
            {
                settings.Menu.Selected = 5;
                row.Adjust(1);
                row.Adjust(1);
            }
            bool set = isRow && Mathf.Approximately(Save.hudScale, 1.5f) && Mathf.Approximately(View.HudLayout.Scale, 1.5f) && row.Value() == "150%"
                && Mathf.Approximately(JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(Save)).hudScale, 1.5f);
            CloseSettings();
            Resume();
            yield return new WaitForSecondsRealtime(2.5f); // the camera eases out to the new framing; the tip is up
            Session.Paused = true;
            yield return Shot(dir, "hud_150");
            string at150 = Measure();
            var overlaps = Hud.BottomOverlaps();
            float hint150 = Hud.HintLabelPx;
            bool larger150 = Hud.HintRows == 2 && Rig.HudOverlap == 0 && overlaps.Count == 0;
            Session.Paused = false;
            Pause();
            OpenSettings(Flow.Paused);
            settings.Menu.Selected = 5;
            row?.Adjust(-1);
            row?.Adjust(-1);
            bool back = Mathf.Approximately(View.HudLayout.Scale, 1f) && row?.Value() == "100%";
            CloseSettings();
            Resume();
            yield return new WaitForSecondsRealtime(2.5f);
            Session.Paused = true;
            yield return Shot(dir, "hud_100");
            string at100 = Measure();
            bool larger = larger150 && hint150 > Hud.HintLabelPx * 1.2f;
            bool backOverlaps = Hud.BottomOverlaps().Count == 0 && Hud.HintRows == 1 && Rig.HudOverlap == 0;
            Session.Paused = false;
            JsonUtility.FromJsonOverwrite(saved, Save);
            forceHudScale = false;
            SetHudScale(hudScaleArg);
            report("hud-size", isRow && set && larger && back && backOverlaps,
                $"{Screen.width}x{Screen.height}; row at 5={isRow}; 150 % set and saved={set}; at 100 %: {at100}; at 150 %: {at150}"
                + $"{(overlaps.Count > 0 ? "; overlapping: " + string.Join(", ", overlaps) : "")}; back to 100 %={back}, one row and no overlap={backOverlaps}");
        }

        IEnumerator Shot(string dir, string name)
        {
            yield return null;
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
            yield return null;
            yield return null;
        }

        /// <summary>
        /// Each plate is wired (link arcs) to exactly the gates and lasers on its channel, and pulses
        /// when pressed. Screenshots the board at rest and mid-pulse for review.
        /// </summary>
        IEnumerator CheckChannels(string dir, string id, int[] expectLinks, System.Action<string, bool, string> report)
        {
            int index = Catalog.Levels.FindIndex(l => l.Id == id);
            LoadLevel(index);
            State = Flow.Playing;
            Hud.SetVisible(true);
            Hud.SkipIntro();
            Session.IntroTime = 0.2f;
            Session.Autoplay = Catalog.SolutionFor(Catalog.Levels[index]).Actions;
            var board = Session.Board;
            bool wired = board.Plates.Count == expectLinks.Length;
            for (int i = 0; wired && i < expectLinks.Length; i++) wired = board.LinkCount(i) == expectLinks[i];
            Session.Paused = true;
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot(dir, $"channels_{id}_rest");
            Session.Paused = false;
            int pressed = -1;
            Session.Events += (st, evs) => { foreach (var e in evs) if (e.Type == Ev.PlateDown && pressed < 0) pressed = st.Tick; };
            float deadline = Time.realtimeSinceStartup + 20f;
            while (pressed < 0 && Session.State != LevelSession.Mode.Won && Time.realtimeSinceStartup < deadline) yield return null;
            if (pressed >= 0)
            {
                yield return new WaitForSecondsRealtime(0.22f); // the spark is mid-arc
                Session.Paused = true;
                yield return Shot(dir, $"channels_{id}_pulse");
                Session.Paused = false;
            }
            report("channels " + id, wired && pressed >= 0, $"links per plate={string.Join(",", System.Linq.Enumerable.Select(System.Linq.Enumerable.Range(0, board.Plates.Count), board.LinkCount))} (expected {string.Join(",", expectLinks)}), first press at tick {pressed}");
        }

        /// <summary>
        /// Settings > Game speed steps through 100/85/70/50 %, survives a save round trip, and slows the
        /// level in real time (ticks per real second) with a SPEED tag on the HUD.
        /// </summary>
        IEnumerator CheckSpeed(string dir, System.Action<string, bool, string> report)
        {
            float saved = Save.speed;
            StartLevel(Catalog.Levels.FindIndex(l => l.Id == "2-1"), false);
            Session.Autoplay = Catalog.SolutionFor(Session.Def).Actions;
            Hud.SkipIntro();
            Pause();
            OpenSettings(Flow.Paused);
            var item = settings.Menu.Items.Find(i => i.Label == "Game speed");
            bool labelled = item != null;
            settings.Menu.Selected = settings.Menu.Items.IndexOf(item);
            item.Adjust(-1);
            item.Adjust(-1);
            bool stepped = Mathf.Approximately(Save.speed, 0.7f) && item.Value() == "70%";
            bool persists = Mathf.Approximately(JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(Save)).speed, 0.7f);
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Shot(dir, "speed_settings");
            CloseSettings();
            Resume();
            LevelSession.GameSpeed = Save.speed; // what ApplySettings does outside scripted runs
            float deadline = Time.realtimeSinceStartup + 5f;
            while (Session.State != LevelSession.Mode.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            int t0 = Session.Tick;
            float r0 = Time.realtimeSinceStartup;
            yield return new WaitForSecondsRealtime(3f);
            float rate = (Session.Tick - t0) / (Time.realtimeSinceStartup - r0);
            bool slowed = Mathf.Abs(rate - Rules.TicksPerSecond * 0.7f) < 2f;
            yield return Shot(dir, "speed_hud");
            LevelSession.GameSpeed = 1f;
            Save.speed = saved;
            report("game-speed", labelled && stepped && persists && slowed,
                $"row={item.Label} stepped={stepped} persists={persists} rate={rate:0.0} ticks/s (expected {Rules.TicksPerSecond * 0.7f:0.0})");
        }

        /// <summary>A spoiler tip starts folded behind H, unfolds on H, and defaults add a Watch solution pointer.</summary>
        IEnumerator CheckHint(string dir, System.Action<string, bool, string> report)
        {
            int index = Catalog.Levels.FindIndex(l => l.Id == "1-5");
            var def = Catalog.Levels[index];
            StartLevel(index, false);
            Session.AllowInput = false;
            yield return new WaitForSecondsRealtime(5f); // the tip slides in after the title intro
            bool folded = def.Spoiler && !Hud.TipText.Contains(def.Hint) && Hud.TipText.Contains("hint");
            yield return Shot(dir, "hint_folded");
            ToggleTip();
            bool open = Hud.TipText.Contains(def.Hint);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot(dir, "hint_open");
            attemptDeaths = NudgeAfterDeaths;
            RefreshTip(false);
            bool nudge = Hud.TipText.Contains("Watch solution");
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot(dir, "hint_nudge");
            ToggleTip();
            bool refolded = !Hud.TipText.Contains(def.Hint) && Hud.TipText.Contains("Watch solution");
            int plain = Catalog.Levels.FindIndex(l => l.Id == "1-2");
            StartLevel(plain, false);
            bool plainOpen = Hud.TipText.Contains(Catalog.Levels[plain].Hint);
            report("hint-toggle", folded && open && nudge && refolded && plainOpen,
                $"folded={folded} open={open} nudge={nudge} refolded={refolded} non-spoiler open={plainOpen}");
        }

        /// <summary>
        /// The Ledger's info panel keeps a spoiler tip folded until its level is settled: every card,
        /// on an empty save (in memory; the save is read-only here) and on one with every level settled.
        /// </summary>
        IEnumerator CheckLedgerTips(string dir, System.Action<string, bool, string> report)
        {
            var ids = Save.ids;
            var best = Save.best;
            var bad = new List<string>();
            int folded = 0, shown = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                bool settled = pass == 1;
                Save.ids = new string[0];
                Save.best = new int[0];
                if (settled)
                    foreach (var d in Catalog.Levels) Save.Record(d.Id, Catalog.SolutionFor(d)?.Par ?? 100);
                ShowLevels(0);
                for (int i = 0; i < Catalog.Levels.Count; i++)
                {
                    var def = Catalog.Levels[i];
                    if (!levels.Unlocked(i)) { Save.Record(Catalog.Levels[i - 1].Id, 100); } // unlock it, leaving it unsettled
                    levels.Select(i);
                    yield return null;
                    yield return null;
                    string text = levels.InfoTip;
                    bool hasTip = !string.IsNullOrEmpty(def.Hint) && text.Contains(def.Hint);
                    bool wantTip = !string.IsNullOrEmpty(def.Hint) && (!def.Spoiler || settled);
                    if (def.Spoiler && !settled) { if (!hasTip && text.Contains("folded")) folded++; else bad.Add(def.Id + " shows its spoiler tip"); }
                    else if (wantTip) { if (hasTip) shown++; else bad.Add(def.Id + " lost its tip"); }
                    if (def.Id == "1-5")
                    {
                        yield return new WaitForSecondsRealtime(1.2f);
                        yield return Shot(dir, settled ? "ledger_tip_settled" : "ledger_tip_folded");
                    }
                }
                levels.Hide();
            }
            Save.ids = ids;
            Save.best = best;
            int spoilers = Catalog.Levels.FindAll(l => l.Spoiler).Count;
            report("ledger-tips", bad.Count == 0 && folded == spoilers,
                $"{folded}/{spoilers} spoiler tips folded while unsettled, {shown} tips shown (settled or not spoilers)" + (bad.Count > 0 ? ": " + string.Join("; ", bad) : ""));
        }

        /// <summary>
        /// Pause > Watch solution plays the solver's route in the real game loop, wins at par, records
        /// nothing, and hands the level back fresh: every level.
        /// </summary>
        IEnumerator CheckWatch(string dir, System.Action<string, bool, string> report)
        {
            string Progress() => string.Join(",", Save.ids) + "|" + string.Join(",", Save.best) + "|" + Save.finished;
            string before = Progress();
            int ok = 0;
            var bad = new List<string>();
            for (int i = 0; i < Catalog.Levels.Count; i++)
            {
                var def = Catalog.Levels[i];
                int par = Catalog.SolutionFor(def)?.Par ?? -1;
                bool shoot = def.Id == "3-3";
                StartLevel(i, false);
                if (shoot) yield return new WaitForSecondsRealtime(0.8f);
                Pause();
                if (shoot) { yield return new WaitForSecondsRealtime(1.6f); yield return Shot(dir, "watch_pause-menu"); }
                WatchFromPause();
                if (State != Flow.Watching || Session.Autoplay == null) { bad.Add(def.Id + " did not start"); continue; }
                Session.Speed = shoot || def.Id == "7-2" ? 1f : 4f; // real speed where a screenshot needs the title banner gone
                var watched = Session;
                var run = new RunWatch(watched);
                bool shot = false, previewShot = false;
                // every borrow in the replay must already be aimed (highlight, ghost, aim tag) when it fires
                int borrows = 0, aimed = 0, wantBorrows = 0;
                foreach (var a in watched.Autoplay) if (Act.IsBorrow(a.Action)) wantBorrows++;
                watched.Events += (st, evs) =>
                {
                    foreach (var e in evs)
                        if (e.Type == Ev.Borrow) { borrows++; if (watched.Aim == e.A) aimed++; }
                };
                while (Session == watched && Session.State != LevelSession.Mode.Won && !Session.Cur.Dead && run.Alive())
                {
                    // 7-2's borrow, 3 s in (most first borrows come under the level-title banner)
                    if (def.Id == "7-2" && !previewShot && Session.UpcomingBorrow(LevelSession.ViewerAimLead - 6) >= 0)
                    {
                        previewShot = true;
                        Session.Paused = true;
                        yield return new WaitForSecondsRealtime(0.4f); // the aim tag and ghost fade in
                        yield return Shot(dir, "watch_preview");
                        Session.Paused = false;
                    }
                    if (shoot && !shot && Session.Tick >= 120) { shot = true; Session.Paused = true; yield return Shot(dir, "watch_playing"); Session.Paused = false; }
                    yield return null;
                }
                int won = Session == watched && Session.State == LevelSession.Mode.Won ? Session.Tick : -1;
                string why = won < 0 ? $" ({run.Why(Session)} flow={State})" : "";
                float handBack = Time.realtimeSinceStartup + 20f;
                while (State == Flow.Watching && Time.realtimeSinceStartup < handBack) yield return null;
                bool fresh = State == Flow.Playing && Session.Autoplay == null && Session.Tick < 5 && !Hud.Watching;
                bool previewed = borrows == wantBorrows && aimed == borrows;
                if (won == par && fresh && previewed) ok++;
                else bad.Add($"{def.Id} won={won} par={par} fresh={fresh} aimed {aimed}/{borrows} of {wantBorrows} borrows{why}");
            }
            bool untouched = Progress() == before;
            report("watch-solution", ok == Catalog.Levels.Count && untouched,
                $"{ok}/{Catalog.Levels.Count} won at par with every borrow aimed first, and returned fresh; progress untouched={untouched}" + (bad.Count > 0 ? "; " + string.Join("; ", bad) : ""));
        }

        /// <summary>
        /// The scripted-run timeout itself: a replay crawling at one tick a second (a loaded machine)
        /// stays alive past the 10 s stall limit, and one whose tick stops dead is caught.
        /// </summary>
        IEnumerator CheckRunWatch(System.Action<string, bool, string> report)
        {
            int index = Catalog.Levels.FindIndex(l => l.Id == "2-3");
            StartLevel(index, false);
            Hud.SkipIntro();
            Session.Autoplay = Catalog.SolutionFor(Session.Def).Actions;
            Session.IntroTime = 0f;
            while (Session.State != LevelSession.Mode.Playing) yield return null;
            Session.Speed = 1f / 20f / LevelSession.GameSpeed; // one tick a second
            var slow = new RunWatch(Session);
            int t0 = Session.Tick;
            bool slowAlive = true;
            float until = Time.realtimeSinceStartup + 13f;
            while (Time.realtimeSinceStartup < until) { slowAlive &= slow.Alive(); yield return null; }
            int crawled = Session.Tick - t0;
            Session.Speed = 0f; // stopped dead while Playing: a hang
            var stuck = new RunWatch(Session);
            float limit = Time.realtimeSinceStartup + 30f;
            while (stuck.Alive() && Time.realtimeSinceStartup < limit) yield return null;
            bool caught = Time.realtimeSinceStartup < limit && stuck.Elapsed >= 10f;
            string why = stuck.Why(Session);
            Session.Speed = 1f;
            report("run-watch", slowAlive && crawled >= 8 && caught,
                $"crawl: {crawled} ticks in 13 s, alive={slowAlive}; hang caught={caught} after {stuck.Elapsed:0.0}s ({why})");
        }

        /// <summary>Seeded random play on the bare simulation until the player thaws inside a hazard.</summary>
        static List<TimedAction> FindThawDeath(LevelDef d, out int deathTick) => FindDeath(d, true, out deathTick);

        /// <summary>Seeded random play until the player dies: thawing inside a hazard, or (thaw false)
        /// caught by one while free to move.</summary>
        static List<TimedAction> FindDeath(LevelDef d, bool thaw, out int deathTick)
        {
            var rng = new System.Random(7);
            for (int trial = 0; trial < 2000; trial++)
            {
                var s = Simulation.Create(d);
                var acts = new List<TimedAction>();
                bool wasFrozen = false;
                while (s.Tick < 600 && !s.Dead && !s.Won)
                {
                    int a = Act.None;
                    if (s.CanActNext)
                    {
                        int r = rng.Next(10);
                        if (r < 6) a = Act.Move(rng.Next(4));
                        else if (r == 6 && d.ObstacleCount > 0) a = Act.Borrow(rng.Next(d.ObstacleCount));
                    }
                    if (a != Act.None) acts.Add(new TimedAction(s.Tick, a));
                    wasFrozen = s.PFrozen > 0;
                    Simulation.Step(d, s, a);
                }
                if (s.Dead && wasFrozen == thaw) { deathTick = s.Tick; return acts; }
            }
            deathTick = -1;
            return null;
        }

        /// <summary>
        /// A death says what did it: the banner's second line names the obstacle the sim blames
        /// (<see cref="SimState.DeathCause"/>), says "thawed" only for a thaw death, and that obstacle
        /// alone is marked red through the death pause, cleared once the rewind starts.
        /// </summary>
        IEnumerator CheckDeathReport(string dir, string id, bool thaw, string noun, System.Action<string, bool, string> report)
        {
            string name = $"death-report {id} {(thaw ? "thaw" : "hit")}";
            int index = Catalog.Levels.FindIndex(l => l.Id == id);
            var def = Catalog.Levels[index];
            var acts = FindDeath(def, thaw, out int deathTick);
            if (acts == null) { report(name, false, "no such death found"); yield break; }
            LoadLevel(index);
            State = Flow.Playing;
            Hud.SetVisible(true);
            Hud.SkipIntro();
            Session.IntroTime = 0.2f;
            Session.Autoplay = acts;
            Session.Speed = 3f;
            int cause = -2, culprit = -2, marked = -1;
            bool thawed = !thaw;
            string sub = null;
            int Marked()
            {
                int n = 0;
                foreach (var v in Session.Board.Sliders) if (v.Culprit) n++;
                foreach (var v in Session.Board.Lasers) if (v.Culprit) n++;
                foreach (var v in Session.Board.Rotors) if (v.Culprit) n++;
                return n;
            }
            Session.Died += c => { cause = c; thawed = Session.DeathThawed; sub = Hud.BannerSub; culprit = Session.Board.Culprit; marked = Marked(); };
            var run = new RunWatch(Session);
            while (Session.Deaths == 0 && run.Alive()) yield return null;
            Session.Speed = 1f;
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot(dir, $"death-report_{id}_{(thaw ? "thaw" : "hit")}_mark");
            float deadline = Time.realtimeSinceStartup + 10f;
            while (Session.State == LevelSession.Mode.Dying && Time.realtimeSinceStartup < deadline) yield return null;
            int after = Session.Board.Culprit, markedAfter = Marked();
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot(dir, $"death-report_{id}_{(thaw ? "thaw" : "hit")}_line");
            string want = cause >= 0 ? DeathReport.Describe(def, cause, thaw) + "  ·  rewinding…" : "(no cause)";
            bool ok = cause >= 0 && thawed == thaw && culprit == cause && marked == 1 && after == -1 && markedAfter == 0
                && sub == want && sub.Contains(noun) && sub.Contains("thawed") == thaw;
            report(name, ok, $"died at tick {deathTick}, cause {cause} ({(cause >= 0 ? DeathReport.Noun(def, cause) : "none")}), thawed={thawed}; "
                + $"banner \"{sub}\"; marked {marked} piece(s), culprit {culprit}; after the pause {markedAfter} marked");
            while (Session.State == LevelSession.Mode.Rewinding && Time.realtimeSinceStartup < deadline) yield return null;
        }

        /// <summary>
        /// Time waits for the first move: a fresh start (with the hold a player gets) stays at tick 0
        /// with its tag up, and starts again from 0 on a restart; Watch solution and the title's
        /// replays never wait. The input bot covers what starts the clock, through real input.
        /// </summary>
        IEnumerator CheckReadyHold(string dir, System.Action<string, bool, string> report)
        {
            forceReadyHold = true;
            int index = Catalog.Levels.FindIndex(l => l.Id == "1-2");
            StartLevel(index, false);
            yield return new WaitForSecondsRealtime(3.5f);
            int held = Session.Tick;
            bool tag = Hud.ReadyShown && Hud.ReadyText.Contains("TIME WAITS");
            yield return Shot(dir, "ready-hold_1-2");
            StartLevel(index, false); // what a restart does
            yield return new WaitForSecondsRealtime(1.5f);
            int again = Session.Tick;
            bool tagAgain = Hud.ReadyShown;
            StartWatch(index);
            yield return new WaitForSecondsRealtime(1.5f);
            int watched = Session.Tick;
            bool watchTag = Hud.ReadyShown;
            LoadAttract();
            yield return new WaitForSecondsRealtime(2f);
            int attract = Session.Tick;
            forceReadyHold = false;
            bool ok = held == 0 && tag && again == 0 && tagAgain && watched > 0 && !watchTag && attract > 0; // replays run on (their intros are 0.6 and 1.2 s)
            report("ready-hold", ok, $"fresh start: tick {held} after 3.5 s, tag {tag}; restart: tick {again}, tag {tagAgain}; "
                + $"Watch solution ran to tick {watched} in 1.5 s (tag {watchTag}); title replay ran to tick {attract} in 2 s");
            StartLevel(0, false);
        }

        /// <summary>
        /// A thaw death auto-rewinds to a tick the player can act from: not back into the freeze
        /// they thawed out of, which would replay the same death forever.
        /// </summary>
        IEnumerator CheckDefaultRewind(string dir, string id, System.Action<string, bool, string> report)
        {
            int index = Catalog.Levels.FindIndex(l => l.Id == id);
            var acts = FindThawDeath(Catalog.Levels[index], out int deathTick);
            if (acts == null) { report("default-rewind " + id, false, "no thaw death found"); yield break; }
            LoadLevel(index);
            State = Flow.Playing;
            Hud.SetVisible(true);
            Session.IntroTime = 0.2f;
            Session.Autoplay = acts;
            Session.Speed = 3f;
            SimState landed = null;
            Session.RewindChanged += on => { if (!on && landed == null) { landed = Session.Cur.Clone(); Session.Paused = true; } };
            float deadline = Time.realtimeSinceStartup + 30f;
            while (Session.State != LevelSession.Mode.Rewinding && Time.realtimeSinceStartup < deadline) yield return null;
            Session.Speed = 1f;
            while (landed == null && Time.realtimeSinceStartup < deadline) yield return null;
            var s = landed ?? Session.Cur;
            bool ok = landed != null && Session.Deaths == 1 && Rewind.Playable(s) && s.Tick <= deathTick - Rewind.DeathTicks;
            report("default-rewind " + id, ok, $"died at tick {deathTick}, rewound to {s.Tick} (frozen={s.PFrozen}, countdown={s.Countdown}, deaths={Session.Deaths})");
            yield return null;
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"default-rewind_{id}.png"));
            yield return null;
            yield return null;
            Session.Paused = false;
        }
    }
}
