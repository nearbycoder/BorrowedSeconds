using System;
using System.Collections.Generic;
using System.IO;
using BorrowedSeconds.Sim;
using NUnit.Framework;

namespace BorrowedSeconds.Tests
{
    /// <summary>
    /// The death banner names what killed the player. The simulation must blame an obstacle for
    /// every death (seeded random play on every level, thaw deaths and others), and the line must
    /// name that obstacle's kind and say "thawed" only for a thaw death.
    /// </summary>
    public class DeathReportTests
    {
        static List<LevelDef> levels;

        static List<LevelDef> Levels() => levels ??= LevelDef.LoadAll(File.ReadAllText("Assets/Resources/Levels/levels.json"));

        static IEnumerable<string> LevelIds()
        {
            foreach (var l in Levels()) yield return l.Id;
        }

        static readonly Dictionary<LevelDef.Kind, string> Nouns = new Dictionary<LevelDef.Kind, string>
        {
            { LevelDef.Kind.Slider, "the block" }, { LevelDef.Kind.Laser, "the beam" }, { LevelDef.Kind.Rotor, "a rotor arm" },
        };

        [TestCaseSource(nameof(LevelIds))]
        public void LineNamesEveryObstacleByKind(string id)
        {
            var d = Levels().Find(l => l.Id == id);
            for (int i = 0; i < d.ObstacleCount; i++)
            {
                string noun = Nouns[d.KindOf(i, out _)];
                Assert.AreEqual(noun, DeathReport.Noun(d, i));
                Assert.AreEqual("you thawed inside " + noun, DeathReport.Describe(d, i, true));
                Assert.AreEqual(noun + " caught you", DeathReport.Describe(d, i, false));
            }
            Assert.AreEqual("", DeathReport.Describe(d, -1, true));
            Assert.AreEqual("", DeathReport.Describe(d, d.ObstacleCount, false));
        }

        static int deaths, thawDeaths;

        [TestCaseSource(nameof(LevelIds))]
        public void EveryDeathHasACause(string id)
        {
            var d = Levels().Find(l => l.Id == id);
            var rng = new Random(2000 + Levels().IndexOf(d));
            for (int trial = 0; trial < 150; trial++)
            {
                var s = Simulation.Create(d);
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
                    wasFrozen = s.PFrozen > 0;
                    Simulation.Step(d, s, a);
                }
                if (!s.Dead) continue;
                deaths++;
                if (wasFrozen) thawDeaths++;
                Assert.That(s.DeathCause, Is.InRange(0, d.ObstacleCount - 1), $"{id}: death at tick {s.Tick} blames no obstacle");
                Assert.IsFalse(s.IsObstacleFrozen(d, s.DeathCause), $"{id}: death at tick {s.Tick} blames a frozen obstacle");
            }
        }

        [Test]
        public void ZzSampleCoversManyDeaths()
        {
            // runs after the per-level cases (alphabetical); keeps the sample from going vacuous
            if (deaths == 0) foreach (var id in LevelIds()) EveryDeathHasACause(id);
            Assert.GreaterOrEqual(deaths, 500, "too few deaths sampled");
            Assert.GreaterOrEqual(thawDeaths, 50, "too few thaw deaths sampled");
        }
    }
}
