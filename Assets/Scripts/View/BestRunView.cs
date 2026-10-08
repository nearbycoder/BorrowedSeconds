using System.Collections.Generic;
using BorrowedSeconds.Sim;
using UnityEngine;

namespace BorrowedSeconds.View
{
    /// <summary>
    /// Race your best: a translucent pawn replaying a saved run in step with the level's clock.
    /// The run is replayed once in the simulation when the level loads, keeping the pawn's place
    /// on every tick; it needs no simulation of its own while you play. Whatever the run has frozen
    /// shows as a violet crystal where it froze (a block, a laser's turret and bar, a rotor's arms),
    /// so the ghost's path through it makes sense. Once its run has won, it fades out on the exit.
    /// </summary>
    public sealed class BestRunView
    {
        readonly BoardView board;
        readonly GameObject root;
        readonly Transform pivot;
        readonly Material mat, ringMat;
        readonly List<Vector2> tiles = new List<Vector2>();
        readonly List<bool> frozen = new List<bool>();
        readonly List<int> dirs = new List<int>();
        // what the run has frozen, per tick and obstacle
        struct Froze { public bool On; public Vector2 Tile; public float Angle; public int Len; }
        readonly List<Froze[]> froze = new List<Froze[]>();
        readonly GameObject[] phantoms;
        readonly Transform[] phantomBars, phantomArms;
        readonly Material crystalMat;
        static readonly Color CrystalTint = new Color(1.1f, 0.85f, 2.6f, 0.55f);
        float fade, yaw;
        static readonly Color Tint = new Color(1.3f, 1.0f, 2.6f, 0.5f), FrozenTint = new Color(0.7f, 1.8f, 2.6f, 0.6f);
        // the ghost is additive, so on white porcelain a violet ring under its feet is what reads
        static readonly Color RingTint = new Color(0.42f, 0.3f, 0.95f, 0.85f);

        /// <summary>The run replayed to a win (a damaged or stale run doesn't: no ghost).</summary>
        public bool Valid { get; }
        /// <summary>The tick the run won on.</summary>
        public int WinTick => tiles.Count - 1;
        /// <summary>The ghost is on screen (checks read it).</summary>
        public bool Showing => fade > 0.5f && root.activeSelf;

