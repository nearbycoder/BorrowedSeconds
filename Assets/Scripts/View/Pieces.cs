using System.Collections.Generic;
using BorrowedSeconds.Sim;
using UnityEngine;

namespace BorrowedSeconds.View
{
    public abstract class PieceView
    {
        public GameObject Root;
        protected readonly BoardView Board;
        protected LevelDef Def => Board.Def;
        protected PieceView(BoardView board) { Board = board; }
        public abstract void Render(SimState a, SimState b, float t, float dt, float time);

        protected static Material Inst(string template) => Mats.Instance(template);

        protected static void SetGlow(Material m, Color c, float intensity)
        {
            m.SetColor("_Color", new Color(c.r * intensity, c.g * intensity, c.b * intensity, c.a));
        }

        protected static float Smooth(float current, float target, float speed, float dt)
            => Mathf.Lerp(current, target, 1f - Mathf.Exp(-speed * dt));

        /// <summary>Channel marks: channel+1 pips in a row along <paramref name="axis"/>, so a plate, its
        /// gates and its linked lasers pair up even without colour.</summary>
        protected static Transform ChannelPips(Transform parent, int channel, Vector3 center, Vector3 axis, float spacing, float size, Material mat)
        {
            var g = Shapes.Group("ChannelPips", parent, center).transform;
            int n = channel + 1;
            for (int k = 0; k < n; k++) Shapes.Ball("Pip" + k, g, axis * ((k - (n - 1) * 0.5f) * spacing), size, mat, false);
            return g;
        }

        protected static Material ChannelGlow(int channel)
        {
            var c = Palette.Channel(channel);
            return Mats.Emissive(c * 0.6f, c * 2.2f, 0.6f);
        }
    }

    /// <summary>A brief arc of light from a pressed plate to something on its channel.</summary>
    public sealed class LinkPulse
    {
        const int Segments = 14;
        const float Life = 0.75f, Travel = 0.4f;
        readonly GameObject root, spark;
        readonly Material lineMat, sparkMat;
        readonly Color color;
        readonly Vector3[] points = new Vector3[Segments + 1];
        float age = 99f;

        public LinkPulse(Transform parent, Vector3 from, Vector3 to, Color color)
        {
            this.color = color;
            root = Shapes.Group("LinkPulse", parent);
            lineMat = Mats.Instance("BS_GlowAdd");
            sparkMat = Mats.Instance("BS_GlowAdd");
            float lift = 0.45f + 0.1f * Vector3.Distance(from, to);
            for (int k = 0; k <= Segments; k++)
            {
                float s = k / (float)Segments;
                points[k] = Vector3.Lerp(from, to, s) + Vector3.up * (Mathf.Sin(Mathf.PI * s) * lift);
            }
            for (int k = 0; k < Segments; k++)
            {
                var a = points[k];
                var b = points[k + 1];
                var seg = Shapes.Box("Seg" + k, root.transform, (a + b) * 0.5f, new Vector3(0.025f, 0.025f, Vector3.Distance(a, b) + 0.01f), lineMat, false);
                seg.transform.localRotation = Quaternion.LookRotation(b - a);
            }
            spark = Shapes.Ball("Spark", root.transform, from, 0.12f, sparkMat, false);
            root.SetActive(false);
        }

        public void Fire() => age = 0f;

        public void Render(float dt)
        {
            age += dt;
            bool on = age < Life;
            if (root.activeSelf != on) root.SetActive(on);
            if (!on) return;
            float fade = 1f - age / Life;
            Glow(lineMat, 2.2f * fade * fade);
            float s = Mathf.Clamp01(age / Travel);
            spark.SetActive(s < 1f);
            float f = s * Segments;
            int k = Mathf.Min(Segments - 1, (int)f);
            spark.transform.localPosition = Vector3.Lerp(points[k], points[k + 1], f - k);
            Glow(sparkMat, 6f * (1f - s * 0.5f));
        }

        void Glow(Material m, float intensity) => m.SetColor("_Color", new Color(color.r * intensity, color.g * intensity, color.b * intensity, color.a));
    }

    /// <summary>The crystal shell + floor timer ring shown on anything frozen.</summary>
    public sealed class FrozenShell
    {
        readonly GameObject shell, ring;
        readonly Material shellMat, ringMat;
        float show;   // 0..1 animated presence
        float pop;
        readonly Vector3 baseScale;

