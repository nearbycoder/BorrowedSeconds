using System.Collections.Generic;
using UnityEngine;

namespace BorrowedSeconds.View
{
    /// <summary>Particles, floor ripples and the borrow tether, all built in code.</summary>
    public sealed class Fx : MonoBehaviour
    {
        ParticleSystem dots, shards, rings, sparkles, chunks;
        LineRenderer tether;
        Material tetherMat;
        float tetherT = 1f;
        Vector3 tetherFrom, tetherTo;
        readonly List<RippleFx> ripples = new List<RippleFx>();

        sealed class RippleFx
        {
            public GameObject Go;
            public Material Mat;
            public float T = 1f, Duration, From, To;
            public Color Color;
        }

        public void Build()
        {
            dots = MakeSystem("Dots", 0, 0.35f, ParticleSystemRenderMode.Billboard);
            shards = MakeSystem("Shards", 1, 1.4f, ParticleSystemRenderMode.Billboard);
            rings = MakeSystem("Rings", 2, 0f, ParticleSystemRenderMode.Billboard);
            sparkles = MakeSystem("Sparkles", 3, -0.15f, ParticleSystemRenderMode.Billboard);
            chunks = MakeSystem("Chunks", 0, 1.6f, ParticleSystemRenderMode.Mesh);
            var cr = chunks.GetComponent<ParticleSystemRenderer>();
            cr.mesh = ShardMesh();
            cr.material = Mats.Shared("BS_Crystal");
            var main = chunks.main;
            main.startRotation3D = true;
            var rot = chunks.rotationOverLifetime;
            rot.enabled = true;
            rot.separateAxes = true;
            rot.x = new ParticleSystem.MinMaxCurve(-6f, 6f);
            rot.y = new ParticleSystem.MinMaxCurve(-6f, 6f);
            rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);

            var tg = new GameObject("Tether");
            tg.transform.SetParent(transform, false);
            tether = tg.AddComponent<LineRenderer>();
            tetherMat = Mats.Instance("BS_GlowAdd");
            tether.sharedMaterial = tetherMat;
            tether.positionCount = 16;
            tether.useWorldSpace = true;
            tether.numCapVertices = 4;
            tether.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tether.enabled = false;
        }

