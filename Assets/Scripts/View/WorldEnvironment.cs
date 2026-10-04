using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BorrowedSeconds.View
{
    /// <summary>
    /// Builds lighting, the clock-ring void and the post-processing stack at runtime, and drives
    /// the "time distortion" pulses (chromatic split, lens warp, desaturation, frozen tint).
    /// </summary>
    public sealed class WorldEnvironment : MonoBehaviour
    {
        public Light Sun;
        public Volume Volume;
        Bloom bloom;
        ChromaticAberration chroma;
        LensDistortion lens;
        ColorAdjustments color;
        Vignette vignette;
        WhiteBalance white;
        Material backdropMat;
        Transform backdrop;
        readonly System.Collections.Generic.List<(Transform, float)> gears = new System.Collections.Generic.List<(Transform, float)>();
        float gearAngle;

        // pulses
        float chromaPulse, lensPulse, flash;
        public float FrozenAmount;   // 0..1 while the player is frozen
        public float RewindAmount;   // 0..1 while rewinding
        public float FocusAmount;    // 0..1 while focusing
        public float DangerAmount;   // 0..1 death moment
        public bool ReduceFlashing;

        public void Build(Camera cam)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Palette.VoidBottom;
            cam.allowHDR = true;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 200f;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;

            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(transform, false);
            Sun = sunGo.AddComponent<Light>();
            Sun.type = LightType.Directional;
            Sun.color = Palette.Hex("#FFE6C4");
            Sun.intensity = 1.35f;
            Sun.shadows = LightShadows.Soft;
            Sun.shadowStrength = 0.72f;
            Sun.shadowBias = 0.03f;
            Sun.shadowNormalBias = 0.25f;
            sunGo.transform.rotation = Quaternion.Euler(52f, -38f, 0f);

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(transform, false);
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = Palette.Hex("#7F8FD8");
            fill.intensity = 0.28f;
            fill.shadows = LightShadows.None;
            fillGo.transform.rotation = Quaternion.Euler(35f, 150f, 0f);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Palette.Hex("#5A6696");
            RenderSettings.ambientEquatorColor = Palette.Hex("#3A4068");
            RenderSettings.ambientGroundColor = Palette.Hex("#1A1D33");
            RenderSettings.fog = false;

            backdropMat = Mats.Instance("BS_Backdrop");
            var bd = Shapes.Make("Backdrop", transform, Shapes.FlatQuad, backdropMat, new Vector3(0, -7f, 2f), new Vector3(90f, 1f, 90f), false);
            backdrop = bd.transform;

            var gearMat = Mats.Lit(Palette.Hex("#2A3164"), 0.2f, 0f);
            var gearSpots = new[]
            {
                (new Vector3(-14f, -6.6f, 6f), 1.5f, 9f), (new Vector3(14.5f, -6.8f, -1f), 2.0f, -6f),
                (new Vector3(-10f, -6.9f, -9f), 1.1f, 14f), (new Vector3(11f, -6.5f, 10f), 0.9f, -16f),
            };
            foreach (var (pos, scale, speed) in gearSpots)
            {
                var g = Shapes.Model("Gear", transform, (_, __) => gearMat, false);
                if (g == null) break;
                g.transform.localPosition = pos;
                g.transform.localScale = Vector3.one * scale;
                gears.Add((g.transform, speed));
            }

            var volGo = new GameObject("Volume");
            volGo.transform.SetParent(transform, false);
            Volume = volGo.AddComponent<Volume>();
            Volume.isGlobal = true;
            Volume.priority = 10;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            Volume.sharedProfile = profile;
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.95f);
            bloom.intensity.Override(0.85f);
            bloom.scatter.Override(0.68f);
            bloom.highQualityFiltering.Override(true);
            vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.3f);
            vignette.smoothness.Override(0.45f);
            vignette.color.Override(Palette.Hex("#05060E"));
            color = profile.Add<ColorAdjustments>(true);
            color.contrast.Override(10f);
            color.saturation.Override(6f);
            color.postExposure.Override(0.1f);
            color.colorFilter.Override(Color.white);
            white = profile.Add<WhiteBalance>(true);
            white.temperature.Override(0f);
            chroma = profile.Add<ChromaticAberration>(true);
            chroma.intensity.Override(0.04f);
            lens = profile.Add<LensDistortion>(true);
            lens.intensity.Override(0f);
            lens.scale.Override(1f);
        }

        public void PulseBorrow()
        {
            chromaPulse = ReduceFlashing ? 0.4f : 1f;
            lensPulse = ReduceFlashing ? -0.12f : -0.32f;
        }

        public void PulseFreeze()
        {
            chromaPulse = Mathf.Max(chromaPulse, ReduceFlashing ? 0.3f : 0.7f);
            lensPulse = ReduceFlashing ? 0.08f : 0.2f;
        }

        public void PulseDeath()
        {
            chromaPulse = ReduceFlashing ? 0.5f : 1.4f;
            lensPulse = ReduceFlashing ? 0.1f : 0.35f;
            DangerAmount = 1f;
        }

        public void Flash(float amount) => flash = Mathf.Max(flash, ReduceFlashing ? amount * 0.3f : amount);

        void Update()
        {
            float dt = Clock.Dt;
            chromaPulse = Mathf.MoveTowards(chromaPulse, 0f, dt * 2.2f);
            lensPulse = Mathf.MoveTowards(lensPulse, 0f, dt * 1.4f);
            flash = Mathf.MoveTowards(flash, 0f, dt * 3f);
            DangerAmount = Mathf.MoveTowards(DangerAmount, 0f, dt * 1.2f);
            if (chroma == null) return;

            chroma.intensity.value = Mathf.Clamp01(0.04f + chromaPulse * 0.9f + RewindAmount * 0.55f + FrozenAmount * 0.12f + DangerAmount * 0.4f);
            lens.intensity.value = Mathf.Clamp(lensPulse + RewindAmount * -0.12f + FocusAmount * -0.08f, -0.8f, 0.8f);
            color.saturation.value = Mathf.Lerp(6f, -55f, Mathf.Max(RewindAmount * 0.85f, FocusAmount * 0.45f));
            color.saturation.value = Mathf.Lerp(color.saturation.value, -25f, FrozenAmount * 0.4f);
            color.postExposure.value = 0.1f + flash * 0.9f;
            var tint = Color.Lerp(Color.white, Palette.Hex("#BFEFFF"), FrozenAmount * 0.55f + FocusAmount * 0.25f);
            tint = Color.Lerp(tint, Palette.Hex("#FFC2C8"), DangerAmount * 0.6f);
            color.colorFilter.value = tint;
            white.temperature.value = -FrozenAmount * 18f - RewindAmount * 25f;
            vignette.intensity.value = 0.3f + FrozenAmount * 0.12f + RewindAmount * 0.15f + DangerAmount * 0.15f + FocusAmount * 0.1f;
            bloom.intensity.value = 0.85f + flash * 1.2f;
            float spin = Mathf.Lerp(1f, -6f, RewindAmount) * (1f - FrozenAmount * 0.85f);
            gearAngle += dt * spin;
            foreach (var (t, speed) in gears) t.localRotation = Quaternion.Euler(0, gearAngle * speed, 0);
            if (backdropMat != null)
            {
                backdropMat.SetFloat("_Spin", Mathf.Lerp(0.02f, -0.6f, RewindAmount));
                backdropMat.SetColor("_Tint", Color.Lerp(Color.white, Palette.Hex("#9FE8FF"), FrozenAmount * 0.6f));
            }
        }
    }
}
