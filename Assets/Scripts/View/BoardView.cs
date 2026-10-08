using System.Collections.Generic;
using BorrowedSeconds.Sim;
using UnityEngine;

namespace BorrowedSeconds.View
{
    /// <summary>Marks a collider as a clickable obstacle.</summary>
    public sealed class ObstaclePick : MonoBehaviour
    {
        public int Index;
    }

    /// <summary>
    /// Builds the level diorama and renders interpolated simulation states onto it.
    /// World layout: tile (x,row) sits at (x - (W-1)/2, 0, (H-1)/2 - row); floor top is y = 0.
    /// </summary>
    public sealed class BoardView : MonoBehaviour
    {
        public LevelDef Def;
        public PlayerView Player;
        public readonly List<SliderView> Sliders = new List<SliderView>();
        public readonly List<LaserView> Lasers = new List<LaserView>();
        public readonly List<RotorView> Rotors = new List<RotorView>();
        public readonly List<PlateView> Plates = new List<PlateView>();
        public readonly List<GateView> Gates = new List<GateView>();
        public readonly List<LockView> Locks = new List<LockView>();
        public ExitView Exit;
        public Bounds Bounds;
        /// <summary>Centre of every floor and wall tile's top face, in board space (camera framing).</summary>
        public readonly List<Vector3> TileTops = new List<Vector3>();
        Transform statics;

        public Vector3 Pos(float x, float y, float h = 0f) => new Vector3(x - (Def.W - 1) * 0.5f, h, (Def.H - 1) * 0.5f - y);
        public Vector3 At(int idx, float h = 0f) => Pos(Def.X(idx), Def.Y(idx), h);
        public Vector3 Pos(Vector2 tile, float h = 0f) => Pos(tile.x, tile.y, h);

        public static Vector3 DirVec(int dir) => new Vector3(Dirs.DX[dir], 0, -Dirs.DY[dir]);

        public void Build(LevelDef d)
        {
            Def = d;
            statics = Shapes.Group("Statics", transform).transform;
            BuildTerrain();
            for (int i = 0; i < d.Plates.Length; i++) Plates.Add(new PlateView(this, i));
            for (int i = 0; i < d.Gates.Length; i++) Gates.Add(new GateView(this, i));
            for (int i = 0; i < d.Locks.Length; i++) Locks.Add(new LockView(this, i));
            Exit = new ExitView(this);
            for (int i = 0; i < d.Sliders.Length; i++) Sliders.Add(new SliderView(this, i));
            for (int i = 0; i < d.Lasers.Length; i++) Lasers.Add(new LaserView(this, i));
            for (int i = 0; i < d.Rotors.Length; i++) Rotors.Add(new RotorView(this, i));
            Player = new PlayerView(this);
            BuildLinks();
            BuildGlowLights();
        }

        // ---- Graphics fidelity Ultra: the glowing pieces light the board around them
        /// <summary>Set by Game.GraphicsFidelity; the lights are built with every board and only switched.</summary>
        public static bool GlowLights;
        Light playerLight, exitLight;
        Light[] laserLights = new Light[0];

        static Light PointLight(Transform parent, Vector3 local, Color color, float range)
        {
            var go = new GameObject("GlowLight");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.range = range;
            l.shadows = LightShadows.None;
            l.enabled = false;
            return l;
        }

        void BuildGlowLights()
        {
            playerLight = PointLight(Player.Root.transform, new Vector3(0, 0.55f, 0), Palette.Amber, 2.6f);
            exitLight = PointLight(Exit.Root.transform, new Vector3(0, 0.7f, 0), Palette.Gold, 3f);
            laserLights = new Light[Lasers.Count];
            for (int i = 0; i < Lasers.Count; i++) laserLights[i] = PointLight(Lasers[i].Root.transform, Vector3.zero, Palette.Coral, 2f);
        }

        /// <summary>How many glow lights are on (checks read it).</summary>
        public int GlowLightsOn
        {
            get
            {
                int n = (playerLight != null && playerLight.enabled ? 1 : 0) + (exitLight != null && exitLight.enabled ? 1 : 0);
                foreach (var l in laserLights) if (l != null && l.enabled) n++;
                return n;
            }
        }

