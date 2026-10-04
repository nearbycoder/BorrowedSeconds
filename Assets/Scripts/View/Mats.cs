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
            m = new Material(Template("BS_LitEmissive")) { name = key };
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

        /// <summary>Palette material for a Blender material name (the contract in ArtSource/build_assets.py).</summary>
        public static Material ForModel(string blenderName)
        {
            switch (blenderName)
            {
                case "Floor": return Lit(Palette.FloorA, 0.55f);
                case "Slate": return Lit(Palette.Wall, 0.3f);
                case "SlateTop": return Lit(Palette.WallTop, 0.4f);
                case "Brass": return Lit(Palette.Brass, 0.62f, 0.75f);
                case "Porcelain": return Lit(Palette.Porcelain, 0.75f);
                case "Amber": return Emissive(Palette.Amber, Palette.Amber * 2.4f, 0.6f);
                case "Ink": return Lit(Palette.Ink, 0.7f);
                case "Graphite": return Lit(Palette.Graphite, 0.45f, 0.2f);
                case "Coral": return Emissive(Palette.Coral, Palette.Coral * 3f, 0.6f);
                case "Mint": return Emissive(Palette.Mint, Palette.Mint * 1.4f, 0.6f);
                case "MintGlass": return Emissive(Palette.Mint * 0.5f, Palette.Mint * 0.8f, 0.7f);
                case "Pad": return Emissive(Palette.Mint * 0.6f, Palette.Mint * 0.4f, 0.6f);
                case "Gold": return Emissive(Palette.Gold, Palette.Gold * 1.6f, 0.8f);
                case "Sand": return Emissive(Palette.Gold, Palette.Gold * 1.2f, 0.4f);
                case "Plinth": return Lit(Palette.Plinth, 0.25f);
                case "Crystal": return Template("BS_Crystal");
                case "Glass":
                    if (Cache.TryGetValue("glass", out var g)) return g;
                    g = new Material(Template("BS_Crystal")) { name = "glass" };
                    g.SetColor("_Color", new Color(1f, 0.95f, 0.85f, 0.16f));
                    g.SetColor("_RimColor", new Color(1.6f, 1.4f, 1.0f, 1f));
                    g.SetFloat("_Sparkle", 0.2f);
                    return Cache["glass"] = g;
                default: return Lit(Color.magenta);
            }
        }

        public static void SetColor(Material m, Color c) => m.SetColor(BaseColor, c);
        public static void SetEmission(Material m, Color c) => m.SetColor(EmissionColor, c);
    }
}
