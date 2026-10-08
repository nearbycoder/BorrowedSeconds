using System.Collections.Generic;
using System.IO;
using BorrowedSeconds.Sim;
using NUnit.Framework;

namespace BorrowedSeconds.Tests
{
    /// <summary>
    /// The best-run ghost's record: a run is what survives the player's rewinds, and its saved text
    /// replays to a win at exactly its time. On every level, the solver's route is played with a
    /// detour of random actions that a rewind then takes back; the recorded run must equal the route
    /// and win at par.
    /// </summary>
    public class RunLogTests
    {
        static List<LevelDef> levels;
        static Dictionary<string, (int par, List<TimedAction> actions)> solutions;

        static void Load()
        {
            if (levels != null) return;
            levels = LevelDef.LoadAll(File.ReadAllText("Assets/Resources/Levels/levels.json"));
            solutions = new Dictionary<string, (int, List<TimedAction>)>();
            var root = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText("Assets/Resources/Levels/solutions.json"));
            foreach (var o in MiniJson.List(root, "levels"))
            {
                var d = (Dictionary<string, object>)o;
                var acts = new List<TimedAction>();
                foreach (var a in MiniJson.List(d, "actions"))
                {
                    var p = (List<object>)a;
                    acts.Add(new TimedAction(System.Convert.ToInt32(p[0]), System.Convert.ToInt32(p[1])));
                }
                solutions[MiniJson.Str(d, "id", "")] = (MiniJson.Int(d, "par", 0), acts);
            }
        }

        static IEnumerable<string> LevelIds()
        {
            Load();
            foreach (var l in levels) yield return l.Id;
        }

        [TestCaseSource(nameof(LevelIds))]
        public void RunWithARewoundDetourReplaysToPar(string id)
        {
            Load();
            var d = levels.Find(l => l.Id == id);
            var (par, route) = solutions[id];
            var rng = new System.Random(500 + levels.IndexOf(d));
            int detourAt = par / 2;

            var log = new RunLog();
            var history = new List<SimState>();
            var s = Simulation.Create(d);
            history.Add(s.Clone());
            int k = 0;
            bool detoured = false;
            while (!s.Won && !s.Dead && s.Tick <= par + 5)
            {
                if (!detoured && s.Tick == detourAt)
                {
                    // wander for a second, then rewind back to where the detour began
                    for (int n = 0; n < 20 && !s.Dead && !s.Won; n++)
                    {
                        int a = s.CanActNext && rng.Next(3) == 0 ? Act.Move(rng.Next(4)) : Act.None;
                        log.Add(s.Tick, a);
                        Simulation.Step(d, s, a);
                        history.Add(s.Clone());
                    }
                    history.RemoveRange(detourAt + 1, history.Count - detourAt - 1);
                    s = history[detourAt].Clone();
                    log.TruncateTo(s.Tick);
                    detoured = true;
                    continue;
                }
                int act = k < route.Count && route[k].Tick == s.Tick ? route[k++].Action : Act.None;
                log.Add(s.Tick, act);
                Simulation.Step(d, s, act);
                history.Add(s.Clone());
            }
            Assert.IsTrue(s.Won, "the route still wins after the detour is rewound");
            Assert.AreEqual(par, s.Tick);
            Assert.AreEqual(par, log.Count, "one recorded action per tick played");

            string text = RunLog.Encode(log.Actions());
            Assert.AreEqual(RunLog.Encode(route), text, "the recorded run is the route, with the detour gone");
            var end = Solver.Replay(d, RunLog.Decode(text), par + 20);
            Assert.IsTrue(end.Won);
            Assert.AreEqual(par, end.Tick);
        }

        [Test]
        public void TextRoundTripsAndDamageIsRefused()
        {
            Load();
            foreach (var (_, route) in solutions.Values)
            {
                var back = RunLog.Decode(RunLog.Encode(route));
                Assert.AreEqual(route.Count, back.Count);
                for (int i = 0; i < route.Count; i++)
                {
                    Assert.AreEqual(route[i].Tick, back[i].Tick);
                    Assert.AreEqual(route[i].Action, back[i].Action);
                }
            }
            Assert.AreEqual(0, RunLog.Decode("").Count);
            Assert.IsNull(RunLog.Decode(null));
            foreach (var bad in new[] { "x", "3:", ":4", "3:1,2:4", "3:1,3:2", "-1:2", "3:1;4:2", "3:1," })
                Assert.IsNull(RunLog.Decode(bad), bad);
        }
    }
}