        void RenderGlowLights(SimState b, float time)
        {
            if (playerLight == null) return;
            bool on = GlowLights;
            playerLight.enabled = on && !b.Dead;
            exitLight.enabled = on;
            if (!on) { foreach (var l in laserLights) l.enabled = false; return; }
            bool frozen = b.PFrozen > 0;
            playerLight.color = frozen ? Palette.Ice : Palette.Amber;
            playerLight.intensity = (frozen ? 1.6f : 1.3f) * (1f + 0.06f * Mathf.Sin(time * 3.1f));
            exitLight.intensity = b.ExitOpen ? 1.6f + 0.2f * Mathf.Sin(time * 2f) : 0.5f;
            for (int i = 0; i < laserLights.Length; i++)
            {
                bool lit = b.LaserLit[i] && b.LFrozen[i] == 0;
                bool bar = b.LFrozen[i] > 0 && b.LFrozenLit[i];
                int len = bar ? b.LFrozenLen[i] : b.BeamLen[i];
                var l = laserLights[i];
                l.enabled = (lit || bar) && len > 0;
                if (!l.enabled) continue;
                // one light at the middle of the beam, reaching both ends
                l.transform.localPosition = new Vector3(0, 0.35f, 0.5f + len * 0.5f);
                l.range = len * 0.5f + 1.6f;
                l.color = bar ? Palette.Ice : Palette.Coral;
                l.intensity = (bar ? 0.9f : 1.5f) * (bar ? 1f : 1f + 0.1f * Mathf.Sin(time * 50f));
            }
        }

        // arcs from each plate to the gates and lasers on its channel, fired when it goes down
        List<LinkPulse>[] links = new List<LinkPulse>[0];

        void BuildLinks()
        {
            links = new List<LinkPulse>[Def.Plates.Length];
            for (int i = 0; i < Def.Plates.Length; i++)
            {
                var pd = Def.Plates[i];
                var color = Palette.Channel(pd.Channel);
                var from = At(pd.Tile, 0.08f);
                links[i] = new List<LinkPulse>();
                foreach (var gd in Def.Gates)
                    if (gd.Channel == pd.Channel) links[i].Add(new LinkPulse(transform, from, At(gd.Tile, 0.95f), color));
                foreach (var ld in Def.Lasers)
                    if (ld.Plate == pd.Channel) links[i].Add(new LinkPulse(transform, from, At(ld.Tile, (Def.Tiles[ld.Tile] == Tile.Wall ? 0.6f : 0f) + 0.45f), color));
            }
        }

        public int LinkCount(int plate) => plate >= 0 && plate < links.Length ? links[plate].Count : 0;

        /// <summary>Shows what a plate drives: a pulse of light to every gate and laser on its channel.</summary>
        public void PulseLinks(int plate)
        {
            if (plate >= 0 && plate < links.Length) foreach (var l in links[plate]) l.Fire();
        }

        void BuildTerrain()
        {
            var floorA = Mats.Lit(Palette.FloorA, 0.55f);
            var floorB = Mats.Lit(Palette.FloorB, 0.55f);
            var plinth = Mats.Lit(Palette.Plinth, 0.25f);
            var wall = Mats.Lit(Palette.Wall, 0.3f);
            var wallTop = Mats.Lit(Palette.WallTop, 0.4f);
            var min = new Vector3(float.MaxValue, 0, float.MaxValue);
            var max = new Vector3(float.MinValue, 0, float.MinValue);
            for (int y = 0; y < Def.H; y++)
            for (int x = 0; x < Def.W; x++)
            {
                var t = Def.Tiles[Def.Idx(x, y)];
                if (t == Tile.Void) continue;
                var p = Pos(x, y);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
                TileTops.Add(p);
                Shapes.Box("Plinth", statics, p + new Vector3(0, -0.85f, 0), new Vector3(1f, 1.4f, 1f), plinth, false);
                if (t == Tile.Floor)
                {
                    var m = model("FloorTile", p);
                    if (m == null) Shapes.Box("Floor", statics, p + new Vector3(0, -0.075f, 0), new Vector3(0.97f, 0.15f, 0.97f), (x + y) % 2 == 0 ? floorA : floorB);
                    else foreach (var r in m.GetComponentsInChildren<Renderer>()) r.sharedMaterial = (x + y) % 2 == 0 ? floorA : floorB;
                }
                else if (model("WallBlock", p) == null)
                {
                    Shapes.Box("Wall", statics, p + new Vector3(0, 0.22f, 0), new Vector3(1f, 0.6f, 1f), wall);
                    Shapes.Box("WallTop", statics, p + new Vector3(0, 0.53f, 0), new Vector3(0.9f, 0.04f, 0.9f), wallTop);
                }
            }
            Bounds = new Bounds((min + max) * 0.5f, max - min + new Vector3(1, 1, 1));

            GameObject model(string name, Vector3 p)
            {
                var m = Shapes.Model(name, statics);
                if (m != null) m.transform.localPosition = p;
                return m;
            }
        }