        /// <param name="model">Optional faceted crystal model, sitting on z = 0 with footprint <paramref name="modelSize"/>.</param>
        public FrozenShell(Transform parent, Vector3 center, Vector3 size, float ringSize, string model = "CrystalBox", Vector3 modelSize = default)
        {
            shellMat = Mats.Instance("BS_Crystal");
            shell = Shapes.Group("Crystal", parent, center);
            var m = model != null ? Shapes.Model(model, shell.transform, (_, __) => shellMat, false) : null;
            if (m != null)
            {
                if (modelSize == default) modelSize = Vector3.one;
                m.transform.localScale = new Vector3(1f / modelSize.x, 1f / modelSize.y, 1f / modelSize.z);
                m.transform.localPosition = new Vector3(0, -0.5f, 0);
            }
            else Shapes.Box("Box", shell.transform, Vector3.zero, Vector3.one, shellMat, false);
            baseScale = size;
            ringMat = Mats.Instance("BS_Ring");
            ring = Shapes.Flat("FrozenRing", parent, new Vector3(0, 0.025f, 0), ringSize, ringMat);
            shell.SetActive(false);
            ring.SetActive(false);
        }

        public void Render(bool frozen, float remaining01, float dt)
        {
            if (frozen && show < 0.01f) pop = 1f;
            show = Mathf.MoveTowards(show, frozen ? 1f : 0f, dt * (frozen ? 9f : 6f));
            pop = Mathf.MoveTowards(pop, 0f, dt * 4f);
            bool vis = show > 0.001f;
            shell.SetActive(vis);
            ring.SetActive(vis && frozen);
            if (!vis) return;
            float s = Mathf.Lerp(0.6f, 1f, EaseOutBack(show)) * (1f + pop * 0.08f);
            shell.transform.localScale = baseScale * s;
            shellMat.SetFloat("_Fade", show);
            ringMat.SetFloat("_Fill", remaining01);
            ringMat.SetFloat("_Alpha", show);
        }

