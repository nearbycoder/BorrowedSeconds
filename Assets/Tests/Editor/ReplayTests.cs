using System.Collections.Generic;
using System.IO;
using BorrowedSeconds.Sim;
using NUnit.Framework;

namespace BorrowedSeconds.Tests
{
    /// <summary>
    /// The solver runs on .NET 8; the game runs on Unity's Mono. These tests replay every saved
    /// solver solution through the simulation inside Unity and require the exact same outcome,
    /// proving the shared sim is deterministic across both runtimes.
    /// </summary>
    public class ReplayTests
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

        [Test]
        public void EveryLevelHasASolution()
        {
            Load();
            Assert.AreEqual(20, levels.Count, "level count");
            foreach (var l in levels) Assert.IsTrue(solutions.ContainsKey(l.Id), "no solution for " + l.Id);
        }

        [TestCaseSource(nameof(LevelIds))]
        public void SolverReplayWinsAtPar(string id)
        {
            Load();
            var lv = levels.Find(l => l.Id == id);
            var (par, actions) = solutions[id];
            var s = Solver.Replay(lv, actions, par + 5);
            Assert.IsFalse(s.Dead, $"{id}: died at tick {s.Tick}");
            Assert.IsTrue(s.Won, $"{id}: did not win");
            Assert.AreEqual(par, s.Tick, $"{id}: won at a different tick than the solver");
        }

        [TestCaseSource(nameof(LevelIds))]
        public void IdlingNeverWins(string id)
        {
            // sanity: no level is won by standing still
            Load();
            var lv = levels.Find(l => l.Id == id);
            var s = Solver.Replay(lv, new List<TimedAction>(), 600);
            Assert.IsFalse(s.Won, $"{id}: won without input");
        }

        [Test]
        public void StateKeyRoundTrips()
        {
            Load();
            foreach (var lv in levels)
            {
                var (_, actions) = solutions[lv.Id];
                var packer = new StatePacker(lv);
                var s = Simulation.Create(lv);
                var probe = new SimState(lv);
                int k = 0;
                while (!s.Dead && !s.Won && s.Tick < 2000)
                {
                    if (s.CanActNext)
                    {
                        var key = packer.Pack(s);
                        packer.Unpack(key, probe, s.Tick);
                        Assert.IsTrue(key.Equals(packer.Pack(probe)), $"{lv.Id}: pack/unpack mismatch at {s.Tick}");
                    }
                    int act = Act.None;
                    if (k < actions.Count && actions[k].Tick == s.Tick) act = actions[k++].Action;
                    Simulation.Step(lv, s, act);
                }
            }
        }
    }
}
