using System;
using System.Collections.Generic;

namespace BorrowedSeconds.Sim
{
    /// <summary>Exact 256-bit packed state used as the solver's visited-set key.</summary>
    public struct StateKey : IEquatable<StateKey>
    {
        public ulong A, B, C, D;
        public bool Equals(StateKey o) => A == o.A && B == o.B && C == o.C && D == o.D;
        public override bool Equals(object obj) => obj is StateKey k && Equals(k);
        public override int GetHashCode()
        {
            ulong h = A * 0x9E3779B97F4A7C15UL;
            h ^= B + 0xC2B2AE3D27D4EB4FUL + (h << 6) + (h >> 2);
            h ^= C + 0x165667B19E3779F9UL + (h << 6) + (h >> 2);
            h ^= D + 0x27D4EB2F165667C5UL + (h << 6) + (h >> 2);
            return (int)(h ^ (h >> 32));
        }
    }

    /// <summary>Packs/unpacks the core state of decision points (player idle) into a <see cref="StateKey"/>.</summary>
    public sealed class StatePacker
    {
        readonly LevelDef d;
        readonly int bP, bPF, bCd, bLoans, bFroz, bLockC;
        readonly int[] bSIdx, bSProg, bSDwell, bLClock, bLLen, bRProg, bRHold;
        public readonly int TotalBits;

        static int Bits(int maxValue)
        {
            int b = 0;
            while ((1 << b) <= maxValue) b++;
            return b;
        }

        public StatePacker(LevelDef def)
        {
            d = def;
            bP = Bits(d.W * d.H - 1);
            bPF = Bits(Rules.FreezeTicks);
            bCd = Bits(d.Term + 1);
            bLoans = d.LoanLimit >= 0 ? Bits(d.LoanLimit) : 0;
            bFroz = Bits(Rules.FreezeTicks + 1);
            bLockC = Bits(Rules.LockTicks);
            int total = bP + bPF + bCd + bLoans;
            bSIdx = new int[d.Sliders.Length]; bSProg = new int[d.Sliders.Length]; bSDwell = new int[d.Sliders.Length];
            for (int i = 0; i < d.Sliders.Length; i++)
            {
                var s = d.Sliders[i];
                bSIdx[i] = Bits(s.Path.Length - 1);
                bSProg[i] = Bits(s.Speed);
                bSDwell[i] = Bits(s.Dwell);
                total += bSIdx[i] + 1 + bSProg[i] + bSDwell[i] + bFroz;
            }
            bLClock = new int[d.Lasers.Length]; bLLen = new int[d.Lasers.Length];
            for (int i = 0; i < d.Lasers.Length; i++)
            {
                bLClock[i] = Bits(d.Lasers[i].Period - 1);
                bLLen[i] = Bits(Math.Max(d.W, d.H));
                total += bLClock[i] + bFroz + 1 + bLLen[i];
            }
            bRProg = new int[d.Rotors.Length]; bRHold = new int[d.Rotors.Length];
            for (int i = 0; i < d.Rotors.Length; i++)
            {
                bRProg[i] = Bits(d.Rotors[i].Turn);
                bRHold[i] = Bits(d.Rotors[i].Hold);
                total += 2 + bRProg[i] + bRHold[i] + 1 + bFroz;
            }
            total += d.Gates.Length + d.Locks.Length * bLockC;
            TotalBits = total;
            if (total > 256) throw new InvalidOperationException($"{d.Id}: state needs {total} bits (>256)");
        }

        struct Writer
        {
            public StateKey K;
            int pos;
            public void Put(int value, int bits)
            {
                if (bits == 0) return;
                ulong v = (ulong)value & ((1UL << bits) - 1);
                for (int k = 0; k < bits; k++, pos++)
                {
                    if (((v >> k) & 1) == 0) continue;
                    ulong bit = 1UL << (pos & 63);
                    switch (pos >> 6)
                    {
                        case 0: K.A |= bit; break;
                        case 1: K.B |= bit; break;
                        case 2: K.C |= bit; break;
                        default: K.D |= bit; break;
                    }
                }
            }
        }