        static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }
    }

    // ==================================================================== player

    public sealed class PlayerView : PieceView
    {
        readonly Transform body, pivot;
        readonly Material ringMat, countdownMat, bodyMat;
        readonly GameObject countdown;
        readonly FrozenShell shell;
        readonly Renderer[] bodyRenderers;
        float yaw, squash, targetYaw;
        public bool Hidden;

        public PlayerView(BoardView board) : base(board)
        {
            Root = Shapes.Group("Player", board.transform);
            pivot = Shapes.Group("Pivot", Root.transform).transform;
            body = Shapes.Group("Body", pivot).transform;
            var porcelain = Mats.Lit(Palette.Porcelain, 0.75f);
            bodyMat = porcelain;
            ringMat = Mats.Emissive(Palette.Amber, Palette.Amber * 2.4f, 0.6f);
            var model = Shapes.Model("Player", body);
            if (model == null)
            {
                Shapes.Cyl("Base", body, new Vector3(0, 0.05f, 0), 0.5f, 0.1f, porcelain);
                Shapes.Make("Torso", body, Shapes.MeshOf(PrimitiveType.Capsule), porcelain, new Vector3(0, 0.38f, 0), new Vector3(0.42f, 0.3f, 0.42f));
                Shapes.Cyl("Core", body, new Vector3(0, 0.36f, 0), 0.47f, 0.06f, ringMat);
                Shapes.Ball("Head", body, new Vector3(0, 0.8f, 0), 0.38f, porcelain);
                var eye = Mats.Lit(Palette.Ink, 0.9f);
                Shapes.Ball("EyeL", body, new Vector3(-0.075f, 0.83f, 0.16f), 0.065f, eye);
                Shapes.Ball("EyeR", body, new Vector3(0.075f, 0.83f, 0.16f), 0.065f, eye);
            }
            bodyRenderers = body.GetComponentsInChildren<Renderer>();
            shell = new FrozenShell(Root.transform, new Vector3(0, 0.52f, 0), new Vector3(0.8f, 1.12f, 0.8f), 1.15f, "CrystalPawn", new Vector3(0.72f, 1.08f, 0.72f));
            countdownMat = Mats.Instance("BS_Ring");
            countdownMat.SetColor("_Color", Palette.Ice * 2.2f);
            countdown = Shapes.Flat("Countdown", Root.transform, new Vector3(0, 1.45f, 0), 0.62f, countdownMat);
            countdown.SetActive(false);
        }

        public Vector3 WorldPos => Root.transform.position;

        public override void Render(SimState a, SimState b, float t, float dt, float time)
        {
            var pa = BoardView.PlayerTile(Def, a);
            var pb = BoardView.PlayerTile(Def, b);
            var p = Vector2.Lerp(pa, pb, t);
            Vector2 from = b.Moving ? BoardView.TileOf(Def, b.F) : a.Moving ? BoardView.TileOf(Def, a.F) : pb;
            float u = Mathf.Clamp01(Vector2.Distance(p, from));
            if (!b.Moving && !a.Moving) u = 0f;
            float hop = Mathf.Sin(u * Mathf.PI) * 0.16f;
            if (a.Moving && !b.Moving && t < 0.2f) squash = 1f;
            squash = Mathf.MoveTowards(squash, 0f, dt * 6f);

            Root.transform.localPosition = Board.Pos(p);
            if (b.Moving || a.Moving) targetYaw = b.MoveDir * 90f;
            yaw = Mathf.LerpAngle(yaw, targetYaw, 1f - Mathf.Exp(-22f * dt));

            bool frozen = b.PFrozen > 0 && !b.Dead;
            float breathe = frozen ? 0f : Mathf.Sin(time * 3.1f) * 0.02f;
            float sq = squash * 0.18f;
            float stretch = (b.Moving ? 0.06f : 0f);
            pivot.localPosition = new Vector3(0, hop, 0);
            pivot.localRotation = Quaternion.Euler(0, yaw, 0);
            body.localScale = new Vector3(1f + sq, 1f - sq + breathe + stretch, 1f + sq);

            shell.Render(frozen, b.PFrozen / (float)Rules.FreezeTicks, dt);
            foreach (var r in bodyRenderers) r.enabled = !Hidden && !b.Dead;

            bool showCountdown = !b.Dead && !frozen && (b.Countdown > 0 || b.Pending);
            countdown.SetActive(showCountdown);
            if (showCountdown)
            {
                float frac = b.Countdown / (float)Def.Term;
                countdownMat.SetFloat("_Fill", Mathf.Clamp01(frac));
                bool urgent = b.Countdown <= Rules.TicksPerSecond;
                float pulse = urgent ? 1f + 0.18f * Mathf.Abs(Mathf.Sin(time * 14f)) : 1f;
                countdown.transform.localScale = new Vector3(0.62f * pulse, 1, 0.62f * pulse);
                countdownMat.SetColor("_Color", (urgent ? Color.Lerp(Palette.Ice, Palette.Danger, 0.5f) : Palette.Ice) * 2.2f);
                var cam = Camera.main;
                if (cam != null) countdown.transform.rotation = Quaternion.LookRotation(cam.transform.up, -cam.transform.forward);
            }
        }
    }

    // ==================================================================== slider

    public sealed class SliderView : PieceView
    {
        readonly int index;
        readonly Transform chevron;
        readonly FrozenShell shell;
        readonly GameObject ghost;
        readonly Material glowMat;
        public bool Highlight;
        float hl;

        public SliderView(BoardView board, int i) : base(board)
        {
            index = i;
            Root = Shapes.Group("Slider" + i, board.transform);
            var graphite = Mats.Lit(Palette.Graphite, 0.45f, 0.2f);
            glowMat = Mats.Emissive(Palette.Coral, Palette.Coral * 3f, 0.5f);
            var model = Shapes.Model("Slider", Root.transform);
            if (model == null)
            {
                Shapes.Box("Body", Root.transform, new Vector3(0, 0.42f, 0), new Vector3(0.84f, 0.84f, 0.84f), graphite);
                for (int k = 0; k < 4; k++)
                {
                    var dir = BoardView.DirVec(k);
                    var side = new Vector3(Mathf.Abs(dir.z) > 0 ? 0.86f : 0.05f, 0.07f, Mathf.Abs(dir.x) > 0 ? 0.86f : 0.05f);
                    Shapes.Box("Strip", Root.transform, dir * 0.42f + new Vector3(0, 0.12f, 0), side, glowMat, false);
                }
            }
            chevron = Shapes.Group("Chevron", Root.transform, new Vector3(0, 0.86f, 0)).transform;
            Shapes.Box("ChevA", chevron, new Vector3(-0.09f, 0, 0.0f), new Vector3(0.07f, 0.04f, 0.3f), glowMat, false).transform.localRotation = Quaternion.Euler(0, 40, 0);
            Shapes.Box("ChevB", chevron, new Vector3(0.09f, 0, 0.0f), new Vector3(0.07f, 0.04f, 0.3f), glowMat, false).transform.localRotation = Quaternion.Euler(0, -40, 0);
            shell = new FrozenShell(Root.transform, new Vector3(0, 0.47f, 0), new Vector3(0.98f, 0.98f, 0.98f), 1.2f);
            var gm = Mats.Instance("BS_Ghost");
            ghost = Shapes.Box("Hover", Root.transform, new Vector3(0, 0.47f, 0), new Vector3(1.04f, 1.04f, 1.04f), gm, false);
            ghost.SetActive(false);
            var col = Root.AddComponent<BoxCollider>();
            col.center = new Vector3(0, 0.5f, 0);
            col.size = new Vector3(1.05f, 1.1f, 1.05f);
            Root.AddComponent<ObstaclePick>().Index = board.Def.ObstacleIndex(LevelDef.Kind.Slider, i);
        }

        public override void Render(SimState a, SimState b, float t, float dt, float time)
        {
            var p = Vector2.Lerp(BoardView.SliderTile(Def, a, index), BoardView.SliderTile(Def, b, index), t);
            Root.transform.localPosition = Board.Pos(p);
            var sd = Def.Sliders[index];
            int n = Simulation.SliderNext(sd, b.SIdx[index], b.SDir[index]);
            if (n < 0) n = Simulation.SliderNext(sd, b.SIdx[index], -b.SDir[index]);
            if (n >= 0)
            {
                var d = BoardView.TileOf(Def, sd.Path[n]) - BoardView.TileOf(Def, sd.Path[b.SIdx[index]]);
                if (b.SDwell[index] > 0 || (Simulation.SliderNext(sd, b.SIdx[index], b.SDir[index]) < 0)) d = -d;
                float ang = Mathf.Atan2(d.x, -d.y) * Mathf.Rad2Deg;
                chevron.localRotation = Quaternion.Slerp(chevron.localRotation, Quaternion.Euler(0, ang, 0), 1f - Mathf.Exp(-20f * dt));
            }
            bool frozen = b.SFrozen[index] > 0;
            chevron.gameObject.SetActive(!frozen);
            shell.Render(frozen, b.SFrozen[index] / (float)Rules.FreezeTicks, dt);
            hl = Smooth(hl, Highlight && !frozen ? 1f : 0f, 18f, dt);
            ghost.SetActive(hl > 0.02f);
            if (hl > 0.02f) ghost.transform.localScale = Vector3.one * (1.0f + 0.06f * hl + 0.02f * Mathf.Sin(time * 8f));
        }
    }

    // ==================================================================== laser

    public sealed class LaserView : PieceView
    {
        readonly int index;
        readonly GameObject beam, warn, bar, ghost;
        readonly Material beamMat, warnMat, barMat;
        readonly BoxCollider beamCol;
        readonly FrozenShell shell;
        readonly Vector3 dir;
        public bool Highlight;
        float hl;

        public LaserView(BoardView board, int i) : base(board)
        {
            index = i;
            var ld = board.Def.Lasers[i];
            dir = BoardView.DirVec(ld.Dir);
            Root = Shapes.Group("Laser" + i, board.transform, board.At(ld.Tile));
            Root.transform.localRotation = Quaternion.LookRotation(dir);
            var graphite = Mats.Lit(Palette.Graphite, 0.45f, 0.2f);
            var lens = Mats.Emissive(Palette.Coral, Palette.Coral * 4f, 0.8f);
            bool onWall = board.Def.Tiles[ld.Tile] == Tile.Wall;
            float baseH = onWall ? 0.6f : 0f;
            var model = Shapes.Model("Laser", Root.transform);
            if (model == null)
            {
                Shapes.Box("Housing", Root.transform, new Vector3(0, baseH + 0.2f, 0.05f), new Vector3(0.62f, 0.4f, 0.7f), graphite);
                Shapes.Box("Lens", Root.transform, new Vector3(0, baseH + 0.2f, 0.42f), new Vector3(0.28f, 0.2f, 0.06f), lens, false);
            }
            else model.transform.localPosition = new Vector3(0, baseH, 0);
            beamMat = Inst("BS_GlowAdd");
            warnMat = Inst("BS_GlowAdd");
            barMat = Inst("BS_Crystal");
            var beamRoot = Shapes.Group("BeamRoot", Root.transform, new Vector3(0, 0.3f, 0.5f)).transform;
            beam = Shapes.Box("Beam", beamRoot, Vector3.zero, Vector3.one, beamMat, false);
            warn = Shapes.Box("Warn", beamRoot, Vector3.zero, Vector3.one, warnMat, false);
            bar = Shapes.Box("FrozenBar", beamRoot, Vector3.zero, Vector3.one, barMat, false);
            ghost = Shapes.Box("Hover", Root.transform, new Vector3(0, baseH + 0.2f, 0.05f), new Vector3(0.8f, 0.6f, 0.9f), Inst("BS_Ghost"), false);
            ghost.SetActive(false);
            shell = new FrozenShell(Root.transform, new Vector3(0, baseH + 0.22f, 0.05f), new Vector3(0.8f, 0.6f, 0.86f), 0f);
            // a laser a plate switches off wears that plate's marks
            if (ld.Plate >= 0) ChannelPips(Root.transform, ld.Plate, new Vector3(0, baseH + 0.44f, -0.04f), Vector3.right, 0.1f, 0.075f, ChannelGlow(ld.Plate));
            var col = Root.AddComponent<BoxCollider>();
            col.center = new Vector3(0, baseH * 0.5f + 0.3f, 0);
            col.size = new Vector3(1f, baseH + 0.7f, 1f);
            Root.AddComponent<ObstaclePick>().Index = board.Def.ObstacleIndex(LevelDef.Kind.Laser, i);
            var bc = new GameObject("BeamPick");
            bc.transform.SetParent(beamRoot, false);
            beamCol = bc.AddComponent<BoxCollider>();
            bc.AddComponent<ObstaclePick>().Index = board.Def.ObstacleIndex(LevelDef.Kind.Laser, i);
        }

        static void Span(GameObject g, float len, float w, float h)
        {
            g.transform.localScale = new Vector3(w, h, Mathf.Max(0.001f, len));
            g.transform.localPosition = new Vector3(0, 0, len * 0.5f);
        }

        public override void Render(SimState a, SimState b, float t, float dt, float time)
        {
            var ld = Def.Lasers[index];
            bool frozen = b.LFrozen[index] > 0;
            int len = b.BeamLen[index];
            bool lit = b.LaserLit[index];
            float beamLen = len;
            Span(beam, beamLen, 0.11f, 0.11f);
            beam.SetActive(lit && !frozen);
            if (lit && !frozen)
            {
                float flick = 1f + 0.15f * Mathf.Sin(time * 50f);
                SetGlow(beamMat, Palette.Coral, 5f * flick);
                beam.transform.localScale = new Vector3(0.11f * flick, 0.11f * flick, Mathf.Max(0.001f, beamLen));
            }
            int toFire = Simulation.LaserTicksToFire(ld, b.LClock[index]);
            bool warning = !frozen && !lit && !b.LaserDisabled[index] && toFire > 0 && toFire <= Rules.LaserWarnTicks;
            warn.SetActive(warning);
            if (warning)
            {
                Span(warn, beamLen, 0.035f, 0.035f);
                float k = 1f - toFire / (float)Rules.LaserWarnTicks;
                SetGlow(warnMat, new Color(Palette.Coral.r, Palette.Coral.g, Palette.Coral.b, 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(time * 40f))), 1.5f + 3f * k);
            }
            bool frozenLit = frozen && b.LFrozenLit[index];
            bar.SetActive(frozenLit);
            if (frozenLit) Span(bar, b.LFrozenLen[index], 0.3f, 0.36f);
            float barLen = frozenLit ? b.LFrozenLen[index] : (lit ? len : 0);
            beamCol.enabled = barLen > 0;
            beamCol.size = new Vector3(0.5f, 0.6f, Mathf.Max(0.01f, barLen));
            beamCol.center = new Vector3(0, 0, barLen * 0.5f);
            shell.Render(frozen, b.LFrozen[index] / (float)Rules.FreezeTicks, dt);
            hl = Smooth(hl, Highlight && !frozen ? 1f : 0f, 18f, dt);
            ghost.SetActive(hl > 0.02f);
        }
    }

    // ==================================================================== rotor

    public sealed class RotorView : PieceView
    {
        readonly int index;
        readonly Transform arms;
        readonly GameObject[] crystals;
        readonly GameObject ghost;
        readonly FrozenShell hubShell;
        public bool Highlight;
        float hl;
        Material[] crystalMats;
        float frozenShow;

        public RotorView(BoardView board, int i) : base(board)
        {
            index = i;
            var rd = board.Def.Rotors[i];
            Root = Shapes.Group("Rotor" + i, board.transform, board.At(rd.Tile));
            var graphite = Mats.Lit(Palette.Graphite, 0.45f, 0.2f);
            var glow = Mats.Emissive(Palette.Coral, Palette.Coral * 3f, 0.6f);
            if (Shapes.Model("RotorHub", Root.transform) == null)
            {
                Shapes.Cyl("Hub", Root.transform, new Vector3(0, 0.36f, 0), 0.64f, 0.72f, graphite);
                Shapes.Cyl("HubRing", Root.transform, new Vector3(0, 0.56f, 0), 0.68f, 0.07f, glow, false);
            }
            arms = Shapes.Group("Arms", Root.transform, new Vector3(0, 0.46f, 0)).transform;
            int count = 0;
            for (int d = 0; d < 4; d++) if ((rd.ArmMask & (1 << d)) != 0) count++;
            crystals = new GameObject[count];
            crystalMats = new Material[count];
            int k = 0;
            float len = rd.Length + 0.3f;
            for (int d = 0; d < 4; d++)
            {
                if ((rd.ArmMask & (1 << d)) == 0) continue;
                var armRoot = Shapes.Group("Arm" + d, arms).transform;
                armRoot.localRotation = Quaternion.Euler(0, d * 90f, 0);
                if (Shapes.Model("RotorArm" + rd.Length, armRoot) == null)
                {
                    Shapes.Box("Bar", armRoot, new Vector3(0, 0, len * 0.5f + 0.1f), new Vector3(0.2f, 0.22f, len), graphite);
                    Shapes.Box("Edge", armRoot, new Vector3(0, 0.12f, len * 0.5f + 0.1f), new Vector3(0.08f, 0.03f, len - 0.1f), glow, false);
                    Shapes.Box("Tip", armRoot, new Vector3(0, 0, len + 0.12f), new Vector3(0.24f, 0.26f, 0.1f), glow, false);
                }
                crystalMats[k] = Inst("BS_Crystal");
                crystals[k] = Shapes.Box("Crystal", armRoot, new Vector3(0, 0, len * 0.5f + 0.1f), new Vector3(0.42f, 0.44f, len + 0.25f), crystalMats[k], false);
                crystals[k].SetActive(false);
                var col = armRoot.gameObject.AddComponent<BoxCollider>();
                col.center = new Vector3(0, 0, len * 0.5f + 0.1f);
                col.size = new Vector3(0.6f, 0.7f, len + 0.2f);
                armRoot.gameObject.AddComponent<ObstaclePick>().Index = board.Def.ObstacleIndex(LevelDef.Kind.Rotor, i);
                k++;
            }
            hubShell = new FrozenShell(Root.transform, new Vector3(0, 0.4f, 0), new Vector3(0.86f, 0.9f, 0.86f), 1.25f);
            ghost = Shapes.Cyl("Hover", Root.transform, new Vector3(0, 0.4f, 0), 0.9f, 0.95f, Inst("BS_Ghost"), false);
            ghost.SetActive(false);
            var hc = Root.AddComponent<CapsuleCollider>();
            hc.center = new Vector3(0, 0.4f, 0);
            hc.radius = 0.45f;
            hc.height = 1f;
            Root.AddComponent<ObstaclePick>().Index = board.Def.ObstacleIndex(LevelDef.Kind.Rotor, i);
        }

        public override void Render(SimState a, SimState b, float t, float dt, float time)
        {
            float aa = BoardView.RotorAngle(Def, a, index), ab = BoardView.RotorAngle(Def, b, index);
            float ang = aa + Mathf.DeltaAngle(aa, ab) * t;
            arms.localRotation = Quaternion.Euler(0, ang, 0);
            bool frozen = b.RFrozen[index] > 0;
            frozenShow = Mathf.MoveTowards(frozenShow, frozen ? 1f : 0f, dt * 8f);
            for (int k = 0; k < crystals.Length; k++)
            {
                crystals[k].SetActive(frozenShow > 0.01f);
                crystalMats[k].SetFloat("_Fade", frozenShow);
            }
            hubShell.Render(frozen, b.RFrozen[index] / (float)Rules.FreezeTicks, dt);
            hl = Smooth(hl, Highlight && !frozen ? 1f : 0f, 18f, dt);
            ghost.SetActive(hl > 0.02f);
        }
    }

    // ==================================================================== devices

    public sealed class PlateView : PieceView
    {
        readonly int index;
        readonly Material mat;
        readonly Transform pad;
        float press;

        public PlateView(BoardView board, int i) : base(board)
        {
            index = i;
            var pd = board.Def.Plates[i];
            Root = Shapes.Group("Plate" + i, board.transform, board.At(pd.Tile));
            color = Palette.Channel(pd.Channel);
            mat = new Material(Mats.Emissive(color * 0.6f, color * 0.4f, 0.6f));
            var model = Shapes.Model("Plate", Root.transform, (part, m) => m == "Pad" ? mat : null);
            if (model != null)
            {
                pad = Shapes.Part(model, "Pad");
                padUp = pad.localPosition.y;
            }
            else
            {
                Shapes.Box("Rim", Root.transform, new Vector3(0, 0.005f, 0), new Vector3(0.86f, 0.02f, 0.86f), Mats.Lit(Palette.Brass, 0.6f, 0.6f));
                pad = Shapes.Box("Pad", Root.transform, new Vector3(0, 0.04f, 0), new Vector3(0.72f, 0.06f, 0.72f), mat).transform;
                padUp = 0.04f;
            }
            pips = ChannelPips(Root.transform, pd.Channel, new Vector3(0, 0.05f, 0), Vector3.right, 0.16f, 0.1f, Mats.Lit(Palette.Ink, 0.5f));
        }

        readonly float padUp;
        readonly Color color;
        readonly Transform pips;

        public override void Render(SimState a, SimState b, float t, float dt, float time)
        {
            bool pressed = b.PlatePressed[index];
            press = Smooth(press, pressed ? 1f : 0f, 20f, dt);
            var lp = pad.localPosition;
            pad.localPosition = new Vector3(lp.x, padUp - 0.028f * press, lp.z);
            pips.localPosition = new Vector3(0, 0.05f - 0.028f * press, 0);
            Mats.SetEmission(mat, color * Mathf.Lerp(0.35f, 2.6f, press));
        }
    }

    public sealed class GateView : PieceView
    {
        readonly int index;
        readonly Transform slab;
        readonly Material mat;
        float open;

        public GateView(BoardView board, int i) : base(board)
        {
            index = i;
            var gd = board.Def.Gates[i];
            Root = Shapes.Group("Gate" + i, board.transform, board.At(gd.Tile));
            // orient across the corridor: if walls are east/west the slab spans east-west
            int e = board.Def.Neighbor(gd.Tile, Dirs.E), w = board.Def.Neighbor(gd.Tile, Dirs.W);
            bool ewWalls = (e < 0 || board.Def.Tiles[e] != Tile.Floor) && (w < 0 || board.Def.Tiles[w] != Tile.Floor);
            Root.transform.localRotation = Quaternion.Euler(0, ewWalls ? 0 : 90, 0);
            color = Palette.Channel(gd.Channel);
            mat = new Material(Mats.Emissive(color * 0.5f, color * 0.8f, 0.7f));
            var slabModel = Shapes.Model("GateSlab", Root.transform, (part, m) => m == "MintGlass" ? mat : null);
            if (slabModel != null && Shapes.Model("GatePosts", Root.transform) != null)
            {
                slab = slabModel.transform;
                slabUp = 0f;
            }
            else
            {
                if (slabModel != null) Object.Destroy(slabModel);
                var brass = Mats.Lit(Palette.Brass, 0.6f, 0.7f);
                Shapes.Box("PostL", Root.transform, new Vector3(-0.47f, 0.4f, 0), new Vector3(0.08f, 0.8f, 0.28f), brass);
                Shapes.Box("PostR", Root.transform, new Vector3(0.47f, 0.4f, 0), new Vector3(0.08f, 0.8f, 0.28f), brass);
                slab = Shapes.Box("Slab", Root.transform, new Vector3(0, 0.38f, 0), new Vector3(0.86f, 0.76f, 0.16f), mat).transform;
                slabUp = 0.38f;
            }
            // dark dots on the glowing caps, matching the dots on the plate
            var ink = Mats.Lit(Palette.Ink, 0.5f);
            foreach (float x in new[] { -0.47f, 0.47f })
                ChannelPips(Root.transform, gd.Channel, new Vector3(x, 0.91f, 0), Vector3.forward, 0.1f, 0.085f, ink);
        }

        readonly float slabUp;
        readonly Color color;

        public override void Render(SimState a, SimState b, float t, float dt, float time)
        {
            bool isOpen = b.GateOpen[index];
            open = Smooth(open, isOpen ? 1f : 0f, 14f, dt);
            slab.localPosition = new Vector3(0, slabUp - 0.8f * open, 0);
            Mats.SetEmission(mat, color * Mathf.Lerp(0.8f, 0.2f, open));
        }
    }

    public sealed class LockView : PieceView
    {
        readonly int index;
        readonly Material[] segMats = new Material[12];
        readonly Material gemMat;
        readonly Transform gem;
        float latchPop;

        public LockView(BoardView board, int i) : base(board)
        {
            index = i;
            Root = Shapes.Group("Lock" + i, board.transform, board.At(board.Def.Locks[i]));
            gemMat = new Material(Mats.Emissive(Palette.Gold * 0.5f, Color.black, 0.9f));
            for (int k = 0; k < 12; k++) segMats[k] = new Material(Mats.Emissive(Palette.Gold * 0.4f, Color.black, 0.7f));
            var segs = new List<Renderer>();
            var model = Shapes.Model("Lock", Root.transform, (part, m) => part == "Gem" ? gemMat : null);
            if (model != null)
            {
                foreach (var r in model.GetComponentsInChildren<Renderer>())
                    if (r.name.StartsWith("Seg")) segs.Add(r);
                // fill clockwise from twelve o'clock as seen by the camera, whatever the export's handedness
                segs.Sort((p, q) => SegAngle(p).CompareTo(SegAngle(q)));
                for (int k = 0; k < segs.Count && k < 12; k++) segs[k].sharedMaterial = segMats[k];
                gem = Shapes.Part(model, "Gem");
                gemScale = Vector3.one;
                return;
            }
            Shapes.Cyl("Dial", Root.transform, new Vector3(0, 0.025f, 0), 0.86f, 0.05f, Mats.Lit(Palette.Brass * 0.75f, 0.6f, 0.8f));
            Shapes.Cyl("Face", Root.transform, new Vector3(0, 0.05f, 0), 0.7f, 0.02f, Mats.Lit(Palette.Ink, 0.7f));
            for (int k = 0; k < 12; k++)
            {
                var segRoot = Shapes.Group("Seg" + k, Root.transform, new Vector3(0, 0.065f, 0)).transform;
                segRoot.localRotation = Quaternion.Euler(0, k * 30f + 15f, 0);
                Shapes.Box("S", segRoot, new Vector3(0, 0, 0.27f), new Vector3(0.11f, 0.03f, 0.1f), segMats[k], false);
            }
            gem = Shapes.Cyl("Gem", Root.transform, new Vector3(0, 0.08f, 0), 0.22f, 0.05f, gemMat).transform;
            gemScale = new Vector3(0.22f, 0.025f, 0.22f);
        }

        readonly Vector3 gemScale;

        float SegAngle(Renderer r)
        {
            var c = Root.transform.InverseTransformPoint(r.bounds.center);
            float a = Mathf.Atan2(c.x, c.z) * Mathf.Rad2Deg;
            return a < 0 ? a + 360f : a;
        }

        public override void Render(SimState a, SimState b, float t, float dt, float time)
        {
            int c = b.LockCharge[index];
            bool latched = c >= Rules.LockTicks;
            float chargeF = Mathf.Lerp(a.LockCharge[index], c, t) / Rules.LockTicks * 12f;
            for (int k = 0; k < 12; k++)
            {
                float lit = latched ? 1f : Mathf.Clamp01(chargeF - k);
                Mats.SetEmission(segMats[k], Palette.Gold * (latched ? 3.5f : lit * 3f));
            }
            if (latched && a.LockCharge[index] < Rules.LockTicks) latchPop = 1f;
            latchPop = Mathf.MoveTowards(latchPop, 0f, dt * 2f);
            Mats.SetEmission(gemMat, Palette.Gold * (latched ? 4f + 6f * latchPop : 0.2f + 0.1f * Mathf.Sin(time * 3f)));
            gem.localScale = gemScale * (1f + latchPop * 0.6f);
            if (latched) gem.localRotation = Quaternion.Euler(0, time * 40f, 0);
        }
    }

    public sealed class ExitView : PieceView
    {
        readonly Material baseMat, beamMat;
        readonly GameObject beam;
        readonly Transform ring, hourglass;
        float open, flip;

        public ExitView(BoardView board) : base(board)
        {
            Root = Shapes.Group("Exit", board.transform, board.At(board.Def.Exit));
            baseMat = new Material(Mats.Emissive(Palette.Gold, Palette.Gold * 0.5f, 0.8f));
            if (Shapes.Model("Exit", Root.transform, (part, m) => part == "Disc" ? baseMat : null) == null)
            {
                Shapes.Cyl("Base", Root.transform, new Vector3(0, 0.03f, 0), 0.86f, 0.06f, Mats.Lit(Palette.Brass, 0.7f, 0.8f));
                Shapes.Cyl("Disc", Root.transform, new Vector3(0, 0.065f, 0), 0.66f, 0.02f, baseMat);
            }
            ring = Shapes.Group("Ring", Root.transform, new Vector3(0, 0.6f, 0)).transform;
            var glass = Shapes.Model("Hourglass", ring, null, false);
            if (glass != null)
            {
                hourglass = glass.transform;
                hourglass.localPosition = new Vector3(0, -0.2f, 0);
                hourglass.localScale = Vector3.one * 0.85f;
            }
            var ringMat = Mats.Emissive(Palette.Gold, Palette.Gold * 2f, 0.8f);
            for (int k = 0; k < 8; k++)
            {
                var r = Shapes.Group("R" + k, ring).transform;
                r.localRotation = Quaternion.Euler(0, k * 45f, 0);
                Shapes.Box("Seg", r, new Vector3(0, 0, 0.36f), new Vector3(0.2f, 0.04f, 0.05f), ringMat, false);
            }
            beamMat = Inst("BS_GlowAdd");
            beamMat.SetFloat("_FadeV", 2.2f);
            beam = Shapes.Make("Light", Root.transform, Shapes.OpenTube, beamMat, new Vector3(0, 0.05f, 0), new Vector3(0.5f, 3.2f, 0.5f), false);
        }

        public override void Render(SimState a, SimState b, float t, float dt, float time)
        {
            open = Smooth(open, b.ExitOpen ? 1f : 0f, 6f, dt);
            ring.localRotation = Quaternion.Euler(0, time * Mathf.Lerp(15f, 70f, open), 0);
            if (hourglass != null)
            {
                // the hourglass flips over once the vault unseals
                flip = Mathf.MoveTowards(flip, open > 0.5f ? 1f : 0f, dt * 1.6f);
                float e = flip * flip * (3f - 2f * flip);
                hourglass.localRotation = Quaternion.Euler(0, 0, e * 180f);
                hourglass.localPosition = new Vector3(0, -0.2f + e * 0.42f, 0);
            }
            ring.localPosition = new Vector3(0, 0.45f + 0.08f * Mathf.Sin(time * 2f) + open * 0.2f, 0);
            Mats.SetEmission(baseMat, Palette.Gold * Mathf.Lerp(0.25f, 2.8f, open));
            beam.SetActive(open > 0.02f);
            SetGlow(beamMat, new Color(Palette.Gold.r, Palette.Gold.g, Palette.Gold.b, (0.22f + 0.05f * Mathf.Sin(time * 3f)) * open), 1.6f);
            ring.gameObject.SetActive(true);
        }
    }
}
