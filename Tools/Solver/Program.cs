using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BorrowedSeconds.Sim;

// Borrowed Seconds level validator.
// For every level it proves, with exhaustive search over the deterministic simulation:
//   * the level is solvable (and records the optimal solution as a replay + par time),
//   * "borrow": with borrowing disabled it is unsolvable,
//   * "debt":   with unlimited debt-free loans it is unsolvable, i.e. the player's own freeze is essential,
//   * the hesitation margin: the largest k such that a solution exists where at every moment the
//     player could idle k more ticks without dying.
static class Program
{
    static int Main(string[] args)
    {
        string levelsPath = null, outPath = null, only = null, trace = null;
        bool quick = false, write = true;
        int marginMax = 4;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--levels": levelsPath = args[++i]; break;
                case "--out": outPath = args[++i]; break;
                case "--level": only = args[++i]; break;
                case "--trace": trace = args[++i]; break;
                case "--quick": quick = true; break;
                case "--no-write": write = false; break;
                case "--margin-max": marginMax = int.Parse(args[++i]); break;
            }
        }
        var levels = LevelDef.LoadAll(File.ReadAllText(levelsPath));
        if (trace != null)
        {
            var lv = levels.First(l => l.Id == trace);
            var r = Solver.Solve(lv, new Solver.Config());
            Console.WriteLine($"{lv.Id} solved={r.Solved} ticks={r.Ticks} states={r.States}");
            if (r.Solved) Trace(lv, r.Actions);
            return 0;
        }

        var existing = new Dictionary<string, object>();
        if (outPath != null && File.Exists(outPath))
        {
            try
            {
                var root = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(outPath));
                foreach (var o in MiniJson.List(root, "levels"))
                {
                    var d = (Dictionary<string, object>)o;
                    existing[MiniJson.Str(d, "id", "")] = d;
                }
            }
            catch { }
        }

        bool allOk = true;
        var results = new List<object>();
        Console.WriteLine($"{"lvl",-5} {"name",-16} {"solve",8} {"par",7} {"loans",5} {"noBorrow",10} {"forgiven",10} {"margin",6}  verdict");
        foreach (var lv in levels)
        {
            if (only != null && lv.Id != only)
            {
                if (existing.TryGetValue(lv.Id, out var keep)) results.Add(keep);
                continue;
            }
            var sb = new StringBuilder();
            bool ok = true;
            var main = Solver.Solve(lv, new Solver.Config());
            sb.Append($"{lv.Id,-5} {Trunc(lv.Name, 16),-16} ");
            if (!main.Solved)
            {
                sb.Append($"{"FAIL",8} ({main.States} states, exhausted={main.Exhausted}, {main.Seconds:F1}s)");
                Console.WriteLine(sb);
                allOk = false;
                continue;
            }
            var replay = Solver.Replay(lv, main.Actions, main.Ticks + 5);
            if (!replay.Won) { ok = false; sb.Append("[REPLAY MISMATCH] "); }
            sb.Append($"{main.Seconds,7:F1}s {Rules.Seconds(main.Ticks),6:F2}s {main.Loans,5} ");

            string nb = "-", fg = "-";
            int margin = -1;
            if (!quick)
            {
                if (lv.ExpectBorrow)
                {
                    var r = Solver.Solve(lv, new Solver.Config { Options = new SimOptions { NoBorrow = true } });
                    nb = r.Solved ? "SOLVABLE" : r.Exhausted ? "proved" : "limit?";
                    if (r.Solved || !r.Exhausted) ok = false;
                }
                if (lv.ExpectDebt)
                {
                    var r = Solver.Solve(lv, new Solver.Config { Options = new SimOptions { ForgiveDebt = true } });
                    fg = r.Solved ? "SOLVABLE" : r.Exhausted ? "proved" : "limit?";
                    if (r.Solved || !r.Exhausted) ok = false;
                }
                margin = 0;
                for (int k = 1; k <= marginMax; k++)
                {
                    var r = Solver.Solve(lv, new Solver.Config { Margin = k });
                    if (!r.Solved) break;
                    margin = k;
                }
                if (margin < lv.ExpectMargin) ok = false;
                if (lv.MinLoans > 1)
                {
                    // prove fewer loans can't do it: cap the loan count one below the design
                    int saved = lv.LoanLimit;
                    lv.LoanLimit = lv.MinLoans - 1;
                    var r = Solver.Solve(lv, new Solver.Config());
                    lv.LoanLimit = saved;
                    nb += r.Solved ? $" <{lv.MinLoans}:SOLVABLE" : r.Exhausted ? $" <{lv.MinLoans}:proved" : " <n:limit?";
                    if (r.Solved || !r.Exhausted) ok = false;
                }
            }
            sb.Append($"{nb,10} {fg,10} {(margin < 0 ? "-" : margin.ToString()),6}  {(ok ? "OK" : "FAIL")}");
            Console.WriteLine(sb);
            allOk &= ok;

            var entry = new Dictionary<string, object>
            {
                ["id"] = lv.Id,
                ["par"] = main.Ticks,
                ["loans"] = main.Loans,
                ["margin"] = margin,
                ["actions"] = main.Actions.Select(a => (object)new List<object> { a.Tick, a.Action }).ToList(),
            };
            results.Add(entry);
        }
        if (write && outPath != null)
        {
            File.WriteAllText(outPath, MiniJson.Serialize(new Dictionary<string, object> { ["levels"] = results }, false));
            Console.WriteLine($"wrote {outPath}");
        }
        Console.WriteLine(allOk ? "ALL OK" : "SOME LEVELS FAILED");
        return allOk ? 0 : 1;
    }

    static string Trunc(string s, int n) => s.Length <= n ? s : s.Substring(0, n);

    static void Trace(LevelDef lv, List<TimedAction> actions)
    {
        var s = Simulation.Create(lv);
        int k = 0;
        Console.WriteLine(Render(lv, s));
        while (!s.Dead && !s.Won && s.Tick < 4000)
        {
            int act = Act.None;
            if (k < actions.Count && actions[k].Tick == s.Tick) act = actions[k++].Action;
            int t = s.Tick;
            Simulation.Step(lv, s, act);
            var evs = string.Join(" ", s.Events.Where(e => e.Type != Ev.Step && e.Type != Ev.RotorSweep && e.Type != Ev.LockCharge).Select(e => e.ToString()));
            if (act != Act.None || evs.Length > 0)
                Console.WriteLine($"t={t,4} ({Rules.Seconds(t),5:F2}s) {Act.Name(act),-5} P=({lv.X(s.P)},{lv.Y(s.P)}) cd={s.Countdown} fz={s.PFrozen} {evs}");
            if (act != Act.None && Act.IsBorrow(act) || s.Events.Any(e => e.Type == Ev.Due || e.Type == Ev.PlayerThaw || e.Type == Ev.LockLatch))
                Console.WriteLine(Render(lv, s));
        }
        Console.WriteLine(Render(lv, s));
        Console.WriteLine(s.Won ? $"WON at {s.Tick}" : "did not win");
    }

    public static string Render(LevelDef d, SimState s)
    {
        var grid = new char[d.H, d.W];
        for (int y = 0; y < d.H; y++)
            for (int x = 0; x < d.W; x++)
            {
                int i = d.Idx(x, y);
                grid[y, x] = d.Tiles[i] == Tile.Wall ? '#' : d.Tiles[i] == Tile.Void ? ' ' : '.';
            }
        void Put(int idx, char c) { if (idx >= 0) grid[d.Y(idx), d.X(idx)] = c; }
        foreach (var p in d.Plates) Put(p.Tile, (char)('a' + p.Channel));
        for (int g = 0; g < d.Gates.Length; g++) Put(d.Gates[g].Tile, s.GateOpen[g] ? '_' : (char)('A' + d.Gates[g].Channel));
        for (int k = 0; k < d.Locks.Length; k++) Put(d.Locks[k], s.LockCharge[k] >= Rules.LockTicks ? 'l' : 'L');
        Put(d.Exit, s.ExitOpen ? 'X' : 'x');
        for (int i = 0; i < d.Tiles.Length; i++)
        {
            if (s.Hazard.Has(i)) Put(i, '*');
            else if (s.Solid.Has(i) && d.Tiles[i] == Tile.Floor) Put(i, '%');
        }
        for (int l = 0; l < d.Lasers.Length; l++) Put(d.Lasers[l].Tile, s.LFrozen[l] > 0 ? 'Z' : ">v<^"[(d.Lasers[l].Dir + 3) & 3]);
        for (int r = 0; r < d.Rotors.Length; r++) Put(d.Rotors[r].Tile, s.RFrozen[r] > 0 ? 'Q' : 'O');
        for (int j = 0; j < d.Sliders.Length; j++)
        {
            Simulation.SliderTiles(d, s, j, out int a, out int b);
            Put(a, s.SFrozen[j] > 0 ? 'F' : 'H');
            if (b >= 0) Put(b, s.SFrozen[j] > 0 ? 'f' : 'h');
        }
        Put(s.P, s.PFrozen > 0 ? '@' : s.Dead ? 'x' : 'P');
        var sb = new StringBuilder();
        for (int y = 0; y < d.H; y++)
        {
            sb.Append("   ");
            for (int x = 0; x < d.W; x++) sb.Append(grid[y, x]);
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
