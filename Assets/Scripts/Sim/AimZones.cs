namespace BorrowedSeconds.Sim
{
    /// <summary>
    /// The ground each obstacle owns for pointer aiming, so a fast block can be aimed by resting
    /// the pointer on its track instead of chasing it: a slider's whole track, a laser's emitter
    /// and lane up to the first static solid, a rotor's pivot and every tile its arms rest on or
    /// sweep (void excluded). Zones may overlap; the view breaks ties by distance.
    /// </summary>
    public static class AimZones
    {
        public static TileMask[] Build(LevelDef d)
        {
            var zones = new TileMask[d.ObstacleCount];
            for (int i = 0; i < d.Sliders.Length; i++)
            {
                ref var z = ref zones[d.ObstacleIndex(LevelDef.Kind.Slider, i)];
                foreach (int t in d.Sliders[i].Path) z.Set(t);
            }
            for (int i = 0; i < d.Lasers.Length; i++)
            {
                var l = d.Lasers[i];
                ref var z = ref zones[d.ObstacleIndex(LevelDef.Kind.Laser, i)];
                z.Set(l.Tile);
                int x = d.X(l.Tile) + Dirs.DX[l.Dir], y = d.Y(l.Tile) + Dirs.DY[l.Dir];
                while (d.InBounds(x, y) && !d.StaticSolid.Has(d.Idx(x, y)))
                {
                    z.Set(d.Idx(x, y));
                    x += Dirs.DX[l.Dir];
                    y += Dirs.DY[l.Dir];
                }
            }
            for (int i = 0; i < d.Rotors.Length; i++)
            {
                var r = d.Rotors[i];
                var sweep = new TileMask();
                for (int o = 0; o < 4; o++)
                {
                    sweep.Or(r.Rays[o]);
                    sweep.Or(r.WedgeCW[o]);
                    sweep.Or(r.WedgeCCW[o]);
                }
                ref var z = ref zones[d.ObstacleIndex(LevelDef.Kind.Rotor, i)];
                z.Set(r.Tile);
                for (int t = 0; t < d.Tiles.Length; t++)
                    if (sweep.Has(t) && d.Tiles[t] != Tile.Void) z.Set(t);
            }
            return zones;
        }
    }
}
