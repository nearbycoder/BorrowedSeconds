using BorrowedSeconds.Sim;
using UnityEngine;

namespace BorrowedSeconds.View
{
    /// <summary>
    /// Truthful previews computed by running the real simulation forward:
    ///  * while a debt is outstanding: red echoes of every hazard at the moment you would thaw if
    ///    you stopped right here, plus a safe / lethal marker under you;
    ///  * while aiming a loan: the same forecast assuming you borrow now and stand still.
    /// The frozen player never blocks anything, so the forecast doesn't depend on your future moves
    /// (only plates you press can change it).
    /// </summary>
    public sealed class GhostPreview
    {
        public enum Verdict { None, Safe, Soon, Lethal, Charges }

        readonly BoardView board;
        readonly LevelDef def;
        readonly SimState probe, thaw;
        readonly GameObject root;
        readonly GameObject[] sliderGhosts, laserGhosts, rotorGhosts;
        readonly Transform[] rotorArms;
        readonly Material ghostMat, beamMat, markerMat, markerRingMat;
        readonly GameObject marker, markerRing;
        bool active;
        float fade;
        public Verdict Result { get; private set; }
        public int ThawTick { get; private set; }
        public int DueTick { get; private set; }
        public bool Visible = true;

        public GhostPreview(BoardView board)
        {
            this.board = board;
            def = board.Def;
            probe = new SimState(def);
            thaw = new SimState(def);
            root = Shapes.Group("Ghosts", board.transform);
            ghostMat = Mats.Instance("BS_Ghost");
            ghostMat.SetColor("_Color", new Color(2.2f, 0.35f, 0.45f, 0.55f));
            beamMat = Mats.Instance("BS_GlowAdd");
            beamMat.SetColor("_Color", new Color(1.6f, 0.25f, 0.35f, 0.35f));

            sliderGhosts = new GameObject[def.Sliders.Length];
            for (int i = 0; i < sliderGhosts.Length; i++)
                sliderGhosts[i] = Shapes.Box("SliderGhost", root.transform, Vector3.zero, new Vector3(0.84f, 0.84f, 0.84f), ghostMat, false);

            laserGhosts = new GameObject[def.Lasers.Length];
            for (int i = 0; i < laserGhosts.Length; i++)
            {
                var lg = Shapes.Group("LaserGhost", root.transform);
                Shapes.Box("Beam", lg.transform, Vector3.zero, Vector3.one, beamMat, false);
                laserGhosts[i] = lg;
            }

            rotorGhosts = new GameObject[def.Rotors.Length];
            rotorArms = new Transform[def.Rotors.Length];
            for (int i = 0; i < rotorGhosts.Length; i++)
            {
                var rd = def.Rotors[i];
                var rg = Shapes.Group("RotorGhost", root.transform, board.At(rd.Tile));
                var arms = Shapes.Group("Arms", rg.transform, new Vector3(0, 0.46f, 0)).transform;
                float len = rd.Length + 0.3f;
                for (int d = 0; d < 4; d++)
                {
                    if ((rd.ArmMask & (1 << d)) == 0) continue;
                    var a = Shapes.Group("Arm", arms).transform;
                    a.localRotation = Quaternion.Euler(0, d * 90f, 0);
                    Shapes.Box("Bar", a, new Vector3(0, 0, len * 0.5f + 0.1f), new Vector3(0.22f, 0.24f, len), ghostMat, false);
                }
                rotorGhosts[i] = rg;
                rotorArms[i] = arms;
            }

            markerMat = Mats.Instance("BS_Ring");
            markerMat.SetFloat("_Inner", 0.0f);
            markerMat.SetFloat("_Outer", 0.62f);
            markerMat.SetFloat("_Ticks", 0f);
            marker = Shapes.Flat("ThawMarker", root.transform, Vector3.zero, 0.9f, markerMat);
            markerRingMat = Mats.Instance("BS_Ring");
            markerRingMat.SetFloat("_Inner", 0.8f);
            markerRingMat.SetFloat("_Outer", 0.95f);
            markerRing = Shapes.Flat("ThawRing", root.transform, Vector3.zero, 1.0f, markerRingMat);
            root.SetActive(false);
        }

