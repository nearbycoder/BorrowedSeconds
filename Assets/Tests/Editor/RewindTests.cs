using System;
using System.Collections.Generic;
using System.IO;
using BorrowedSeconds.Sim;
using NUnit.Framework;

namespace BorrowedSeconds.Tests
{
    /// <summary>
    /// The automatic rewind after a death must hand control back. Thawing inside a hazard kills
    /// three seconds into a freeze, so a plain two-second rewind lands inside the same freeze and
    /// the death replays forever. Seeded random play produces thaw deaths on every level it can;
    /// each one must rewind to a state the player can act from, and from which some play survives.
    /// </summary>
    public class RewindTests
    {
        static List<LevelDef> levels;

        static List<LevelDef> Levels() => levels ??= LevelDef.LoadAll(File.ReadAllText("Assets/Resources/Levels/levels.json"));

        static IEnumerable<string> LevelIds()
        {
            foreach (var l in Levels()) yield return l.Id;
        }

        static int RandomAction(Random rng, LevelDef d, SimState s)
        {
            if (!s.CanActNext) return Act.None;
            int r = rng.Next(10);
            if (r < 6) return Act.Move(rng.Next(4));
            if (r == 6 && d.ObstacleCount > 0) return Act.Borrow(rng.Next(d.ObstacleCount));
            return Act.None;
        }

        /// <summary>Thaw deaths from seeded random play, as (history, death index).</summary>
        static List<(List<SimState> history, int death)> ThawDeaths(LevelDef d, Random rng, int want)
        {
            var found = new List<(List<SimState>, int)>();
            for (int trial = 0; trial < 300 && found.Count < want; trial++)
            {
                var s = Simulation.Create(d);
                var history = new List<SimState> { s.Clone() };
                while (s.Tick < 600 && !s.Dead && !s.Won)
                {
                    Simulation.Step(d, s, RandomAction(rng, d, s));
                    history.Add(s.Clone());
                }
                if (s.Dead && history[history.Count - 2].PFrozen > 0) found.Add((history, history.Count - 1));
            }
            return found;
        }

        static int totalThawDeaths;

        [TestCaseSource(nameof(LevelIds))]
        public void AutoRewindAfterThawDeathHandsBackControl(string id)
        {
            var d = Levels().Find(l => l.Id == id);
            var rng = new Random(1000 + Levels().IndexOf(d));
            foreach (var (history, death) in ThawDeaths(d, rng, 6))
            {
                totalThawDeaths++;
                Assert.IsTrue(history[death - Rewind.DeathTicks].PFrozen > 0, $"{id}: expected the old two-second rewind to land in the freeze");
                int target = Rewind.AfterDeath(history, death);
                Assert.LessOrEqual(target, death - Rewind.DeathTicks, $"{id}: rewound less than two seconds");
                Assert.IsTrue(target == 0 || Rewind.Playable(history[target]), $"{id}: rewound to tick {target}, where the player can't act");

                bool survived = false;
                for (int k = 0; k < 100 && !survived; k++)
                {
                    var s = history[target].Clone();
                    while (s.Tick < death && !s.Dead && !s.Won) Simulation.Step(d, s, RandomAction(rng, d, s));
                    survived = !s.Dead;
                }
                Assert.IsTrue(survived, $"{id}: no play from tick {target} avoids the death at tick {death}");
            }
        }

        [Test]
        public void ZzSampleCoversManyThawDeaths()
        {
            // runs after the per-level cases (alphabetical); keeps the sample from going vacuous
            if (totalThawDeaths == 0) foreach (var id in LevelIds()) AutoRewindAfterThawDeathHandsBackControl(id);
            Assert.GreaterOrEqual(totalThawDeaths, 50, "too few thaw deaths sampled");
        }

        [Test]
        public void OrdinaryDeathKeepsTheTwoSecondRewind()
        {
            // a free, undebted player rewinds exactly two seconds, as before
            var d = Levels()[0];
            var history = new List<SimState>();
            var s = Simulation.Create(d);
            history.Add(s.Clone());
            for (int i = 0; i < 100; i++)
            {
                Simulation.Step(d, s, Act.None);
                history.Add(s.Clone());
            }
            Assert.AreEqual(100 - Rewind.DeathTicks, Rewind.AfterDeath(history, 100));
            Assert.AreEqual(0, Rewind.AfterDeath(history, 10));
        }
    }
}
