using System.Collections.Generic;
using System.IO;
using BorrowedSeconds.Sim;
using NUnit.Framework;

namespace BorrowedSeconds.Tests
{
    /// <summary>
    /// The exit says why it won't let you leave: sealed until every dial latches (with the count),
    /// or still in debt. Along every solver route, each tick spent standing on the exit without
    /// winning has one of those two reasons, so the note never leaves a player guessing.
    /// </summary>
    public class ExitNoteTests
    {
        static List<LevelDef> levels;
        static Dictionary<string, List<TimedAction>> solutions;

        static void Load()
        {
            if (levels != null) return;
            levels = LevelDef.LoadAll(File.ReadAllText("Assets/Resources/Levels/levels.json"));
            solutions = new Dictionary<string, List<TimedAction>>();
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
                solutions[MiniJson.Str(d, "id", "")] = acts;
            }
        }

        static IEnumerable<string> LevelIds()
        {
            Load();
            foreach (var l in levels) yield return l.Id;
        }

        static IEnumerable<string> DialLevelIds()
        {
            Load();
            foreach (var l in levels) if (l.Locks.Length > 0) yield return l.Id;
        }

        [TestCaseSource(nameof(DialLevelIds))]
        public void SealedExitCountsTheDials(string id)
        {
            Load();
            var d = levels.Find(l => l.Id == id);
            var s = Simulation.Create(d);
            Assert.AreEqual("", ExitNote.Line(d, s), "nothing at the start tile");
            s.P = s.F = d.Exit;
            Assert.AreEqual(ExitNote.Kind.Sealed, ExitNote.Of(d, s, out int latched));
            Assert.AreEqual(0, latched);
            string none = d.Locks.Length == 1 ? "Exit sealed: latch the dial first" : $"Exit sealed: latch every dial (0 of {d.Locks.Length} latched)";
            Assert.AreEqual(none, ExitNote.Line(d, s));
            if (d.Locks.Length > 1)
            {
                s.LockCharge[0] = Rules.LockTicks;
                s.LockCharge[1] = Rules.LockTicks - 1; // charging isn't latched
                Assert.AreEqual($"Exit sealed: latch every dial (1 of {d.Locks.Length} latched)", ExitNote.Line(d, s));
            }
            s.Moving = true;
            Assert.AreEqual("", ExitNote.Line(d, s), "nothing while stepping over it");
        }

        [Test]
        public void InDebtOnAnOpenExit()
        {
            Load();
            var d = levels.Find(l => l.Id == "1-1");
            var s = Simulation.Create(d);
            s.P = s.F = d.Exit;
            s.ExitOpen = true;
            Assert.AreEqual("", ExitNote.Line(d, s), "free to leave: nothing to say");
            s.Countdown = 30;
            Assert.AreEqual("Pay your debt here: you leave as you thaw", ExitNote.Line(d, s));
            s.Countdown = 0;
            s.PFrozen = 20;
            Assert.AreEqual(ExitNote.Kind.InDebt, ExitNote.Of(d, s, out _), "paying on the exit");
            s.PFrozen = 0;
            s.Pending = true;
            Assert.AreEqual(ExitNote.Kind.InDebt, ExitNote.Of(d, s, out _));
        }

        /// <summary>Every tick a route stands on the exit without winning, the note has a reason.</summary>
        [TestCaseSource(nameof(LevelIds))]
        public void EveryWaitOnTheExitHasAReason(string id)
        {
            Load();
            var d = levels.Find(l => l.Id == id);
            var actions = solutions[id];
            var s = Simulation.Create(d);
            int k = 0;
            while (!s.Won && !s.Dead && s.Tick < 2000)
            {
                if (s.P == d.Exit && !s.Moving)
                    Assert.AreNotEqual(ExitNote.Kind.None, ExitNote.Of(d, s, out _), $"on the exit at tick {s.Tick} with nothing to say");
                int act = k < actions.Count && actions[k].Tick == s.Tick ? actions[k++].Action : Act.None;
                Simulation.Step(d, s, act);
            }
            Assert.IsTrue(s.Won);
            Assert.AreEqual(ExitNote.Kind.None, ExitNote.Of(d, s, out _), "a win says nothing");
        }
    }
}
