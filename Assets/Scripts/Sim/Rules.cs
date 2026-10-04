namespace BorrowedSeconds.Sim
{
    /// <summary>Fixed rule constants. Everything in the simulation is measured in integer ticks.</summary>
    public static class Rules
    {
        public const int TicksPerSecond = 20;
        /// <summary>Ticks the player needs to step one tile.</summary>
        public const int MoveTicks = 3;
        /// <summary>Length of every loan and every repayment (3.0 s).</summary>
        public const int FreezeTicks = 60;
        /// <summary>Continuous pressure a time-lock needs before it latches (3.0 s).</summary>
        public const int LockTicks = 60;
        public const int DefaultTerm = 100;
        /// <summary>Lasers flicker this long before firing (visual telegraph only).</summary>
        public const int LaserWarnTicks = 10;
        public const int MaxTiles = 256;

        public static float Seconds(int ticks) => ticks / (float)TicksPerSecond;
    }

    public enum Tile : byte { Void = 0, Floor = 1, Wall = 2 }

    /// <summary>Grid directions; rows grow downward so North is row - 1.</summary>
    public static class Dirs
    {
        public const int N = 0, E = 1, S = 2, W = 3;
        public static readonly int[] DX = { 0, 1, 0, -1 };
        public static readonly int[] DY = { -1, 0, 1, 0 };

        public static int Parse(char c)
        {
            switch (char.ToUpperInvariant(c))
            {
                case 'N': return N;
                case 'E': return E;
                case 'S': return S;
                case 'W': return W;
            }
            throw new System.FormatException("bad direction " + c);
        }
    }

    /// <summary>Player inputs, one per tick.</summary>
    public static class Act
    {
        public const int None = 0;
        public const int MoveN = 1, MoveE = 2, MoveS = 3, MoveW = 4;
        public const int BorrowBase = 16;
        public static int Move(int dir) => 1 + dir;
        public static int Borrow(int obstacle) => BorrowBase + obstacle;
        public static bool IsMove(int a) => a >= 1 && a <= 4;
        public static bool IsBorrow(int a) => a >= BorrowBase;
        public static int MoveDir(int a) => a - 1;
        public static int BorrowTarget(int a) => a - BorrowBase;

        public static string Name(int a)
        {
            if (a == None) return "wait";
            if (IsMove(a)) return "NESW"[MoveDir(a)].ToString();
            return "B" + BorrowTarget(a);
        }
    }
}
