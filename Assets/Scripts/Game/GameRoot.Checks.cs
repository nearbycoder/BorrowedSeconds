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
            void Report(string name, bool ok, string detail)
            {
                if (!ok) fail++;
                log.Add($"{(ok ? "PASS" : "FAIL")} {name}: {detail}");
                Debug.Log("[Checks] " + log[log.Count - 1]);
            }
            // -bsOnly a,b runs just the checks whose names start with one of these
            bool Want(string name) => only == null || System.Array.Exists(only.Split(','), o => name.StartsWith(o));

            if (Want("default-rewind"))
                foreach (var id in new[] { "1-2", "2-1", "4-5" })
                    yield return CheckDefaultRewind(dir, id, Report);
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

            log.Add($"done fail={fail}");
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
        static List<TimedAction> FindThawDeath(LevelDef d, out int deathTick)
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
                if (s.Dead && wasFrozen) { deathTick = s.Tick; return acts; }
            }
            deathTick = -1;
            return null;
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
