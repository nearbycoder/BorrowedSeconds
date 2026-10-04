using System.Collections.Generic;
using UnityEngine;

namespace BorrowedSeconds.View
{
    /// <summary>
    /// Runtime material factory. Templates live in Resources/Materials so their shaders ship in
    /// builds; every material here is a cached copy with colours applied.
    /// </summary>
    public static class Mats
    {
        static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
        static readonly int Metallic = Shader.PropertyToID("_Metallic");

        static Material Template(string name)
        {
            if (Cache.TryGetValue("tpl:" + name, out var m)) return m;
            m = Resources.Load<Material>("Materials/" + name);
            if (m == null)
            {
                Debug.LogError("[Mats] missing template Materials/" + name);
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            }
            Cache["tpl:" + name] = m;
            return m;
        }

        public static Material Lit(Color color, float smooth = 0.35f, float metal = 0f)
        {
            string key = $"lit:{color}:{smooth}:{metal}";
            if (Cache.TryGetValue(key, out var m)) return m;
            m = new Material(Template("BS_Lit")) { name = key };
            m.SetColor(BaseColor, color);
            m.SetFloat(Smoothness, smooth);
            m.SetFloat(Metallic, metal);
            Cache[key] = m;
            return m;
        }

        public static Material Emissive(Color color, Color emission, float smooth = 0.5f)
        {
            string key = $"emit:{color}:{emission}:{smooth}";
            if (Cache.TryGetValue(key, out var m)) return m;
            m = new Material(Template("BS_Lit")) { name = key };
            m.SetColor(BaseColor, color);
            m.SetFloat(Smoothness, smooth);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            m.SetColor(EmissionColor, emission);
            Cache[key] = m;
            return m;
        }

        /// <summary>A fresh (uncached) instance of a template, for per-object animation.</summary>
        public static Material Instance(string template) => new Material(Template(template));

        public static Material Shared(string template) => Template(template);

        public static void SetColor(Material m, Color c) => m.SetColor(BaseColor, c);
        public static void SetEmission(Material m, Color c) => m.SetColor(EmissionColor, c);
    }
}
