using System.Collections;
using System.Collections.Generic;
using System.IO;
using BorrowedSeconds.View;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BorrowedSeconds.Game
{
    /// <summary>
    /// Graphics fidelity in the real game: the `fidelity` check (Settings row to pipeline), and
    /// -bsFidelityShots DIR, which screenshots one held frame at every step and then measures frame
    /// times at every step (Tools/fidelity.sh).
    /// </summary>
    public sealed partial class GameRoot
    {
        /// <summary>What the pipeline, camera, light and views are doing right now, in a line.</summary>
        string FidelityState()
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var cam = Cam.GetUniversalAdditionalCameraData();
            return $"MSAA {(urp != null ? urp.msaaSampleCount : 0)}x, {cam.antialiasing} {cam.antialiasingQuality}, "
                + $"{Env.Sun.shadows} shadows {(urp != null ? urp.mainLightShadowmapResolution : 0)} x{(urp != null ? urp.shadowCascadeCount : 0)}, "
                + $"bloom {Env.BloomDownscale}{(Env.BloomHighQuality ? " HQ" : "")}, SSAO {(GraphicsFidelity.AmbientOcclusionActive?.ToString() ?? "absent")} ({GraphicsFidelity.AoSettings}{GraphicsFidelity.AoNote}), "
                + $"glow lights {(Session != null ? Session.Board.GlowLightsOn : 0)}, particles {Fx.Density:0.##}x, watch {UI.WatchStage.QualityState}";
        }

        /// <summary>
        /// Settings > Graphics fidelity reaches the pipeline at every step: the row steps Low to Ultra
        /// and each step's MSAA, anti-aliasing, shadows, bloom, SSAO, glow lights,
        /// particles and watch texture are read back; the choice survives a save round trip, and a save
        /// from before the setting loads as High.
        /// </summary>
        IEnumerator CheckFidelity(string dir, System.Action<string, bool, string> report)
        {
            var saved = JsonUtility.ToJson(Save);
            forceDisplay = true; // ApplySettings uses the save's step, not the scripted run's
            StartLevel(Catalog.Levels.FindIndex(l => l.Id == "4-5"), false);
            Session.Autoplay = Catalog.SolutionFor(Session.Def).Actions;
            Hud.SkipIntro();
            yield return new WaitForSecondsRealtime(1.5f);
            Pause();
            OpenSettings(Flow.Paused);
            var row = settings.Menu.Items[UI.SettingsScreen.FidelityRow];
            bool isRow = row.Label == "Graphics fidelity" && row.Slider != null;
            settings.Menu.Selected = UI.SettingsScreen.FidelityRow;
            for (int i = 0; i < 4; i++) row.Adjust(-1);
            var steps = new List<string>();
            bool all = isRow;
            for (int k = 0; k < GraphicsFidelity.Steps.Length; k++)
            {
                if (k > 0) row.Adjust(1);
                yield return null;
                yield return null;
                var want = GraphicsFidelity.Steps[k];
                var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                var cam = Cam.GetUniversalAdditionalCameraData();
                bool ok = Save.fidelity == k && GraphicsFidelity.Current == k && row.Value() == want.Name.ToUpperInvariant()
                    && urp != null && urp.msaaSampleCount == want.Msaa && urp.mainLightShadowmapResolution == want.ShadowResolution && urp.shadowCascadeCount == want.Cascades
                    && cam.antialiasing == (want.Antialiasing == GraphicsFidelity.Aa.Fxaa ? AntialiasingMode.FastApproximateAntialiasing : AntialiasingMode.SubpixelMorphologicalAntiAliasing)
                    && Env.Sun.shadows == (want.SoftShadows ? LightShadows.Soft : LightShadows.Hard)
                    && Env.BloomHighQuality == want.BloomHighQuality && Env.BloomDownscale == (want.BloomHalfRes ? BloomDownscaleMode.Half : BloomDownscaleMode.Quarter)
                    && GraphicsFidelity.AmbientOcclusionActive == want.AmbientOcclusion
                    && (!want.AmbientOcclusion || GraphicsFidelity.AoSettings.StartsWith($"{want.AoIntensity} r{want.AoRadius} {(want.AoDownsample ? "half" : "full")}"))
                    && (Session.Board.GlowLightsOn > 0) == want.GlowLights
                    && Mathf.Approximately(Fx.Density, want.Particles)
                    && UI.WatchStage.QualityState.StartsWith(Mathf.RoundToInt(512 * want.WatchTexture) + "px " + want.WatchMsaa + "x")
                    && JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(Save)).fidelity == k;
                all &= ok;
                steps.Add($"{(ok ? "" : "FAIL ")}{want.Name}: {FidelityState()}");
            }
            // a save written before the setting existed has no "fidelity" key
            var json = JsonUtility.ToJson(new SaveData());
            json = System.Text.RegularExpressions.Regex.Replace(json, "\"fidelity\":\\d+,?", "");
            bool oldSave = !json.Contains("fidelity") && JsonUtility.FromJson<SaveData>(json).fidelity == GraphicsFidelity.Default;
            // back to High
            for (int i = 0; i < 4; i++) row.Adjust(-1);
            row.Adjust(1);
            row.Adjust(1);
            bool backHigh = Save.fidelity == GraphicsFidelity.Default && GraphicsFidelity.Current == GraphicsFidelity.Default;
            CloseSettings();
            Resume();
            JsonUtility.FromJsonOverwrite(saved, Save);
            forceDisplay = false;
            ApplySettings();
            report("fidelity", all && oldSave && backHigh,
                $"row {UI.SettingsScreen.FidelityRow} is Graphics fidelity: {isRow}; {string.Join(" | ", steps)}; an old save loads as {GraphicsFidelity.Steps[JsonUtility.FromJson<SaveData>(json).fidelity].Name}: {oldSave}; back to High: {backHigh}");
        }

        /// <summary>
        /// -bsFidelityShots DIR (Tools/fidelity.sh): for each level in SHOTS, plays its solution to a
        /// fixed tick, holds it there and screenshots the same frame at every step; then, for each level
        /// in TIMED, plays its solution at each step (Low to Ultra, then Ultra to Low) and logs frame
        /// times. Add -bsNoVsync to measure what the GPU can do rather than the refresh rate.
        /// </summary>
        IEnumerator FidelityTour(string dir)
        {
            Directory.CreateDirectory(dir);
            var log = new List<string>();
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-bsNoVsync") >= 0)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
            }
            log.Add($"window {Screen.width}x{Screen.height}, vsync {QualitySettings.vSyncCount}, render scale {DisplayOptions.CurrentRenderScale:0.00}, GPU {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType})");
            void Apply(int step) => GraphicsFidelity.Apply(step, Cam, Env.Sun, Env);

            foreach (var (id, tick) in new[] { ("4-5", 196), ("2-3", 150), ("3-5", 120) })
            {
                int idx = Catalog.Levels.FindIndex(l => l.Id == id);
                Apply(GraphicsFidelity.Default);
                LoadLevel(idx);
                State = Flow.Playing;
                Hud.SetVisible(true);
                Hud.SkipIntro();
                Session.Autoplay = Catalog.SolutionFor(Catalog.Levels[idx]).Actions;
                Session.IntroTime = 0.2f;
                float deadline = Time.realtimeSinceStartup + 30f;
                while (Session.Tick < tick && Time.realtimeSinceStartup < deadline) yield return null;
                Session.Paused = true; // the same simulation frame for every step
                for (int step = 0; step < GraphicsFidelity.Steps.Length; step++)
                {
                    Apply(step);
                    for (int f = 0; f < 20; f++) yield return null;
                    string file = $"{id}_{step}_{GraphicsFidelity.Steps[step].Name.ToLowerInvariant()}.png";
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, file));
                    yield return null;
                    yield return null;
                    log.Add($"shot {file} at tick {Session.Tick}: {FidelityState()}");
                }
                Session.Paused = false;
            }

            var order = new[] { 0, 1, 2, 3, 3, 2, 1, 0 };
            foreach (var id in new[] { "4-5", "3-5", "2-4" })
            {
                int idx = Catalog.Levels.FindIndex(l => l.Id == id);
                foreach (int step in order)
                {
                    Apply(step);
                    LoadLevel(idx);
                    State = Flow.Playing;
                    Hud.SetVisible(true);
                    Hud.SkipIntro();
                    Session.Autoplay = Catalog.SolutionFor(Catalog.Levels[idx]).Actions;
                    Session.IntroTime = 0.2f;
                    yield return new WaitForSecondsRealtime(1.5f); // warm-up
                    var dts = new List<float>();
                    float end = Time.realtimeSinceStartup + 5f;
                    while (Time.realtimeSinceStartup < end)
                    {
                        yield return null;
                        dts.Add(Time.unscaledDeltaTime);
                    }
                    dts.Sort();
                    float total = 0f;
                    foreach (var d in dts) total += d;
                    float p99 = dts[Mathf.Clamp((int)(dts.Count * 0.99f), 0, dts.Count - 1)];
                    log.Add($"time {id} {GraphicsFidelity.Steps[step].Name}: mean {total / dts.Count * 1000f:0.00} ms ({dts.Count / total:0} fps), median {dts[dts.Count / 2] * 1000f:0.00} ms, worst 1% {p99 * 1000f:0.00} ms, {dts.Count} frames");
                }
            }
            Apply(GraphicsFidelity.Default);
            log.Add("done");
            File.WriteAllLines(Path.Combine(dir, "fidelity.log"), log);
            Debug.Log("[Fidelity] " + string.Join(" | ", log));
            Application.Quit(0);
        }
    }
}