        public BestRunView(BoardView board, IList<TimedAction> run)
        {
            this.board = board;
            var d = board.Def;
            var s = Simulation.Create(d);
            int k = 0;
            Keep(d, s);
            while (!s.Dead && !s.Won && s.Tick < 20 * 60 * 10)
            {
                int act = k < run.Count && run[k].Tick == s.Tick ? run[k++].Action : Act.None;
                Simulation.Step(d, s, act);
                Keep(d, s);
            }
            Valid = s.Won;

            root = Shapes.Group("BestRun", board.transform);
            pivot = Shapes.Group("Pivot", root.transform).transform;
            mat = Mats.Instance("BS_Ghost");
            mat.SetColor("_Color", Tint);
            if (Shapes.Model("Player", pivot, (part, m) => mat, false) == null)
            {
                Shapes.Cyl("Base", pivot, new Vector3(0, 0.05f, 0), 0.5f, 0.1f, mat, false);
                Shapes.Make("Torso", pivot, Shapes.MeshOf(PrimitiveType.Capsule), mat, new Vector3(0, 0.38f, 0), new Vector3(0.42f, 0.3f, 0.42f), false);
                Shapes.Ball("Head", pivot, new Vector3(0, 0.8f, 0), 0.38f, mat, false);
            }
            ringMat = Mats.Instance("BS_Ring");
            ringMat.SetColor("_Color", RingTint);
            ringMat.SetColor("_BackColor", new Color(0, 0, 0, 0));
            ringMat.SetFloat("_Inner", 0.5f);
            ringMat.SetFloat("_Outer", 0.72f);
            ringMat.SetFloat("_Ticks", 0f);
            Shapes.Flat("Ring", root.transform, new Vector3(0, 0.03f, 0), 0.9f, ringMat);

            // the run's frozen obstacles, built like the forecast's ghosts but in the ghost's violet
            crystalMat = Mats.Instance("BS_Ghost");
            crystalMat.SetColor("_Color", CrystalTint);
            var frozenRoot = Shapes.Group("Frozen", board.transform).transform;
            phantoms = new GameObject[d.ObstacleCount];
            phantomBars = new Transform[d.ObstacleCount];
            phantomArms = new Transform[d.ObstacleCount];
            for (int o = 0; o < d.ObstacleCount; o++)
            {
                switch (d.KindOf(o, out int i))
                {
                    case LevelDef.Kind.Slider:
                        phantoms[o] = Shapes.Box("FrozenBlock", frozenRoot, Vector3.zero, new Vector3(0.9f, 0.9f, 0.9f), crystalMat, false);
                        break;
                    case LevelDef.Kind.Laser:
                    {
                        var ld = d.Lasers[i];
                        var g = Shapes.Group("FrozenLaser", frozenRoot, board.At(ld.Tile));
                        Shapes.Box("Turret", g.transform, new Vector3(0, 0.45f, 0), new Vector3(0.8f, 0.9f, 0.8f), crystalMat, false);
                        phantomBars[o] = Shapes.Box("Bar", g.transform, Vector3.zero, Vector3.one, crystalMat, false).transform;
                        phantoms[o] = g;
                        break;
                    }
                    default:
                    {
                        var rd = d.Rotors[i];
                        var g = Shapes.Group("FrozenRotor", frozenRoot, board.At(rd.Tile));
                        var arms = Shapes.Group("Arms", g.transform, new Vector3(0, 0.46f, 0)).transform;
                        float len = rd.Length + 0.3f;
                        for (int dir = 0; dir < 4; dir++)
                        {
                            if ((rd.ArmMask & (1 << dir)) == 0) continue;
                            var a = Shapes.Group("Arm", arms).transform;
                            a.localRotation = Quaternion.Euler(0, dir * 90f, 0);
                            Shapes.Box("Bar", a, new Vector3(0, 0, len * 0.5f + 0.1f), new Vector3(0.3f, 0.32f, len), crystalMat, false);
                        }
                        phantomArms[o] = arms;
                        phantoms[o] = g;
                        break;
                    }
                }
                phantoms[o].SetActive(false);
            }
            frozenRoot.SetParent(root.transform, true); // fades and hides with the ghost
            root.SetActive(false);
        }

        void Keep(LevelDef d, SimState s)
        {
            tiles.Add(BoardView.PlayerTile(d, s));
            frozen.Add(s.PFrozen > 0);
            dirs.Add(s.Moving ? s.MoveDir : -1);
            var f = new Froze[d.ObstacleCount];
            for (int o = 0; o < f.Length; o++)
            {
                f[o].On = s.IsObstacleFrozen(d, o);
                if (!f[o].On) continue;
                switch (d.KindOf(o, out int i))
                {
                    case LevelDef.Kind.Slider: f[o].Tile = BoardView.SliderTile(d, s, i); break;
                    case LevelDef.Kind.Laser: f[o].Len = s.LFrozenLit[i] ? s.LFrozenLen[i] : 0; break;
                    default: f[o].Angle = BoardView.RotorAngle(d, s, i); break;
                }
            }
            froze.Add(f);
        }

        /// <summary>The tick the ghost was last drawn at (checks compare what it showed with the run).</summary>
        public int RenderedTick { get; private set; } = -1;

        /// <summary>Whether the run's frozen <paramref name="obstacle"/> is drawn, and how: where on the
        /// board, a laser's bar length (0: none), a rotor's arm angle (checks read it).</summary>
        public bool FrozenShown(int obstacle, out Vector3 boardPos, out int len, out float angle)
        {
            var t = phantoms[obstacle].transform;
            boardPos = t.localPosition + root.transform.localPosition;
            var bar = phantomBars[obstacle];
            len = bar != null && bar.gameObject.activeSelf ? Mathf.RoundToInt(bar.localScale.z) : 0;
            angle = phantomArms[obstacle] != null ? phantomArms[obstacle].localEulerAngles.y : 0f;
            return root.activeSelf && phantoms[obstacle].activeSelf;
        }