        public void Render(SimState a, SimState b, float t, float dt)
        {
            float time = Time.time;
            Player.Render(a, b, t, dt, time);
            foreach (var v in Sliders) v.Render(a, b, t, dt, time);
            foreach (var v in Lasers) v.Render(a, b, t, dt, time);
            foreach (var v in Rotors) v.Render(a, b, t, dt, time);
            foreach (var v in Plates) v.Render(a, b, t, dt, time);
            foreach (var v in Gates) v.Render(a, b, t, dt, time);
            foreach (var v in Locks) v.Render(a, b, t, dt, time);
            Exit.Render(a, b, t, dt, time);
            RenderGlowLights(b, time);
            foreach (var list in links) foreach (var l in list) l.Render(dt);
            if (clueRing != null && clueRing.activeSelf)
            {
                clueRing.transform.localRotation = Quaternion.Euler(0, time * 24f, 0);
                clueMat.SetFloat("_Alpha", 0.75f + 0.25f * Mathf.Sin(time * 2.5f));
            }
        }

        /// <summary>The clue's obstacle (-1: none): its hover ghost glows gold.</summary>
        public int ClueObstacle { get; private set; } = -1;
        /// <summary>The clue's tile (-1: none): a dashed gold ring, slowly turning, where the route's first debt falls due (the HUD labels it DEBT HERE).</summary>
        public int ClueTile { get; private set; } = -1;
        GameObject clueRing;
        Material clueMat;

        public void SetClue(int obstacle, int tile)
        {
            ClueObstacle = obstacle;
            ClueTile = tile;
            for (int i = 0; i < Sliders.Count; i++) Sliders[i].Clue = Def.ObstacleIndex(LevelDef.Kind.Slider, i) == obstacle;
            for (int i = 0; i < Lasers.Count; i++) Lasers[i].Clue = Def.ObstacleIndex(LevelDef.Kind.Laser, i) == obstacle;
            for (int i = 0; i < Rotors.Count; i++) Rotors[i].Clue = Def.ObstacleIndex(LevelDef.Kind.Rotor, i) == obstacle;
            if (tile >= 0 && clueRing == null)
            {
                // eight short arcs: a dashed ring, a shape no other marker on the board uses
                clueMat = Mats.Instance("BS_Ring");
                clueMat.SetColor("_Color", new Color(2.4f, 1.7f, 0.5f, 1f));
                clueMat.SetColor("_BackColor", new Color(0, 0, 0, 0));
                clueMat.SetFloat("_Inner", 0.78f);
                clueMat.SetFloat("_Outer", 0.96f);
                clueMat.SetFloat("_Fill", 0.075f);
                clueMat.SetFloat("_Ticks", 0f);
                clueRing = Shapes.Group("ClueRing", transform);
                for (int k = 0; k < 8; k++)
                {
                    var dash = Shapes.Flat("Dash", clueRing.transform, Vector3.zero, 1.12f, clueMat);
                    dash.transform.localRotation = Quaternion.Euler(0, k * 45f, 0);
                }
            }
            if (clueRing != null)
            {
                clueRing.SetActive(tile >= 0);
                if (tile >= 0) clueRing.transform.localPosition = At(tile, 0.035f);
            }
        }

        public int Pick(Ray ray)
        {
            var hits = Physics.RaycastAll(ray, 200f);
            int best = -1;
            float bestDist = float.MaxValue;
            foreach (var h in hits)
            {
                var p = h.collider.GetComponent<ObstaclePick>();
                if (p == null || h.distance >= bestDist) continue;
                best = p.Index;
                bestDist = h.distance;
            }
            return best;
        }

