using System.Collections.Generic;

namespace BorrowedSeconds.Sim
{
    /// <summary>
    /// The pause menu's clue, a step between the level's tip and Watch solution: from the solver's
    /// route, the obstacle it borrows first and the tile it stands on when its first debt freezes
    /// it. It points at the trick and leaves the timing to the player. On a route with several
    /// loans there is one step per loan (<see cref="Steps"/>), and the clue follows the loans the
    /// player has taken (<see cref="StepFor"/>).
    /// </summary>
    public readonly struct Clue
    {
        public readonly int Obstacle, BorrowTick, Tile, FreezeTick;

        public Clue(int obstacle, int borrowTick, int tile, int freezeTick)
        {
            Obstacle = obstacle;
            BorrowTick = borrowTick;
            Tile = tile;
            FreezeTick = freezeTick;
        }

        public bool Valid => Obstacle >= 0 && Tile >= 0;

        /// <summary>Replays the route from the level start up to its first debt freeze.</summary>
        public static Clue For(LevelDef d, IList<TimedAction> route)
        {
            var s = Simulation.Create(d);
            int k = 0, obstacle = -1, borrowTick = -1;
            while (!s.Dead && !s.Won && s.Tick < 100000)
            {
                int act = Act.None;
                if (k < route.Count && route[k].Tick == s.Tick) act = route[k++].Action;
                if (obstacle < 0 && Act.IsBorrow(act)) { obstacle = Act.BorrowTarget(act); borrowTick = s.Tick; }
                bool wasFrozen = s.PFrozen > 0;
                Simulation.Step(d, s, act);
                if (!wasFrozen && s.PFrozen > 0) return new Clue(obstacle, borrowTick, s.P, s.Tick);
            }
            return new Clue(obstacle, borrowTick, -1, -1);
        }

        /// <summary>One clue per loan of the route, in order: each borrow's target and the tile the
        /// route stands on when that loan's debt freezes it (one loan is out at a time, so the k-th
        /// freeze repays the k-th borrow).</summary>
        public static List<Clue> Steps(LevelDef d, IList<TimedAction> route)
        {
            var steps = new List<Clue>();
            var s = Simulation.Create(d);
            int k = 0, obstacle = -1, borrowTick = -1;
            while (!s.Dead && !s.Won && s.Tick < 100000)
            {
                int act = Act.None;
                if (k < route.Count && route[k].Tick == s.Tick) act = route[k++].Action;
                int loans = s.Loans;
                bool wasFrozen = s.PFrozen > 0;
                Simulation.Step(d, s, act);
                if (s.Loans > loans) { obstacle = Act.BorrowTarget(act); borrowTick = s.Tick - 1; }
                if (!wasFrozen && s.PFrozen > 0 && obstacle >= 0)
                {
                    steps.Add(new Clue(obstacle, borrowTick, s.P, s.Tick));
                    obstacle = -1;
                }
            }
            return steps;
        }

        /// <summary>Which of <paramref name="count"/> steps a player in this state is on: the loan they
        /// have out (until they thaw from its debt), else the next one they'll take. Past the last
        /// step it stays on the last; a rewind takes it back with the loans.</summary>
        public static int StepFor(SimState s, int count)
        {
            if (count <= 0) return -1;
            int k = s.Loans - (LoanOut(s) ? 1 : 0);
            return k < 0 ? 0 : k >= count ? count - 1 : k;
        }

        /// <summary>A loan is out until the player can borrow again: its debt is running, or they're
        /// frozen paying it with more than the thaw tick left (<see cref="SimState.CanActNext"/>).</summary>
        public static bool LoanOut(SimState s) => s.Countdown > 0 || s.Pending || s.PFrozen > 1;

        /// <summary>The obstacle's label: FREEZE FIRST, then FREEZE NEXT.</summary>
        public static string ObstacleLabel(int step) => step <= 0 ? "FREEZE FIRST" : "FREEZE NEXT";

        /// <summary>What a borrow freezes: "block", "laser" or "rotor".</summary>
        public static string Noun(LevelDef d, int obstacle)
        {
            switch (d.KindOf(obstacle, out _))
            {
                case LevelDef.Kind.Slider: return "block";
                case LevelDef.Kind.Laser: return "laser";
                default: return "rotor";
            }
        }

        /// <summary>The clue in words (plain text; the tip panel adds colour).</summary>
        public string Words(LevelDef d) => !Valid ? ""
            : $"freeze the {Noun(d, Obstacle)} marked FREEZE FIRST, and be on the tile marked DEBT HERE when your debt falls due.";

        /// <summary>Step <paramref name="step"/> of <paramref name="count"/> in words: as <see cref="Words(LevelDef)"/>
        /// on a one-loan route, else naming the loan.</summary>
        public string Words(LevelDef d, int step, int count) => !Valid ? "" : count <= 1 ? Words(d)
            : $"for loan {step + 1} of {count}, freeze the {Noun(d, Obstacle)} marked {ObstacleLabel(step)}, and be on the tile marked DEBT HERE when its debt falls due.";
    }
}
