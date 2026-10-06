using System;
using System.Collections.Generic;

namespace BorrowedSeconds.Sim
{
    /// <summary>
    /// Where the automatic rewind after a death lands. A plain two-second rewind is not enough:
    /// a thaw death comes three seconds into a freeze, so two seconds back the player is still
    /// frozen and would replay the same death forever. The rewind goes back at least
    /// <see cref="DeathTicks"/> and further if needed, to the latest tick where the player is free
    /// to act with at least <see cref="ControlTicks"/> before the debt freezes them.
    /// </summary>
    public static class Rewind
    {
        public const int DeathTicks = 2 * Rules.TicksPerSecond;
        public const int ControlTicks = Rules.TicksPerSecond;

        /// <summary>True if the player can still change their fate from this state.</summary>
        public static bool Playable(SimState s) =>
            !s.Dead && !s.Won && s.PFrozen == 0 && !s.Pending && (s.Countdown == 0 || s.Countdown >= ControlTicks);

        /// <summary>
        /// Index into <paramref name="history"/> (one state per tick, index 0 = the start) to rewind
        /// to after dying at <paramref name="deathIndex"/>.
        /// </summary>
        public static int AfterDeath(IReadOnlyList<SimState> history, int deathIndex)
        {
            for (int t = Math.Max(0, deathIndex - DeathTicks); t > 0; t--)
                if (Playable(history[t])) return t;
            return 0;
        }
    }
}
