using BorrowedSeconds.Game;
using BorrowedSeconds.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BorrowedSeconds.UI
{
    /// <summary>
    /// A BS/UIPanel quad: chamfered brass-rimmed enamel with glow, sheen and clock-hand reveal.
    /// Owners set the animated fields (Glow, Sheen, Reveal, RimBoost) every frame and call Apply.
    /// </summary>
    public sealed class Panel
    {
        public enum Style { Window, Card, Plate, Pill, Band, Tag }

        public readonly RectTransform Rt;
        public readonly Image Image;
        readonly Material mat;
        readonly float pad;
        Vector2 size;
        public float Glow, Sheen = -2f, Reveal = 1f, RimBoost;
        Color rimBase;

        static readonly int SizeId = Shader.PropertyToID("_Size"), GlowId = Shader.PropertyToID("_Glow"), SheenId = Shader.PropertyToID("_Sheen"),
            RevealId = Shader.PropertyToID("_Reveal"), RimId = Shader.PropertyToID("_RimColor"), FillTopId = Shader.PropertyToID("_FillTop"),
            FillBotId = Shader.PropertyToID("_FillBottom"), GlowColId = Shader.PropertyToID("_GlowColor");

        public Panel(string name, Transform parent, Vector2 size, Style style)
        {
            pad = 26f;
            Rt = Ui.Rect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            var imgGo = new GameObject("Skin", typeof(RectTransform));
            imgGo.transform.SetParent(Rt, false);
            Image = imgGo.AddComponent<Image>();
            Image.raycastTarget = false;
            mat = Mats.Instance("BS_UIPanel");
            Image.material = mat;
            var irt = (RectTransform)imgGo.transform;
            irt.anchorMin = Vector2.zero;
            irt.anchorMax = Vector2.one;
            irt.offsetMin = new Vector2(-pad, -pad);
            irt.offsetMax = new Vector2(pad, pad);
            irt.SetAsFirstSibling();
            mat.SetFloat("_Pad", pad);
            ApplyStyle(style);
            SetSize(size);
        }

        void ApplyStyle(Style s)
        {
            Color top, bot, rim = Palette.Brass;
            float chamfer = 14f, radius = 4f, rimW = 2.5f, shadow = 0.55f, grain = 0f;
            switch (s)
            {
                case Style.Window:
                    top = new Color(0.1f, 0.115f, 0.23f, 0.97f); bot = new Color(0.035f, 0.042f, 0.095f, 0.97f);
                    chamfer = 26f; rimW = 3f; break;
                case Style.Card:
                    top = new Color(0.115f, 0.13f, 0.25f, 0.96f); bot = new Color(0.06f, 0.07f, 0.15f, 0.96f);
                    chamfer = 14f; rimW = 2f; rim = new Color(0.55f, 0.46f, 0.3f, 1f); break;
                case Style.Plate:
                    top = new Color(0.42f, 0.31f, 0.14f, 0.92f); bot = new Color(0.2f, 0.14f, 0.06f, 0.92f);
                    chamfer = 10f; rimW = 2f; rim = Palette.Gold; shadow = 0.35f; break;
                case Style.Pill:
                    top = new Color(0.08f, 0.1f, 0.2f, 0.92f); bot = new Color(0.04f, 0.05f, 0.11f, 0.92f);
                    chamfer = 0f; radius = 999f; rimW = 1.5f; rim = new Color(0.55f, 0.75f, 0.9f, 0.7f); break;
                case Style.Band:
                    top = new Color(0.07f, 0.08f, 0.16f, 0.9f); bot = new Color(0.03f, 0.035f, 0.08f, 0.9f);
                    chamfer = 0f; radius = 0f; rimW = 2f; break;
                default: // Tag
                    top = new Color(0.07f, 0.12f, 0.2f, 0.94f); bot = new Color(0.03f, 0.06f, 0.11f, 0.94f);
                    chamfer = 8f; rimW = 1.5f; rim = Palette.Ice; break;
            }
            mat.SetColor(FillTopId, top);
            mat.SetColor(FillBotId, bot);
            mat.SetColor(RimId, rim);
            rimBase = rim;
            mat.SetFloat("_Chamfer", chamfer);
            mat.SetFloat("_Radius", radius);
            mat.SetFloat("_RimWidth", rimW);
            mat.SetFloat("_Shadow", shadow);
            mat.SetFloat("_Grain", grain);
            mat.SetColor(GlowColId, s == Style.Tag ? Palette.Ice * 1.5f : Palette.Gold * 1.2f);
        }

        public void SetFill(Color top, Color bottom)
        {
            mat.SetColor(FillTopId, top);
            mat.SetColor(FillBotId, bottom);
        }

        public void SetRim(Color c) { rimBase = c; mat.SetColor(RimId, c); }
        public void SetGlowColor(Color c) => mat.SetColor(GlowColId, c);

        public void SetSize(Vector2 s)
        {
            size = s;
            Rt.sizeDelta = s;
            mat.SetVector(SizeId, new Vector4(s.x + pad * 2, s.y + pad * 2, 0, 0));
            float r = mat.GetFloat("_Radius");
            if (r > 100f) mat.SetFloat("_Radius", Mathf.Min(s.x, s.y) * 0.5f);
        }

        public Vector2 Size => size;

        public void Apply()
        {
            mat.SetFloat(GlowId, Glow);
            mat.SetFloat(SheenId, Sheen);
            mat.SetFloat(RevealId, Reveal);
            mat.SetColor(RimId, Color.Lerp(rimBase, Color.white, RimBoost * 0.45f));
        }
    }

    /// <summary>Small reusable UI parts.</summary>
    public static class Kit
    {
        static Sprite RuleSprite, NotchSprite;

        /// <summary>A brass hairline that fades out at both ends, with a diamond in the middle.</summary>
        public static RectTransform Rule(Transform parent, float width, Color color, bool diamond = true)
        {
            var rt = Ui.Rect("Rule", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, 14));
            if (RuleSprite == null) RuleSprite = Gen(256, 8, (x, y) => Mathf.Clamp01(1f - Mathf.Pow(Mathf.Abs(x), 1.6f)) * Mathf.Clamp01(1f - Mathf.Abs(y) * 2.2f));
            var line = Ui.Img("Line", rt, RuleSprite, color);
            Ui.Place(line.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, 3));
            if (diamond)
            {
                var d = Ui.Img("Diamond", rt, Ui.Diamond, color);
                Ui.Place(d.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(14, 14));
                var d2 = Ui.Img("Inner", d.rectTransform, Ui.Diamond, new Color(0.04f, 0.05f, 0.1f, 1f));
                Ui.Place(d2.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(6, 6));
            }
            return rt;
        }

        static readonly System.Collections.Generic.Dictionary<string, Sprite> coins = new System.Collections.Generic.Dictionary<string, Sprite>();

        /// <summary>The Blender-rendered medal coin for a medal (null for none). Cached.</summary>
        public static Sprite Coin(Medal m, bool large = false) => CoinVariant(m, large ? "_large" : "");

        static Sprite CoinVariant(Medal m, string suffix)
        {
            string n = m switch { Medal.Gold => "gold", Medal.Silver => "silver", Medal.Bronze => "bronze", _ => null };
            if (n == null) return null;
            string key = "UI/medal_" + n + suffix;
            if (coins.TryGetValue(key, out var sp)) return sp;
            var tex = Resources.Load<Texture2D>(key);
            sp = tex == null ? null : Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            coins[key] = sp;
            return sp;
        }

        /// <summary>Icon-sized coin rendered with flatter, more saturated metal for small sizes.</summary>
        public static Sprite CoinSmall(Medal m) => CoinVariant(m, "_icon");

        static Sprite lockSprite;
        public static Sprite Lock
        {
            get
            {
                if (lockSprite != null) return lockSprite;
                var tex = Resources.Load<Texture2D>("UI/lock");
                if (tex != null) lockSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                return lockSprite;
            }
        }

        /// <summary>An empty, recessed coin socket.</summary>
        public static Sprite Socket => NotchSprite != null ? NotchSprite : NotchSprite = Gen(128, 128, (x, y) =>
        {
            float r = Mathf.Sqrt(x * x + y * y);
            return Mathf.Clamp01((0.96f - r) * 40f) * (r > 0.78f ? 0.9f : 0.45f);
        });

        static Sprite Gen(int w, int h, System.Func<float, float, float> alpha)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                float x = (i + 0.5f) / w * 2f - 1f, y = (j + 0.5f) / h * 2f - 1f;
                px[j * w + i] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(x, y)) * 255));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        }
    }

    /// <summary>A horizontal value slider: brass track, glowing gold fill and a knob on a spring.</summary>
    public sealed class SliderWidget
    {
        readonly RectTransform root, fill, knob;
        readonly Image knobGlow;
        Spring pos = Spring.Make(0f, 320f, 26f);
        float w;

        public SliderWidget(Transform parent, float width)
        {
            w = width;
            root = Ui.Rect("Slider", parent, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-170, 0), new Vector2(width, 20));
            var track = Ui.Img("Track", root, Ui.Pill, new Color(0.02f, 0.025f, 0.06f, 0.9f));
            Ui.Place(track.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(width, 8));
            var edge = Ui.Img("Edge", track.rectTransform, Ui.Pill, new Color(Palette.Brass.r, Palette.Brass.g, Palette.Brass.b, 0.35f));
            Ui.Fill(edge.rectTransform);
            edge.rectTransform.offsetMin = new Vector2(-1.5f, -1.5f);
            edge.rectTransform.offsetMax = new Vector2(1.5f, 1.5f);
            edge.transform.SetAsFirstSibling();
            var f = Ui.Img("Fill", root, Ui.Pill, Palette.Gold);
            fill = f.rectTransform;
            Ui.Place(fill, new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(0, 6));
            knobGlow = Ui.Img("KnobGlow", root, Ui.Glow, new Color(Palette.Gold.r, Palette.Gold.g, Palette.Gold.b, 0.5f));
            var k = Ui.Img("Knob", root, Ui.Circle, Palette.Paper);
            knob = k.rectTransform;
            Ui.Place(knob, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18, 18));
            var ring = Ui.Img("Ring", knob, Ui.Ring, Palette.Gold);
            Ui.Fill(ring.rectTransform);
            Ui.Place(knobGlow.rectTransform, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56, 56));
        }

        public void Update(float value01, float hl, float dt, float alpha)
        {
            float x = pos.Step(Mathf.Clamp01(value01), dt) * w;
            fill.sizeDelta = new Vector2(Mathf.Max(6, x), 6);
            knob.anchoredPosition = new Vector2(x, 0);
            knobGlow.rectTransform.anchoredPosition = new Vector2(x, 0);
            knobGlow.color = new Color(Palette.Gold.r, Palette.Gold.g, Palette.Gold.b, 0.6f * hl * alpha);
            knob.localScale = Vector3.one * (1f + 0.25f * hl);
        }
    }

    /// <summary>An on/off switch: a pill whose knob slides on a spring and lights up when on.</summary>
    public sealed class ToggleWidget
    {
        readonly RectTransform knob;
        readonly Image back, knobImg;
        Spring pos = Spring.Make(0f, 380f, 24f);

        public ToggleWidget(Transform parent)
        {
            var root = Ui.Rect("Toggle", parent, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-170, 0), new Vector2(64, 30));
            back = Ui.Img("Back", root, Ui.Pill, new Color(0.03f, 0.035f, 0.08f, 0.95f));
            Ui.Fill(back.rectTransform);
            var rim = Ui.Img("Rim", root, Ui.Pill, new Color(Palette.Brass.r, Palette.Brass.g, Palette.Brass.b, 0.5f));
            Ui.Fill(rim.rectTransform);
            rim.rectTransform.offsetMin = new Vector2(-1.5f, -1.5f);
            rim.rectTransform.offsetMax = new Vector2(1.5f, 1.5f);
            rim.transform.SetAsFirstSibling();
            knobImg = Ui.Img("Knob", root, Ui.Circle, Palette.Paper);
            knob = knobImg.rectTransform;
            Ui.Place(knob, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(15, 0), new Vector2(22, 22));
        }

        public void Update(bool on, float dt)
        {
            float t = pos.Step(on ? 1f : 0f, dt);
            knob.anchoredPosition = new Vector2(Mathf.Lerp(15, 49, t), 0);
            float sq = Mathf.Clamp01(Mathf.Abs(pos.Velocity) * 0.04f);
            knob.localScale = new Vector3(1f + sq * 0.35f, 1f - sq * 0.2f, 1f);
            back.color = Color.Lerp(new Color(0.03f, 0.035f, 0.08f, 0.95f), new Color(0.42f, 0.32f, 0.12f, 0.95f), Mathf.Clamp01(t));
            knobImg.color = Color.Lerp(new Color(0.6f, 0.64f, 0.75f), Palette.Gold, Mathf.Clamp01(t));
        }
    }
}
