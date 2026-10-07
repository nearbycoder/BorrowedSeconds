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
            var item = settings.Menu.Items[7];
            bool labelled = item.Label == "Game speed";
            settings.Menu.Selected = 7;
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