        struct Reader
        {
            public StateKey K;
            int pos;
            public int Get(int bits)
            {
                int v = 0;
                for (int k = 0; k < bits; k++, pos++)
                {
                    ulong word;
                    switch (pos >> 6)
                    {
                        case 0: word = K.A; break;
                        case 1: word = K.B; break;
                        case 2: word = K.C; break;
                        default: word = K.D; break;
                    }
                    if (((word >> (pos & 63)) & 1) != 0) v |= 1 << k;
                }
                return v;
            }
        }

        public StateKey Pack(SimState s)
        {
            var w = new Writer();
            w.Put(s.P, bP);
            w.Put(s.PFrozen, bPF);
            w.Put(s.Countdown, bCd);
            w.Put(s.Loans, bLoans);
            for (int i = 0; i < d.Sliders.Length; i++)
            {
                w.Put(s.SIdx[i], bSIdx[i]);
                w.Put(s.SDir[i] > 0 ? 1 : 0, 1);
                w.Put(s.SProg[i], bSProg[i]);
                w.Put(s.SDwell[i], bSDwell[i]);
                w.Put(s.SFrozen[i], bFroz);
            }
            for (int i = 0; i < d.Lasers.Length; i++)
            {
                w.Put(s.LClock[i], bLClock[i]);
                w.Put(s.LFrozen[i], bFroz);
                w.Put(s.LFrozenLit[i] ? 1 : 0, 1);
                w.Put(s.LFrozenLen[i], bLLen[i]);
            }
            for (int i = 0; i < d.Rotors.Length; i++)
            {
                w.Put(s.ROrient[i], 2);
                w.Put(s.RProg[i], bRProg[i]);
                w.Put(s.RHold[i], bRHold[i]);
                w.Put(s.RDir[i] > 0 ? 1 : 0, 1);
                w.Put(s.RFrozen[i], bFroz);
            }
            for (int g = 0; g < d.Gates.Length; g++) w.Put(s.GateOpen[g] ? 1 : 0, 1);
            for (int k = 0; k < d.Locks.Length; k++) w.Put(s.LockCharge[k], bLockC);
            return w.K;
        }

        /// <summary>Restores a decision-point state (player idle) from a key.</summary>
        public void Unpack(StateKey key, SimState s, int tick)
        {
            var r = new Reader { K = key };
            s.Tick = tick;
            s.P = s.F = r.Get(bP);
            s.PFrozen = r.Get(bPF);
            s.Countdown = r.Get(bCd);
            s.Loans = r.Get(bLoans);
            s.Moving = false; s.MoveProg = 0; s.Pending = false; s.Dead = false; s.Won = false; s.DeathCause = -1;
            for (int i = 0; i < d.Sliders.Length; i++)
            {
                s.SIdx[i] = r.Get(bSIdx[i]);
                s.SDir[i] = r.Get(1) == 1 ? 1 : -1;
                s.SProg[i] = r.Get(bSProg[i]);
                s.SDwell[i] = r.Get(bSDwell[i]);
                s.SFrozen[i] = r.Get(bFroz);
            }
            for (int i = 0; i < d.Lasers.Length; i++)
            {
                s.LClock[i] = r.Get(bLClock[i]);
                s.LFrozen[i] = r.Get(bFroz);
                s.LFrozenLit[i] = r.Get(1) == 1;
                s.LFrozenLen[i] = r.Get(bLLen[i]);
            }
            for (int i = 0; i < d.Rotors.Length; i++)
            {
                s.ROrient[i] = r.Get(2);
                s.RProg[i] = r.Get(bRProg[i]);
                s.RHold[i] = r.Get(bRHold[i]);
                s.RDir[i] = r.Get(1) == 1 ? 1 : -1;
                s.RFrozen[i] = r.Get(bFroz);
            }
            for (int g = 0; g < d.Gates.Length; g++) s.GateOpen[g] = r.Get(1) == 1;
            for (int k = 0; k < d.Locks.Length; k++) s.LockCharge[k] = r.Get(bLockC);
            s.Events.Clear();
            Simulation.Recompute(d, s);
        }
    }

