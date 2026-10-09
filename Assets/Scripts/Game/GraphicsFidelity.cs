using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BorrowedSeconds.Game
{
    /// <summary>
    /// Settings > Graphics fidelity: four steps from Low to Ultra. High is the look the game always
    /// had (soft 4096 shadows, 4x MSAA with SMAA, high-quality bloom); Low trades it for speed on weak
    /// GPUs, Ultra adds deeper ambient occlusion, light from the glowing pieces and more particles.
    /// Render resolution stays a separate control (DisplayOptions).
    /// </summary>
    public static class GraphicsFidelity
    {
        public enum Aa { Fxaa, SmaaMedium, SmaaHigh }

        public sealed class Step
        {
            public string Name;
            public bool SoftShadows;
            public int ShadowResolution;
            public int Cascades;
            public int Msaa;
            public Aa Antialiasing;
            public bool BloomHalfRes;        // else quarter resolution
            public bool BloomHighQuality;    // on at every step: builds only carry the high-quality bloom variant (off, beams lost their glow)
            public float AoIntensity;        // URP's SSAO renderer feature (0 = off); the game always had 0.4
            public float AoRadius;
            public bool AoDownsample;        // half-resolution occlusion
            public bool AoFastBlur;          // Kawase blur instead of bilateral
            public bool AmbientOcclusion => AoIntensity > 0f;
            public bool GlowLights;          // point lights from the pawn's core, the exit and lit beams (View.BoardView)
            public float Particles;          // multiplies every burst (View.Fx)
            public float WatchTexture;       // the pocket watch's render texture size (UI.WatchStage)
            public int WatchMsaa;
            /// <summary>What the step changes, in a line (the Settings row's description).</summary>
            public string Summary;
        }

        public static readonly Step[] Steps =
        {
            new Step
            {
                Name = "Low", SoftShadows = false, ShadowResolution = 1024, Cascades = 1, Msaa = 1, Antialiasing = Aa.Fxaa,
                BloomHalfRes = false, BloomHighQuality = true, Particles = 0.5f, WatchTexture = 0.75f, WatchMsaa = 2,
                Summary = "Hard 1024 shadows, no ambient occlusion, FXAA, quarter-size bloom, half the particles.",
            },
            new Step
            {
                Name = "Medium", SoftShadows = true, ShadowResolution = 2048, Cascades = 2, Msaa = 2, Antialiasing = Aa.SmaaMedium,
                BloomHalfRes = true, BloomHighQuality = true, AoIntensity = 0.4f, AoRadius = 0.3f, AoDownsample = true, AoFastBlur = true,
                Particles = 0.75f, WatchTexture = 1f, WatchMsaa = 4,
                Summary = "Soft 2048 shadows, half-size ambient occlusion, 2× MSAA with SMAA, fewer particles.",
            },
            new Step
            {
                Name = "High", SoftShadows = true, ShadowResolution = 4096, Cascades = 2, Msaa = 4, Antialiasing = Aa.SmaaHigh,
                BloomHalfRes = true, BloomHighQuality = true, AoIntensity = 0.4f, AoRadius = 0.3f, Particles = 1f, WatchTexture = 1f, WatchMsaa = 8,
                Summary = "Soft 4096 shadows, ambient occlusion, 4× MSAA with SMAA, full bloom.",
            },
            new Step
            {
                Name = "Ultra", SoftShadows = true, ShadowResolution = 8192, Cascades = 2, Msaa = 8, Antialiasing = Aa.SmaaHigh,
                BloomHalfRes = true, BloomHighQuality = true, AoIntensity = 0.9f, AoRadius = 0.45f, GlowLights = true,
                Particles = 1.5f, WatchTexture = 2f, WatchMsaa = 8,
                Summary = "Glowing pieces light the board, deeper occlusion, 8× MSAA, 8192 shadows, more particles.",
            },
        };

        /// <summary>High on the desktop; Medium in a browser, where WebGL costs more and many players
        /// are on laptops; Low on a phone or tablet, whose browser tab has far less memory (no MSAA, a
        /// quarter of the shadow map) and a slower GPU (the Settings line says which step is the default).</summary>
        public static readonly int Default = DisplayOptions.Web ? (WebBridge.TouchFirst ? 0 : 1) : 2;

        public static int Clamp(int step) => Mathf.Clamp(step, 0, Steps.Length - 1);

        public static Step For(int step) => Steps[Clamp(step)];

        /// <summary>The step last applied (checks read it).</summary>
        public static int Current { get; private set; } = Default;

        /// <summary>
        /// Applies a step to the pipeline, the camera, the sun and the views. The pipeline asset is
        /// left alone in the editor, where a change would be written into the asset on disk (as with
        /// DisplayOptions.ApplyRenderScale); the camera, light and views still follow.
        /// </summary>
        public static void Apply(int step, Camera cam, Light sun, View.WorldEnvironment env)
        {
            Current = Clamp(step);
            var s = Steps[Current];
            if (!Application.isEditor && GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.msaaSampleCount = s.Msaa;
                urp.mainLightShadowmapResolution = s.ShadowResolution;
                urp.shadowCascadeCount = s.Cascades;
                SetAmbientOcclusion(urp, s);
            }
            if (cam != null)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                data.antialiasing = s.Antialiasing == Aa.Fxaa ? AntialiasingMode.FastApproximateAntialiasing : AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.antialiasingQuality = s.Antialiasing == Aa.SmaaMedium ? AntialiasingQuality.Medium : AntialiasingQuality.High;
            }
            if (sun != null) sun.shadows = s.SoftShadows ? LightShadows.Soft : LightShadows.Hard;
            if (env != null) env.SetFidelity(s);
            View.Fx.Density = s.Particles;
            View.BoardView.GlowLights = s.GlowLights;
            UI.WatchStage.SetQuality(s.WatchTexture, s.WatchMsaa);
        }

        /// <summary>The SSAO feature on the main renderer. It stays active in the asset so its shader
        /// variants ship (URP strips the variants of inactive features), and is switched here. Only
        /// settings that need no other shader variant change per step: intensity, radius, resolution
        /// and blur (the sample count is a variant, so it stays at the asset's medium).</summary>
        static void SetAmbientOcclusion(UniversalRenderPipelineAsset urp, Step s)
        {
            var list = urp.rendererDataList;
            if (list.Length == 0 || list[0] == null) return;
            foreach (var f in list[0].rendererFeatures)
            {
                if (!(f is ScreenSpaceAmbientOcclusion)) continue;
                f.SetActive(s.AmbientOcclusion);
                // the feature's settings are internal to URP: reached by reflection, and left as they are if that fails
                const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var settings = typeof(ScreenSpaceAmbientOcclusion).GetField("m_Settings", Any)?.GetValue(f);
                if (settings == null) { AoNote = "settings not found"; return; }
                var t = settings.GetType();
                void Set(string name, object value)
                {
                    var field = t.GetField(name, Any);
                    if (field == null) { AoNote = "no " + name; return; }
                    field.SetValue(settings, field.FieldType.IsEnum ? System.Enum.ToObject(field.FieldType, value) : value);
                }
                if (s.AmbientOcclusion)
                {
                    Set("Intensity", s.AoIntensity);
                    Set("Radius", s.AoRadius);
                    Set("Downsample", s.AoDownsample);
                    Set("BlurQuality", s.AoFastBlur ? 2 : 0); // Low (Kawase) or High (bilateral)
                }
                AoNote ??= "";
                AoSettings = $"{t.GetField("Intensity", Any)?.GetValue(settings)} r{t.GetField("Radius", Any)?.GetValue(settings)} "
                    + $"{((bool)(t.GetField("Downsample", Any)?.GetValue(settings) ?? false) ? "half" : "full")} {t.GetField("BlurQuality", Any)?.GetValue(settings)}";
            }
        }

        /// <summary>The SSAO feature's intensity, radius, resolution and blur as last set (checks read it).</summary>
        public static string AoSettings = "";
        /// <summary>Why a setting couldn't be reached, if one couldn't.</summary>
        public static string AoNote;

        /// <summary>Whether the main renderer's SSAO feature is on (checks read it); null if absent or in the editor.</summary>
        public static bool? AmbientOcclusionActive
        {
            get
            {
                if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)) return null;
                var list = urp.rendererDataList;
                if (list.Length == 0 || list[0] == null) return null;
                foreach (var f in list[0].rendererFeatures)
                    if (f is ScreenSpaceAmbientOcclusion) return f.isActive;
                return null;
            }
        }
    }
}
