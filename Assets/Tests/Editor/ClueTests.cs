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

        [Test]
        public void NoBorrowNoClue()
        {
            Load();
            var d = levels[0];
            Assert.IsFalse(Clue.For(d, new List<TimedAction>()).Valid);
        }
    }
}
