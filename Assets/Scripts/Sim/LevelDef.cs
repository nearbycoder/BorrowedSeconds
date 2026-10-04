using System;
using System.Collections.Generic;

namespace BorrowedSeconds.Sim
{
    public sealed class SliderDef
    {
        public int[] Path;      // tile indices along the track
        public bool Loop;       // loop (wraps) or ping-pong
        public int Speed = 4;   // ticks per tile
        public int Dwell;       // extra rest ticks at each end (ping-pong only)
        public int Start;       // starting index on the path
        public int StartDir = 1;
        public int Phase;       // ticks of pre-roll before the level starts
    }

    public sealed class LaserDef
    {
        public int Tile;        // emitter tile (solid)
        public int Dir;
        public int On = 20, Off = 20;  // Off == 0 means always lit
        public int Phase;
        public int Plate = -1;  // plate channel that switches this laser off while held
        public int Period => Off == 0 ? 1 : On + Off;
    }

    public sealed class RotorDef
    {
        public int Tile;        // pivot tile (solid)
        public int ArmMask;     // arms at orientation 0, bit d = direction d
        public int Length = 2;
        public bool Clockwise = true;
        public int Turn = 4;    // ticks per 90 degree sweep
        public int Hold = 16;   // rest ticks between sweeps
        public int Phase;
        public int StartOrient;

        // precomputed per orientation
        public TileMask[] Rays = new TileMask[4];
        public TileMask[] WedgeCW = new TileMask[4];
        public TileMask[] WedgeCCW = new TileMask[4];
        public bool[] WedgeCWOut = new bool[4];   // sweep would leave the board
        public bool[] WedgeCCWOut = new bool[4];
    }

    public struct DeviceDef
    {
        public int Tile;
        public int Channel;
    }

    /// <summary>A parsed, validated level. Coordinates are (col,row) with row 0 at the top.</summary>
    public sealed class LevelDef
    {
        public string Id = "", Name = "", Hint = "";
        public int Chapter, Number;
        public int W, H;
        public Tile[] Tiles;
        public int Start, Exit;
        public int Term = Rules.DefaultTerm;
        public int LoanLimit = -1;
        public DeviceDef[] Plates = Array.Empty<DeviceDef>();
        public DeviceDef[] Gates = Array.Empty<DeviceDef>();
        public int[] Locks = Array.Empty<int>();
        public SliderDef[] Sliders = Array.Empty<SliderDef>();
        public LaserDef[] Lasers = Array.Empty<LaserDef>();
        public RotorDef[] Rotors = Array.Empty<RotorDef>();
        public TileMask StaticSolid;   // walls, void, pivots, emitters
        public TileMask ArmBlock;      // what stops a rotor arm: walls, pivots, emitters (arms pass over void)

        // validation expectations (checked by the solver)
        public bool ExpectBorrow, ExpectDebt;
        public int ExpectMargin;
        public int MinLoans;
        /// <summary>Solver state cap for this level's proofs (0 = solver default).</summary>
        public int SearchBudget;
        /// <summary>Ticks at the start where the par solution may only wait (reaction time).</summary>
        public int StartDelay;
        public string Notes = "";

        public int ObstacleCount => Sliders.Length + Lasers.Length + Rotors.Length;
        public int Idx(int x, int y) => y * W + x;
        public int X(int idx) => idx % W;
        public int Y(int idx) => idx / W;
        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;

        public int Neighbor(int idx, int dir)
        {
            int x = X(idx) + Dirs.DX[dir], y = Y(idx) + Dirs.DY[dir];
            return InBounds(x, y) ? Idx(x, y) : -1;
        }

        public enum Kind { Slider, Laser, Rotor }

        public Kind KindOf(int obstacle, out int local)
        {
            if (obstacle < Sliders.Length) { local = obstacle; return Kind.Slider; }
            obstacle -= Sliders.Length;
            if (obstacle < Lasers.Length) { local = obstacle; return Kind.Laser; }
            local = obstacle - Lasers.Length;
            return Kind.Rotor;
        }

        public int ObstacleIndex(Kind kind, int local)
        {
            switch (kind)
            {
                case Kind.Slider: return local;
                case Kind.Laser: return Sliders.Length + local;
                default: return Sliders.Length + Lasers.Length + local;
            }
        }

        // ------------------------------------------------------------------ loading

        public static List<LevelDef> LoadAll(string json)
        {
            var root = (Dictionary<string, object>)MiniJson.Parse(json);
            var list = new List<LevelDef>();
            int n = 0;
            foreach (var o in MiniJson.List(root, "levels"))
            {
                var lv = Parse((Dictionary<string, object>)o);
                lv.Number = ++n;
                list.Add(lv);
            }
            return list;
        }

        static int[] Coord(object o)
        {
            var l = (List<object>)o;
            return new[] { Convert.ToInt32(l[0]), Convert.ToInt32(l[1]) };
        }

