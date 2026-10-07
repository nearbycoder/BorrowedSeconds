using System.Collections.Generic;
using System.IO;
using BorrowedSeconds.Sim;
using NUnit.Framework;

namespace BorrowedSeconds.Tests
{
    /// <summary>
    /// The ground each obstacle owns for pointer aiming (<see cref="AimZones"/>): a slider's track,
    /// a laser's lane up to the first static solid, a rotor's pivot and sweep, never void (apart from a pivot).
    /// </summary>
    public class AimZoneTests
    {
        static List<LevelDef> levels;

        static List<LevelDef> Levels() => levels ??= LevelDef.LoadAll(File.ReadAllText("Assets/Resources/Levels/levels.json"));

        static IEnumerable<string> LevelIds()
        {
            foreach (var l in Levels()) yield return l.Id;
        }

        [Test]
        public void FirstLoanSliderOwnsItsWholeLane()
        {
            var d = Levels().Find(l => l.Id == "1-1");
            var zone = AimZones.Build(d)[d.ObstacleIndex(LevelDef.Kind.Slider, 0)];
            int count = 0;
            for (int t = 0; t < d.Tiles.Length; t++)
                if (zone.Has(t)) { count++; Assert.AreEqual(4, d.Y(t), "only lane tiles"); }
            Assert.AreEqual(15, count);
        }

        [TestCaseSource(nameof(LevelIds))]
        public void ZonesCoverWhereEachObstacleIs(string id)
        {
            var d = Levels().Find(l => l.Id == id);
            var zones = AimZones.Build(d);
            Assert.AreEqual(d.ObstacleCount, zones.Length);
            var pivots = new HashSet<int>();
            foreach (var r in d.Rotors) pivots.Add(r.Tile); // a pivot may stand off the floor (3-1's hangs below its corridor)
            for (int t = 0; t < d.Tiles.Length; t++)
                for (int o = 0; o < zones.Length; o++)
                    if (zones[o].Has(t) && !pivots.Contains(t)) Assert.AreNotEqual(Tile.Void, d.Tiles[t], $"{id}: obstacle {o} owns void at {d.X(t)},{d.Y(t)}");
            for (int i = 0; i < d.Sliders.Length; i++)
            {
                var z = zones[d.ObstacleIndex(LevelDef.Kind.Slider, i)];
                foreach (int t in d.Sliders[i].Path) Assert.IsTrue(z.Has(t), $"{id}: slider {i} track");
            }
            for (int i = 0; i < d.Lasers.Length; i++)
            {
                var l = d.Lasers[i];
                var z = zones[d.ObstacleIndex(LevelDef.Kind.Laser, i)];
                Assert.IsTrue(z.Has(l.Tile), $"{id}: laser {i} emitter");
                int x = d.X(l.Tile) + Dirs.DX[l.Dir], y = d.Y(l.Tile) + Dirs.DY[l.Dir];
                while (d.InBounds(x, y) && !d.StaticSolid.Has(d.Idx(x, y)))
                {
                    Assert.IsTrue(z.Has(d.Idx(x, y)), $"{id}: laser {i} lane at {x},{y}");
                    x += Dirs.DX[l.Dir];
                    y += Dirs.DY[l.Dir];
                }
                if (d.InBounds(x, y)) Assert.IsFalse(z.Has(d.Idx(x, y)), $"{id}: laser {i} lane runs past the wall at {x},{y}");
            }
            for (int i = 0; i < d.Rotors.Length; i++)
            {
                var r = d.Rotors[i];
                var z = zones[d.ObstacleIndex(LevelDef.Kind.Rotor, i)];
                Assert.IsTrue(z.Has(r.Tile), $"{id}: rotor {i} pivot");
                for (int o = 0; o < 4; o++)
                    for (int t = 0; t < d.Tiles.Length; t++)
                        if (r.Rays[o].Has(t) && d.Tiles[t] != Tile.Void) Assert.IsTrue(z.Has(t), $"{id}: rotor {i} arm at {d.X(t)},{d.Y(t)}");
            }
        }
    }
}
