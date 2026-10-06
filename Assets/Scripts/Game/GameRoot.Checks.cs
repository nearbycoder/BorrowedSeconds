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
        IEnumerator Checks(string dir)
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

            foreach (var id in new[] { "1-2", "2-1", "4-5" })
                yield return CheckDefaultRewind(dir, id, Report);

            yield return CheckFocusPause(dir, Report);
            yield return CheckHint(dir, Report);
            yield return CheckChannels(dir, "6-4", new[] { 1, 1 }, Report);
            yield return CheckChannels(dir, "2-2", new[] { 2 }, Report);
            yield return CheckWatch(dir, Report);

            log.Add($"done fail={fail}");
            File.WriteAllLines(Path.Combine(dir, "checks.log"), log);
            yield return new WaitForSecondsRealtime(0.3f);
            Application.Quit(fail == 0 ? 0 : 1);
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
                Session.Speed = shoot ? 1f : 4f;
                float deadline = Time.realtimeSinceStartup + 40f;
                bool shot = false;
                while (Session.State != LevelSession.Mode.Won && !Session.Cur.Dead && Time.realtimeSinceStartup < deadline)
                {
                    if (shoot && !shot && Session.Tick >= 120) { shot = true; Session.Paused = true; yield return Shot(dir, "watch_playing"); Session.Paused = false; }
                    yield return null;
                }
                int won = Session.State == LevelSession.Mode.Won ? Session.Tick : -1;
                while (State == Flow.Watching && Time.realtimeSinceStartup < deadline) yield return null;
                bool fresh = State == Flow.Playing && Session.Autoplay == null && Session.Tick < 5 && !Hud.Watching;
                if (won == par && fresh) ok++;
                else bad.Add($"{def.Id} won={won} par={par} fresh={fresh}");
            }
            bool untouched = Progress() == before;
            report("watch-solution", ok == Catalog.Levels.Count && untouched,
                $"{ok}/{Catalog.Levels.Count} won at par and returned fresh; progress untouched={untouched}" + (bad.Count > 0 ? "; " + string.Join("; ", bad) : ""));
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