        public static LevelDef Parse(Dictionary<string, object> d)
        {
            var lv = new LevelDef
            {
                Id = MiniJson.Str(d, "id", "?"),
                Name = MiniJson.Str(d, "name", "Untitled"),
                Hint = MiniJson.Str(d, "hint", ""),
                Notes = MiniJson.Str(d, "notes", ""),
                Chapter = MiniJson.Int(d, "chapter", 1),
                Term = MiniJson.Int(d, "term", Rules.DefaultTerm),
                LoanLimit = MiniJson.Int(d, "loans", -1),
            };
            var rows = MiniJson.List(d, "map");
            lv.H = rows.Count;
            lv.W = 0;
            foreach (var r in rows) lv.W = Math.Max(lv.W, ((string)r).Length);
            if (lv.W * lv.H > Rules.MaxTiles || lv.W > 16 || lv.H > 16)
                throw new FormatException($"{lv.Id}: board {lv.W}x{lv.H} too large");
            lv.Tiles = new Tile[lv.W * lv.H];
            lv.Start = lv.Exit = -1;
            var plates = new List<DeviceDef>();
            var gates = new List<DeviceDef>();
            var locks = new List<int>();
            for (int y = 0; y < lv.H; y++)
            {
                string row = (string)rows[y];
                for (int x = 0; x < lv.W; x++)
                {
                    char c = x < row.Length ? row[x] : ' ';
                    int i = lv.Idx(x, y);
                    lv.Tiles[i] = Tile.Floor;
                    if (c == '#') lv.Tiles[i] = Tile.Wall;
                    else if (c == ' ' || c == '_') lv.Tiles[i] = Tile.Void;
                    else if (c == 'S') lv.Start = i;
                    else if (c == 'X') lv.Exit = i;
                    else if (c == 'L') locks.Add(i);
                    else if (c >= 'a' && c <= 'f') plates.Add(new DeviceDef { Tile = i, Channel = c - 'a' });
                    else if (c >= 'A' && c <= 'F') gates.Add(new DeviceDef { Tile = i, Channel = c - 'A' });
                    else if (c != '.') throw new FormatException($"{lv.Id}: unknown map char '{c}'");
                }
            }
            if (lv.Start < 0 || lv.Exit < 0) throw new FormatException($"{lv.Id}: map needs S and X");
            lv.Plates = plates.ToArray();
            lv.Gates = gates.ToArray();
            lv.Locks = locks.ToArray();

            var sliders = new List<SliderDef>();
            foreach (var o in MiniJson.List(d, "sliders"))
            {
                var sd = (Dictionary<string, object>)o;
                var s = new SliderDef
                {
                    Loop = MiniJson.Bool(sd, "loop", false),
                    Speed = MiniJson.Int(sd, "speed", 4),
                    Dwell = MiniJson.Int(sd, "dwell", 0),
                    Start = MiniJson.Int(sd, "start", 0),
                    StartDir = MiniJson.Int(sd, "dir", 1) >= 0 ? 1 : -1,
                    Phase = MiniJson.Int(sd, "phase", 0),
                };
                var path = new List<int>();
                var pts = MiniJson.List(sd, "path");
                for (int k = 0; k < pts.Count; k++)
                {
                    var p = Coord(pts[k]);
                    if (k == 0) { path.Add(lv.Idx(p[0], p[1])); continue; }
                    AppendSegment(lv, path, p[0], p[1]);
                }
                if (s.Loop)
                {
                    var first = Coord(pts[0]);
                    AppendSegment(lv, path, first[0], first[1]);
                    path.RemoveAt(path.Count - 1); // the closing tile is the first tile
                }
                s.Path = path.ToArray();
                foreach (int t in s.Path)
                    if (lv.Tiles[t] != Tile.Floor) throw new FormatException($"{lv.Id}: slider path crosses non-floor at {lv.X(t)},{lv.Y(t)}");
                if (s.Speed < 1) throw new FormatException($"{lv.Id}: slider speed must be >= 1");
                sliders.Add(s);
            }
            lv.Sliders = sliders.ToArray();

            var lasers = new List<LaserDef>();
            foreach (var o in MiniJson.List(d, "lasers"))
            {
                var ld = (Dictionary<string, object>)o;
                var at = Coord(ld["at"]);
                var l = new LaserDef
                {
                    Tile = lv.Idx(at[0], at[1]),
                    Dir = Dirs.Parse(MiniJson.Str(ld, "dir", "E")[0]),
                    On = MiniJson.Int(ld, "on", 20),
                    Off = MiniJson.Int(ld, "off", 20),
                    Phase = MiniJson.Int(ld, "phase", 0),
                };
                string plate = MiniJson.Str(ld, "plate", "");
                if (plate.Length > 0) l.Plate = plate[0] - 'a';
                lasers.Add(l);
            }
            lv.Lasers = lasers.ToArray();

            var rotors = new List<RotorDef>();
            foreach (var o in MiniJson.List(d, "rotors"))
            {
                var rd = (Dictionary<string, object>)o;
                var at = Coord(rd["at"]);
                var r = new RotorDef
                {
                    Tile = lv.Idx(at[0], at[1]),
                    Length = MiniJson.Int(rd, "len", 2),
                    Clockwise = MiniJson.Bool(rd, "cw", true),
                    Turn = MiniJson.Int(rd, "turn", 4),
                    Hold = MiniJson.Int(rd, "hold", 16),
                    Phase = MiniJson.Int(rd, "phase", 0),
                };
                foreach (char c in MiniJson.Str(rd, "arms", "N")) r.ArmMask |= 1 << Dirs.Parse(c);
                if (r.Turn < 1) r.Turn = 1;
                rotors.Add(r);
            }
            lv.Rotors = rotors.ToArray();

            // static solids
            for (int i = 0; i < lv.Tiles.Length; i++)
                if (lv.Tiles[i] != Tile.Floor) lv.StaticSolid.Set(i);
            foreach (var l in lv.Lasers) lv.StaticSolid.Set(l.Tile);
            foreach (var r in lv.Rotors) lv.StaticSolid.Set(r.Tile);
            for (int i = 0; i < lv.Tiles.Length; i++)
                if (lv.Tiles[i] == Tile.Wall) lv.ArmBlock.Set(i);
            foreach (var l in lv.Lasers) lv.ArmBlock.Set(l.Tile);
            foreach (var r in lv.Rotors) lv.ArmBlock.Set(r.Tile);
            foreach (var r in lv.Rotors) BuildRotorMasks(lv, r);

            if (d.TryGetValue("expect", out var ex) && ex is Dictionary<string, object> exd)
            {
                lv.ExpectBorrow = MiniJson.Bool(exd, "borrow", false);
                lv.ExpectDebt = MiniJson.Bool(exd, "debt", false);
                lv.ExpectMargin = MiniJson.Int(exd, "margin", 0);
                lv.MinLoans = MiniJson.Int(exd, "minLoans", 0);
                lv.SearchBudget = MiniJson.Int(exd, "budget", 0);
                lv.StartDelay = MiniJson.Int(exd, "startDelay", 0);
            }
            return lv;
        }

