using System.Collections.Generic;
using BorrowedSeconds.Sim;
using UnityEngine;

namespace BorrowedSeconds.View
{
    /// <summary>
    /// Race your best: a translucent pawn replaying a saved run in step with the level's clock.
    /// The run is replayed once in the simulation when the level loads, keeping the pawn's place
    /// on every tick; it needs no simulation of its own while you play. Once its run has won, it
    /// fades out on the exit.
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
            root.SetActive(false);
        }

        void Keep(LevelDef d, SimState s)
        {
            tiles.Add(BoardView.PlayerTile(d, s));
            frozen.Add(s.PFrozen > 0);
            dirs.Add(s.Moving ? s.MoveDir : -1);
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
        }
    }
}
