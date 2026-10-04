namespace BorrowedSeconds.Sim
{
    /// <summary>A 256-bit set of tile indices (boards are at most 16x16).</summary>
    public struct TileMask
    {
        public ulong A, B, C, D;

        public void Set(int i)
        {
            ulong bit = 1UL << (i & 63);
            switch (i >> 6)
            {
                case 0: A |= bit; break;
                case 1: B |= bit; break;
                case 2: C |= bit; break;
                default: D |= bit; break;
            }
        }

        public bool Has(int i)
        {
            if (i < 0) return false;
            ulong bit = 1UL << (i & 63);
            switch (i >> 6)
            {
                case 0: return (A & bit) != 0;
                case 1: return (B & bit) != 0;
                case 2: return (C & bit) != 0;
                case 3: return (D & bit) != 0;
                default: return false;
            }
        }

        public void Or(in TileMask o) { A |= o.A; B |= o.B; C |= o.C; D |= o.D; }
        public bool Intersects(in TileMask o) => ((A & o.A) | (B & o.B) | (C & o.C) | (D & o.D)) != 0;
        public bool Any => (A | B | C | D) != 0;
        public void Clear() { A = B = C = D = 0; }
    }
}
