using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BorrowedSeconds.EditorTools
{
    /// <summary>
    /// One-shot, idempotent project wiring: material templates (so shaders ship), URP quality,
    /// the bootstrap scene, player settings, TextMeshPro resources and font assets.
    /// Batch: Tools/unity.sh batch BorrowedSeconds.EditorTools.ProjectSetup.Apply
    /// </summary>
    public static class ProjectSetup
    {
        const string MatDir = "Assets/Resources/Materials";
        const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Borrowed Seconds/Apply Project Setup")]
        public static void Apply()
        {
            bool ok = true;
            try
            {
                ImportTmpEssentials();
                EnsureDir(MatDir);
                ok &= Mat("BS_Lit", "Universal Render Pipeline/Lit", m =>
                {
                    m.SetFloat("_Smoothness", 0.4f);
                    m.enableInstancing = true;
                }) != null;
                // a separate template keeps the _EMISSION variant from being stripped out of builds
                ok &= Mat("BS_LitEmissive", "Universal Render Pipeline/Lit", m =>
                {
                    m.SetFloat("_Smoothness", 0.5f);
                    m.enableInstancing = true;
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", Color.white);
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                }) != null;
                ok &= Mat("BS_Crystal", "BS/Crystal", null) != null;
                ok &= Mat("BS_Glow", "BS/Glow", m => { m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); }) != null;
                ok &= Mat("BS_GlowAdd", "BS/Glow", m => { m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.One); }) != null;
                ok &= Mat("BS_Ghost", "BS/Ghost", null) != null;
                ok &= Mat("BS_Ring", "BS/Ring", null) != null;
                ok &= Mat("BS_Backdrop", "BS/Backdrop", null) != null;
                ok &= Mat("BS_Particle", "BS/Particle", null) != null;
                var ripple = Mat("BS_TimeRipple", "BS/TimeRipple", null);
                ok &= ripple != null;
                ConfigureUrp();
                if (ripple != null) AddRipplePass(ripple);
                ConfigurePlayer();
                BuildScene();
                BuildFonts();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                ok = false;
            }
            Debug.Log("[ProjectSetup] " + (ok ? "done" : "FAILED"));
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        static void EnsureDir(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                Directory.CreateDirectory(path);
                AssetDatabase.Refresh();
            }
        }

        static Material Mat(string name, string shaderName, System.Action<Material> setup)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"[ProjectSetup] missing shader {shaderName}");
                return null;
            }
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            setup?.Invoke(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        const string RippleName = "BS Time Ripple";

        /// <summary>Adds (once) the full-screen time-distortion pass to every renderer.</summary>
        static void AddRipplePass(Material mat)
        {
            foreach (var path in new[] { "Assets/Settings/PC_Renderer.asset", "Assets/Settings/Mobile_Renderer.asset" })
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (data == null) continue;
                var feature = data.rendererFeatures.OfType<FullScreenPassRendererFeature>().FirstOrDefault(f => f.name == RippleName);
                if (feature == null)
                {
                    feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
                    feature.name = RippleName;
                    AssetDatabase.AddObjectToAsset(feature, data);
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);
                    var so = new SerializedObject(data);
                    var features = so.FindProperty("m_RendererFeatures");
                    var map = so.FindProperty("m_RendererFeatureMap");
                    features.arraySize++;
                    features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
                    map.arraySize++;
                    map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                    so.ApplyModifiedProperties();
                }
                feature.passMaterial = mat;
                feature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing;
                feature.requirements = ScriptableRenderPassInput.None;
                feature.fetchColorBuffer = true;
                feature.SetActive(true);
                EditorUtility.SetDirty(feature);
                EditorUtility.SetDirty(data);
            }
        }

        static void ConfigureUrp()
        {
            var pc = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            if (pc == null) { Debug.LogError("[ProjectSetup] PC_RPAsset missing"); return; }
            pc.supportsHDR = true;
            pc.msaaSampleCount = 4;
            pc.renderScale = 1f;
            pc.shadowDistance = 45f;
            pc.shadowCascadeCount = 2;
            pc.supportsCameraOpaqueTexture = true;
            pc.supportsCameraDepthTexture = true;
            var so = new SerializedObject(pc);
            Set(so, "m_SoftShadowsSupported", 1);
            Set(so, "m_MainLightShadowmapResolution", 4096);
            Set(so, "m_SoftShadowQuality", 3);
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(pc);

            GraphicsSettings.defaultRenderPipeline = pc;
            var names = QualitySettings.names;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pc;
            }
            int pcIndex = System.Array.IndexOf(names, "PC");
            QualitySettings.SetQualityLevel(pcIndex >= 0 ? pcIndex : current, true);
            // make PC the default for standalone
            var qso = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
            var perPlatform = qso.FindProperty("m_PerPlatformDefaultQuality");
            if (perPlatform != null && pcIndex >= 0)
            {
                for (int i = 0; i < perPlatform.arraySize; i++)
                {
                    var e = perPlatform.GetArrayElementAtIndex(i);
                    var key = e.FindPropertyRelative("first");
                    var val = e.FindPropertyRelative("second");
                    if (key != null && val != null && (key.stringValue == "Standalone" || key.stringValue == "Linux")) val.intValue = pcIndex;
                }
                qso.ApplyModifiedProperties();
            }
        }

        static void Set(SerializedObject so, string prop, int value)
        {
            var p = so.FindProperty(prop);
            if (p == null) { Debug.LogWarning($"[ProjectSetup] no property {prop}"); return; }
            if (p.propertyType == SerializedPropertyType.Boolean) p.boolValue = value != 0;
            else p.intValue = value;
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "Borrowed Seconds";
            PlayerSettings.productName = "Borrowed Seconds";
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        }

        static void BuildScene()
        {
            EnsureDir("Assets/Scenes");
            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.05f, 0.06f, 0.13f);
                camGo.AddComponent<AudioListener>();
                camGo.AddComponent<UniversalAdditionalCameraData>();
                camGo.transform.position = new Vector3(0, 12, -8);
                camGo.transform.rotation = Quaternion.Euler(56, 0, 0);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            if (File.Exists("Assets/Scenes/SampleScene.unity")) AssetDatabase.DeleteAsset("Assets/Scenes/SampleScene.unity");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        static void ImportTmpEssentials()
        {
            if (File.Exists("Assets/TextMesh Pro/Resources/TMP Settings.asset")) return;
            string pkg = Path.GetFullPath("Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage");
            if (!File.Exists(pkg))
            {
                Debug.LogError("[ProjectSetup] TMP essentials package not found at " + pkg);
                return;
            }
            AssetDatabase.ImportPackage(pkg, false);
            AssetDatabase.Refresh();
        }

        static void BuildFonts()
        {
            foreach (var name in new[] { "FiraSans-Regular", "FiraSans-SemiBold", "FiraSans-ExtraBold", "FiraSans-Light" })
            {
                string assetPath = $"Assets/Resources/Fonts/{name} SDF.asset";
                if (File.Exists(assetPath)) continue;
                var font = AssetDatabase.LoadAssetAtPath<Font>($"Assets/Resources/Fonts/{name}.ttf");
                if (font == null) { Debug.LogError("[ProjectSetup] missing font " + name); continue; }
                var fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
                fa.name = name + " SDF";
                AssetDatabase.CreateAsset(fa, assetPath);
                // the atlas texture and material must live inside the font asset
                fa.atlasTextures[0].name = name + " Atlas";
                AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
                fa.material.name = name + " Material";
                AssetDatabase.AddObjectToAsset(fa.material, fa);
                EditorUtility.SetDirty(fa);
            }
        }
    }
}
