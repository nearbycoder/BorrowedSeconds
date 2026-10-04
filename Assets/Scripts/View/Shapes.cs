using System.Collections.Generic;
using UnityEngine;

namespace BorrowedSeconds.View
{
    /// <summary>Collider-free primitive and model spawning helpers.</summary>
    public static class Shapes
    {
        static readonly Dictionary<PrimitiveType, Mesh> Meshes = new Dictionary<PrimitiveType, Mesh>();
        static Mesh quad;

        public static Mesh MeshOf(PrimitiveType t)
        {
            if (Meshes.TryGetValue(t, out var m)) return m;
            var go = GameObject.CreatePrimitive(t);
            m = go.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(go);
            Meshes[t] = m;
            return m;
        }

        /// <summary>A unit quad lying flat in XZ (facing +Y), uv 0..1.</summary>
        public static Mesh FlatQuad
        {
            get
            {
                if (quad != null) return quad;
                quad = new Mesh { name = "FlatQuad" };
                quad.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(-0.5f, 0, 0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(0.5f, 0, -0.5f) };
                quad.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
                quad.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
                quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                quad.RecalculateBounds();
                return quad;
            }
        }

        static Mesh tube;

        /// <summary>An uncapped unit-diameter tube from y = 0 to 1, uv.y running up its length.</summary>
        public static Mesh OpenTube
        {
            get
            {
                if (tube != null) return tube;
                const int n = 32;
                var v = new Vector3[(n + 1) * 2];
                var uv = new Vector2[v.Length];
                var nrm = new Vector3[v.Length];
                var tri = new int[n * 6];
                for (int i = 0; i <= n; i++)
                {
                    float a = i / (float)n * Mathf.PI * 2f;
                    var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    v[i * 2] = d * 0.5f;
                    v[i * 2 + 1] = d * 0.5f + Vector3.up;
                    uv[i * 2] = new Vector2(i / (float)n, 0);
                    uv[i * 2 + 1] = new Vector2(i / (float)n, 1);
                    nrm[i * 2] = nrm[i * 2 + 1] = d;
                    if (i == n) break;
                    int b = i * 6, k = i * 2;
                    tri[b] = k; tri[b + 1] = k + 1; tri[b + 2] = k + 2;
                    tri[b + 3] = k + 1; tri[b + 4] = k + 3; tri[b + 5] = k + 2;
                }
                tube = new Mesh { name = "OpenTube", vertices = v, uv = uv, normals = nrm, triangles = tri };
                tube.RecalculateBounds();
                return tube;
            }
        }

        public static GameObject Make(string name, Transform parent, Mesh mesh, Material mat, Vector3 pos, Vector3 scale, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = shadows;
            return go;
        }

        public static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material mat, bool shadows = true)
            => Make(name, parent, MeshOf(PrimitiveType.Cube), mat, pos, size, shadows);

        public static GameObject Cyl(string name, Transform parent, Vector3 pos, float diameter, float height, Material mat, bool shadows = true)
            => Make(name, parent, MeshOf(PrimitiveType.Cylinder), mat, pos, new Vector3(diameter, height * 0.5f, diameter), shadows);

        public static GameObject Ball(string name, Transform parent, Vector3 pos, float diameter, Material mat, bool shadows = true)
            => Make(name, parent, MeshOf(PrimitiveType.Sphere), mat, pos, Vector3.one * diameter, shadows);

        public static GameObject Flat(string name, Transform parent, Vector3 pos, float size, Material mat)
            => Make(name, parent, FlatQuad, mat, pos, new Vector3(size, 1, size), false);

        public static GameObject Group(string name, Transform parent, Vector3 pos = default)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            return go;
        }

        /// <summary>
        /// Spawns a Blender model from Resources/Models with palette materials, or null if it isn't
        /// there. <paramref name="pick"/> may override the material for a part (part name, Blender material).
        /// </summary>
        public static GameObject Model(string name, Transform parent, System.Func<string, string, Material> pick = null, bool shadows = true)
        {
            var prefab = Resources.Load<GameObject>("Models/" + name);
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, parent, false);
            go.name = name;
            foreach (var c in go.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string mat = mats[i] != null ? BaseName(mats[i].name) : "";
                    mats[i] = pick?.Invoke(r.name, mat) ?? Mats.ForModel(mat);
                }
                r.sharedMaterials = mats;
                bool glassy = mats.Length > 0 && mats[0] != null && mats[0].renderQueue >= 3000;
                r.shadowCastingMode = shadows && !glassy ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = shadows && !glassy;
            }
            return go;
        }

        static string BaseName(string n)
        {
            int cut = n.IndexOf(" (", System.StringComparison.Ordinal);
            if (cut >= 0) n = n.Substring(0, cut);
            cut = n.IndexOf('.');
            return cut >= 0 ? n.Substring(0, cut) : n;
        }

        /// <summary>Finds a named part anywhere under a model.</summary>
        public static Transform Part(GameObject model, string name)
        {
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
