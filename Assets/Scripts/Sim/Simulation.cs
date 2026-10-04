namespace BorrowedSeconds.Sim
{
    /// <summary>Rule variants used by the solver to prove what a level requires.</summary>
    public struct SimOptions
    {
        /// <summary>Borrowing is disabled entirely.</summary>
        public bool NoBorrow;
        /// <summary>Loans never come due and may overlap (the level's loan cap still applies).
        /// A strict relaxation of the real rules except that the player never freezes.</summary>
        public bool ForgiveDebt;
    }

    /// <summary>
    /// The deterministic rules. One call to <see cref="Step"/> advances exactly one 20 Hz tick.
    /// Integer-only, no allocation in the hot path, no unordered iteration: the game, the ghost
    /// previews, the solver and the replay tests all run this exact code.
    /// </summary>
    public static class Simulation
    {
        public static SimState Create(LevelDef d)
        {
            var s = new SimState(d) { P = d.Start, F = d.Start };
            for (int i = 0; i < d.Sliders.Length; i++)
            {
                var sd = d.Sliders[i];
                s.SIdx[i] = sd.Start;
                s.SDir[i] = sd.StartDir;
                for (int k = 0; k < sd.Phase; k++) AdvanceSlider(d, s, i, true);
            }
            for (int i = 0; i < d.Lasers.Length; i++)
                s.LClock[i] = d.Lasers[i].Phase % d.Lasers[i].Period;
            for (int i = 0; i < d.Rotors.Length; i++)
            {
                var rd = d.Rotors[i];
                s.ROrient[i] = rd.StartOrient;
                s.RDir[i] = rd.Clockwise ? 1 : -1;
                s.RHold[i] = rd.Hold;
                for (int k = 0; k < rd.Phase; k++) AdvanceRotor(d, s, i, true);
            }
            ComputePressed(d, s);
            for (int g = 0; g < d.Gates.Length; g++) s.GateOpen[g] = s.ChannelPressed[d.Gates[g].Channel];
            ComputeMasks(d, s);
            s.Events.Clear();
            return s;
        }

        /// <summary>Recomputes every derived field from the core state.</summary>
        public static void Recompute(LevelDef d, SimState s)
        {
            ComputePressed(d, s);
            ComputeMasks(d, s);
        }

        public static void Step(LevelDef d, SimState s, int action) => Step(d, s, action, default);

        public static void Step(LevelDef d, SimState s, int action, in SimOptions opt)
        {
            s.Events.Clear();
            if (s.Dead || s.Won) return;

            // 0. repayment timer
            if (s.PFrozen > 0)
            {
                s.PFrozen--;
                if (s.PFrozen == 0) s.Events.Add(new SimEvent(Ev.PlayerThaw, s.P));
            }

            // 1. player action
            if (s.PFrozen == 0 && !s.Moving && action != Act.None)
            {
                if (Act.IsMove(action)) TryStartMove(d, s, Act.MoveDir(action));
                else if (Act.IsBorrow(action)) TryBorrow(d, s, Act.BorrowTarget(action), opt);
            }

            // 2. obstacles
            for (int i = 0; i < d.Sliders.Length; i++) AdvanceSlider(d, s, i, false);
            for (int i = 0; i < d.Lasers.Length; i++) AdvanceLaser(d, s, i);
            for (int i = 0; i < d.Rotors.Length; i++) AdvanceRotor(d, s, i, false);

            // 3. player step progress
            if (s.Moving)
            {
                s.MoveProg++;
                if (s.MoveProg >= Rules.MoveTicks)
                {
                    s.Moving = false;
                    s.MoveProg = 0;
                    s.F = s.P;
                }
            }

            // 4. debt
            if (s.Countdown > 0)
            {
                s.Countdown--;
                if (s.Countdown == 0) s.Pending = true;
            }
            if (s.Pending && !s.Moving)
            {
                s.Pending = false;
                s.PFrozen = Rules.FreezeTicks;
                s.Events.Add(new SimEvent(Ev.Due, s.P));
            }

            // 5. devices
            UpdateDevices(d, s);

            // 6. beams, solids, hazards
            ComputeMasks(d, s);

            // 7. death
            if (s.PFrozen == 0)
            {
                var occ = PlayerOccupancy(d, s);
                if (occ.Intersects(s.Hazard))
                {
                    s.Dead = true;
                    s.DeathCause = FindKiller(d, s, occ);
                    s.Events.Add(new SimEvent(Ev.Death, s.P, s.DeathCause));
                }
            }

            // 8. win
            if (!s.Dead && !s.Moving && s.PFrozen == 0 && s.Countdown == 0 && !s.Pending && s.P == d.Exit && s.ExitOpen)
            {
                s.Won = true;
                s.Events.Add(new SimEvent(Ev.Win, s.P));
            }
            s.Tick++;
        }

        // ------------------------------------------------------------------ player

        public static TileMask PlayerOccupancy(LevelDef d, SimState s)
        {
            var m = new TileMask();
            m.Set(s.P);
            if (s.Moving && s.MoveProg <= 1) m.Set(s.F);
            return m;
        }

        /// <summary>Whether the player could step into <paramref name="target"/> right now.</summary>
        public static bool CanEnter(LevelDef d, SimState s, int target)
        {
            return target >= 0 && d.Tiles[target] == Tile.Floor && !s.Solid.Has(target) && !s.Hazard.Has(target);
        }

        static void TryStartMove(LevelDef d, SimState s, int dir)
        {
            int target = d.Neighbor(s.P, dir);
            s.MoveDir = dir;
            if (!CanEnter(d, s, target))
            {
                s.Events.Add(new SimEvent(Ev.Bump, s.P, dir));
                return;
            }
            s.F = s.P;
            s.P = target;
            s.Moving = true;
            s.MoveProg = 0;
            s.Events.Add(new SimEvent(Ev.Step, s.F, dir));
        }

        public static bool CanBorrow(LevelDef d, SimState s, int target, in SimOptions opt)
        {
            if (opt.NoBorrow || s.Dead || s.Won || s.Moving || s.PFrozen > 0) return false;
            if (target < 0 || target >= d.ObstacleCount) return false;
            if (s.IsObstacleFrozen(d, target)) return false;
            if (d.LoanLimit >= 0 && s.Loans >= d.LoanLimit) return false;
            if (opt.ForgiveDebt) return true;
            if (s.Countdown > 0 || s.Pending) return false;
            return true;
        }

        static void TryBorrow(LevelDef d, SimState s, int target, in SimOptions opt)
        {
            if (!CanBorrow(d, s, target, opt))
            {
                s.Events.Add(new SimEvent(Ev.BorrowDenied, target));
                return;
            }
            // +1 because the obstacle phase of this same tick consumes one count.
            int frozen = Rules.FreezeTicks + 1;
            switch (d.KindOf(target, out int i))
            {
                case LevelDef.Kind.Slider: s.SFrozen[i] = frozen; break;
                case LevelDef.Kind.Laser:
                    s.LFrozen[i] = frozen;
                    s.LFrozenLit[i] = s.LaserLit[i];
                    s.LFrozenLen[i] = s.LaserLit[i] ? s.BeamLen[i] : 0;
                    break;
                default: s.RFrozen[i] = frozen; break;
            }
            s.Loans++;
            if (!opt.ForgiveDebt) s.Countdown = d.Term + 1;
            s.Events.Add(new SimEvent(Ev.Borrow, target, s.P));
        }

        // ------------------------------------------------------------------ sliders

        public static int SliderNext(SliderDef sd, int idx, int dir)
        {
            int n = sd.Path.Length;
            if (sd.Loop) return ((idx + dir) % n + n) % n;
            int j = idx + dir;
            return j < 0 || j >= n ? -1 : j;
        }

        /// <summary>Tiles a slider covers: its rest tile, plus the tile it's sliding into.</summary>
        public static void SliderTiles(LevelDef d, SimState s, int i, out int a, out int b)
        {
            var sd = d.Sliders[i];
            a = sd.Path[s.SIdx[i]];
            b = -1;
            if (s.SProg[i] > 0)
            {
                int n = SliderNext(sd, s.SIdx[i], s.SDir[i]);
                if (n >= 0) b = sd.Path[n];
            }
        }

        static void AdvanceSlider(LevelDef d, SimState s, int i, bool isolated)
        {
            var sd = d.Sliders[i];
            if (!isolated && s.SFrozen[i] > 0)
            {
                s.SFrozen[i]--;
                if (s.SFrozen[i] > 0) return;
                s.Events.Add(new SimEvent(Ev.ObstacleThaw, d.ObstacleIndex(LevelDef.Kind.Slider, i)));
            }
            if (s.SProg[i] > 0)
            {
                s.SProg[i]++;
                if (s.SProg[i] >= sd.Speed)
                {
                    s.SIdx[i] = SliderNext(sd, s.SIdx[i], s.SDir[i]);
                    s.SProg[i] = 0;
                }
                return;
            }
            if (s.SDwell[i] > 0) { s.SDwell[i]--; return; }

            int next = SliderNext(sd, s.SIdx[i], s.SDir[i]);
            if (next < 0)
            {
                s.SDir[i] = -s.SDir[i];
                if (sd.Dwell > 0) { s.SDwell[i] = sd.Dwell - 1; return; }
                next = SliderNext(sd, s.SIdx[i], s.SDir[i]);
                if (next < 0) return;
            }
            if (!isolated && SliderBlocked(d, s, i, sd.Path[next]))
            {
                s.SDir[i] = -s.SDir[i];
                s.Events.Add(new SimEvent(Ev.SliderBounce, d.ObstacleIndex(LevelDef.Kind.Slider, i)));
                return;
            }
            s.SProg[i] = 1;
            if (s.SProg[i] >= sd.Speed)
            {
                s.SIdx[i] = next;
                s.SProg[i] = 0;
            }
        }

        static bool SliderBlocked(LevelDef d, SimState s, int self, int tile)
        {
            if (d.StaticSolid.Has(tile)) return true;
            for (int g = 0; g < d.Gates.Length; g++)
                if (!s.GateOpen[g] && d.Gates[g].Tile == tile) return true;
            for (int j = 0; j < d.Sliders.Length; j++)
            {
                if (j == self) continue;
                SliderTiles(d, s, j, out int a, out int b);
                if (a == tile || b == tile) return true;
            }
            for (int l = 0; l < d.Lasers.Length; l++)
                if (s.LFrozen[l] > 0 && s.LFrozenLit[l] && BeamContains(d, l, s.LFrozenLen[l], tile)) return true;
            for (int r = 0; r < d.Rotors.Length; r++)
                if (RotorTiles(d, s, r).Has(tile)) return true;
            return false;
        }

        // ------------------------------------------------------------------ lasers

        static void AdvanceLaser(LevelDef d, SimState s, int i)
        {
            if (s.LFrozen[i] > 0)
            {
                s.LFrozen[i]--;
                if (s.LFrozen[i] > 0) return;
                s.LFrozenLit[i] = false;
                s.LFrozenLen[i] = 0;
                s.Events.Add(new SimEvent(Ev.ObstacleThaw, d.ObstacleIndex(LevelDef.Kind.Laser, i)));
            }
            s.LClock[i] = (s.LClock[i] + 1) % d.Lasers[i].Period;
        }

        public static bool LaserCycleLit(LaserDef ld, int clock) => ld.Off == 0 || clock < ld.On;

        /// <summary>Ticks until an unlit laser fires (for telegraphs); 0 if lit or never.</summary>
        public static int LaserTicksToFire(LaserDef ld, int clock) => ld.Off == 0 || clock < ld.On ? 0 : ld.Period - clock;

        public static bool BeamContains(LevelDef d, int laser, int len, int tile)
        {
            var ld = d.Lasers[laser];
            int t = ld.Tile;
            for (int k = 0; k < len; k++)
            {
                t = d.Neighbor(t, ld.Dir);
                if (t < 0) return false;
                if (t == tile) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ rotors

        static void AdvanceRotor(LevelDef d, SimState s, int i, bool isolated)
        {
            var rd = d.Rotors[i];
            if (!isolated && s.RFrozen[i] > 0)
            {
                s.RFrozen[i]--;
                if (s.RFrozen[i] > 0) return;
                s.Events.Add(new SimEvent(Ev.ObstacleThaw, d.ObstacleIndex(LevelDef.Kind.Rotor, i)));
            }
            if (s.RProg[i] > 0)
            {
                s.RProg[i]++;
                if (s.RProg[i] >= rd.Turn)
                {
                    s.ROrient[i] = (s.ROrient[i] + s.RDir[i] + 4) & 3;
                    s.RProg[i] = 0;
                    s.RHold[i] = rd.Hold;
                }
                return;
            }
            if (s.RHold[i] > 0) { s.RHold[i]--; return; }
            if (WedgeBlocked(d, s, i, s.RDir[i], isolated))
            {
                s.RDir[i] = -s.RDir[i];
                if (!isolated) s.Events.Add(new SimEvent(Ev.RotorReverse, d.ObstacleIndex(LevelDef.Kind.Rotor, i)));
                return;
            }
            s.RProg[i] = 1;
            if (!isolated) s.Events.Add(new SimEvent(Ev.RotorSweep, d.ObstacleIndex(LevelDef.Kind.Rotor, i)));
            if (s.RProg[i] >= rd.Turn)
            {
                s.ROrient[i] = (s.ROrient[i] + s.RDir[i] + 4) & 3;
                s.RProg[i] = 0;
                s.RHold[i] = rd.Hold;
            }
        }

        static bool WedgeBlocked(LevelDef d, SimState s, int i, int dir, bool isolated)
        {
            var rd = d.Rotors[i];
            int o = s.ROrient[i];
            if (dir > 0 ? rd.WedgeCWOut[o] : rd.WedgeCCWOut[o]) return true;
            var wedge = dir > 0 ? rd.WedgeCW[o] : rd.WedgeCCW[o];
            if (wedge.Intersects(d.ArmBlock)) return true;
            if (isolated) return false;
            for (int g = 0; g < d.Gates.Length; g++)
                if (!s.GateOpen[g] && wedge.Has(d.Gates[g].Tile)) return true;
            for (int j = 0; j < d.Sliders.Length; j++)
            {
                SliderTiles(d, s, j, out int a, out int b);
                if (wedge.Has(a) || (b >= 0 && wedge.Has(b))) return true;
            }
            for (int l = 0; l < d.Lasers.Length; l++)
            {
                if (s.LFrozen[l] == 0 || !s.LFrozenLit[l]) continue;
                int t = d.Lasers[l].Tile;
                for (int k = 0; k < s.LFrozenLen[l]; k++)
                {
                    t = d.Neighbor(t, d.Lasers[l].Dir);
                    if (wedge.Has(t)) return true;
                }
            }
            for (int r = 0; r < d.Rotors.Length; r++)
                if (r != i && s.RFrozen[r] > 0 && wedge.Intersects(RotorTiles(d, s, r))) return true;
            return false;
        }

        /// <summary>Tiles covered by a rotor's arms: rays at rest, the swept quadrant mid-turn.</summary>
        public static TileMask RotorTiles(LevelDef d, SimState s, int i)
        {
            var rd = d.Rotors[i];
            int o = s.ROrient[i];
            if (s.RProg[i] == 0) return rd.Rays[o];
            return s.RDir[i] > 0 ? rd.WedgeCW[o] : rd.WedgeCCW[o];
        }

        // ------------------------------------------------------------------ devices

        static void ComputePressed(LevelDef d, SimState s)
        {
            var weight = PlayerOccupancy(d, s);
            for (int j = 0; j < d.Sliders.Length; j++)
            {
                SliderTiles(d, s, j, out int a, out int b);
                weight.Set(a);
                if (b >= 0) weight.Set(b);
            }
            for (int c = 0; c < s.ChannelPressed.Length; c++) s.ChannelPressed[c] = false;
            for (int p = 0; p < d.Plates.Length; p++)
            {
                bool pressed = weight.Has(d.Plates[p].Tile);
                s.PlatePressed[p] = pressed;
                if (pressed) s.ChannelPressed[d.Plates[p].Channel] = true;
            }
            bool open = true;
            for (int k = 0; k < d.Locks.Length; k++)
                if (s.LockCharge[k] < Rules.LockTicks) open = false;
            s.ExitOpen = open;
        }

        static void UpdateDevices(LevelDef d, SimState s)
        {
            // plate edges for feedback
            for (int p = 0; p < d.Plates.Length; p++) Scratch[p] = s.PlatePressed[p];
            ComputePressed(d, s);
            for (int p = 0; p < d.Plates.Length; p++)
                if (Scratch[p] != s.PlatePressed[p])
                    s.Events.Add(new SimEvent(s.PlatePressed[p] ? Ev.PlateDown : Ev.PlateUp, p));

            var weight = PlayerOccupancy(d, s);
            for (int j = 0; j < d.Sliders.Length; j++)
            {
                SliderTiles(d, s, j, out int a, out int b);
                weight.Set(a);
                if (b >= 0) weight.Set(b);
            }
            for (int g = 0; g < d.Gates.Length; g++)
            {
                bool want = s.ChannelPressed[d.Gates[g].Channel];
                bool open = want || (s.GateOpen[g] && weight.Has(d.Gates[g].Tile));
                if (open != s.GateOpen[g])
                {
                    s.GateOpen[g] = open;
                    s.Events.Add(new SimEvent(open ? Ev.GateOpen : Ev.GateClose, g));
                }
            }

            bool wasOpen = s.ExitOpen;
            for (int k = 0; k < d.Locks.Length; k++)
            {
                int c = s.LockCharge[k];
                if (c >= Rules.LockTicks) continue;
                if (weight.Has(d.Locks[k]))
                {
                    c++;
                    s.LockCharge[k] = c;
                    if (c >= Rules.LockTicks) s.Events.Add(new SimEvent(Ev.LockLatch, k));
                    else if (c % (Rules.LockTicks / 12) == 0) s.Events.Add(new SimEvent(Ev.LockCharge, k, c / (Rules.LockTicks / 12)));
                }
                else if (c > 0)
                {
                    s.LockCharge[k] = 0;
                    s.Events.Add(new SimEvent(Ev.LockReset, k));
                }
            }
            bool open2 = true;
            for (int k = 0; k < d.Locks.Length; k++)
                if (s.LockCharge[k] < Rules.LockTicks) open2 = false;
            s.ExitOpen = open2;
            if (open2 && !wasOpen) s.Events.Add(new SimEvent(Ev.ExitOpen, d.Exit));
        }

        [System.ThreadStatic] static bool[] _scratch;
        static bool[] Scratch => _scratch ?? (_scratch = new bool[64]);

        // ------------------------------------------------------------------ masks

        static void ComputeMasks(LevelDef d, SimState s)
        {
            var solid = d.StaticSolid;
            var hazard = new TileMask();
            var beamBlock = new TileMask();
            for (int i = 0; i < d.Tiles.Length; i++)
                if (d.Tiles[i] == Tile.Wall) beamBlock.Set(i);
            foreach (var l in d.Lasers) beamBlock.Set(l.Tile);
            foreach (var r in d.Rotors) beamBlock.Set(r.Tile);

            for (int g = 0; g < d.Gates.Length; g++)
                if (!s.GateOpen[g]) { solid.Set(d.Gates[g].Tile); beamBlock.Set(d.Gates[g].Tile); }

            for (int j = 0; j < d.Sliders.Length; j++)
            {
                SliderTiles(d, s, j, out int a, out int b);
                solid.Set(a); beamBlock.Set(a);
                if (b >= 0) { solid.Set(b); beamBlock.Set(b); }
                if (s.SFrozen[j] == 0)
                {
                    hazard.Set(a);
                    if (b >= 0) hazard.Set(b);
                }
            }
            for (int r = 0; r < d.Rotors.Length; r++)
            {
                var tiles = RotorTiles(d, s, r);
                if (s.RFrozen[r] > 0) { solid.Or(tiles); beamBlock.Or(tiles); }
                else hazard.Or(tiles);
            }
            // frozen beams first: they are solid and can block live beams
            for (int l = 0; l < d.Lasers.Length; l++)
            {
                if (s.LFrozen[l] == 0) continue;
                s.LaserDisabled[l] = false;
                s.LaserLit[l] = s.LFrozenLit[l];
                s.BeamLen[l] = s.LFrozenLen[l];
                if (!s.LFrozenLit[l]) continue;
                int t = d.Lasers[l].Tile;
                for (int k = 0; k < s.LFrozenLen[l]; k++)
                {
                    t = d.Neighbor(t, d.Lasers[l].Dir);
                    solid.Set(t);
                    beamBlock.Set(t);
                }
            }
            for (int l = 0; l < d.Lasers.Length; l++)
            {
                if (s.LFrozen[l] > 0) continue;
                var ld = d.Lasers[l];
                bool disabled = ld.Plate >= 0 && s.ChannelPressed[ld.Plate];
                bool lit = !disabled && LaserCycleLit(ld, s.LClock[l]);
                s.LaserDisabled[l] = disabled;
                s.LaserLit[l] = lit;
                int len = 0, t = ld.Tile;
                while (true)
                {
                    t = d.Neighbor(t, ld.Dir);
                    if (t < 0 || beamBlock.Has(t)) break;
                    len++;
                    if (lit) hazard.Set(t);
                }
                s.BeamLen[l] = len;
            }
            s.Solid = solid;
            s.Hazard = hazard;
            s.BeamBlock = beamBlock;
        }

        static int FindKiller(LevelDef d, SimState s, TileMask occ)
        {
            for (int j = 0; j < d.Sliders.Length; j++)
            {
                if (s.SFrozen[j] > 0) continue;
                SliderTiles(d, s, j, out int a, out int b);
                if (occ.Has(a) || (b >= 0 && occ.Has(b))) return d.ObstacleIndex(LevelDef.Kind.Slider, j);
            }
            for (int l = 0; l < d.Lasers.Length; l++)
            {
                if (s.LFrozen[l] > 0 || !s.LaserLit[l]) continue;
                int t = d.Lasers[l].Tile;
                for (int k = 0; k < s.BeamLen[l]; k++)
                {
                    t = d.Neighbor(t, d.Lasers[l].Dir);
                    if (occ.Has(t)) return d.ObstacleIndex(LevelDef.Kind.Laser, l);
                }
            }
            for (int r = 0; r < d.Rotors.Length; r++)
                if (s.RFrozen[r] == 0 && RotorTiles(d, s, r).Intersects(occ)) return d.ObstacleIndex(LevelDef.Kind.Rotor, r);
            return -1;
        }
    }
}