    public struct TimedAction
    {
        public int Tick, Action;
        public TimedAction(int tick, int action) { Tick = tick; Action = action; }
    }

    public sealed class SolveResult
    {
        public bool Solved;
        /// <summary>True if the whole reachable state space was explored (so "unsolved" is a proof).</summary>
        public bool Exhausted;
        public int Ticks = -1;
        public int States;
        public int Loans;
        public List<TimedAction> Actions = new List<TimedAction>();
        public double Seconds;
    }

    /// <summary>
    /// Exact shortest-time search over decision points (ticks where the player may act) using a
    /// bucket queue keyed by tick. Moves cost 3 ticks, waiting/borrowing 1, freezes are skipped.
    /// </summary>
    public static class Solver
    {
        public sealed class Config
        {
            public SimOptions Options;
            /// <summary>Hesitation margin: every visited state must survive this many idle ticks.</summary>
            public int Margin;
            public int MaxTicks = 20 * 120;
            public int MaxStates = 30_000_000;
            /// <summary>Optional: only allow loans on these obstacle indices (null = any).</summary>
            public bool[] BorrowMask;
        }

        public static SolveResult Solve(LevelDef d, Config cfg)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var res = new SolveResult();
            var packer = new StatePacker(d);
            var visited = new Dictionary<StateKey, int>(1 << 16);
            var keys = new List<StateKey>(1 << 16);
            var parent = new List<int>(1 << 16);
            var viaAction = new List<int>(1 << 16);
            var viaTick = new List<int>(1 << 16);
            var arrival = new List<int>(1 << 16);
            var buckets = new List<int>[cfg.MaxTicks + 1];

            var root = Simulation.Create(d);
            // Advance from the very start until the player can act (they can, immediately).
            int rootIdx = Add(packer.Pack(root), -1, Act.None, 0, 0);
            Push(0, rootIdx);

            var cur = new SimState(d);
            var next = new SimState(d);
            var probe = new SimState(d);
            int actions = 5 + d.ObstacleCount;
            var opt = cfg.Options;

            for (int t = 0; t <= cfg.MaxTicks; t++)
            {
                var bucket = buckets[t];
                if (bucket == null) continue;
                buckets[t] = null;
                foreach (int node in bucket)
                {
                    if (arrival[node] != t) continue; // superseded by a faster route
                    packer.Unpack(keys[node], cur, t);
                    for (int a = 0; a < actions; a++)
                    {
                        int act = a < 5 ? a : Act.Borrow(a - 5);
                        if (Act.IsBorrow(act))
                        {
                            int target = Act.BorrowTarget(act);
                            if (cfg.BorrowMask != null && !cfg.BorrowMask[target]) continue;
                            // CanBorrow is evaluated after the thaw tick decrements PFrozen; mimic it.
                            if (cur.PFrozen > 1) continue;
                            if (!BorrowPossible(d, cur, target, opt)) continue;
                        }
                        else if (Act.IsMove(act))
                        {
                            if (cur.PFrozen > 1) continue;
                            if (!Simulation.CanEnter(d, cur, d.Neighbor(cur.P, Act.MoveDir(act)))) continue;
                        }

                        next.CopyFrom(cur);
                        Simulation.Step(d, next, act, opt);
                        bool ok = !next.Dead && (cfg.Margin == 0 || next.Won || Safe(d, next, probe, cfg.Margin, opt));
                        while (ok && !next.Won && !next.CanActNext)
                        {
                            Simulation.Step(d, next, Act.None, opt);
                            ok = !next.Dead && (cfg.Margin == 0 || next.Won || Safe(d, next, probe, cfg.Margin, opt));
                        }
                        if (!ok) continue;
                        if (next.Won)
                        {
                            res.Solved = true;
                            res.Ticks = next.Tick;
                            res.States = keys.Count;
                            BuildActions(node, t, act);
                            res.Seconds = sw.Elapsed.TotalSeconds;
                            return res;
                        }
                        if (next.Tick > cfg.MaxTicks) continue;
                        var key = packer.Pack(next);
                        if (visited.TryGetValue(key, out int seen))
                        {
                            if (arrival[seen] <= next.Tick) continue;
                            arrival[seen] = next.Tick;
                            parent[seen] = node;
                            viaAction[seen] = act;
                            viaTick[seen] = t;
                            Push(next.Tick, seen);
                            continue;
                        }
                        int idx = Add(key, node, act, t, next.Tick);
                        Push(next.Tick, idx);
                        if (keys.Count > cfg.MaxStates)
                        {
                            res.States = keys.Count;
                            res.Seconds = sw.Elapsed.TotalSeconds;
                            return res; // gave up: not exhausted
                        }
                    }
                }
            }