        ParticleSystem MakeSystem(string name, int shape, float gravity, ParticleSystemRenderMode mode)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 3000;
            main.gravityModifier = gravity;
            main.useUnscaledTime = true;
            var em = ps.emission;
            em.enabled = false;
            var sh = ps.shape;
            sh.enabled = false;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, shape == 2 ? AnimationCurve.EaseInOut(0, 0.3f, 1, 1.6f) : AnimationCurve.EaseInOut(0, 1f, 1, 0.1f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = mode;
            if (mode == ParticleSystemRenderMode.Billboard)
            {
                var m = Mats.Instance("BS_Particle");
                m.SetFloat("_Shape", shape);
                m.SetFloat("_Intensity", 2.2f);
                r.sharedMaterial = m;
            }
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        static Mesh ShardMesh()
        {
            var m = new Mesh { name = "Shard" };
            var v = new[]
            {
                new Vector3(0, 0.5f, 0), new Vector3(0.28f, -0.1f, 0.12f), new Vector3(-0.22f, -0.1f, 0.2f),
                new Vector3(-0.05f, -0.1f, -0.3f), new Vector3(0, -0.5f, 0),
            };
            var tris = new[] { 0, 1, 2, 0, 2, 3, 0, 3, 1, 4, 2, 1, 4, 3, 2, 4, 1, 3 };
            var verts = new List<Vector3>();
            var idx = new List<int>();
            for (int i = 0; i < tris.Length; i++) { verts.Add(v[tris[i]]); idx.Add(i); }
            m.SetVertices(verts);
            m.SetTriangles(idx, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        public void Spray(ParticleSystem ps, Vector3 pos, int count, Color color, float speed, float size, float life, float up = 0.5f, float spread = 1f)
        {
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                var dir = Random.insideUnitSphere * spread;
                dir.y = Mathf.Abs(dir.y) * 0.6f + up;
                ep.position = pos + Random.insideUnitSphere * 0.12f;
                ep.velocity = dir.normalized * speed * Random.Range(0.4f, 1.1f);
                ep.startSize = size * Random.Range(0.6f, 1.3f);
                ep.startLifetime = life * Random.Range(0.6f, 1.2f);
                ep.startColor = color;
                ep.rotation3D = new Vector3(Random.value * 360f, Random.value * 360f, Random.value * 360f);
                ps.Emit(ep, 1);
            }
        }

        public void Ripple(Vector3 pos, Color color, float from, float to, float duration)
        {
            RippleFx r = null;
            foreach (var x in ripples) if (x.T >= 1f) { r = x; break; }
            if (r == null)
            {
                r = new RippleFx { Mat = Mats.Instance("BS_Ring") };
                r.Mat.SetFloat("_Ticks", 0f);
                r.Mat.SetFloat("_Inner", 0.86f);
                r.Mat.SetFloat("_Outer", 0.98f);
                r.Mat.SetFloat("_Fill", 1f);
                r.Go = Shapes.Flat("Ripple", transform, Vector3.zero, 1f, r.Mat);
                ripples.Add(r);
            }
            r.Go.SetActive(true);
            r.Go.transform.position = pos;
            r.T = 0f;
            r.Duration = duration;
            r.From = from;
            r.To = to;
            r.Color = color;
        }

        // ---------------------------------------------------------------- named effects

        public void Borrow(Vector3 from, Vector3 to)
        {
            tetherFrom = from;
            tetherTo = to;
            tetherT = 0f;
            Spray(shards, to, 26, Palette.Ice, 3.2f, 0.22f, 0.55f, 0.3f);
            Spray(sparkles, to, 10, Palette.Ice, 1.2f, 0.4f, 0.7f, 0.6f);
            Ripple(new Vector3(to.x, 0.04f, to.z), Palette.Ice * 2f, 0.4f, 3.6f, 0.55f);
            Ripple(new Vector3(from.x, 0.04f, from.z), Palette.Ice * 1.4f, 0.2f, 1.6f, 0.35f);
        }

        public void Freeze(Vector3 at)
        {
            Spray(shards, at + Vector3.up * 0.5f, 22, Palette.Ice, 2.4f, 0.2f, 0.5f, 0.2f);
            Spray(sparkles, at + Vector3.up * 0.6f, 8, Color.white, 0.8f, 0.35f, 0.9f, 0.8f);
            Ripple(new Vector3(at.x, 0.04f, at.z), Palette.Ice * 2.2f, 1.6f, 0.4f, 0.4f);
        }

        public void Thaw(Vector3 at)
        {
            Spray(chunks, at + Vector3.up * 0.55f, 16, Color.white, 3.4f, 0.22f, 0.9f, 0.9f);
            Spray(shards, at + Vector3.up * 0.5f, 18, Palette.Ice, 3.8f, 0.18f, 0.5f, 0.5f);
            Ripple(new Vector3(at.x, 0.04f, at.z), Palette.Ice * 1.6f, 0.4f, 2.4f, 0.45f);
        }

        public void ObstacleThaw(Vector3 at)
        {
            Spray(chunks, at, 10, Color.white, 2.6f, 0.2f, 0.8f, 0.8f);
            Spray(shards, at, 10, Palette.Ice, 2.6f, 0.16f, 0.45f, 0.4f);
        }

        public void Death(Vector3 at)
        {
            Spray(chunks, at + Vector3.up * 0.5f, 22, Color.white, 4.5f, 0.2f, 1.1f, 1.0f);
            Spray(shards, at + Vector3.up * 0.5f, 40, Palette.Danger, 5f, 0.24f, 0.6f, 0.5f);
            Spray(dots, at + Vector3.up * 0.4f, 20, Palette.Porcelain, 2f, 0.5f, 0.7f, 0.6f);
            Ripple(new Vector3(at.x, 0.04f, at.z), Palette.Danger * 2f, 0.3f, 3f, 0.5f);
        }

        public void Dust(Vector3 at)
        {
            Spray(dots, at + Vector3.up * 0.05f, 4, new Color(1f, 0.95f, 0.85f, 0.35f), 0.7f, 0.28f, 0.45f, 0.25f);
        }

        public void Bump(Vector3 at)
        {
            Spray(dots, at + Vector3.up * 0.2f, 5, new Color(1f, 0.9f, 0.8f, 0.4f), 1.1f, 0.2f, 0.3f, 0.3f);
        }

        public void Plate(Vector3 at, bool down)
        {
            if (!down) return;
            Spray(sparkles, at + Vector3.up * 0.1f, 6, Palette.Mint, 1.2f, 0.25f, 0.5f, 0.8f);
            Ripple(new Vector3(at.x, 0.05f, at.z), Palette.Mint * 1.6f, 0.4f, 1.3f, 0.35f);
        }

        public void LockTick(Vector3 at)
        {
            Spray(sparkles, at + Vector3.up * 0.1f, 2, Palette.Gold, 0.8f, 0.2f, 0.4f, 1f);
        }

        public void Latch(Vector3 at)
        {
            Spray(sparkles, at + Vector3.up * 0.2f, 24, Palette.Gold, 2.4f, 0.4f, 1.0f, 1.2f);
            Spray(dots, at + Vector3.up * 0.1f, 20, Palette.Gold, 1.6f, 0.4f, 0.8f, 0.9f);
            Ripple(new Vector3(at.x, 0.05f, at.z), Palette.Gold * 2.4f, 0.4f, 4.5f, 0.7f);
        }

        public void ExitOpen(Vector3 at)
        {
            Spray(sparkles, at + Vector3.up * 0.5f, 30, Palette.Gold, 2f, 0.45f, 1.2f, 1.5f);
            Ripple(new Vector3(at.x, 0.05f, at.z), Palette.Gold * 2f, 0.4f, 3f, 0.6f);
        }

        public void Win(Vector3 at)
        {
            Spray(sparkles, at + Vector3.up * 0.6f, 50, Palette.Gold, 3.2f, 0.5f, 1.4f, 2f);
            Spray(dots, at + Vector3.up * 0.3f, 30, Palette.Paper, 2.5f, 0.35f, 1.2f, 1.5f);
            Ripple(new Vector3(at.x, 0.05f, at.z), Palette.Gold * 2.5f, 0.3f, 6f, 0.9f);
        }

        public void Laser(Vector3 at)
        {
            Spray(sparkles, at, 3, Palette.Coral, 1.0f, 0.25f, 0.3f, 0.5f);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (tetherT < 1f)
            {
                tetherT = Mathf.Min(1f, tetherT + dt / 0.35f);
                tether.enabled = true;
                float w = Mathf.Lerp(0.14f, 0f, tetherT);
                tether.startWidth = w;
                tether.endWidth = w * 1.6f;
                var mid = (tetherFrom + tetherTo) * 0.5f + Vector3.up * (0.6f + 0.2f * (tetherTo - tetherFrom).magnitude * 0.15f);
                for (int i = 0; i < tether.positionCount; i++)
                {
                    float u = i / (tether.positionCount - 1f);
                    var p = Vector3.Lerp(Vector3.Lerp(tetherFrom, mid, u), Vector3.Lerp(mid, tetherTo, u), u);
                    p += Random.insideUnitSphere * 0.04f * (1f - tetherT);
                    tether.SetPosition(i, p);
                }
                tetherMat.SetColor("_Color", new Color(Palette.Ice.r * 4f, Palette.Ice.g * 4f, Palette.Ice.b * 4f, 1f - tetherT));
            }
            else tether.enabled = false;

            foreach (var r in ripples)
            {
                if (r.T >= 1f) { r.Go.SetActive(false); continue; }
                r.T = Mathf.Min(1f, r.T + dt / r.Duration);
                float e = 1f - Mathf.Pow(1f - r.T, 3f);
                float s = Mathf.Lerp(r.From, r.To, e);
                r.Go.transform.localScale = new Vector3(s, 1f, s);
                r.Mat.SetColor("_Color", r.Color);
                r.Mat.SetColor("_BackColor", r.Color);
                r.Mat.SetFloat("_Alpha", 1f - r.T);
            }
        }
    }
}