        static void AppendSegment(LevelDef lv, List<int> path, int tx, int ty)
        {
            int last = path[path.Count - 1];
            int x = lv.X(last), y = lv.Y(last);
            if (x != tx && y != ty) throw new FormatException($"{lv.Id}: slider waypoints must be axis aligned");
            int dx = Math.Sign(tx - x), dy = Math.Sign(ty - y);
            while (x != tx || y != ty)
            {
                x += dx; y += dy;
                path.Add(lv.Idx(x, y));
            }
        }

        static void BuildRotorMasks(LevelDef lv, RotorDef r)
        {
            int px = lv.X(r.Tile), py = lv.Y(r.Tile);
            int lim = (2 * r.Length + 1) * (2 * r.Length + 1);
            for (int o = 0; o < 4; o++)
            {
                for (int arm = 0; arm < 4; arm++)
                {
                    if ((r.ArmMask & (1 << arm)) == 0) continue;
                    int d = (arm + o) & 3;
                    for (int k = 1; k <= r.Length; k++)
                    {
                        int x = px + Dirs.DX[d] * k, y = py + Dirs.DY[d] * k;
                        if (lv.InBounds(x, y)) r.Rays[o].Set(lv.Idx(x, y));
                    }
                    if (AddWedge(lv, ref r.WedgeCW[o], px, py, d, (d + 1) & 3, lim, r.Length)) r.WedgeCWOut[o] = true;
                    if (AddWedge(lv, ref r.WedgeCCW[o], px, py, d, (d + 3) & 3, lim, r.Length)) r.WedgeCCWOut[o] = true;
                }
            }
        }

        /// <summary>Adds the swept quadrant between d0 and d1; returns true if part of it is off the board.</summary>
        static bool AddWedge(LevelDef lv, ref TileMask m, int px, int py, int d0, int d1, int lim, int len)
        {
            bool outside = false;
            for (int a = 0; a <= len; a++)
            for (int b = 0; b <= len; b++)
            {
                if (a == 0 && b == 0) continue;
                if (4 * (a * a + b * b) > lim) continue;
                int x = px + Dirs.DX[d0] * a + Dirs.DX[d1] * b;
                int y = py + Dirs.DY[d0] * a + Dirs.DY[d1] * b;
                if (lv.InBounds(x, y)) m.Set(lv.Idx(x, y));
                else outside = true;
            }
            return outside;
        }
    }
}
