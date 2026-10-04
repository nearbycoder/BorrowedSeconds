using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BorrowedSeconds.UI
{
    /// <summary>Runtime uGUI construction helpers and procedurally generated sprites.</summary>
    public static class Ui
    {
        static readonly Dictionary<string, TMP_FontAsset> Fonts = new Dictionary<string, TMP_FontAsset>();
        static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();

        public static TMP_FontAsset Font(string name)
        {
            if (Fonts.TryGetValue(name, out var f)) return f;
            f = Resources.Load<TMP_FontAsset>("Fonts/" + name + " SDF");
            Fonts[name] = f;
            return f;
        }

        public static TMP_FontAsset Regular => Font("FiraSans-Regular");
        public static TMP_FontAsset Semi => Font("FiraSans-SemiBold");
        public static TMP_FontAsset Heavy => Font("FiraSans-ExtraBold");
        public static TMP_FontAsset Light => Font("FiraSans-Light");

        public static Canvas MakeCanvas(string name, int order, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = order;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return c;
        }

        public static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Stretch(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static TextMeshProUGUI Text(string name, Transform parent, string text, TMP_FontAsset font, float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.text = text;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        public static Image Img(string name, Transform parent, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var i = go.AddComponent<Image>();
            i.sprite = sprite;
            i.color = color;
            i.raycastTarget = false;
            if (sprite != null && sprite.border.sqrMagnitude > 0) i.type = Image.Type.Sliced;
            return i;
        }

        public static void Fill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        public static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        // ---------------------------------------------------------------- generated sprites

        public static Sprite Circle => Get("circle", () => Gen(128, (x, y) => Disc(x, y, 0.98f)));
        public static Sprite SoftCircle => Get("soft", () => Gen(128, (x, y) => Mathf.Clamp01(1f - Mathf.Sqrt(x * x + y * y)) * Mathf.Clamp01(1f - Mathf.Sqrt(x * x + y * y))));
        public static Sprite Ring => Get("ring", () => Gen(256, (x, y) => Annulus(x, y, 0.84f, 0.98f)));
        public static Sprite ThinRing => Get("thinring", () => Gen(256, (x, y) => Annulus(x, y, 0.93f, 0.985f)));
        public static Sprite Rounded => Get("rounded", () => RoundedSprite(64, 22));
        public static Sprite Pill => Get("pill", () => RoundedSprite(64, 31));
        public static Sprite Keycap => Get("keycap", () => RoundedSprite(32, 7));
        public static Sprite Glow => Get("glow", () => Gen(128, (x, y) => Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(x * x + y * y)), 2.2f)));
        public static Sprite Diamond => Get("diamond", () => Gen(64, (x, y) => Mathf.Clamp01((1f - (Mathf.Abs(x) + Mathf.Abs(y))) * 32f)));
        public static Sprite Ticks => Get("ticks", () => Gen(256, TickFace));
        /// <summary>Opaque at the bottom, fading out towards the top.</summary>
        public static Sprite VGradient => Get("vgrad", () => Gen(256, (x, y) => Mathf.Pow(Mathf.Clamp01((1f - y) * 0.5f), 1.6f)));
        /// <summary>Opaque on the left, fading out to the right.</summary>
        public static Sprite HGradient => Get("hgrad", () => Gen(256, (x, y) => Mathf.SmoothStep(1f, 0f, Mathf.Clamp01((x + 1f) * 0.5f))));

        static Sprite Get(string key, System.Func<Sprite> make)
        {
            if (Sprites.TryGetValue(key, out var s)) return s;
            s = make();
            Sprites[key] = s;
            return s;
        }

        static float Disc(float x, float y, float r)
        {
            float d = Mathf.Sqrt(x * x + y * y);
            return Mathf.Clamp01((r - d) * 64f);
        }

        static float Annulus(float x, float y, float r0, float r1)
        {
            float d = Mathf.Sqrt(x * x + y * y);
            return Mathf.Clamp01((d - r0) * 128f) * Mathf.Clamp01((r1 - d) * 128f);
        }

        static float TickFace(float x, float y)
        {
            float d = Mathf.Sqrt(x * x + y * y);
            float ang = Mathf.Atan2(x, y) / (2f * Mathf.PI) + 0.5f;
            float s60 = Mathf.Abs(Mathf.Repeat(ang * 60f + 0.5f, 1f) - 0.5f);
            float s12 = Mathf.Abs(Mathf.Repeat(ang * 12f + 0.5f, 1f) - 0.5f);
            float minor = (s60 < 0.08f && d > 0.86f && d < 0.95f) ? 0.55f : 0f;
            float major = (s12 < 0.06f && d > 0.78f && d < 0.95f) ? 1f : 0f;
            return Mathf.Max(minor, major);
        }

        static Sprite Gen(int n, System.Func<float, float, float> alpha)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float x = (i + 0.5f) / n * 2f - 1f, y = (j + 0.5f) / n * 2f - 1f;
                byte a = (byte)(Mathf.Clamp01(alpha(x, y)) * 255f);
                px[j * n + i] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        }

        static Sprite RoundedSprite(int n, int radius)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float cx = Mathf.Clamp(i + 0.5f, radius, n - radius);
                float cy = Mathf.Clamp(j + 0.5f, radius, n - radius);
                float d = Vector2.Distance(new Vector2(i + 0.5f, j + 0.5f), new Vector2(cx, cy));
                byte a = (byte)(Mathf.Clamp01(radius - d + 0.5f) * 255f);
                px[j * n + i] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        public static string Secs(int ticks) => (ticks / 20f).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        public static string Secs1(int ticks) => (ticks / 20f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
    }
}
