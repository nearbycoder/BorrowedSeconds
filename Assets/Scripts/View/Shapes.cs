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

        /// <summary>Spawns a Blender model from Resources/Models, or null if it isn't there.</summary>
        public static GameObject Model(string name, Transform parent)
        {
            var prefab = Resources.Load<GameObject>("Models/" + name);
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, parent, false);
            go.name = name;
            foreach (var c in go.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            return go;
        }
    }
}
