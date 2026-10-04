using System;
using System.Collections.Generic;

namespace BorrowedSeconds.Sim
{
    public enum Ev : byte
    {
        Borrow, BorrowDenied, ObstacleThaw, Due, PlayerThaw, Step, Bump,
        SliderBounce, RotorReverse, RotorSweep, LaserFire, LaserOff,
        PlateDown, PlateUp, GateOpen, GateClose, LockCharge, LockReset, LockLatch, ExitOpen,
        Death, Win,
    }

    public struct SimEvent
    {
        public Ev Type;
        public int A, B;
        public SimEvent(Ev type, int a = 0, int b = 0) { Type = type; A = a; B = b; }
        public override string ToString() => $"{Type}({A},{B})";
    }

    /// <summary>
    /// Complete simulation state. "Core" fields evolve; "derived" fields are recomputed from the core
    /// at the end of every tick (and after unpacking a solver key), so they always agree.
    /// </summary>
    public sealed class SimState
    {
        public int Tick;

        // player
        public int P, F;            // current tile (destination while stepping) and origin tile
        public bool Moving;
        public int MoveProg, MoveDir;
        public int PFrozen;         // repayment ticks left
        public int Countdown;       // ticks until the debt is due (0 = no outstanding loan)
        public bool Pending;        // debt fell due mid-step; freeze on landing
        public int Loans;
        public bool Dead, Won;
        public int DeathCause = -1; // obstacle index that killed the player

        // sliders
        public int[] SIdx, SDir, SProg, SDwell, SFrozen;
        // lasers
        public int[] LClock, LFrozen, LFrozenLen;
        public bool[] LFrozenLit;
        // rotors
        public int[] ROrient, RProg, RHold, RDir, RFrozen;
        // devices
        public bool[] GateOpen;
        public int[] LockCharge;

        // ---- derived ----
        public bool[] PlatePressed;
        public bool[] ChannelPressed = new bool[6];
        public bool[] LaserLit, LaserDisabled;
        public int[] BeamLen;
        public TileMask Solid, Hazard, BeamBlock;
        public bool ExitOpen;

        public List<SimEvent> Events = new List<SimEvent>();

        public SimState(LevelDef d)
        {
            int ns = d.Sliders.Length, nl = d.Lasers.Length, nr = d.Rotors.Length;
            SIdx = new int[ns]; SDir = new int[ns]; SProg = new int[ns]; SDwell = new int[ns]; SFrozen = new int[ns];
            LClock = new int[nl]; LFrozen = new int[nl]; LFrozenLen = new int[nl]; LFrozenLit = new bool[nl];
            ROrient = new int[nr]; RProg = new int[nr]; RHold = new int[nr]; RDir = new int[nr]; RFrozen = new int[nr];
            GateOpen = new bool[d.Gates.Length];
            LockCharge = new int[d.Locks.Length];
            PlatePressed = new bool[d.Plates.Length];
            LaserLit = new bool[nl]; LaserDisabled = new bool[nl]; BeamLen = new int[nl];
        }

        public SimState Clone()
        {
            var c = (SimState)MemberwiseClone();
            c.CopyArraysFrom(this, true);
            return c;
        }

        /// <summary>Copies another state of the same level into this one without allocating.</summary>
        public void CopyFrom(SimState o)
        {
            Tick = o.Tick; P = o.P; F = o.F; Moving = o.Moving; MoveProg = o.MoveProg; MoveDir = o.MoveDir;
            PFrozen = o.PFrozen; Countdown = o.Countdown; Pending = o.Pending; Loans = o.Loans;
            Dead = o.Dead; Won = o.Won; DeathCause = o.DeathCause;
            Solid = o.Solid; Hazard = o.Hazard; BeamBlock = o.BeamBlock; ExitOpen = o.ExitOpen;
            CopyArraysFrom(o, false);
            Events.Clear();
            Events.AddRange(o.Events);
        }

        void CopyArraysFrom(SimState o, bool alloc)
        {
            SIdx = Cp(o.SIdx, SIdx, alloc); SDir = Cp(o.SDir, SDir, alloc); SProg = Cp(o.SProg, SProg, alloc);
            SDwell = Cp(o.SDwell, SDwell, alloc); SFrozen = Cp(o.SFrozen, SFrozen, alloc);
            LClock = Cp(o.LClock, LClock, alloc); LFrozen = Cp(o.LFrozen, LFrozen, alloc);
            LFrozenLen = Cp(o.LFrozenLen, LFrozenLen, alloc); LFrozenLit = Cp(o.LFrozenLit, LFrozenLit, alloc);
            ROrient = Cp(o.ROrient, ROrient, alloc); RProg = Cp(o.RProg, RProg, alloc); RHold = Cp(o.RHold, RHold, alloc);
            RDir = Cp(o.RDir, RDir, alloc); RFrozen = Cp(o.RFrozen, RFrozen, alloc);
            GateOpen = Cp(o.GateOpen, GateOpen, alloc); LockCharge = Cp(o.LockCharge, LockCharge, alloc);
            PlatePressed = Cp(o.PlatePressed, PlatePressed, alloc); ChannelPressed = Cp(o.ChannelPressed, ChannelPressed, alloc);
            LaserLit = Cp(o.LaserLit, LaserLit, alloc); LaserDisabled = Cp(o.LaserDisabled, LaserDisabled, alloc);
            BeamLen = Cp(o.BeamLen, BeamLen, alloc);
            // MemberwiseClone shares the event list instance; give the clone its own.
            if (alloc) Events = new List<SimEvent>(o.Events);
        }

        static T[] Cp<T>(T[] src, T[] dst, bool alloc)
        {
            if (alloc || dst == null || dst.Length != src.Length) dst = new T[src.Length];
            Array.Copy(src, dst, src.Length);
            return dst;
        }

        public bool IsObstacleFrozen(LevelDef d, int obstacle)
        {
            switch (d.KindOf(obstacle, out int i))
            {
                case LevelDef.Kind.Slider: return SFrozen[i] > 0;
                case LevelDef.Kind.Laser: return LFrozen[i] > 0;
                default: return RFrozen[i] > 0;
            }
        }

        public int ObstacleFrozenTicks(LevelDef d, int obstacle)
        {
            switch (d.KindOf(obstacle, out int i))
            {
                case LevelDef.Kind.Slider: return SFrozen[i];
                case LevelDef.Kind.Laser: return LFrozen[i];
                default: return RFrozen[i];
            }
        }

        /// <summary>True when the player will be able to act on the next tick.</summary>
        public bool CanActNext => !Dead && !Won && !Moving && !Pending && PFrozen <= 1;

        public bool LoanActive => Countdown > 0 || Pending || PFrozen > 0;
    }
}