        /// <summary>The ghost's tile on a tick (checks compare it with the run's own replay).</summary>
        public Vector2 TileAt(int tick) => tiles[Mathf.Clamp(tick, 0, tiles.Count - 1)];
        public bool FrozenAt(int tick) => frozen[Mathf.Clamp(tick, 0, frozen.Count - 1)];

        /// <param name="tick">The level's tick, <paramref name="alpha"/> of the way to the next one.</param>
        public void Render(int tick, float alpha, bool show, float dt, float time)
        {
            bool on = show && Valid && tick < WinTick + 10;
            fade = Mathf.MoveTowards(fade, on ? 1f : 0f, dt * (on ? 3f : 2f));
            root.SetActive(fade > 0.01f);
            if (!root.activeSelf) return;
            int t = Mathf.Clamp(tick, 0, tiles.Count - 1), u = Mathf.Min(t + 1, tiles.Count - 1);
            var p = Vector2.Lerp(tiles[t], tiles[u], alpha);
            root.transform.localPosition = board.Pos(p);
            if (dirs[u] >= 0) yaw = Mathf.LerpAngle(yaw, dirs[u] * 90f, 1f - Mathf.Exp(-22f * dt));
            pivot.localRotation = Quaternion.Euler(0, yaw, 0);
            float hop = dirs[u] >= 0 ? Mathf.Abs(Mathf.Sin((tiles[u] - p).magnitude * Mathf.PI)) * 0.12f : 0f;
            pivot.localPosition = new Vector3(0, hop + 0.02f * Mathf.Sin(time * 2.4f), 0);
            var c = frozen[t] ? FrozenTint : Tint;
            mat.SetColor("_Color", new Color(c.r, c.g, c.b, c.a * fade));
            ringMat.SetFloat("_Alpha", fade);

            // what the run has frozen on this tick (frozen things don't move, so no blending)
            var d = board.Def;
            var fz = froze[t];
            for (int o = 0; o < phantoms.Length; o++)
            {
                bool held = fz[o].On;
                if (phantoms[o].activeSelf != held) phantoms[o].SetActive(held);
                if (!held) continue;
                switch (d.KindOf(o, out int i))
                {
                    case LevelDef.Kind.Slider:
                        phantoms[o].transform.localPosition = board.Pos(fz[o].Tile, 0.43f) - root.transform.localPosition;
                        break;
                    case LevelDef.Kind.Laser:
                    {
                        var ld = d.Lasers[i];
                        phantoms[o].transform.localPosition = board.At(ld.Tile) - root.transform.localPosition;
                        int len = fz[o].Len;
                        phantomBars[o].gameObject.SetActive(len > 0);
                        if (len > 0)
                        {
                            var dirV = BoardView.DirVec(ld.Dir);
                            phantomBars[o].localPosition = new Vector3(0, 0.3f, 0) + dirV * (0.5f + len * 0.5f);
                            phantomBars[o].localRotation = Quaternion.LookRotation(dirV);
                            phantomBars[o].localScale = new Vector3(0.3f, 0.3f, len);
                        }
                        break;
                    }
                    default:
                        phantoms[o].transform.localPosition = board.At(d.Rotors[i].Tile) - root.transform.localPosition;
                        phantomArms[o].localRotation = Quaternion.Euler(0, fz[o].Angle, 0);
                        break;
                }
            }
            crystalMat.SetColor("_Color", new Color(CrystalTint.r, CrystalTint.g, CrystalTint.b, CrystalTint.a * fade * (0.85f + 0.15f * Mathf.Sin(time * 3f))));
            RenderedTick = t;
        }
    }
}
