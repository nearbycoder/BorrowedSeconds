using System.Collections.Generic;
using System.IO;
using BorrowedSeconds.Sim;
using NUnit.Framework;

namespace BorrowedSeconds.Tests
{
    /// <summary>
    /// The pause menu's clue: on every level it names the solver route's first borrow, and the tile
    /// the route stands on when its first debt freezes it, checked against a separate replay.
    /// </summary>
    public class ClueTests
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
        public void ClueNamesTheFirstBorrowAndTheFirstDebtTile(string id)
        {
            Load();
            var d = levels.Find(l => l.Id == id);
            var (par, actions) = solutions[id];
            var c = Clue.For(d, actions);
            Assert.IsTrue(c.Valid, "every route borrows and pays a debt");

            var first = actions.Find(a => Act.IsBorrow(a.Action));
            Assert.AreEqual(Act.BorrowTarget(first.Action), c.Obstacle, "the first borrow's target");
            Assert.AreEqual(first.Tick, c.BorrowTick);
            Assert.Less(c.BorrowTick, c.FreezeTick);
            Assert.LessOrEqual(c.FreezeTick, par, "the debt falls due before the win");

            // a separate replay: the player is free one tick before, frozen on the clue's tile at its tick
            var s = Simulation.Create(d);
            int k = 0;
            while (s.Tick < c.FreezeTick)
            {
                Assert.AreEqual(0, s.PFrozen, $"frozen before the clue's tick, at {s.Tick}");
                int act = k < actions.Count && actions[k].Tick == s.Tick ? actions[k++].Action : Act.None;
                Simulation.Step(d, s, act);
            }
            Assert.Greater(s.PFrozen, 0);
            Assert.AreEqual(c.Tile, s.P);
            Assert.IsFalse(s.Moving, "frozen on a tile, not between two");

            string kind = d.KindOf(c.Obstacle, out _) switch { LevelDef.Kind.Slider => "block", LevelDef.Kind.Laser => "laser", _ => "rotor" };
            string words = c.Words(d);
            Assert.AreEqual($"freeze the {kind} marked FREEZE FIRST, and be on the tile marked DEBT HERE when your debt falls due.", words);
        }

        /// <summary>One step per loan: each borrow's target, and the tile the route is frozen on when
        /// that loan's debt falls due; a player in the route's state is on the right step.</summary>
        [TestCaseSource(nameof(LevelIds))]
        public void StepsFollowEveryLoan(string id)
        {
            Load();
            var d = levels.Find(l => l.Id == id);
            var (par, actions) = solutions[id];
            var steps = Clue.Steps(d, actions);
            var borrows = actions.FindAll(a => Act.IsBorrow(a.Action));
            Assert.AreEqual(borrows.Count, steps.Count, "a step per loan");
            var first = Clue.For(d, actions);
            Assert.AreEqual(first.Obstacle, steps[0].Obstacle);
            Assert.AreEqual(first.Tile, steps[0].Tile);
            Assert.AreEqual(first.FreezeTick, steps[0].FreezeTick);

            var s = Simulation.Create(d);
            SimState early = null;
            int k = 0;
            while (!s.Won && !s.Dead && s.Tick <= par)
            {
                for (int n = 0; n < steps.Count; n++)
                {
                    if (s.Tick == steps[n].BorrowTick)
                    {
                        Assert.IsFalse(Clue.LoanOut(s), $"step {n}: free to borrow at {s.Tick}");
                        Assert.IsTrue(s.CanActNext);
                        Assert.AreEqual(n, Clue.StepFor(s, steps.Count), $"on step {n} when its borrow comes");
                    }
                    if (s.Tick == steps[n].FreezeTick)
                    {
                        Assert.Greater(s.PFrozen, 0, $"step {n}: frozen at its tick");
                        Assert.AreEqual(steps[n].Tile, s.P, $"step {n}: on its tile");
                        Assert.AreEqual(n, Clue.StepFor(s, steps.Count), $"still on step {n} while its debt is paid");
                    }
                }
                if (s.Tick == steps[0].BorrowTick + 1) early = s.Clone();
                int act = k < actions.Count && actions[k].Tick == s.Tick ? actions[k++].Action : Act.None;
                if (Act.IsBorrow(act))
                {
                    int n = borrows.FindIndex(b => b.Tick == s.Tick);
                    Assert.AreEqual(Act.BorrowTarget(act), steps[n].Obstacle, $"step {n} names borrow {n}'s target");
                    Assert.Less(steps[n].BorrowTick, steps[n].FreezeTick);
                    if (n > 0) Assert.Greater(steps[n].BorrowTick, steps[n - 1].FreezeTick, "one loan at a time");
                }
                Simulation.Step(d, s, act);
            }
            Assert.IsTrue(s.Won);
            Assert.AreEqual(steps.Count - 1, Clue.StepFor(s, steps.Count), "past the last loan it stays on the last step");
            Assert.AreEqual(0, Clue.StepFor(early, steps.Count), "a state from the first loan (what a rewind restores) is on step 0");
        }

        [Test]
        public void StepWords()
        {
            Load();
            var d = levels.Find(l => l.Id == "4-2");
            var steps = Clue.Steps(d, solutions["4-2"].actions);
            Assert.AreEqual(3, steps.Count);
            Assert.AreEqual("for loan 2 of 3, freeze the laser marked FREEZE NEXT, and be on the tile marked DEBT HERE when its debt falls due.", steps[1].Words(d, 1, 3));
            Assert.AreEqual(steps[0].Words(d), steps[0].Words(d, 0, 1), "one loan: the words as before");
            Assert.AreEqual("FREEZE FIRST", Clue.ObstacleLabel(0));
            Assert.AreEqual("FREEZE NEXT", Clue.ObstacleLabel(2));
            Assert.AreEqual(-1, Clue.StepFor(Simulation.Create(d), 0), "no steps, no step");
        }

        [Test]
        public void NoBorrowNoClue()
        {
            Load();
            var d = levels[0];
            Assert.IsFalse(Clue.For(d, new List<TimedAction>()).Valid);
        }
    }
}
