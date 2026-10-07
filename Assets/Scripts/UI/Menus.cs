using System;
using System.Collections.Generic;
using BorrowedSeconds.Audio;
using BorrowedSeconds.Game;
using BorrowedSeconds.Sim;
using BorrowedSeconds.View;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BorrowedSeconds.UI
{
    /// <summary>
    /// A full-screen layer on the menu canvas. Shows with its own choreographed intro (driven by
    /// <see cref="Age"/>), hides with a quick fade and slight push-out, and can ask for the board
    /// behind it to be blurred.
    /// </summary>
    public abstract class MenuScreen
    {
        public readonly RectTransform Root;
        protected readonly CanvasGroup Group;
        protected float Shown, Target, Age;
        public bool Visible => Target > 0f;
        /// <summary>How much the 3D board should blur behind this screen (0..1).</summary>
        public virtual float Blur => 0f;
        public float BlurNow => Blur * Mathf.Clamp01(Shown * 1.5f);

        protected MenuScreen(Transform canvas, string name)
        {
            Root = Ui.Stretch(name, canvas);
            Group = Root.gameObject.AddComponent<CanvasGroup>();
            Group.alpha = 0f;
            Root.gameObject.SetActive(false);
        }

        public virtual void Show()
        {
            Target = 1f;
            Age = 0f;
            Root.gameObject.SetActive(true);
            Root.SetAsLastSibling();
        }

        public virtual void Hide() => Target = 0f;

        /// <summary>Fades and, while visible, runs <see cref="Tick"/>. Input only reaches the top screen.</summary>
        public void Update(InputReader input, float dt, bool top)
        {
            Age += dt;
            Shown = Mathf.MoveTowards(Shown, Target, dt * (Target > 0f ? FadeIn : FadeOut));
            Group.alpha = Target > 0f ? Mathf.Clamp01(Shown * 1.4f) : Ease.InCubic(Shown);
            Group.blocksRaycasts = Target > 0f;
            Root.localScale = Vector3.one * (Target > 0f ? 1f : 1f + (1f - Shown) * 0.035f);
            if (Shown <= 0f && Target <= 0f) { Root.gameObject.SetActive(false); return; }
            Tick(input, dt, top && Target > 0f && Age > 0.15f);
        }

        protected virtual float FadeIn => 5f;
        protected virtual float FadeOut => 7f;
        protected abstract void Tick(InputReader input, float dt, bool hasInput);

        protected static Image Shade(Transform parent, float alpha)
        {
            var img = Ui.Img("Shade", parent, null, new Color(0.02f, 0.025f, 0.06f, alpha));
            Ui.Fill(img.rectTransform);
            img.raycastTarget = true;
            return img;
        }

        protected static TextMeshProUGUI Label(Transform parent, string text, TMP_FontAsset font, float size, Color color, Vector2 anchor, Vector2 pos, Vector2 box, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var t = Ui.Text(text, parent, text, font, size, color, align);
            Ui.Place(t.rectTransform, anchor, new Vector2(0.5f, 0.5f), pos, box);
            t.richText = true;
            return t;
        }
    }

    // ==================================================================== menu list

    /// <summary>
    /// Selectable rows (vertical or horizontal) with one shared brass selector plate that springs
    /// between items, staggered entrances, sliders and switches, and a press flash.
    /// </summary>
    public sealed class MenuList
    {
        public sealed class Item
        {
            public string Label;
            public Action Activate;
            public Func<string> Value;
            public Action<int> Adjust;
            public Func<bool> Enabled;
            public Func<float> Fraction;
            public Func<bool> Toggle;
            public RectTransform Rt, Content;
            public CanvasGroup Group;
            public TextMeshProUGUI LabelText, ValueText;
            public SliderWidget Slider;
            public ToggleWidget Switch;
            public Spring Shift = Spring.Make(0f, 300f, 24f);
            public Spring Spacing = Spring.Make(6f, 200f, 20f);
            public float Hl, Punch;
        }

        public readonly RectTransform Root;
        public readonly List<Item> Items = new List<Item>();
        public int Selected;
        readonly float rowH, width, size;
        readonly bool left, horizontal;
        readonly Panel selector;
        readonly RectTransform pip;
        // (a default Spring2 has zero stiffness and never moves: the plate only ever snapped on Show)
        Spring2 selPos = Spring2.Make(Vector2.zero, 280f, 24f);
        Spring selW = Spring.Make(0f, 280f, 24f), selH = Spring.Make(0f, 280f, 24f), pipSpin = Spring.Make(0f, 240f, 13f);
        float pipTarget;
        float flash, sheenT = 2f, lastAge;
        bool placed;
        public float IntroDelay = 0.12f;

        public MenuList(Transform parent, Vector2 anchor, Vector2 pos, float width, float rowH, float size, bool left, bool horizontal = false)
        {
            this.width = width;
            this.rowH = rowH;
            this.size = size;
            this.left = left;
            this.horizontal = horizontal;
            Root = Ui.Rect("Menu", parent, anchor, new Vector2(left ? 0f : 0.5f, 1f), pos, new Vector2(width, 10));
            selector = new Panel("Selector", Root, new Vector2(width, rowH - 10), Panel.Style.Plate);
            selector.Rt.anchorMin = selector.Rt.anchorMax = new Vector2(0.5f, 1f);
            var pipImg = Ui.Img("Pip", selector.Rt, Ui.Diamond, Palette.Gold);
            pip = pipImg.rectTransform;
            Ui.Place(pip, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(20, 0), new Vector2(16, 16));
            var pipCore = Ui.Img("Core", pip, Ui.Diamond, new Color(0.15f, 0.1f, 0.04f, 1f));
            Ui.Place(pipCore.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(7, 7));
        }

        Item NewItem(string label, Action activate)
        {
            var it = new Item { Label = label, Activate = activate };
            int n = Items.Count;
            it.Rt = Ui.Rect("Item " + label, Root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), horizontal ? Vector2.zero : new Vector2(0, -n * rowH), new Vector2(width, rowH - 10));
            it.Group = it.Rt.gameObject.AddComponent<CanvasGroup>();
            it.Content = Ui.Stretch("Content", it.Rt);
            it.LabelText = Ui.Text("Label", it.Content, label, Ui.Semi, size, Palette.Paper, left ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center);
            Ui.Fill(it.LabelText.rectTransform);
            it.LabelText.rectTransform.offsetMin = new Vector2(left ? 44 : 0, 0);
            it.LabelText.characterSpacing = 6;
            Root.sizeDelta = new Vector2(width, (n + 1) * rowH);
            Items.Add(it);
            return it;
        }

        public Item Add(string label, Action activate, Func<string> value = null, Action<int> adjust = null, Func<bool> enabled = null)
        {
            var it = NewItem(label, activate);
            it.Value = value;
            it.Adjust = adjust;
            it.Enabled = enabled;
            if (value != null) AddValueText(it);
            return it;
        }

        public Item AddSlider(string label, Func<float> fraction, Func<string> value, Action<int> adjust)
        {
            var it = NewItem(label, null);
            it.Fraction = fraction;
            it.Value = value;
            it.Adjust = adjust;
            it.Slider = new SliderWidget(it.Content, 240f);
            AddValueText(it);
            return it;
        }

        public Item AddToggle(string label, Func<bool> on, Action toggle)
        {
            var it = NewItem(label, null);
            it.Toggle = on;
            it.Adjust = _ => toggle();
            it.Switch = new ToggleWidget(it.Content);
            it.Value = () => on() ? "ON" : "OFF";
            AddValueText(it);
            return it;
        }

        void AddValueText(Item it)
        {
            it.LabelText.alignment = TextAlignmentOptions.MidlineLeft;
            it.LabelText.rectTransform.offsetMin = new Vector2(44, 0);
            it.ValueText = Ui.Text("Value", it.Content, "", Ui.Semi, size * 0.8f, Palette.Gold, TextAlignmentOptions.MidlineRight);
            Ui.Fill(it.ValueText.rectTransform);
            it.ValueText.rectTransform.offsetMax = new Vector2(-30, 0);
            it.ValueText.characterSpacing = 4;
        }

        /// <summary>Lays items out in a row (horizontal menus) with the given item width.</summary>
        public void Row(float itemWidth, float gap)
        {
            for (int i = 0; i < Items.Count; i++)
            {
                Items[i].Rt.anchoredPosition = new Vector2((i - (Items.Count - 1) * 0.5f) * (itemWidth + gap), 0);
                Items[i].Rt.sizeDelta = new Vector2(itemWidth, rowH - 10);
            }
        }

        bool IsEnabled(Item it) => it.Enabled == null || it.Enabled();

        public void Flash() { flash = 1f; }
        public float SelectorY => selector.Rt.anchoredPosition.y;
        public float RowHeight => rowH;

        /// <summary>Returns true if an item was activated this frame. <paramref name="age"/> drives the entrance.</summary>
        public bool Update(InputReader input, float dt, bool hasInput, float age)
        {
            if (age < lastAge) placed = false; // re-shown: replay the entrance and snap the selector
            lastAge = age;
            bool fired = false;
            int before = Selected;
            if (hasInput)
            {
                int back = horizontal ? Dirs.W : Dirs.N, fwd = horizontal ? Dirs.E : Dirs.S;
                if (input.PressedDir == back) Move(-1);
                if (input.PressedDir == fwd) Move(1);
                var cur = Items[Selected];
                if (!horizontal && cur.Adjust != null && (input.PressedDir == Dirs.E || input.PressedDir == Dirs.W))
                {
                    cur.Adjust(input.PressedDir == Dirs.E ? 1 : -1);
                    cur.Punch = 1f;
                    Sfx.Play("ui_tick");
                }
                int hover = -1;
                for (int i = 0; i < Items.Count; i++)
                    if (RectTransformUtility.RectangleContainsScreenPoint(Items[i].Rt, input.Pointer, null)) hover = i;
                if (hover >= 0 && input.PointerMoved && hover != Selected && IsEnabled(Items[hover]))
                {
                    Selected = hover;
                    Sfx.Play("ui_hover");
                }
                bool click = input.Click && hover >= 0 && hover == Selected;
                if ((input.Confirm || click) && IsEnabled(cur))
                {
                    if (cur.Activate != null) { cur.Activate(); fired = true; Sfx.Play("ui_click"); }
                    else if (cur.Adjust != null) { cur.Adjust(1); Sfx.Play("ui_tick"); }
                    cur.Punch = 1f;
                    flash = 1f;
                }
            }
            if (Selected != before) { sheenT = -0.6f; pipTarget += Selected > before ? -90f : 90f; }

            // selector plate follows the selected row on springs and squashes with its speed
            var target = Items[Selected].Rt;
            Vector2 tPos = target.anchoredPosition + new Vector2(0, -(rowH - 10) * 0.5f);
            Vector2 tSize = target.sizeDelta;
            if (!placed) { selPos.Snap(tPos); selW.Snap(tSize.x); selH.Snap(tSize.y); placed = true; }
            var p = selPos.Step(tPos, dt);
            float w = selW.Step(tSize.x, dt), h = selH.Step(tSize.y, dt);
            float speed = selPos.Velocity.magnitude;
            float squash = Mathf.Clamp01(speed / 2600f);
            selector.Rt.anchoredPosition = p;
            selector.SetSize(new Vector2(w * (1f - squash * 0.06f), h * (1f - squash * 0.25f)));
            flash = Mathf.MoveTowards(flash, 0f, dt * 3f);
            sheenT = Mathf.Min(sheenT + dt * 2.6f, 2f);
            float selIntro = Ease.Stagger(age, Selected, IntroDelay, 0.05f, 0.45f);
            selector.Glow = (0.35f + flash * 1.2f) * selIntro;
            selector.Sheen = sheenT;
            selector.RimBoost = flash;
            selector.Reveal = 1f;
            selector.Apply();
            selector.Image.color = new Color(1, 1, 1, Ease.OutCubic(selIntro));
            pip.GetComponent<Image>().color = new Color(Palette.Gold.r, Palette.Gold.g, Palette.Gold.b, Ease.OutCubic(selIntro));
            pip.localRotation = Quaternion.Euler(0, 0, pipSpin.Step(pipTarget, dt));
            pip.gameObject.SetActive(left || horizontal == false);

            for (int i = 0; i < Items.Count; i++)
            {
                var it = Items[i];
                bool sel = i == Selected;
                bool en = IsEnabled(it);
                float intro = Ease.Stagger(age, i, IntroDelay, 0.05f, 0.45f);
                it.Group.alpha = Ease.OutCubic(intro) * (en ? 1f : 0.35f);
                it.Hl = Mathf.MoveTowards(it.Hl, sel ? 1f : 0f, dt * 9f);
                it.Punch = Mathf.MoveTowards(it.Punch, 0f, dt * 5f);
                float shift = it.Shift.Step(sel && left ? 12f : 0f, dt);
                float introX = (1f - Ease.OutCubic(intro)) * (horizontal ? 0f : -40f);
                float introY = horizontal ? (1f - Ease.OutCubic(intro)) * -24f : 0f;
                it.Content.anchoredPosition = new Vector2(shift + introX, introY);
                it.Content.localScale = Vector3.one * (1f + 0.05f * Ease.OutCubic(it.Punch) + 0.015f * it.Hl);
                it.LabelText.characterSpacing = it.Spacing.Step(sel ? 3f : 7f, dt);
                it.LabelText.color = Color.Lerp(new Color(0.78f, 0.82f, 0.95f, 0.78f), Palette.Paper, it.Hl);
                it.LabelText.text = it.Label;
                if (it.ValueText != null)
                {
                    string v = it.Value != null ? it.Value() : "";
                    it.ValueText.text = !horizontal && it.Adjust != null && sel && it.Slider == null && it.Switch == null ? $"‹  {v}  ›" : v;
                    it.ValueText.color = Color.Lerp(new Color(Palette.Gold.r, Palette.Gold.g, Palette.Gold.b, 0.7f), Palette.Gold, it.Hl);
                }
                it.Slider?.Update(it.Fraction(), it.Hl, dt, it.Group.alpha);
                it.Switch?.Update(it.Toggle(), dt);
            }
            return fired;
        }

        void Move(int d)
        {
            for (int k = 0; k < Items.Count; k++)
            {
                Selected = (Selected + d + Items.Count) % Items.Count;
                if (IsEnabled(Items[Selected])) break;
            }
            Sfx.Play("ui_hover");
        }
    }

    // ==================================================================== title

    public sealed class TitleScreen : MenuScreen
    {
        string promptText;
        /// <summary>The key row under the menu (checks read it).</summary>
        public string Footer => promptText;
        public readonly MenuList Menu;
        readonly RectTransform logo, emblemRt, rule, menuRoot;
        readonly TextMeshProUGUI word1, word2, tag;
        readonly RectTransform promptRow;
        readonly CanvasGroup promptGroup;
        readonly TextFx fx1, fx2, fxTag;
        readonly WatchStage watch;
        readonly RawImage emblem;
        readonly Image emblemGlow;
        readonly Func<string> continueLabel;
        bool played;
        float nextShimmer = 3f;
        readonly List<(RectTransform rt, Image img, Vector2 v, float phase, float size)> motes = new List<(RectTransform, Image, Vector2, float, float)>();

        public TitleScreen(Transform canvas, Action onContinue, Action onLevels, Action onSettings, Action onQuit, Func<string> continueLabel) : base(canvas, "Title")
        {
            this.continueLabel = continueLabel;
            var grad = Ui.Img("Gradient", Root, Ui.HGradient, new Color(0.015f, 0.02f, 0.05f, 0.95f));
            grad.rectTransform.anchorMin = Vector2.zero;
            grad.rectTransform.anchorMax = new Vector2(0.78f, 1f);
            grad.rectTransform.offsetMin = grad.rectTransform.offsetMax = Vector2.zero;

            // slow light motes drifting up through the dark side of the title, like dust in a vault
            var rnd = new System.Random(7);
            for (int i = 0; i < 34; i++)
            {
                float sz = 3f + (float)rnd.NextDouble() * 7f;
                var m = Ui.Img("Mote", Root, Ui.SoftCircle, new Color(1f, 0.85f, 0.6f, 0f));
                Ui.Place(m.rectTransform, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2((float)rnd.NextDouble() * 1400f, (float)rnd.NextDouble() * 1080f), new Vector2(sz, sz) * 2.5f);
                motes.Add((m.rectTransform, m, new Vector2(((float)rnd.NextDouble() - 0.5f) * 10f, 8f + (float)rnd.NextDouble() * 22f), (float)rnd.NextDouble() * 6.28f, sz));
            }
            logo = Ui.Rect("Logo", Root, new Vector2(0, 1), new Vector2(0, 1), new Vector2(90, -70), new Vector2(1000, 330));
            watch = WatchStage.Create(Root.transform.root, 640);
            emblemGlow = Ui.Img("EmblemGlow", logo, Ui.Glow, new Color(1f, 0.75f, 0.4f, 0f));
            Ui.Place(emblemGlow.rectTransform, new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(140, -150), new Vector2(520, 520));
            emblem = watch.Show(logo, new Vector2(330, 330));
            emblemRt = emblem.rectTransform;
            Ui.Place(emblemRt, new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(140, -150), new Vector2(330, 330));

            word1 = Ui.Text("Borrowed", logo, "BORROWED", Ui.Heavy, 116, Palette.Paper, TextAlignmentOptions.TopLeft);
            Ui.Place(word1.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(300, -14), new Vector2(900, 130));
            word1.characterSpacing = 12;
            fx1 = TextFx.On(word1, TextFx.Kind.Drop, 0.05f, 0.6f, 90f);
            word2 = Ui.Text("Seconds", logo, "SECONDS", Ui.Heavy, 116, Palette.Ice, TextAlignmentOptions.TopLeft);
            Ui.Place(word2.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(300, -126), new Vector2(900, 130));
            word2.characterSpacing = 12;
            fx2 = TextFx.On(word2, TextFx.Kind.Stamp, 0.06f, 0.3f, 60f);
            fx2.ShimmerColor = Color.white;
            rule = Kit.Rule(logo, 620, new Color(Palette.Brass.r, Palette.Brass.g, Palette.Brass.b, 0.9f));
            rule.anchorMin = rule.anchorMax = new Vector2(0, 1);
            rule.pivot = new Vector2(0, 0.5f);
            rule.anchoredPosition = new Vector2(306, -262);
            tag = Ui.Text("Tag", logo, "Freeze anything for three seconds.  Then pay them back.", Ui.Light, 30, new Color(0.86f, 0.89f, 1f, 0.9f), TextAlignmentOptions.TopLeft);
            Ui.Place(tag.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(308, -284), new Vector2(1000, 40));
            fxTag = TextFx.On(tag, TextFx.Kind.Type, 0.016f, 0.2f);

            menuRoot = Ui.Rect("MenuRoot", Root, new Vector2(0, 0.5f), new Vector2(0, 1f), new Vector2(110, -90), new Vector2(460, 300));
            Menu = new MenuList(menuRoot, new Vector2(0, 1), Vector2.zero, 460, 70, 34, true);
            Menu.Add("Continue", onContinue);
            Menu.Add("Levels", onLevels);
            Menu.Add("Settings", onSettings);
            Menu.Add("Quit", onQuit);

            promptRow = Ui.Rect("Prompt", Root, new Vector2(0, 0), new Vector2(0, 0), new Vector2(126, 50), new Vector2(900, 34));
            promptGroup = promptRow.gameObject.AddComponent<CanvasGroup>();

        }

        public override void Show()
        {
            base.Show();
            if (played) Age = 1.7f; // the logo is already assembled; only the menu re-enters
            played = true;
            nextShimmer = Age + 2.5f;
        }

        protected override void Tick(InputReader input, float dt, bool hasInput)
        {
            Menu.Items[0].Label = continueLabel();
            float a = Age;
            for (int i = 0; i < motes.Count; i++)
            {
                var m = motes[i];
                var p = m.rt.anchoredPosition + m.v * dt;
                if (p.y > 1110f) p = new Vector2(p.x, -30f);
                m.rt.anchoredPosition = p;
                float tw = 0.5f + 0.5f * Mathf.Sin(Clock.Now * 0.9f + m.phase);
                float fadeX = 1f - Mathf.Clamp01((p.x - 900f) / 500f);
                m.img.color = new Color(1f, 0.82f, 0.55f, 0.22f * tw * fadeX * Ease.OutCubic(a * 0.7f) * (m.size / 10f + 0.3f));
            }
            // emblem: spins in and settles, then sways while showing real time
            float e = Ease.Clamp(a / 1.1f);
            float t = Clock.Now;
            watch.Scale = Mathf.Lerp(0.2f, 1f, Ease.OutBack(e, 1.4f));
            watch.Yaw = Mathf.Lerp(-200f, 0f, Ease.OutCubic(e)) + Mathf.Sin(t * 0.55f) * 16f;
            watch.Pitch = Mathf.Sin(t * 0.41f) * 7f - 4f;
            watch.Roll = Mathf.Sin(t * 0.33f) * 3f;
            watch.ShowRealTime();
            emblem.color = new Color(1, 1, 1, Ease.OutCubic(a * 3f));
            emblemGlow.color = new Color(1f, 0.72f, 0.36f, 0.16f * Ease.OutCubic(e) * (0.85f + 0.15f * Mathf.Sin(t * 1.3f)));
            emblemRt.anchoredPosition = new Vector2(140, -150 + Mathf.Sin(t * 0.9f) * 5f);

            fx1.Age = a - 0.35f;
            fx2.Age = a - 0.8f;
            if (a > nextShimmer) nextShimmer = a + 5f;
            float sh = (a - (nextShimmer - 5f)) * 12f - 2f;
            fx2.Shimmer = a > 1.6f && sh < 12f ? sh : -100f;
            float r = Ease.OutExpo((a - 1.15f) / 0.7f);
            rule.localScale = new Vector3(r, 1, 1);
            fxTag.Age = a - 1.25f;
            string prompt = input.UsingGamepad ? $"<b>{input.Pad.Dpad}</b> choose <b>{input.Pad.South}</b> confirm"
                : $"<b>{input.KeyName(KeyAction.Up)} {input.KeyName(KeyAction.Down)}</b> choose <b>Space</b> confirm";
            if (prompt != promptText) { promptText = prompt; Kit.Keycaps(promptRow, prompt); }
            promptGroup.alpha = Ease.Clamp((a - 2.2f) * 2f);
            Menu.IntroDelay = 1.45f;
            Menu.Update(input, dt, hasInput && a > 1.6f, a);
        }
    }

    // ==================================================================== level select

    public sealed class LevelSelectScreen : MenuScreen
    {
        string lastKeys;
        /// <summary>The key row in the info panel (checks read it).</summary>
        public string Footer => lastKeys;

        string Keys(bool open)
        {
            var input = GameRoot.I.Input;
            var p = input.Pad;
            if (input.UsingGamepad)
                return (pages > 1 ? $"<b>{p.Shoulders}</b> page " : "") + (open ? $"<b>{p.South}</b> play <b>{p.East}</b> back" : $"<b>{p.East}</b> back");
            string turn = pages > 1 ? $"<b>{input.KeyName(KeyAction.AimPrev)} {input.KeyName(KeyAction.AimNext)}</b> page " : "";
            return turn + (open ? "<b>Space</b> play <b>Esc</b> back" : "<b>Esc</b> back");
        }
        sealed class Card
        {
            public Panel Panel;
            public RectTransform Rt, Body;
            public Image Coin, LockImg;
            public TextMeshProUGUI Num, Name, Best;
            public Spring Lift = Spring.Make(0f, 260f, 20f);
            public float Hl, Sheen = 2f, CoinSpin = 10f;
            public int Row, Col, Page, Chapter;
        }

        sealed class Tab
        {
            public Panel Panel;
            public TextMeshProUGUI Label;
            public float Hl;
        }

        readonly List<Card> cards = new List<Card>();
        readonly List<RectTransform> chapterLabels = new List<RectTransform>();
        readonly List<Tab> tabs = new List<Tab>();
        const int PerPage = 4;
        int page, oldPage = -1, pageDir = 1, pages = 1;
        float pageAge;
        readonly LevelCatalog catalog;
        readonly SaveData save;
        readonly Action<int> onPick;
        readonly Action onBack;
        readonly TextMeshProUGUI title, totals, infoNum, infoName, infoHint, infoStats;
        readonly RectTransform infoKeys;
        bool lastOpen = true;
        readonly TextFx titleFx, totalsFx, infoNameFx, infoHintFx;
        readonly RectTransform rule, info;
        readonly Panel infoPanel;
        readonly Image infoCoin;
        readonly CanvasGroup infoGroup;
        int sel, shownInfo = -1;
        float infoAge;
        const int Cols = 5;
        public override float Blur => 1f;

        public LevelSelectScreen(Transform canvas, LevelCatalog catalog, SaveData save, Action<int> onPick, Action onBack) : base(canvas, "LevelSelect")
        {
            this.catalog = catalog;
            this.save = save;
            this.onPick = onPick;
            this.onBack = onBack;
            Shade(Root, 0.5f);
            title = Label(Root, "THE LEDGER", Ui.Heavy, 66, Palette.Paper, new Vector2(0.5f, 1), new Vector2(0, -70), new Vector2(1200, 90));
            title.characterSpacing = 34;
            titleFx = TextFx.On(title, TextFx.Kind.Spread, 0.03f, 0.7f, 120f);
            rule = Kit.Rule(Root, 760, new Color(Palette.Brass.r, Palette.Brass.g, Palette.Brass.b, 0.8f));
            rule.anchorMin = rule.anchorMax = new Vector2(0.5f, 1);
            rule.anchoredPosition = new Vector2(0, -122);
            totals = Label(Root, "", Ui.Semi, 21, new Color(0.8f, 0.84f, 0.95f, 0.85f), new Vector2(0.5f, 1), new Vector2(0, -150), new Vector2(1200, 30));
            totals.characterSpacing = 8;
            totalsFx = TextFx.On(totals, TextFx.Kind.Type, 0.012f, 0.2f);

            const float cw = 252, ch = 124, gx = 20, gy = 22;
            float x0 = -(Cols - 1) * (cw + gx) * 0.5f + 96;
            int chapters = 1;
            foreach (var l in catalog.Levels) chapters = Mathf.Max(chapters, l.Chapter);
            pages = (chapters + PerPage - 1) / PerPage;
            for (int c = 0; c < chapters; c++)
            {
                float y = 228 - (c % PerPage) * (ch + gy);
                var info = LevelCatalog.Chapters[Mathf.Min(c, LevelCatalog.Chapters.Length - 1)];
                var grp = Ui.Rect("Chapter" + c, Root, new Vector2(0.5f, 0.5f), new Vector2(1, 0.5f), new Vector2(x0 - cw * 0.5f - 30, y), new Vector2(200, ch));
                var numeral = Ui.Text("Numeral", grp, Hud.Roman(c + 1), Ui.Heavy, 64, Palette.Gold, TextAlignmentOptions.Right);
                Ui.Place(numeral.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(0, 14), new Vector2(200, 70));
                var cap = Ui.Text("Title", grp, info.Title.ToUpperInvariant(), Ui.Semi, 17, new Color(0.62f, 0.68f, 0.88f), TextAlignmentOptions.Right);
                Ui.Place(cap.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(0, -32), new Vector2(200, 24));
                cap.characterSpacing = 10;
                chapterLabels.Add(grp);
            }
            for (int i = 0; i < catalog.Levels.Count; i++)
            {
                var d = catalog.Levels[i];
                int row = (d.Chapter - 1) % PerPage;
                int col = 0;
                for (int k = 0; k < i; k++) if (catalog.Levels[k].Chapter == d.Chapter) col++;
                var card = new Card { Row = row, Col = col, Page = (d.Chapter - 1) / PerPage, Chapter = d.Chapter };
                card.Rt = Ui.Rect("Card " + d.Id, Root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x0 + col * (cw + gx), 228 - row * (ch + gy)), new Vector2(cw, ch));
                card.Body = Ui.Stretch("Body", card.Rt);
                card.Panel = new Panel("Panel", card.Body, new Vector2(cw, ch), Panel.Style.Card);
                card.Num = Ui.Text("Num", card.Body, $"{d.Chapter}-{col + 1}", Ui.Heavy, 36, Palette.Gold, TextAlignmentOptions.TopLeft);
                Ui.Place(card.Num.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -12), new Vector2(150, 46));
                card.Name = Ui.Text("Name", card.Body, d.Name, Ui.Semi, 23, Palette.Paper, TextAlignmentOptions.TopLeft);
                Ui.Place(card.Name.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -58), new Vector2(220, 32));
                card.Best = Ui.Text("Best", card.Body, "", Ui.Regular, 17, new Color(0.8f, 0.84f, 0.95f, 0.8f), TextAlignmentOptions.BottomLeft);
                Ui.Place(card.Best.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(18, 12), new Vector2(220, 24));
                card.Best.richText = true;
                card.Coin = Ui.Img("Coin", card.Body, Kit.Socket, new Color(0.02f, 0.025f, 0.06f, 0.9f));
                Ui.Place(card.Coin.rectTransform, new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-38, -38), new Vector2(50, 50));
                card.LockImg = Ui.Img("Lock", card.Body, Kit.Lock, Color.white);
                Ui.Place(card.LockImg.rectTransform, new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-44, -8), new Vector2(64, 64));
                cards.Add(card);
            }

            // volume tabs: one per page of four chapters
            for (int pg = 0; pg < pages; pg++)
            {
                var tab = new Tab();
                tab.Panel = new Panel("Tab" + pg, Root, new Vector2(170, 40), Panel.Style.Tag);
                tab.Panel.Rt.anchorMin = tab.Panel.Rt.anchorMax = new Vector2(0.5f, 1);
                tab.Panel.Rt.anchoredPosition = new Vector2((pg - (pages - 1) * 0.5f) * 190f, -196);
                int first = pg * PerPage + 1, last = Mathf.Min(chapters, first + PerPage - 1);
                tab.Label = Ui.Text("Label", tab.Panel.Rt, $"{Hud.Roman(first)} – {Hud.Roman(last)}", Ui.Heavy, 19, Palette.Paper, TextAlignmentOptions.Center);
                Ui.Fill(tab.Label.rectTransform);
                tab.Label.characterSpacing = 8;
                tabs.Add(tab);
            }

            // info bar for the selected level
            info = Ui.Rect("Info", Root, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(1500, 132));
            infoGroup = info.gameObject.AddComponent<CanvasGroup>();
            infoPanel = new Panel("InfoPanel", info, new Vector2(1500, 132), Panel.Style.Window);
            infoPanel.Rt.anchoredPosition = Vector2.zero;
            infoCoin = Ui.Img("InfoCoin", info, Kit.Socket, Color.white);
            Ui.Place(infoCoin.rectTransform, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(84, 0), new Vector2(96, 96));
            infoNum = Ui.Text("InfoNum", info, "", Ui.Heavy, 26, Palette.Gold, TextAlignmentOptions.TopLeft);
            Ui.Place(infoNum.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(156, -18), new Vector2(300, 32));
            infoNum.characterSpacing = 8;
            infoName = Ui.Text("InfoName", info, "", Ui.Heavy, 44, Palette.Paper, TextAlignmentOptions.TopLeft);
            Ui.Place(infoName.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(156, -46), new Vector2(560, 56));
            infoNameFx = TextFx.On(infoName, TextFx.Kind.Rise, 0.018f, 0.35f, 22f);
            infoHint = Ui.Text("InfoHint", info, "", Ui.Regular, 21, new Color(0.84f, 0.87f, 0.97f, 0.9f), TextAlignmentOptions.TopLeft);
            Ui.Place(infoHint.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(560, -22), new Vector2(560, 90));
            infoHint.textWrappingMode = TextWrappingModes.Normal;
            infoHintFx = TextFx.On(infoHint, TextFx.Kind.Type, 0.006f, 0.15f);
            infoStats = Ui.Text("InfoStats", info, "", Ui.Semi, 21, Palette.Paper, TextAlignmentOptions.TopRight);
            Ui.Place(infoStats.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-40, -22), new Vector2(300, 60));
            infoStats.lineSpacing = 8;
            infoKeys = Ui.Rect("InfoKeys", info, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-34, 14), new Vector2(330, 34));
        }

        /// <summary>The info panel's tip. A tip that gives the trick away stays folded until the level
        /// is settled, as it does in the level until H is pressed.</summary>
        public string TipText(LevelDef d)
        {
            if (string.IsNullOrEmpty(d.Hint)) return "";
            var input = GameRoot.I.Input;
            if (d.Spoiler && !save.Cleared(d.Id))
                return $"<color=#8E9AC8>This tip gives the trick away, so it stays folded. Press <color=#FFD27A>{(input.UsingGamepad ? input.Pad.Select : input.KeyName(KeyAction.Hint))}</color> in the level to read it.</color>";
            return input.UsingGamepad && !string.IsNullOrEmpty(d.HintPad) ? input.Pad.Fill(d.HintPad) : d.Hint;
        }

        /// <summary>What the info panel says about the selected level (checks read it).</summary>
        public string InfoTip => infoHint.text;
        public int SelectedIndex => sel;

        public bool Unlocked(int i) => i == 0 || save.Cleared(catalog.Levels[i - 1].Id) || save.Cleared(catalog.Levels[i].Id);

        /// <summary>Moves the selection the way input would, turning the page if needed (scripted tours).</summary>
        public void Select(int i)
        {
            int prev = sel;
            sel = Mathf.Clamp(i, 0, cards.Count - 1);
            if (cards[sel].Page != page)
            {
                oldPage = page;
                pageDir = cards[sel].Page > page ? 1 : -1;
                page = cards[sel].Page;
                pageAge = 0f;
                Sfx.Play("rotor_whoosh");
            }
            if (sel != prev) Sfx.Play("ui_hover");
        }

        public void Show(int focus)
        {
            sel = Mathf.Clamp(focus, 0, cards.Count - 1);
            shownInfo = -1;
            page = cards.Count > 0 ? cards[sel].Page : 0;
            oldPage = -1;
            pageAge = 0f;
            Show();
        }

        protected override void Tick(InputReader input, float dt, bool hasInput)
        {
            float a = Age;
            titleFx.Age = a - 0.05f;
            rule.localScale = new Vector3(Ease.OutExpo((a - 0.25f) / 0.6f), 1, 1);
            totalsFx.Age = a - 0.4f;
            pageAge += dt;
            for (int c = 0; c < chapterLabels.Count; c++)
            {
                var g = chapterLabels[c];
                float t = PageIntro(c / PerPage, c % PerPage, 0.2f, 0.07f, 0.5f, out float dx, out bool live);
                if (g.gameObject.activeSelf != live) g.gameObject.SetActive(live);
                if (!live) continue;
                foreach (var t2 in g.GetComponentsInChildren<TextMeshProUGUI>()) t2.alpha = Ease.OutCubic(t);
                g.pivot = new Vector2(1 + (1 - Ease.OutCubic(t)) * 0.4f - dx / 200f, 0.5f);
            }
            for (int pg = 0; pg < tabs.Count; pg++)
            {
                var tab = tabs[pg];
                tab.Hl = Mathf.MoveTowards(tab.Hl, pg == page ? 1f : 0f, dt * 6f);
                float tin = Ease.OutCubic((a - 0.35f - pg * 0.06f) / 0.4f);
                tab.Panel.Glow = 0.6f * tab.Hl;
                tab.Panel.RimBoost = tab.Hl;
                tab.Panel.SetRim(Color.Lerp(new Color(0.42f, 0.4f, 0.5f), Palette.Gold, tab.Hl));
                tab.Panel.Apply();
                tab.Panel.Image.color = new Color(1, 1, 1, tin * (0.55f + 0.45f * tab.Hl));
                tab.Label.color = Color.Lerp(new Color(0.62f, 0.66f, 0.8f, tin * 0.8f), new Color(Palette.Gold.r, Palette.Gold.g, Palette.Gold.b, tin), tab.Hl);
                tab.Panel.Rt.localScale = Vector3.one * (1f + 0.06f * tab.Hl);
            }

            int cleared = 0, gold = 0, totalBest = 0, totalPar = 0;
            for (int i = 0; i < cards.Count; i++)
            {
                var d = catalog.Levels[i];
                var c = cards[i];
                bool open = Unlocked(i);
                int best = save.Best(d.Id);
                var sol = catalog.SolutionFor(d);
                int par = sol?.Par ?? 0;
                var medal = SaveData.MedalFor(best, par);
                if (best > 0) { cleared++; totalBest += best; totalPar += par; }
                if (medal == Medal.Gold) gold++;
                bool isSel = i == sel;
                if (isSel && c.Hl < 0.05f) { c.Sheen = -0.6f; c.CoinSpin = 0f; c.Rt.SetAsLastSibling(); info.SetAsLastSibling(); }
                c.Hl = Mathf.MoveTowards(c.Hl, isSel ? 1f : 0f, dt * 8f);
                float intro = PageIntro(c.Page, c.Row * 2 + c.Col, 0.3f, 0.035f, 0.55f, out float cdx, out bool live);
                if (c.Rt.gameObject.activeSelf != live) c.Rt.gameObject.SetActive(live);
                if (!live) continue;
                float lift = c.Lift.Step(isSel ? 1f : 0f, dt);
                c.Body.anchoredPosition = new Vector2(cdx, (1f - Ease.OutCubic(intro)) * -34f + lift * 6f);
                c.Body.localScale = Vector3.one * (Mathf.Lerp(0.84f, 1f, Ease.OutBack(intro, 1.6f)) * (1f + 0.06f * lift));
                c.Sheen = Mathf.Min(c.Sheen + dt * 2.2f, 2f);
                c.Panel.Glow = 0.85f * c.Hl;
                c.Panel.RimBoost = c.Hl;
                c.Panel.Sheen = c.Sheen;
                c.Panel.Apply();
                c.Panel.SetFill(open ? new Color(0.115f, 0.13f, 0.25f, 0.96f) : new Color(0.06f, 0.065f, 0.12f, 0.92f),
                                open ? new Color(0.06f, 0.07f, 0.15f, 0.96f) : new Color(0.035f, 0.04f, 0.08f, 0.92f));
                c.Panel.SetRim(open ? Color.Lerp(new Color(0.55f, 0.46f, 0.3f), Palette.Gold, c.Hl) : new Color(0.3f, 0.32f, 0.42f));
                float alpha = Ease.OutCubic(intro);
                c.Panel.Image.color = new Color(1, 1, 1, alpha);
                c.Num.alpha = alpha * (open ? 1f : 0.35f);
                c.Num.color = new Color(Palette.Gold.r, Palette.Gold.g, Palette.Gold.b, c.Num.alpha);
                c.Name.text = open ? d.Name : "Sealed";
                c.Name.color = open ? new Color(Palette.Paper.r, Palette.Paper.g, Palette.Paper.b, alpha) : new Color(0.6f, 0.64f, 0.75f, alpha * 0.6f);
                c.Best.text = !open ? "" : best > 0 ? $"best <b>{Ui.Secs(best)}</b>   par {Ui.Secs(par)}" : $"par {Ui.Secs(par)}";
                c.Best.alpha = alpha;
                var coin = Kit.CoinSmall(medal);
                c.Coin.gameObject.SetActive(open);
                c.Coin.sprite = coin ?? Kit.Socket;
                c.Coin.color = coin != null ? new Color(1, 1, 1, alpha) : new Color(0.02f, 0.025f, 0.06f, 0.85f * alpha);
                c.CoinSpin = Mathf.Min(c.CoinSpin + dt * 2.2f, 10f);
                float spin = c.CoinSpin < 1f ? Mathf.Cos(Ease.OutCubic(c.CoinSpin) * Mathf.PI * 2f) : 1f;
                c.Coin.rectTransform.localScale = new Vector3(coin != null ? spin : 1f, 1f, 1f) * (1f + 0.08f * c.Hl);
                c.LockImg.gameObject.SetActive(!open && Kit.Lock != null);
                c.LockImg.color = new Color(1, 1, 1, alpha * 0.85f);
            }
            totals.text = $"{cleared} / {cards.Count} SETTLED   ·   {gold} TIME THIEF" + (cleared > 0 ? $"   ·   {Ui.Secs(totalBest)}s  vs  par {Ui.Secs(totalPar)}s" : "");
            UpdateInfo(dt, a);

            if (!hasInput) return;
            int prev = sel;
            var cur = cards[sel];
            int chap = cur.Chapter, col = cur.Col, maxChap = cards[cards.Count - 1].Chapter, dir = 0;
            if (input.PressedDir == Dirs.E) col++;
            if (input.PressedDir == Dirs.W) col--;
            if (input.PressedDir == Dirs.S) { chap = chap % maxChap + 1; dir = 1; }
            if (input.PressedDir == Dirs.N) { chap = (chap + maxChap - 2) % maxChap + 1; dir = -1; }
            int flip = (input.CycleNext ? 1 : 0) - (input.CyclePrev ? 1 : 0) + (input.Scroll < 0 ? 1 : 0) - (input.Scroll > 0 ? 1 : 0);
            for (int pg = 0; pg < tabs.Count; pg++)
                if (input.Click && pg != page && RectTransformUtility.RectangleContainsScreenPoint(tabs[pg].Panel.Rt, input.Pointer, null))
                    flip = pg - page;
            if (flip != 0)
            {
                int np = ((cur.Page + flip) % pages + pages) % pages;
                chap = Mathf.Min(np * PerPage + (chap - 1) % PerPage + 1, maxChap);
                dir = flip > 0 ? 1 : -1;
            }
            sel = Find(chap, col);
            if (cards[sel].Page != page)
            {
                oldPage = page;
                page = cards[sel].Page;
                pageDir = dir >= 0 ? 1 : -1;
                pageAge = 0f;
                Sfx.Play("rotor_whoosh");
            }
            int hover = -1;
            for (int i = 0; i < cards.Count; i++)
                if (cards[i].Page == page && cards[i].Rt.gameObject.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(cards[i].Rt, input.Pointer, null)) hover = i;
            if (hover >= 0 && input.PointerMoved) sel = hover;
            if (sel != prev) Sfx.Play("ui_hover");
            bool click = input.Click && hover >= 0 && hover == sel;
            if (input.Confirm || click)
            {
                if (Unlocked(sel)) { Sfx.Play("ui_click"); cards[sel].Lift.Kick(-14f); onPick(sel); }
                else { Sfx.Play("bump"); cards[sel].Lift.Kick(-10f); }
            }
            else if (input.Back) { Sfx.Play("ui_back"); onBack(); }
        }

        /// <summary>Intro factor for an element on page <paramref name="pg"/>: staggered entry on the
        /// current page (sliding in from the side after a page turn), a quick slide-out on the page
        /// being left, and hidden otherwise.</summary>
        float PageIntro(int pg, int order, float delay, float step, float dur, out float dx, out bool live)
        {
            dx = 0f;
            live = true;
            if (pg == page)
            {
                bool turned = oldPage >= 0;
                float t = Ease.Stagger(pageAge, order, turned ? 0.1f : delay, turned ? 0.025f : step, turned ? 0.4f : dur);
                if (turned) dx = (1f - Ease.OutCubic(t)) * 160f * pageDir;
                return t;
            }
            if (pg == oldPage)
            {
                float o = Ease.Clamp(pageAge / 0.2f);
                dx = -Ease.InCubic(o) * 160f * pageDir;
                live = o < 1f;
                return 1f - o;
            }
            live = false;
            return 0f;
        }

        int Find(int chapter, int col)
        {
            int first = -1, n = 0;
            for (int i = 0; i < cards.Count; i++)
                if (cards[i].Chapter == chapter) { if (first < 0) first = i; n++; }
            if (first < 0) return sel;
            return first + ((col % n) + n) % n;
        }

        void UpdateInfo(float dt, float a)
        {
            float intro = Ease.OutCubic((a - 0.55f) / 0.5f);
            info.anchoredPosition = new Vector2(0, 26 - (1f - intro) * 60f);
            infoGroup.alpha = intro;
            infoPanel.Image.color = new Color(1, 1, 1, intro);
            infoPanel.Reveal = 1f;
            infoPanel.Apply();
            var d = catalog.Levels[sel];
            bool open = Unlocked(sel);
            if (sel != shownInfo)
            {
                shownInfo = sel;
                infoAge = 0f;
                int inCh = 1;
                foreach (var l in catalog.Levels) { if (l == d) break; if (l.Chapter == d.Chapter) inCh++; }
                var ch = LevelCatalog.Chapters[Mathf.Clamp(d.Chapter - 1, 0, LevelCatalog.Chapters.Length - 1)];
                infoNum.text = $"{d.Chapter}-{inCh}   ·   {ch.Title.ToUpperInvariant()}";
                infoName.text = open ? d.Name : "Sealed";
                infoHint.text = open ? TipText(d) : "Settle the previous level to break the seal.";
                int best = save.Best(d.Id), par = catalog.SolutionFor(d)?.Par ?? 0;
                var medal = SaveData.MedalFor(best, par);
                infoStats.text = best > 0
                    ? $"best <b>{Ui.Secs(best)}s</b>\n<color=#8E9AC8>par {Ui.Secs(par)}s</color>   <color=#FFD27A>{SaveData.MedalName(medal)}</color>"
                    : $"<color=#8E9AC8>par {Ui.Secs(par)}s</color>\n<color=#8E9AC8>not yet settled</color>";
                infoStats.richText = true;
                var coin = Kit.CoinSmall(medal);
                infoCoin.sprite = coin ?? (open ? Kit.Socket : Kit.Lock);
                infoCoin.color = coin != null || !open ? Color.white : new Color(0.02f, 0.025f, 0.06f, 0.9f);
            }
            else if (open && infoHint.text != TipText(d)) infoHint.text = TipText(d); // the device (and its hint key) changed
            string keys = Keys(open); // follows the device as well as the selection
            if (keys != lastKeys)
            {
                lastKeys = keys;
                float w = Kit.Keycaps(infoKeys, keys);
                foreach (RectTransform c in infoKeys) c.anchoredPosition += new Vector2(330 - w, 0);
                lastOpen = open;
            }
            infoAge += dt;
            infoNameFx.Age = infoAge;
            infoHintFx.Age = infoAge - 0.1f;
            infoNum.alpha = intro * Ease.OutCubic(infoAge * 4f);
            infoStats.alpha = intro * Ease.OutCubic(infoAge * 3f);
            float pop = Ease.OutBack(Ease.Clamp(infoAge * 4f), 2f);
            infoCoin.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, pop);
            infoCoin.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(Clock.Now * 1.4f) * 3f);
            var ic = infoCoin.color;
            infoCoin.color = new Color(ic.r, ic.g, ic.b, intro);
        }
    }

    // ==================================================================== pause & settings

    /// <summary>Shared look for a centred window: brass-rimmed panel with a clock-hand reveal.</summary>
    public abstract class WindowScreen : MenuScreen
    {
        protected readonly Panel Window;
        protected readonly RectTransform Body;
        protected readonly TextMeshProUGUI Heading, Kicker;
        readonly TextFx headingFx;
        readonly RectTransform rule;

        protected WindowScreen(Transform canvas, string name, Vector2 size, string kicker, string heading, float shade) : base(canvas, name)
        {
            Shade(Root, shade);
            Window = new Panel("Window", Root, size, Panel.Style.Window);
            Body = Ui.Stretch("Body", Window.Rt);
            Kicker = Ui.Text("Kicker", Body, kicker, Ui.Semi, 18, Palette.Ice, TextAlignmentOptions.Top);
            Ui.Place(Kicker.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -34), new Vector2(size.x, 26));
            Kicker.characterSpacing = 26;
            Heading = Ui.Text("Heading", Body, heading, Ui.Heavy, 60, Palette.Paper, TextAlignmentOptions.Top);
            Ui.Place(Heading.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -60), new Vector2(size.x, 80));
            Heading.characterSpacing = 26;
            headingFx = TextFx.On(Heading, TextFx.Kind.Spread, 0.025f, 0.55f, 80f);
            rule = Kit.Rule(Body, size.x * 0.62f, new Color(Palette.Brass.r, Palette.Brass.g, Palette.Brass.b, 0.8f));
            rule.anchorMin = rule.anchorMax = new Vector2(0.5f, 1);
            rule.anchoredPosition = new Vector2(0, -150);
        }

        public override float Blur => 1f;
        protected override float FadeIn => 9f;

        /// <summary>Animates the window chrome; returns the age at which content may enter.</summary>
        protected float AnimateWindow()
        {
            float a = Age;
            float r = Ease.OutCubic(a / 0.42f);
            Window.Reveal = r;
            Window.Glow = 0.12f + 0.55f * (1f - Ease.OutCubic((a - 0.3f) / 0.6f));
            Window.Sheen = -0.6f + a * 2.2f;
            Window.Apply();
            Window.Rt.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, Ease.OutBack(a / 0.5f, 1.3f));
            Kicker.alpha = Ease.OutCubic((a - 0.2f) * 3f);
            headingFx.Age = a - 0.12f;
            rule.localScale = new Vector3(Ease.OutExpo((a - 0.25f) / 0.6f), 1, 1);
            return a;
        }
    }

    public sealed class PauseScreen : WindowScreen
    {
        public readonly MenuList Menu;
        readonly Action onResume;

        public PauseScreen(Transform canvas, Action onResume, Action onRestart, Action onWatch, Action onLevels, Action onSettings, Action onTitle)
            : base(canvas, "Pause", new Vector2(560, 692), "TIME  STOPPED", "PAUSED", 0.3f)
        {
            this.onResume = onResume;
            Menu = new MenuList(Body, new Vector2(0.5f, 1), new Vector2(0, -186), 420, 72, 32, false);
            Menu.Add("Resume", onResume);
            Menu.Add("Restart", onRestart);
            Menu.Add("Watch solution", onWatch);
            Menu.Add("Levels", onLevels);
            Menu.Add("Settings", onSettings);
            Menu.Add("Title", onTitle);
            Menu.IntroDelay = 0.25f;
        }

        public override void Show() { base.Show(); Menu.Selected = 0; }

        protected override void Tick(InputReader input, float dt, bool hasInput)
        {
            float a = AnimateWindow();
            if (hasInput && (input.Back || input.Pause)) { Sfx.Play("ui_back"); onResume(); return; }
            Menu.Update(input, dt, hasInput, a);
        }
    }

    public sealed class SettingsScreen : WindowScreen
    {
        readonly MenuList menu;
        readonly Action onBack;
        readonly RectTransform footRow;
        readonly CanvasGroup footGroup;

        /// <summary>Index of the Controls row (scripted tours select rows by index).</summary>
        public const int ControlsRow = 10;
        string footText;
        /// <summary>The key row under the window (checks read it).</summary>
        public string Footer => footText;

        public SettingsScreen(Transform canvas, SaveData save, Action apply, Action applyDisplay, Action onBack, Action onControls)
            : base(canvas, "Settings", new Vector2(860, 1004), "ADJUST  THE  MECHANISM", "SETTINGS", 0.35f)
        {
            this.onBack = onBack;
            menu = new MenuList(Body, new Vector2(0.5f, 1), new Vector2(-370, -180), 740, 62, 27, true);
            string Pct(float v) => Mathf.RoundToInt(v * 100) + "%";
            float Step(float v, int d) => Mathf.Clamp01(Mathf.Round((v + d * 0.1f) * 10f) / 10f);
            menu.AddSlider("Master volume", () => save.master, () => Pct(save.master), d => { save.master = Step(save.master, d); apply(); });
            menu.AddSlider("Music", () => save.music, () => Pct(save.music), d => { save.music = Step(save.music, d); apply(); });
            menu.AddSlider("Effects", () => save.sfx, () => Pct(save.sfx), d => { save.sfx = Step(save.sfx, d); apply(); });
            // Fullscreen, or a window of a size that fits the desktop (each step applies at once)
            menu.Add("Display", null, () => DisplayOptions.Label(save, DisplayOptions.Desktop), d =>
            {
                var desk = DisplayOptions.Desktop;
                int n = DisplayOptions.Fitting(desk).Count + 1;
                DisplayOptions.Choose(save, desk, (DisplayOptions.Choice(save, desk) + d + n) % n);
                applyDisplay();
            });
            var scales = DisplayOptions.RenderScales;
            menu.AddSlider("Render resolution", () => DisplayOptions.ScaleStep(save.renderScale) / (float)(scales.Length - 1),
                () => Pct(scales[DisplayOptions.ScaleStep(save.renderScale)]),
                d => { save.renderScale = scales[Mathf.Clamp(DisplayOptions.ScaleStep(save.renderScale) + d, 0, scales.Length - 1)]; apply(); });
            menu.AddToggle("Screen shake", () => save.shake, () => { save.shake = !save.shake; apply(); });
            menu.AddToggle("Reduce flashing", () => save.reduceFlashing, () => { save.reduceFlashing = !save.reduceFlashing; apply(); });
            menu.AddSlider("Focus slow-motion", () => (save.focus - 0.1f) / 0.5f, () => Pct(save.focus),
                d => { save.focus = Mathf.Clamp(Mathf.Round((save.focus + d * 0.1f) * 10f) / 10f, 0.1f, 0.6f); apply(); });
            float[] speeds = { 0.5f, 0.7f, 0.85f, 1f };
            int SpeedStep() { int k = 0; for (int i = 0; i < speeds.Length; i++) if (Mathf.Abs(speeds[i] - save.speed) < Mathf.Abs(speeds[k] - save.speed)) k = i; return k; }
            menu.AddSlider("Game speed", () => SpeedStep() / (float)(speeds.Length - 1), () => Pct(speeds[SpeedStep()]),
                d => { save.speed = speeds[Mathf.Clamp(SpeedStep() + d, 0, speeds.Length - 1)]; apply(); });
            menu.AddToggle("Toggle Focus (press, not hold)", () => save.focusToggle, () => { save.focusToggle = !save.focusToggle; apply(); });
            menu.Add("Controls", onControls, () => "keyboard  ›");
            menu.Add("Back", onBack);
            menu.IntroDelay = 0.25f;
            footRow = Ui.Rect("Foot", Body, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 22), new Vector2(700, 34));
            footGroup = footRow.gameObject.AddComponent<CanvasGroup>();

        }

        public override void Show() { base.Show(); menu.Selected = 0; }
        public MenuList Menu => menu;

        protected override void Tick(InputReader input, float dt, bool hasInput)
        {
            float a = AnimateWindow();
            string foot = input.UsingGamepad ? $"<b>{input.Pad.Dpad}</b> choose and adjust <b>{input.Pad.East}</b> back"
                : $"<b>{input.KeyName(KeyAction.Up)} {input.KeyName(KeyAction.Down)}</b> choose <b>{input.KeyName(KeyAction.Left)} {input.KeyName(KeyAction.Right)}</b> adjust <b>Esc</b> back";
            if (foot != footText) { footText = foot; Kit.Keycaps(footRow, foot, 1f, true); }
            footGroup.alpha = Ease.OutCubic((a - 0.8f) * 2f);
            if (hasInput && input.Back) { Sfx.Play("ui_back"); onBack(); return; }
            menu.Update(input, dt, hasInput, a);
        }
    }

    // ==================================================================== controls

    /// <summary>Settings > Controls: rebinds the keyboard (KeyBindings). Pick a row, press a key.</summary>
    public sealed class ControlsScreen : WindowScreen
    {
        readonly MenuList menu;
        readonly Action onBack;
        readonly Action<KeyAction, Key> bind;
        readonly RectTransform footRow;
        readonly CanvasGroup footGroup;
        string footText;
        /// <summary>The key row under the window (checks read it).</summary>
        public string Footer => footText;
        int listening = -1;
        float listenAge;
        public string Notice = "";
        float noticeT;
        readonly TextMeshProUGUI notice;

        public ControlsScreen(Transform canvas, Func<Key[]> keys, Action<KeyAction, Key> bind, Action reset, Action onBack)
            : base(canvas, "Controls", new Vector2(860, 1000), "KEYBOARD", "CONTROLS", 0.35f)
        {
            this.onBack = onBack;
            this.bind = bind;
            menu = new MenuList(Body, new Vector2(0.5f, 1), new Vector2(-370, -172), 740, 52, 24, true);
            for (int i = 0; i < KeyBindings.Count; i++)
            {
                int a = i;
                menu.Add(KeyBindings.Labels[i], () => { listening = a; listenAge = 0f; },
                    () => listening == a ? "press a key" : InputReader.KeyLabel(keys()[a]));
            }
            menu.Add("Reset to defaults", () => { reset(); Say("Default keys restored"); });
            menu.Add("Back", onBack);
            menu.IntroDelay = 0.25f;
            notice = Ui.Text("Notice", Body, "", Ui.Semi, 18, Palette.Ice, TextAlignmentOptions.Center);
            Ui.Place(notice.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 64), new Vector2(760, 26));
            footRow = Ui.Rect("Foot", Body, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 22), new Vector2(700, 34));
            footGroup = footRow.gameObject.AddComponent<CanvasGroup>();
        }

        public MenuList Menu => menu;
        public bool Listening => listening >= 0;

        public override void Show() { base.Show(); menu.Selected = 0; listening = -1; Say(""); }

        void Say(string text) { Notice = text; noticeT = 0f; }

        protected override void Tick(InputReader input, float dt, bool hasInput)
        {
            float a = AnimateWindow();
            noticeT += dt;
            notice.text = Notice;
            notice.alpha = Notice.Length > 0 ? Mathf.Clamp01(3f - noticeT * 0.6f) : 0f;
            string foot = listening >= 0 ? (input.UsingGamepad ? $"press a key  <b>{input.Pad.East}</b> cancel" : "<b>Esc</b> cancel")
                : input.UsingGamepad ? $"<b>{input.Pad.Dpad}</b> choose <b>{input.Pad.South}</b> rebind <b>{input.Pad.East}</b> back"
                : "<b>↑ ↓</b> choose <b>Enter</b> rebind <b>Esc</b> back";
            if (foot != footText) { footText = foot; Kit.Keycaps(footRow, foot, 1f, true); }
            footGroup.alpha = Ease.OutCubic((a - 0.8f) * 2f);
            if (listening >= 0)
            {
                listenAge += dt;
                if (hasInput && listenAge > 0.1f)
                {
                    var key = input.PressedKey;
                    if (input.Back) { listening = -1; Sfx.Play("ui_back"); Say(""); }
                    else if (key != Key.None && KeyBindings.Reserved(key)) { Sfx.Play("denied", 0.7f); Say($"{InputReader.KeyLabel(key)} keeps its own job: pick another key"); }
                    else if (key != Key.None)
                    {
                        var action = (KeyAction)listening;
                        listening = -1;
                        bind(action, key);
                        Sfx.Play("ui_click");
                        Say($"{KeyBindings.Labels[(int)action]}: {InputReader.KeyLabel(key)}");
                    }
                }
                menu.Update(input, dt, false, a);
                return;
            }
            if (hasInput && input.Back) { Sfx.Play("ui_back"); onBack(); return; }
            menu.Update(input, dt, hasInput, a);
        }

        /// <summary>Scripted runs: start listening on a row as if it had been chosen.</summary>
        public void ListenFor(KeyAction action) { menu.Selected = (int)action; listening = (int)action; listenAge = 0f; }
    }

    // ==================================================================== level complete

    public sealed class CompleteScreen : MenuScreen
    {
        public readonly MenuList Menu;
        readonly Panel window, ribbon;
        readonly TextMeshProUGUI title, time, par, ribbonText, barLabel;
        readonly TextFx titleFx;
        readonly RectTransform coinRt, ringRt, barFill, goldMark, silverMark, body;
        readonly Image coin, ring, coinShadow;
        readonly List<(RectTransform rt, Vector2 vel, float spin)> sparks = new List<(RectTransform, Vector2, float)>();
        readonly Action<float> shake;
        Medal medal;
        int ticks, parTicks, lastShown = -1;
        bool stamped, ribbonOn;
        public override float Blur => 0.6f;

        public CompleteScreen(Transform canvas, Action onNext, Action onRetry, Action onLevels, Action<float> shake = null) : base(canvas, "Complete")
        {
            this.shake = shake;
            Shade(Root, 0.25f);
            window = new Panel("Window", Root, new Vector2(1040, 470), Panel.Style.Window);
            window.Rt.anchoredPosition = new Vector2(0, 30);
            body = Ui.Stretch("Body", window.Rt);
            title = Ui.Text("Title", body, "SETTLED", Ui.Heavy, 92, Palette.Gold, TextAlignmentOptions.TopLeft);
            Ui.Place(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(56, -30), new Vector2(700, 110));
            title.characterSpacing = 26;
            titleFx = TextFx.On(title, TextFx.Kind.Stamp, 0.07f, 0.22f, 70f);
            time = Ui.Text("Time", body, "", Ui.Heavy, 78, Palette.Paper, TextAlignmentOptions.TopLeft);
            Ui.Place(time.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -150), new Vector2(520, 90));
            par = Ui.Text("Par", body, "", Ui.Semi, 24, new Color(0.8f, 0.84f, 0.95f, 0.85f), TextAlignmentOptions.TopLeft);
            Ui.Place(par.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(62, -244), new Vector2(560, 32));
            par.characterSpacing = 6;
            par.richText = true;

            // par bar: how close to the solver's time, with medal thresholds marked
            var bar = Ui.Rect("Bar", body, new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(62, -300), new Vector2(560, 14));
            var track = Ui.Img("Track", bar, Ui.Pill, new Color(0.02f, 0.025f, 0.06f, 0.95f));
            Ui.Fill(track.rectTransform);
            var fillImg = Ui.Img("Fill", bar, Ui.Pill, Palette.Gold);
            barFill = fillImg.rectTransform;
            Ui.Place(barFill, new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(0, 10));
            goldMark = Marker(bar, Palette.Gold);
            silverMark = Marker(bar, new Color(0.82f, 0.86f, 0.95f));
            barLabel = Ui.Text("BarLabel", body, "", Ui.Regular, 17, new Color(0.7f, 0.75f, 0.9f, 0.8f), TextAlignmentOptions.TopLeft);
            Ui.Place(barLabel.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(62, -318), new Vector2(560, 24));
            barLabel.characterSpacing = 6;

            ribbon = new Panel("Ribbon", body, new Vector2(230, 40), Panel.Style.Tag);
            ribbon.Rt.anchorMin = ribbon.Rt.anchorMax = new Vector2(0, 1);
            ribbon.Rt.anchoredPosition = new Vector2(530, -186);
            ribbonText = Ui.Text("Text", ribbon.Rt, "", Ui.Heavy, 17, Palette.Ice);
            Ui.Fill(ribbonText.rectTransform);
            ribbonText.characterSpacing = 10;

            // the medal coin: drops, spins, stamps
            coinShadow = Ui.Img("CoinShadow", body, Ui.Glow, new Color(0, 0, 0, 0));
            Ui.Place(coinShadow.rectTransform, new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-210, 10), new Vector2(300, 300));
            ring = Ui.Img("Shock", body, Ui.ThinRing, new Color(1, 1, 1, 0));
            ringRt = ring.rectTransform;
            Ui.Place(ringRt, new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-210, 30), new Vector2(240, 240));
            coin = Ui.Img("Coin", body, null, Color.white);
            coinRt = coin.rectTransform;
            Ui.Place(coinRt, new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-210, 30), new Vector2(250, 250));
            for (int i = 0; i < 14; i++)
            {
                var s = Ui.Img("Spark", body, Ui.Diamond, Palette.Gold);
                Ui.Place(s.rectTransform, new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-210, 30), new Vector2(10, 22));
                s.gameObject.SetActive(false);
                sparks.Add((s.rectTransform, Vector2.zero, 0f));
            }

            Menu = new MenuList(body, new Vector2(0.5f, 0), new Vector2(0, 104), 1000, 68, 28, false, true);
            Menu.Add("Next", onNext);
            Menu.Add("Retry", onRetry);
            Menu.Add("Levels", onLevels);
            Menu.Row(260, 26);
            Menu.IntroDelay = 1.6f;
        }

        static RectTransform Marker(Transform bar, Color c)
        {
            var m = Ui.Img("Mark", bar, Ui.Diamond, c);
            Ui.Place(m.rectTransform, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(16, 16));
            return m.rectTransform;
        }

        public void Show(int ticks, int parTicks, int prevBest, bool isLast)
        {
            this.ticks = ticks;
            this.parTicks = parTicks;
            medal = SaveData.MedalFor(ticks, parTicks);
            coin.sprite = Kit.Coin(medal, true);
            Menu.Items[0].Label = isLast ? "Finish" : "Next";
            Menu.Selected = 0;
            ribbonOn = prevBest == 0 || ticks < prevBest;
            ribbonText.text = prevBest == 0 ? "FIRST  CLEAR" : ticks < prevBest ? "NEW  BEST" : $"BEST  {Ui.Secs(prevBest)}";
            int diff = ticks - parTicks;
            par.text = parTicks > 0 ? $"PAR {Ui.Secs(parTicks)}s   ·   {(diff <= 0 ? "<color=#FFD27A>on par</color>" : "+" + Ui.Secs(diff) + "s")}" : "";
            stamped = false;
            lastShown = -1;
            float gx = parTicks > 0 ? (float)parTicks / (parTicks + 20) : 0.95f;
            float sx = parTicks > 0 ? (float)parTicks / (parTicks + 80) : 0.8f;
            goldMark.anchoredPosition = new Vector2(560 * gx, 0);
            silverMark.anchoredPosition = new Vector2(560 * sx, 0);
            barLabel.text = $"<color=#FFD27A>TIME THIEF</color>  within par + 1.0s       <color=#D1DBF2>SILVER</color>  within par + 4.0s";
            barLabel.richText = true;
            foreach (var s in sparks) s.rt.gameObject.SetActive(false);
            Show();
        }

        protected override void Tick(InputReader input, float dt, bool hasInput)
        {
            float a = Age;
            window.Reveal = Ease.OutCubic(a / 0.45f);
            window.Glow = 0.3f + 0.9f * Mathf.Clamp01(1f - (a - 1.2f) * 1.5f) * (a > 1.2f ? 1f : 0.3f);
            window.Sheen = -0.6f + (a - 0.2f) * 2f;
            window.Apply();
            window.Rt.localScale = Vector3.one * Mathf.Lerp(0.95f, 1f, Ease.OutBack(a / 0.5f));
            titleFx.Age = a - 0.25f;

            // odometer count-up of the run time with ticks
            float ct = Ease.OutCubic((a - 0.55f) / 0.9f);
            int shown = Mathf.RoundToInt(ticks * ct);
            time.text = $"<mspace=0.62em>{Ui.Secs(shown)}</mspace><size=60%>s</size>";
            time.richText = true;
            time.alpha = Ease.Clamp((a - 0.5f) * 5f);
            if (shown / 4 != lastShown / 4 && a > 0.55f && ct < 1f) Sfx.Play("ui_tick", 0.35f, 1f + ct * 0.6f);
            lastShown = shown;
            par.alpha = Ease.OutCubic((a - 1.2f) * 3f);
            float fill = parTicks > 0 ? Mathf.Min(1f, (float)parTicks / Mathf.Max(1, shown)) : 1f;
            barFill.sizeDelta = new Vector2(560 * fill * ct, 10);
            barFill.GetComponent<Image>().color = medal == Medal.Gold ? Palette.Gold : medal == Medal.Silver ? new Color(0.82f, 0.86f, 0.95f) : new Color(0.8f, 0.52f, 0.32f);
            barLabel.alpha = Ease.OutCubic((a - 1.3f) * 2f) * 0.8f;

            // ribbon slides in
            float rb = ribbonOn ? Ease.OutBack((a - 1.5f) / 0.4f, 2f) : 0f;
            ribbon.Rt.localScale = new Vector3(rb, rb, 1);
            ribbon.Glow = 0.6f * rb;
            ribbon.Apply();

            // the coin: falls in spinning, slams, shockwave and sparks
            float ca = a - 1.05f;
            if (coin.sprite != null && ca > 0f)
            {
                float fall = Ease.Clamp(ca / 0.32f);
                float sc = Mathf.Lerp(2.6f, 1f, Ease.InCubic(fall));
                float spin = Mathf.Cos(fall * Mathf.PI * 4f);
                if (fall >= 1f)
                {
                    if (!stamped) Stamp();
                    float settle = ca - 0.32f;
                    sc = 1f + Mathf.Sin(settle * 18f) * Mathf.Exp(-settle * 7f) * 0.06f;
                    spin = 1f;
                }
                coinRt.localScale = new Vector3(sc * Mathf.Max(0.08f, Mathf.Abs(spin)), sc, 1f);
                coinRt.localRotation = Quaternion.Euler(0, 0, fall < 1f ? Mathf.Lerp(-30f, -10f, fall) : -10f + Mathf.Sin(Clock.Now * 1.2f) * 2f);
                coin.color = new Color(1, 1, 1, Ease.Clamp(ca * 6f));
                coinShadow.color = new Color(0, 0, 0, 0.5f * fall);
                float ra = ca - 0.32f;
                if (ra > 0f)
                {
                    float rk = Ease.OutCubic(ra / 0.6f);
                    ringRt.localScale = Vector3.one * Mathf.Lerp(0.9f, 1.9f, rk);
                    ring.color = new Color(Palette.Gold.r, Palette.Gold.g, Palette.Gold.b, (1f - rk) * 0.9f);
                }
            }
            else
            {
                coin.color = new Color(1, 1, 1, 0);
                ring.color = new Color(1, 1, 1, 0);
            }
            for (int i = 0; i < sparks.Count; i++)
            {
                var s = sparks[i];
                if (!s.rt.gameObject.activeSelf) continue;
                s.vel += new Vector2(0, -900f) * dt;
                s.rt.anchoredPosition += s.vel * dt;
                s.rt.localRotation = Quaternion.Euler(0, 0, s.rt.localEulerAngles.z + s.spin * dt);
                var img = s.rt.GetComponent<Image>();
                img.color = new Color(img.color.r, img.color.g, img.color.b, Mathf.Max(0, img.color.a - dt * 1.4f));
                sparks[i] = s;
            }

            int prev = Menu.Selected;
            if (hasInput)
            {
                if (input.Restart) { Menu.Items[1].Activate(); return; }
                if (input.Back) { Menu.Items[2].Activate(); return; }
            }
            Menu.Update(input, dt, hasInput && a > 1.5f, a);
        }

        void Stamp()
        {
            stamped = true;
            Sfx.Play("stamp");
            shake?.Invoke(0.25f);
            for (int i = 0; i < sparks.Count; i++)
            {
                float ang = (i / (float)sparks.Count) * Mathf.PI * 2f + UnityEngine.Random.Range(-0.2f, 0.2f);
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                var rt = sparks[i].rt;
                rt.gameObject.SetActive(true);
                rt.anchoredPosition = new Vector2(-210, 30) + dir * 110f;
                rt.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
                var img = rt.GetComponent<Image>();
                img.color = medal == Medal.Gold ? Palette.Gold : medal == Medal.Silver ? new Color(0.85f, 0.9f, 1f) : new Color(0.95f, 0.6f, 0.38f);
                sparks[i] = (rt, dir * UnityEngine.Random.Range(380f, 620f) + new Vector2(0, 260f), UnityEngine.Random.Range(-500f, 500f));
            }
        }
    }

    // ==================================================================== chapter card & ending

    public sealed class ChapterCard : MenuScreen
    {
        readonly TextMeshProUGUI small, big, epigraph;
        readonly RectTransform dial;
        readonly Image dialTicks, dialRing;
        readonly TextFx smallFx, bigFx, epiFx;
        readonly RectTransform rule;
        Action done;
        public override float Blur => 1f;

        public ChapterCard(Transform canvas) : base(canvas, "ChapterCard")
        {
            Shade(Root, 0.72f);
            dial = Ui.Rect("Dial", Root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(1250, 1250));
            dialTicks = Ui.Img("Ticks", dial, Ui.Ticks, new Color(Palette.Gold.r, Palette.Gold.g, Palette.Gold.b, 0f));
            Ui.Fill(dialTicks.rectTransform);
            dialRing = Ui.Img("Ring", dial, Ui.ThinRing, new Color(Palette.Brass.r, Palette.Brass.g, Palette.Brass.b, 0f));
            Ui.Fill(dialRing.rectTransform);
            small = Ui.Text("Small", Root, "", Ui.Semi, 26, new Color(0.75f, 0.8f, 0.95f, 0.95f));
            Ui.Place(small.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 130), new Vector2(1200, 40));
            small.characterSpacing = 40;
            smallFx = TextFx.On(small, TextFx.Kind.Rise, 0.03f, 0.5f, 24f);
            big = Ui.Text("Big", Root, "", Ui.Heavy, 140, Palette.Gold);
            Ui.Place(big.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 30), new Vector2(1800, 170));
            big.characterSpacing = 42;
            bigFx = TextFx.On(big, TextFx.Kind.Spread, 0.03f, 1.1f, 260f);
            rule = Kit.Rule(Root, 640, new Color(Palette.Brass.r, Palette.Brass.g, Palette.Brass.b, 0.9f));
            rule.anchoredPosition = new Vector2(0, -62);
            epigraph = Ui.Text("Epigraph", Root, "", Ui.Light, 40, Palette.Paper);
            Ui.Place(epigraph.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -114), new Vector2(1600, 60));
            epigraph.fontStyle = FontStyles.Italic;
            epiFx = TextFx.On(epigraph, TextFx.Kind.Type, 0.028f, 0.25f);
        }

        public void Show(int chapter, Action onDone)
        {
            var info = LevelCatalog.Chapters[Mathf.Clamp(chapter - 1, 0, LevelCatalog.Chapters.Length - 1)];
            small.text = $"CHAPTER  {Hud.Roman(chapter)}";
            big.text = info.Title.ToUpperInvariant();
            epigraph.text = $"“{info.Epigraph}”";
            done = onDone;
            Show();
            Sfx.Play("chapter");
        }

        protected override float FadeIn => 4f;
        protected override float FadeOut => 2.5f;

        protected override void Tick(InputReader input, float dt, bool hasInput)
        {
            float a = Age;
            dial.localScale = Vector3.one * Mathf.Lerp(1.25f, 1f, Ease.OutCubic(a / 2.5f));
            dial.localRotation = Quaternion.Euler(0, 0, -a * 6f);
            float dv = Ease.OutCubic(a * 1.2f);
            dialTicks.color = new Color(Palette.Gold.r, Palette.Gold.g, Palette.Gold.b, 0.028f * dv);
            dialRing.color = new Color(Palette.Brass.r, Palette.Brass.g, Palette.Brass.b, 0.08f * dv);
            smallFx.Age = a - 0.1f;
            bigFx.Age = a - 0.2f;
            rule.localScale = new Vector3(Ease.OutExpo((a - 0.7f) / 0.8f), 1, 1);
            epiFx.Age = a - 1.0f;
            if (Target > 0f && (a > 3.6f || (hasInput && a > 0.6f && (input.AnyKey || input.Confirm))))
            {
                Hide();
                var d = done;
                done = null;
                d?.Invoke();
            }
        }
    }

    public sealed class EndingScreen : MenuScreen
    {
        readonly TextMeshProUGUI head, sub, body, foot;
        readonly TextFx headFx, subFx, bodyFx;
        readonly Image dawn, sun;
        readonly WatchStage watch;
        readonly RawImage watchImg;
        Action done;
        public override float Blur => 0.7f;

        public EndingScreen(Transform canvas) : base(canvas, "Ending")
        {
            Shade(Root, 0.5f);
            // dawn: a warm sun rising behind the vault, washing up from the bottom edge
            dawn = Ui.Img("Dawn", Root, Ui.VGradient, new Color(1f, 0.72f, 0.42f, 0f));
            Ui.Fill(dawn.rectTransform);
            sun = Ui.Img("Sun", Root, Ui.Glow, new Color(1f, 0.8f, 0.5f, 0f));
            Ui.Place(sun.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0, -200), new Vector2(2200, 1400));
            watch = WatchStage.Create(Root.transform.root, 640);
            watchImg = watch.Show(Root, new Vector2(300, 300));
            Ui.Place(watchImg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 330), new Vector2(300, 300));
            head = Ui.Text("Head", Root, "Account settled.", Ui.Heavy, 100, Palette.Gold);
            Ui.Place(head.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(1600, 130));
            headFx = TextFx.On(head, TextFx.Kind.Drop, 0.05f, 0.7f, 80f);
            sub = Ui.Text("Sub", Root, "Time well spent.", Ui.Light, 48, Palette.Paper);
            Ui.Place(sub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 32), new Vector2(1600, 70));
            sub.fontStyle = FontStyles.Italic;
            subFx = TextFx.On(sub, TextFx.Kind.Type, 0.05f, 0.3f);
            body = Ui.Text("Body", Root, "", Ui.Regular, 28, new Color(0.88f, 0.9f, 1f, 0.92f));
            Ui.Place(body.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -190), new Vector2(1600, 360));
            body.richText = true;
            body.lineSpacing = 12;
            bodyFx = TextFx.On(body, TextFx.Kind.Rise, 0.006f, 0.6f, 18f);
            foot = Ui.Text("Foot", Root, "press any key", Ui.Semi, 22, new Color(0.8f, 0.84f, 0.95f, 0.6f));
            Ui.Place(foot.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 50), new Vector2(800, 30));
            foot.characterSpacing = 12;
        }

        public void Show(int totalBest, int totalPar, int golds, int count, Action onDone)
        {
            body.text = $"<size=34><b>{Ui.Secs(totalBest)}s</b></size>  total     <color=#8E9AC8>solver par {Ui.Secs(totalPar)}s</color>\n" +
                        $"{golds} of {count} levels as <color=#FFD27A><b>Time Thief</b></color>\n\n" +
                        "<size=22><color=#8E9AC8>BORROWED SECONDS</color>\n" +
                        "design, code, models, sound and music generated from scratch in Unity, Blender and Python\n" +
                        "set in Fira Sans (SIL Open Font License)</size>";
            done = onDone;
            Show();
        }

        protected override float FadeIn => 1f;

        protected override void Tick(InputReader input, float dt, bool hasInput)
        {
            float a = Age;
            float rise = Ease.InOutCubic(a / 5f);
            dawn.color = new Color(1f, 0.72f, 0.42f, 0.32f * rise);
            sun.color = new Color(1f, 0.78f, 0.48f, 0.55f * rise);
            sun.rectTransform.anchoredPosition = new Vector2(0, Mathf.Lerp(-520f, -200f, rise));
            // the watch spins its hands forward through the night, then settles on the real time
            float w = Ease.OutBack((a - 0.7f) / 1.1f, 1.3f);
            watch.Scale = Mathf.Lerp(0.3f, 1f, w);
            watch.Yaw = Mathf.Sin(Clock.Now * 0.5f) * 14f;
            watch.Pitch = -5f + Mathf.Sin(Clock.Now * 0.4f) * 4f;
            watchImg.color = new Color(1, 1, 1, Ease.OutCubic((a - 0.7f) * 2.5f));
            watch.ShowRealTime();
            float extra = (1f - Ease.OutCubic(a / 3.5f)) * -1440f;
            watch.MinuteDeg += extra;
            watch.HourDeg += extra / 12f;
            headFx.Age = a - 0.6f;
            subFx.Age = a - 1.8f;
            bodyFx.Age = a - 2.8f;
            foot.alpha = Mathf.Clamp01(a - 4.5f) * (0.45f + 0.25f * Mathf.Sin(Clock.Now * 3f));
            if (Target > 0f && a > 4f && hasInput && (input.AnyKey || input.Confirm))
            {
                Hide();
                var d = done;
                done = null;
                d?.Invoke();
            }
        }
    }
}