            res.States = keys.Count;
            res.Exhausted = true;
            for (int t = 0; t < buckets.Length; t++)
                if (buckets[t] != null) { res.Exhausted = false; break; }
            res.Seconds = sw.Elapsed.TotalSeconds;
            return res;

            int Add(StateKey k, int par, int act, int tick, int arrive)
            {
                int idx = keys.Count;
                keys.Add(k);
                parent.Add(par);
                viaAction.Add(act);
                viaTick.Add(tick);
                arrival.Add(arrive);
                visited[k] = idx;
                return idx;
            }

            void Push(int tick, int idx)
            {
                if (tick > cfg.MaxTicks) return;
                (buckets[tick] ?? (buckets[tick] = new List<int>())).Add(idx);
            }

            void BuildActions(int node, int tick, int act)
            {
                var list = new List<TimedAction>();
                if (act != Act.None) list.Add(new TimedAction(tick, act));
                while (node > 0)
                {
                    if (viaAction[node] != Act.None) list.Add(new TimedAction(viaTick[node], viaAction[node]));
                    node = parent[node];
                }
                list.Reverse();
                res.Actions = list;
                foreach (var ta in list) if (Act.IsBorrow(ta.Action)) res.Loans++;
            }
        }

        static bool BorrowPossible(LevelDef d, SimState s, int target, in SimOptions opt)
        {
            if (opt.NoBorrow || s.Moving) return false;
            if (s.IsObstacleFrozen(d, target)) return false;
            if (d.LoanLimit >= 0 && s.Loans >= d.LoanLimit) return false;
            if (opt.ForgiveDebt) return true;
            if (s.Countdown > 0 || s.Pending) return false;
            return true;
        }

        /// <summary>Would the player survive <paramref name="margin"/> more ticks of doing nothing?</summary>
        static bool Safe(LevelDef d, SimState s, SimState probe, int margin, in SimOptions opt)
        {
            probe.CopyFrom(s);
            for (int k = 0; k < margin; k++)
            {
                Simulation.Step(d, probe, Act.None, opt);
                if (probe.Dead) return false;
                if (probe.Won) return true;
            }
            return true;
        }

        /// <summary>Replays timed actions from the level start. Returns the final state.</summary>
        public static SimState Replay(LevelDef d, IList<TimedAction> actions, int maxTicks, in SimOptions opt = default)
        {
            var s = Simulation.Create(d);
            int k = 0;
            while (!s.Dead && !s.Won && s.Tick <= maxTicks)
            {
                int act = Act.None;
                if (k < actions.Count && actions[k].Tick == s.Tick) act = actions[k++].Action;
                Simulation.Step(d, s, act, opt);
            }
            return s;
        }
    }
}
