using System.Collections.Generic;

namespace BorrowedSeconds.Sim
{
    /// <summary>
    /// The pause menu's clue, a step between the level's tip and Watch solution: from the solver's
    /// route, the obstacle it borrows first and the tile it stands on when its first debt freezes
    /// it. It points at the trick and leaves the timing to the player.
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
    }
}
