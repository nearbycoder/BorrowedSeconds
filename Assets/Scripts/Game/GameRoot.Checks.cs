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

            log.Add($"done fail={fail}");
            File.WriteAllLines(Path.Combine(dir, "checks.log"), log);
            yield return new WaitForSecondsRealtime(0.3f);
            Application.Quit(fail == 0 ? 0 : 1);
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