        TileMask[] zones;

        /// <summary>
        /// The obstacle whose ground (<see cref="AimZones"/>: track, lane or sweep) is under the ray
        /// on the floor plane, or -1. Where zones overlap, the obstacle nearest the point wins.
        /// </summary>
        public int PickZone(Ray ray)
        {
            zones ??= AimZones.Build(Def);
            var o = transform.InverseTransformPoint(ray.origin);
            var dir = transform.InverseTransformDirection(ray.direction);
            if (dir.y > -1e-4f) return -1;
            var p = o + dir * (-o.y / dir.y);
            int x = Mathf.RoundToInt(p.x + (Def.W - 1) * 0.5f), y = Mathf.RoundToInt((Def.H - 1) * 0.5f - p.z);
            if (!Def.InBounds(x, y)) return -1;
            int tile = Def.Idx(x, y), best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < zones.Length; i++)
            {
                if (!zones[i].Has(tile)) continue;
                var c = transform.InverseTransformPoint(ObstacleCenter(i));
                float d = new Vector2(c.x - p.x, c.z - p.z).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>World-space centre of an obstacle right now (for tethers and labels).</summary>
        public Vector3 ObstacleCenter(int obstacle)
        {
            switch (Def.KindOf(obstacle, out int i))
            {
                case LevelDef.Kind.Slider: return Sliders[i].Root.transform.position + Vector3.up * 0.45f;
                case LevelDef.Kind.Laser: return Lasers[i].Root.transform.position + Vector3.up * 0.45f;
                default: return Rotors[i].Root.transform.position + Vector3.up * 0.5f;
            }
        }

        public void SetHighlight(int obstacle)
        {
            for (int i = 0; i < Sliders.Count; i++) Sliders[i].Highlight = Def.ObstacleIndex(LevelDef.Kind.Slider, i) == obstacle;
            for (int i = 0; i < Lasers.Count; i++) Lasers[i].Highlight = Def.ObstacleIndex(LevelDef.Kind.Laser, i) == obstacle;
            for (int i = 0; i < Rotors.Count; i++) Rotors[i].Highlight = Def.ObstacleIndex(LevelDef.Kind.Rotor, i) == obstacle;
        }

        /// <summary>The obstacle marked red as the one that killed the player (-1: none).</summary>
        public int Culprit { get; private set; } = -1;

        public void SetCulprit(int obstacle)
        {
            Culprit = obstacle;
            for (int i = 0; i < Sliders.Count; i++) Sliders[i].Culprit = Def.ObstacleIndex(LevelDef.Kind.Slider, i) == obstacle;
            for (int i = 0; i < Lasers.Count; i++) Lasers[i].Culprit = Def.ObstacleIndex(LevelDef.Kind.Laser, i) == obstacle;
            for (int i = 0; i < Rotors.Count; i++) Rotors[i].Culprit = Def.ObstacleIndex(LevelDef.Kind.Rotor, i) == obstacle;
        }

        // ---------------------------------------------------------------- shared interpolation

        public static Vector2 TileOf(LevelDef d, int idx) => new Vector2(d.X(idx), d.Y(idx));

        public static Vector2 PlayerTile(LevelDef d, SimState s)
        {
            if (!s.Moving) return TileOf(d, s.P);
            return Vector2.Lerp(TileOf(d, s.F), TileOf(d, s.P), s.MoveProg / (float)Rules.MoveTicks);
        }

        public static Vector2 SliderTile(LevelDef d, SimState s, int i)
        {
            var sd = d.Sliders[i];
            var a = TileOf(d, sd.Path[s.SIdx[i]]);
            if (s.SProg[i] == 0) return a;
            int n = Simulation.SliderNext(sd, s.SIdx[i], s.SDir[i]);
            if (n < 0) return a;
            return Vector2.Lerp(a, TileOf(d, sd.Path[n]), s.SProg[i] / (float)sd.Speed);
        }

        public static float RotorAngle(LevelDef d, SimState s, int i)
        {
            float ang = s.ROrient[i] * 90f;
            if (s.RProg[i] > 0) ang += s.RDir[i] * 90f * s.RProg[i] / d.Rotors[i].Turn;
            return ang;
        }
    }
}