        /// <summary>Recompute after every simulation tick. aimTarget >= 0 previews borrowing it now.</summary>
        public void Refresh(SimState cur, int aimTarget, bool loanAvailable)
        {
            active = false;
            Result = Verdict.None;
            if (cur.Dead || cur.Won) return;
            bool debt = cur.Countdown > 0 || cur.Pending;
            bool aiming = !debt && cur.PFrozen == 0 && aimTarget >= 0 && loanAvailable && !cur.IsObstacleFrozen(def, aimTarget);
            if (!debt && !aiming) return;

            probe.CopyFrom(cur);
            int guard = 0;
            if (aiming)
            {
                while (!probe.CanActNext && guard++ < 10) Simulation.Step(def, probe, Act.None);
                Simulation.Step(def, probe, Act.Borrow(aimTarget));
                if (probe.Countdown == 0) return;
            }
            // run to the freeze, then to the thaw
            bool sawFreeze = false;
            DueTick = -1;
            ThawTick = -1;
            int latchedBefore = CountLatched(probe);
            while (guard++ < 400 && !probe.Dead && !probe.Won)
            {
                Simulation.Step(def, probe, Act.None);
                if (!sawFreeze && probe.PFrozen > 0) { sawFreeze = true; DueTick = probe.Tick; }
                if (sawFreeze && probe.PFrozen == 0) break;
            }
            if (!sawFreeze && !probe.Won) return;
            ThawTick = probe.Tick;
            thaw.CopyFrom(probe);
            // does the player survive the thaw and the moment after?
            var after = probe;
            int extra = 0;
            while (extra < 12 && !after.Dead && !after.Won)
            {
                Simulation.Step(def, after, Act.None);
                extra++;
                if (after.Dead) break;
            }
            if (thaw.Dead || (after.Dead && extra <= 2)) Result = Verdict.Lethal;
            else if (after.Dead) Result = Verdict.Soon;
            else Result = CountLatched(thaw) > latchedBefore ? Verdict.Charges : Verdict.Safe;
            active = true;
        }

        static int CountLatched(SimState s)
        {
            int n = 0;
            foreach (int c in s.LockCharge) if (c >= Rules.LockTicks) n++;
            return n;
        }

        public void Render(float dt, float time)
        {
            fade = Mathf.MoveTowards(fade, active && Visible ? 1f : 0f, dt * 6f);
            root.SetActive(fade > 0.01f);
            if (fade <= 0.01f) return;
            var s = thaw;
            for (int i = 0; i < sliderGhosts.Length; i++)
            {
                bool show = s.SFrozen[i] == 0;
                sliderGhosts[i].SetActive(show);
                if (show) sliderGhosts[i].transform.localPosition = board.Pos(BoardView.SliderTile(def, s, i), 0.43f);
            }
            for (int i = 0; i < laserGhosts.Length; i++)
            {
                var ld = def.Lasers[i];
                bool lit = s.LaserLit[i] && s.LFrozen[i] == 0;
                laserGhosts[i].SetActive(lit);
                if (!lit) continue;
                int len = s.BeamLen[i];
                var dirV = BoardView.DirVec(ld.Dir);
                var start = board.At(ld.Tile, 0.3f) + dirV * 0.5f;
                laserGhosts[i].transform.localPosition = start + dirV * (len * 0.5f);
                laserGhosts[i].transform.localRotation = Quaternion.LookRotation(dirV);
                laserGhosts[i].transform.localScale = new Vector3(0.22f, 0.22f, Mathf.Max(0.01f, len));
            }
            for (int i = 0; i < rotorGhosts.Length; i++)
            {
                bool show = s.RFrozen[i] == 0;
                rotorGhosts[i].SetActive(show);
                if (show) rotorArms[i].localRotation = Quaternion.Euler(0, BoardView.RotorAngle(def, s, i), 0);
            }
            Color c;
            switch (Result)
            {
                case Verdict.Lethal: c = new Color(2.4f, 0.25f, 0.3f, 0.75f); break;
                case Verdict.Soon: c = new Color(2.2f, 1.2f, 0.2f, 0.6f); break;
                case Verdict.Charges: c = new Color(2.4f, 1.8f, 0.6f, 0.75f); break;
                default: c = new Color(0.4f, 2.2f, 1.4f, 0.55f); break;
            }
            float pulse = 0.85f + 0.15f * Mathf.Sin(time * 6f);
            markerMat.SetColor("_Color", c * pulse);
            markerMat.SetColor("_BackColor", c);
            markerMat.SetFloat("_Alpha", 0.35f * fade);
            markerRingMat.SetColor("_Color", c);
            markerRingMat.SetColor("_BackColor", c);
            markerRingMat.SetFloat("_Alpha", 0.9f * fade);
            var p = board.At(s.P, 0.03f);
            marker.transform.localPosition = p;
            markerRing.transform.localPosition = p;
            ghostMat.SetColor("_Color", new Color(2.2f, 0.35f, 0.45f, 0.5f * fade));
        }
    }
}
