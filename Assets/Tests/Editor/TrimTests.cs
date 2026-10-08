using System.Collections.Generic;
using System.IO;
using BorrowedSeconds.Sim;
using BorrowedSeconds.View;
using NUnit.Framework;

namespace BorrowedSeconds.Tests
{
    /// <summary>
    /// The board's brass trim (<see cref="BoardView.ExposedEdges"/>): every side of a floor or wall
    /// tile that faces void or the map's edge gets trim, once, and no trim sits between two tiles.
    /// </summary>
    public class TrimTests
    {
        static List<LevelDef> levels;

        static List<LevelDef> Levels() => levels ??= LevelDef.LoadAll(File.ReadAllText("Assets/Resources/Levels/levels.json"));

        static IEnumerable<string> LevelIds()
        {
            foreach (var l in Levels()) yield return l.Id;
        }

        [TestCaseSource(nameof(LevelIds))]
        public void TrimRunsRoundEveryOuterEdge(string id)
        {
            var d = Levels().Find(l => l.Id == id);
            var edges = BoardView.ExposedEdges(d);
            var seen = new HashSet<(int, int)>();
            foreach (var (tile, dir) in edges)
            {
                Assert.IsTrue(seen.Add((tile, dir)), $"{id}: edge {tile}/{dir} twice");
                Assert.AreNotEqual(Tile.Void, d.Tiles[tile], $"{id}: trim on a void tile");
                int nx = d.X(tile) + Dirs.DX[dir], ny = d.Y(tile) + Dirs.DY[dir];
                bool outside = nx < 0 || ny < 0 || nx >= d.W || ny >= d.H;
                Assert.IsTrue(outside || d.Tiles[d.Idx(nx, ny)] == Tile.Void, $"{id}: trim between two tiles at {tile}/{dir}");
            }
            // counted the other way round: each void cell (or the outside) next to a tile is one edge
            int expected = 0;
            for (int y = -1; y <= d.H; y++)
            for (int x = -1; x <= d.W; x++)
            {
                bool inside = x >= 0 && y >= 0 && x < d.W && y < d.H;
                if (inside && d.Tiles[d.Idx(x, y)] != Tile.Void) continue;
                for (int dir = 0; dir < 4; dir++)
                {
                    int nx = x + Dirs.DX[dir], ny = y + Dirs.DY[dir];
                    if (nx >= 0 && ny >= 0 && nx < d.W && ny < d.H && d.Tiles[d.Idx(nx, ny)] != Tile.Void) expected++;
                }
            }
            Assert.AreEqual(expected, edges.Count, id);
            Assert.Greater(edges.Count, 0, id);
        }
    }
}
