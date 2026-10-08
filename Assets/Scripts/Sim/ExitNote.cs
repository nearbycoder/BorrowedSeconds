namespace BorrowedSeconds.Sim
{
    /// <summary>
    /// What the exit says to a player standing on it who can't leave yet: it's sealed until every
    /// dial latches, or they're still in debt and leave as they thaw. Empty when they aren't on the
    /// exit, or can leave.
    /// </summary>
    public static class ExitNote
    {
        public enum Kind { None, Sealed, InDebt }

        public static Kind Of(LevelDef d, SimState s, out int latched)
        {
            latched = 0;
            if (s.Dead || s.Won || s.Moving || s.P != d.Exit) return Kind.None;
            foreach (int c in s.LockCharge) if (c >= Rules.LockTicks) latched++;
            if (!s.ExitOpen) return Kind.Sealed;
            return s.Countdown > 0 || s.Pending || s.PFrozen > 0 ? Kind.InDebt : Kind.None;
        }

        /// <summary>"Exit sealed: latch every dial (1 of 2 latched)", "Pay your debt here: you leave
        /// as you thaw", or empty.</summary>
        public static string Line(LevelDef d, SimState s)
        {
            switch (Of(d, s, out int latched))
            {
                case Kind.Sealed:
                    return d.Locks.Length == 1 ? "Exit sealed: latch the dial first"
                        : $"Exit sealed: latch every dial ({latched} of {d.Locks.Length} latched)";
                case Kind.InDebt: return "Pay your debt here: you leave as you thaw";
                default: return "";
            }
        }
    }
}
