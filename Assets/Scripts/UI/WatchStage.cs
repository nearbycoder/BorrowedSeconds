using BorrowedSeconds.View;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace BorrowedSeconds.UI
{
    /// <summary>
    /// A live 3D pocket watch (Blender model) rendered by its own camera into a render texture that
    /// UI shows through a RawImage. Each stage sits far from the board on a private layer.
    /// The owner poses it every frame: hand angles, tilt, a "frost" amount for when time stops.
    /// </summary>
    public sealed class WatchStage : MonoBehaviour
    {
        public const int Layer = 30;
        static int count;
        static Cubemap studio;

        public RenderTexture Texture { get; private set; }
        Camera cam;
        Transform pivot, hour, minute, second, small;
        Material frostMat;
        Renderer[] renderers;
        public float Yaw, Pitch, Roll;
        public float HourDeg, MinuteDeg, SecondDeg, SmallDeg;
        public float Frost;
        public float Scale = 1f;
        public bool MainHands = true;
        /// <summary>The sub-dial's small seconds hand (the HUD hides it: its label sits over the sub-dial).</summary>
        public bool SmallHand = true;
        /// <summary>The UI image showing this stage; the camera only renders while it is visible.</summary>
        public Graphic Viewer;

        public static WatchStage Create(Transform parent, int pixels, float distance = 7.4f)
        {
            var go = new GameObject("WatchStage" + count);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(5000 + count * 100, 5000, 0);
            count++;
            var st = go.AddComponent<WatchStage>();
            st.Build(pixels, distance);
            return st;
        }

        void Build(int pixels, float distance)
        {
            EnsureReflections();
            Texture = new RenderTexture(pixels, pixels, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8, name = name + "RT" };
            var camGo = new GameObject("Cam");
            camGo.transform.SetParent(transform, false);
            camGo.transform.localPosition = new Vector3(0, 0, -distance);
            cam = camGo.AddComponent<Camera>();
            cam.orthographic = false;
            cam.fieldOfView = 22f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.cullingMask = 1 << Layer;
            cam.targetTexture = Texture;
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 30f;
            cam.allowHDR = false;
            cam.allowMSAA = true;
            // keep the render direct to the texture so its alpha survives (no intermediate copies)
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
            data.antialiasing = AntialiasingMode.None;
            data.SetRenderer(1); // UI3D renderer: no full-screen time pass (ProjectSetup.EnsureUi3DRenderer)

            pivot = new GameObject("Pivot").transform;
            pivot.SetParent(transform, false);
            var model = Shapes.Model("PocketWatch", pivot, Pick, false);
            if (model != null)
            {
                // Blender front is -Y; after the FBX axis bake it faces -Z, towards our camera.
                model.transform.localRotation = Quaternion.identity;
                model.transform.localPosition = new Vector3(0, -0.22f, 0);
                hour = Shapes.Part(model, "HandHour");
                minute = Shapes.Part(model, "HandMinute");
                second = Shapes.Part(model, "HandSecond");
                small = Shapes.Part(model, "HandSmall");
            }
            foreach (var t in GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            renderers = pivot.GetComponentsInChildren<Renderer>();

            // studio lights that only matter here (far from the board)
            AddLight(new Vector3(-3.5f, 3.5f, -2.5f), new Color(1f, 0.92f, 0.8f), 11f);
            AddLight(new Vector3(3.5f, -1f, -2.5f), new Color(0.55f, 0.75f, 1f), 9f);
            AddLight(new Vector3(0.5f, 2.5f, 2.5f), new Color(1f, 0.9f, 0.7f), 7f);
        }

        void AddLight(Vector3 local, Color c, float intensity)
        {
            var lg = new GameObject("Light");
            lg.transform.SetParent(transform, false);
            lg.transform.localPosition = local;
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.intensity = intensity;
            l.range = 14f;
            l.shadows = LightShadows.None;
            lg.layer = Layer;
        }

        Material Pick(string part, string blenderMat)
        {
            switch (blenderMat)
            {
                case "Brass": return Mats.Lit(new Color(0.86f, 0.66f, 0.36f), 0.82f, 1f);
                case "BrassDark": return Mats.Lit(new Color(0.55f, 0.4f, 0.22f), 0.6f, 1f);
                case "Gold": return Mats.Lit(new Color(1f, 0.8f, 0.46f), 0.88f, 1f);
                case "Enamel": return Mats.Lit(new Color(0.045f, 0.055f, 0.12f), 0.92f, 0f);
                case "Ice": return Mats.Emissive(Palette.Ice, Palette.Ice * 2.2f, 0.8f);
                case "Glass":
                    if (frostMat == null)
                    {
                        frostMat = Mats.Instance("BS_Crystal");
                        frostMat.SetColor("_Color", new Color(0.8f, 0.95f, 1f, 0.05f));
                        frostMat.SetColor("_RimColor", new Color(0.9f, 1.1f, 1.3f, 1f));
                        frostMat.SetFloat("_RimPower", 3.5f);
                        frostMat.SetFloat("_Sparkle", 0.15f);
                    }
                    return frostMat;
                default: return null;
            }
        }

        /// <summary>A small generated studio cubemap so metals have something to reflect.</summary>
        static void EnsureReflections()
        {
            if (studio != null) return;
            const int n = 64;
            studio = new Cubemap(n, TextureFormat.RGBA32, false) { name = "StudioReflections" };
            var faces = new[] { CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY, CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ };
            var px = new Color[n * n];
            foreach (var f in faces)
            {
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                    Vector3 d = f switch
                    {
                        CubemapFace.PositiveX => new Vector3(1, -v, -u),
                        CubemapFace.NegativeX => new Vector3(-1, -v, u),
                        CubemapFace.PositiveY => new Vector3(u, 1, v),
                        CubemapFace.NegativeY => new Vector3(u, -1, -v),
                        CubemapFace.PositiveZ => new Vector3(u, -v, 1),
                        _ => new Vector3(-u, -v, -1),
                    };
                    d.Normalize();
                    // dark navy studio, a warm softbox up-left, a cool strip right, a bright top
                    var c = Color.Lerp(new Color(0.03f, 0.035f, 0.07f), new Color(0.2f, 0.22f, 0.36f), Mathf.Clamp01(d.y * 0.5f + 0.5f));
                    c += new Color(1.6f, 1.4f, 1.1f) * Mathf.Pow(Mathf.Clamp01(Vector3.Dot(d, new Vector3(-0.5f, 0.6f, -0.6f).normalized)), 18f);
                    c += new Color(0.5f, 0.75f, 1.2f) * Mathf.Pow(Mathf.Clamp01(Vector3.Dot(d, new Vector3(0.9f, 0.1f, -0.3f).normalized)), 30f);
                    c += new Color(0.9f, 0.9f, 0.95f) * Mathf.Pow(Mathf.Clamp01(d.y), 8f) * 0.6f;
                    px[y * n + x] = c;
                }
                studio.SetPixels(px, f);
            }
            studio.Apply();
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = studio;
            RenderSettings.reflectionIntensity = 1f;
        }

        public RawImage Show(Transform uiParent, Vector2 size)
        {
            var go = new GameObject("Watch", typeof(RectTransform));
            go.transform.SetParent(uiParent, false);
            var img = go.AddComponent<RawImage>();
            img.texture = Texture;
            img.raycastTarget = false;
            ((RectTransform)go.transform).sizeDelta = size;
            Viewer = img;
            return img;
        }

        void LateUpdate()
        {
            if (pivot == null) return;
            pivot.localRotation = Quaternion.Euler(Pitch, Yaw, Roll);
            pivot.localScale = Vector3.one * Scale;
            // hands turn about the model's depth axis (+Z after export), clockwise seen from the front
            if (hour != null) hour.gameObject.SetActive(MainHands);
            if (minute != null) minute.gameObject.SetActive(MainHands);
            if (second != null) second.gameObject.SetActive(MainHands);
            if (small != null) small.gameObject.SetActive(SmallHand);
            if (hour != null) hour.localRotation = Quaternion.Euler(0, 0, -HourDeg);
            if (minute != null) minute.localRotation = Quaternion.Euler(0, 0, -MinuteDeg);
            if (second != null) second.localRotation = Quaternion.Euler(0, 0, -SecondDeg);
            if (small != null) small.localRotation = Quaternion.Euler(0, 0, -SmallDeg);
            if (frostMat != null)
            {
                frostMat.SetColor("_Color", Color.Lerp(new Color(0.8f, 0.95f, 1f, 0.05f), new Color(0.55f, 0.95f, 1f, 0.55f), Frost));
                frostMat.SetFloat("_Sparkle", Mathf.Lerp(0.15f, 1.2f, Frost));
            }
            cam.enabled = Viewer == null || (Viewer.isActiveAndEnabled && Viewer.canvasRenderer.GetInheritedAlpha() > 0.001f);
        }

        /// <summary>Real wall-clock time on the hands, with a ticking second hand.</summary>
        public void ShowRealTime(float tickBounce = 0.12f)
        {
            var now = System.DateTime.Now;
            float sec = now.Second + now.Millisecond / 1000f;
            float whole = Mathf.Floor(sec);
            float frac = sec - whole;
            float tick = Mathf.Clamp01(frac * 9f);
            float snap = whole + tick + tickBounce * Mathf.Sin(tick * Mathf.PI) * (1 - tick);
            SecondDeg = snap * 6f;
            SmallDeg = SecondDeg;
            MinuteDeg = (now.Minute + sec / 60f) * 6f;
            HourDeg = ((now.Hour % 12) + now.Minute / 60f) * 30f;
        }
    }
}
