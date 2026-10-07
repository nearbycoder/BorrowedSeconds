namespace BorrowedSeconds.Sim
{
    /// <summary>
    /// The death banner's second line: which obstacle killed the player (<see cref="SimState.DeathCause"/>)
    /// and whether they thawed out of the debt's freeze inside it or it ran into them, so a default
    /// says what happened before the rewind shows it again.
    /// </summary>
    public static class DeathReport
    {
        public static string Noun(LevelDef d, int obstacle)
        {
            switch (d.KindOf(obstacle, out _))
            {
                case LevelDef.Kind.Slider: return "the block";
                case LevelDef.Kind.Laser: return "the beam";
                default: return "a rotor arm";
            }
        }

        /// <summary>"you thawed inside the beam", "the block caught you"; empty if the cause is unknown.</summary>
        public static string Describe(LevelDef d, int obstacle, bool thawed)
        {
            if (obstacle < 0 || obstacle >= d.ObstacleCount) return "";
            return thawed ? $"you thawed inside {Noun(d, obstacle)}" : $"{Noun(d, obstacle)} caught you";
        }
    }
}
