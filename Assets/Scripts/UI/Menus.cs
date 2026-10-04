using System;
using System.Collections.Generic;
using BorrowedSeconds.Audio;
using BorrowedSeconds.Game;
using BorrowedSeconds.Sim;
using BorrowedSeconds.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BorrowedSeconds.UI
{
    /// <summary>A fading full-screen layer on the menu canvas.</summary>
    public abstract class MenuScreen
    {
        public readonly RectTransform Root;
        protected readonly CanvasGroup Group;
        protected float Shown, Target, Age;
        public bool Visible => Target > 0f;

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
            Shown = Mathf.MoveTowards(Shown, Target, dt * FadeSpeed);
            Group.alpha = Shown;
            Group.blocksRaycasts = Target > 0f;
            if (Shown <= 0f && Target <= 0f) { Root.gameObject.SetActive(false); return; }
            Tick(input, dt, top && Target > 0f && Age > 0.12f);
        }

        protected virtual float FadeSpeed => 6f;
        protected abstract void Tick(InputReader input, float dt, bool hasInput);

        protected static Image Shade(Transform parent, float alpha)
        {
            var img = Ui.Img("Shade", parent, null, new Color(0.03f, 0.035f, 0.08f, alpha));
            Ui.Fill(img.rectTransform);
            img.raycastTarget = true;
            return img;
        }
    }

    /// <summary>Vertical list of selectable rows driven by keys, stick or mouse.</summary>
    public sealed class MenuList
    {
        public sealed class Item
        {
            public string Label;
            public Action Activate;
            public Func<string> Value;
            public Action<int> Adjust;
            public Func<bool> Enabled;
            public RectTransform Rt;
            public TextMeshProUGUI LabelText, ValueText;
            public Image Bar;
            public float Hl;
        }

        public readonly RectTransform Root;
        public readonly List<Item> Items = new List<Item>();
        public int Selected;
        readonly float rowH, width, size;
        readonly bool left;

        public MenuList(Transform parent, Vector2 anchor, Vector2 pos, float width, float rowH, float size, bool left)
        {
            this.width = width;
            this.rowH = rowH;
            this.size = size;
            this.left = left;
            Root = Ui.Rect("Menu", parent, anchor, new Vector2(left ? 0f : 0.5f, 1f), pos, new Vector2(width, 10));
        }

        public Item Add(string label, Action activate, Func<string> value = null, Action<int> adjust = null, Func<bool> enabled = null)
        {
            var it = new Item { Label = label, Activate = activate, Value = value, Adjust = adjust, Enabled = enabled };
            it.Rt = Ui.Rect("Item " + label, Root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -Items.Count * rowH), new Vector2(width, rowH - 8));
            it.Bar = Ui.Img("Bar", it.Rt, Ui.Pill, new Color(1, 1, 1, 0));
            Ui.Fill(it.Bar.rectTransform);
            var align = left && value == null ? TextAlignmentOptions.MidlineLeft : value != null ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center;
            it.LabelText = Ui.Text("Label", it.Rt, label, Ui.Semi, size, Palette.Paper, align);
            Ui.Fill(it.LabelText.rectTransform);
            it.LabelText.rectTransform.offsetMin = new Vector2(28, 0);
            it.LabelText.characterSpacing = 6;
            if (value != null)
            {
                it.ValueText = Ui.Text("Value", it.Rt, "", Ui.Semi, size, Palette.Gold, TextAlignmentOptions.MidlineRight);
                Ui.Fill(it.ValueText.rectTransform);
                it.ValueText.rectTransform.offsetMax = new Vector2(-28, 0);
            }
            Root.sizeDelta = new Vector2(width, Items.Count * rowH + rowH);
            Items.Add(it);
            return it;
        }

        bool IsEnabled(Item it) => it.Enabled == null || it.Enabled();

        /// <summary>Returns true if an item was activated this frame.</summary>
        public bool Update(InputReader input, float dt, bool hasInput)
        {
            bool fired = false;
            if (hasInput)
            {
                if (input.PressedDir == Dirs.N) Move(-1);
                if (input.PressedDir == Dirs.S) Move(1);
                var cur = Items[Selected];
                if (cur.Adjust != null && (input.PressedDir == Dirs.E || input.PressedDir == Dirs.W))
                {
                    cur.Adjust(input.PressedDir == Dirs.E ? 1 : -1);
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
                }
            }
            for (int i = 0; i < Items.Count; i++)
            {
                var it = Items[i];
                bool sel = i == Selected;
                bool en = IsEnabled(it);
                it.Hl = Mathf.MoveTowards(it.Hl, sel ? 1f : 0f, dt * 10f);
                it.Bar.color = new Color(Palette.Gold.r, Palette.Gold.g, Palette.Gold.b, 0.16f * it.Hl);
                var c = en ? Color.Lerp(new Color(0.85f, 0.87f, 0.95f, 0.85f), Palette.Paper, it.Hl) : new Color(1, 1, 1, 0.25f);
                it.LabelText.color = c;
                it.LabelText.text = sel && en ? $"<color=#FFD27A>›</color>  {it.Label}" : $"   {it.Label}";
                if (it.ValueText != null) it.ValueText.text = it.Adjust != null && sel ? $"‹  {it.Value()}  ›" : it.Value();
                it.Rt.localScale = Vector3.one * (1f + 0.02f * it.Hl);
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
        public readonly MenuList Menu;
        readonly RectTransform hand, logo;
        readonly TextMeshProUGUI prompt;

        public TitleScreen(Transform canvas, Action onContinue, Action onLevels, Action onSettings, Action onQuit, Func<string> continueLabel) : base(canvas, "Title")
        {
            var grad = Ui.Img("Gradient", Root, Ui.HGradient, new Color(0.02f, 0.025f, 0.06f, 0.92f));
            grad.rectTransform.anchorMin = Vector2.zero;
            grad.rectTransform.anchorMax = new Vector2(0.62f, 1f);
            grad.rectTransform.offsetMin = grad.rectTransform.offsetMax = Vector2.zero;

            logo = Ui.Rect("Logo", Root, new Vector2(0, 1), new Vector2(0, 1), new Vector2(150, -150), new Vector2(900, 360));
            var clock = Ui.Rect("Clock", logo, new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(70, -70), new Vector2(120, 120));
            var face = Ui.Img("Face", clock, Ui.Circle, new Color(0.06f, 0.07f, 0.15f, 0.9f));
            Ui.Fill(face.rectTransform);
            var ticks = Ui.Img("Ticks", clock, Ui.Ticks, new Color(1, 1, 1, 0.5f));
            Ui.Fill(ticks.rectTransform);
            var rim = Ui.Img("Rim", clock, Ui.Ring, Palette.Brass);
            Ui.Fill(rim.rectTransform);
            hand = Ui.Rect("Hand", clock, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.1f), Vector2.zero, new Vector2(5, 52));
            Ui.Fill(Ui.Img("H", hand, null, Palette.Ice).rectTransform);
            var hub = Ui.Img("Hub", clock, Ui.Circle, Palette.Gold);
            Ui.Place(hub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(14, 14));

            var t1 = Ui.Text("Borrowed", logo, "BORROWED", Ui.Heavy, 118, Palette.Paper, TextAlignmentOptions.TopLeft);
            Ui.Place(t1.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(150, 6), new Vector2(900, 130));
            t1.characterSpacing = 10;
            var t2 = Ui.Text("Seconds", logo, "SECONDS", Ui.Heavy, 118, Palette.Ice, TextAlignmentOptions.TopLeft);
            Ui.Place(t2.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(150, -100), new Vector2(900, 130));
            t2.characterSpacing = 10;
            var tag = Ui.Text("Tag", logo, "Freeze anything for three seconds. Then pay them back.", Ui.Light, 30, new Color(0.85f, 0.88f, 1f, 0.85f), TextAlignmentOptions.TopLeft);
            Ui.Place(tag.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(156, -236), new Vector2(900, 40));

            Menu = new MenuList(Root, new Vector2(0, 0.5f), new Vector2(180, -60), 440, 66, 34, true);
            Menu.Add("Continue", onContinue);
            Menu.Add("Levels", onLevels);
            Menu.Add("Settings", onSettings);
            Menu.Add("Quit", onQuit);
            this.continueLabel = continueLabel;

            prompt = Ui.Text("Prompt", Root, "", Ui.Regular, 20, new Color(0.8f, 0.84f, 0.95f, 0.6f), TextAlignmentOptions.BottomLeft);
            Ui.Place(prompt.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(186, 60), new Vector2(900, 30));
            prompt.text = "<b>WASD</b> / <b>Arrows</b> choose     <b>Space</b> / <b>Enter</b> confirm";
            prompt.richText = true;
        }

        readonly Func<string> continueLabel;

        protected override void Tick(InputReader input, float dt, bool hasInput)
        {
            Menu.Items[0].Label = continueLabel();
            float sec = Mathf.Floor(Clock.Now);
            float frac = Clock.Now - sec;
            float snap = sec + Mathf.Clamp01(frac * 8f) * (1f + 0.15f * Mathf.Sin(Mathf.Clamp01(frac * 8f) * Mathf.PI));
            hand.localRotation = Quaternion.Euler(0, 0, -snap * 6f);
            logo.anchoredPosition = new Vector2(150, -150 + (1f - Mathf.Clamp01(Age * 2f)) * 30f);
            Menu.Update(input, dt, hasInput);
        }
    }

    // ==================================================================== level select

    public sealed class LevelSelectScreen : MenuScreen
    {
        sealed class Card
        {
            public RectTransform Rt;
            public Image Bg, Edge, MedalImg;
            public TextMeshProUGUI Num, Name, Best;
            public float Hl;
        }

        readonly List<Card> cards = new List<Card>();
        readonly LevelCatalog catalog;
        readonly SaveData save;
        readonly Action<int> onPick;
        readonly Action onBack;
        readonly TextMeshProUGUI footer, totals;
        int sel;
        const int Cols = 5;

        public LevelSelectScreen(Transform canvas, LevelCatalog catalog, SaveData save, Action<int> onPick, Action onBack) : base(canvas, "LevelSelect")
        {
            this.catalog = catalog;
            this.save = save;
            this.onPick = onPick;
            this.onBack = onBack;
            Shade(Root, 0.86f);
            var title = Ui.Text("Title", Root, "THE LEDGER", Ui.Heavy, 64, Palette.Paper, TextAlignmentOptions.Top);
            Ui.Place(title.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -56), new Vector2(1200, 80));
            title.characterSpacing = 30;
            totals = Ui.Text("Totals", Root, "", Ui.Semi, 22, new Color(0.8f, 0.84f, 0.95f, 0.8f), TextAlignmentOptions.Top);
            Ui.Place(totals.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -138), new Vector2(1200, 30));
            totals.characterSpacing = 8;

            const float cw = 270, ch = 150, gx = 22, gy = 34;
            float x0 = -(Cols - 1) * (cw + gx) * 0.5f + 90;
            for (int c = 0; c < LevelCatalog.Chapters.Length; c++)
            {
                float y = 250 - c * (ch + gy);
                var info = LevelCatalog.Chapters[c];
                var lab = Ui.Text("Chapter", Root, $"<size=22><color=#8E9AC8>CHAPTER</color></size>\n<b>{Hud.Roman(c + 1)}</b>\n<size=20>{info.Title.ToUpperInvariant()}</size>", Ui.Semi, 44, Palette.Gold, TextAlignmentOptions.Right);
                lab.richText = true;
                lab.lineSpacing = -18;
                Ui.Place(lab.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(1, 0.5f), new Vector2(x0 - cw * 0.5f - 34, y), new Vector2(240, ch));
            }
            for (int i = 0; i < catalog.Levels.Count; i++)
            {
                var d = catalog.Levels[i];
                int row = Mathf.Clamp(d.Chapter - 1, 0, 3);
                int col = 0;
                for (int k = 0; k < i; k++) if (catalog.Levels[k].Chapter == d.Chapter) col++;
                var card = new Card();
                card.Rt = Ui.Rect("Card " + d.Id, Root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x0 + col * (cw + gx), 250 - row * (ch + gy)), new Vector2(cw, ch));
                card.Edge = Ui.Img("Edge", card.Rt, Ui.Rounded, Palette.Gold);
                Ui.Place(card.Edge.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(cw + 6, ch + 6));
                card.Bg = Ui.Img("Bg", card.Rt, Ui.Rounded, new Color(0.09f, 0.1f, 0.2f, 0.95f));
                Ui.Fill(card.Bg.rectTransform);
                card.Num = Ui.Text("Num", card.Rt, $"{d.Chapter}-{col + 1}", Ui.Heavy, 40, Palette.Gold, TextAlignmentOptions.TopLeft);
                Ui.Place(card.Num.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -14), new Vector2(200, 50));
                card.Name = Ui.Text("Name", card.Rt, d.Name, Ui.Semi, 25, Palette.Paper, TextAlignmentOptions.TopLeft);
                Ui.Place(card.Name.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -64), new Vector2(240, 34));
                card.Best = Ui.Text("Best", card.Rt, "", Ui.Regular, 19, new Color(0.8f, 0.84f, 0.95f, 0.8f), TextAlignmentOptions.BottomLeft);
                Ui.Place(card.Best.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(20, 14), new Vector2(240, 28));
                card.Best.richText = true;
                card.MedalImg = Ui.Img("Medal", card.Rt, Ui.Circle, Color.clear);
                Ui.Place(card.MedalImg.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-18, -18), new Vector2(30, 30));
                cards.Add(card);
            }
            footer = Ui.Text("Footer", Root, "<b>Arrows</b> choose     <b>Space</b> play     <b>Esc</b> back", Ui.Regular, 20, new Color(0.8f, 0.84f, 0.95f, 0.6f), TextAlignmentOptions.Bottom);
            Ui.Place(footer.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 46), new Vector2(1200, 30));
            footer.richText = true;
        }

        public bool Unlocked(int i) => i == 0 || save.Cleared(catalog.Levels[i - 1].Id) || save.Cleared(catalog.Levels[i].Id);

        public void Show(int focus)
        {
            sel = Mathf.Clamp(focus, 0, cards.Count - 1);
            Show();
        }

        protected override void Tick(InputReader input, float dt, bool hasInput)
        {
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
                c.Hl = Mathf.MoveTowards(c.Hl, i == sel ? 1f : 0f, dt * 10f);
                c.Rt.localScale = Vector3.one * (1f + 0.05f * c.Hl);
                c.Edge.color = new Color(Palette.Gold.r, Palette.Gold.g, Palette.Gold.b, c.Hl);
                c.Bg.color = open ? new Color(0.09f, 0.1f, 0.2f, 0.95f) : new Color(0.06f, 0.065f, 0.12f, 0.9f);
                c.Num.color = open ? Palette.Gold : new Color(1, 1, 1, 0.2f);
                c.Name.color = open ? Palette.Paper : new Color(1, 1, 1, 0.2f);
                c.Name.text = open ? d.Name : "Locked";
                c.Best.text = !open ? "" : best > 0 ? $"best <b>{Ui.Secs(best)}</b>   par {Ui.Secs(par)}" : $"par {Ui.Secs(par)}";
                c.MedalImg.color = MedalColor(medal);
            }
            totals.text = $"{cleared} / {cards.Count} SETTLED   ·   {gold} TIME THIEF" + (cleared > 0 ? $"   ·   {Ui.Secs(totalBest)}s vs par {Ui.Secs(totalPar)}s" : "");

            if (!hasInput) return;
            int prev = sel;
            int col = sel % Cols, row = sel / Cols, rows = (cards.Count + Cols - 1) / Cols;
            if (input.PressedDir == Dirs.E) col = (col + 1) % Cols;
            if (input.PressedDir == Dirs.W) col = (col + Cols - 1) % Cols;
            if (input.PressedDir == Dirs.S) row = (row + 1) % rows;
            if (input.PressedDir == Dirs.N) row = (row + rows - 1) % rows;
            sel = Mathf.Min(row * Cols + col, cards.Count - 1);
            int hover = -1;
            for (int i = 0; i < cards.Count; i++)
                if (RectTransformUtility.RectangleContainsScreenPoint(cards[i].Rt, input.Pointer, null)) hover = i;
            if (hover >= 0 && input.PointerMoved) sel = hover;
            if (sel != prev) Sfx.Play("ui_hover");
            bool click = input.Click && hover >= 0 && hover == sel;
            if (input.Confirm || click)
            {
                if (Unlocked(sel)) { Sfx.Play("ui_click"); onPick(sel); }
                else Sfx.Play("bump");
            }
            else if (input.Back) { Sfx.Play("ui_back"); onBack(); }
        }

        public static Color MedalColor(Medal m) => m switch
        {
            Medal.Gold => Palette.Gold,
            Medal.Silver => new Color(0.82f, 0.86f, 0.95f),
            Medal.Bronze => new Color(0.8f, 0.52f, 0.32f),
            _ => new Color(1, 1, 1, 0.08f),
        };
    }

    // ==================================================================== pause & settings

    public sealed class PauseScreen : MenuScreen
    {
        public readonly MenuList Menu;
        readonly Action onResume;

        public PauseScreen(Transform canvas, Action onResume, Action onRestart, Action onLevels, Action onSettings, Action onTitle) : base(canvas, "Pause")
        {
            this.onResume = onResume;
            Shade(Root, 0.6f);
            var title = Ui.Text("Title", Root, "PAUSED", Ui.Heavy, 72, Palette.Paper);
            Ui.Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 230), new Vector2(800, 90));
            title.characterSpacing = 40;
            Menu = new MenuList(Root, new Vector2(0.5f, 0.5f), new Vector2(0, 140), 420, 66, 32, false);
            Menu.Add("Resume", onResume);
            Menu.Add("Restart", onRestart);
            Menu.Add("Levels", onLevels);
            Menu.Add("Settings", onSettings);
            Menu.Add("Title", onTitle);
        }

        public override void Show() { base.Show(); Menu.Selected = 0; }
        protected override float FadeSpeed => 10f;

        protected override void Tick(InputReader input, float dt, bool hasInput)
        {
            if (hasInput && (input.Back || input.Pause)) { Sfx.Play("ui_back"); onResume(); return; }
            Menu.Update(input, dt, hasInput);
        }
    }

    public sealed class SettingsScreen : MenuScreen
    {
        readonly MenuList menu;
        readonly Action onBack;

        public SettingsScreen(Transform canvas, SaveData save, Action apply, Action onBack) : base(canvas, "Settings")
        {
            this.onBack = onBack;
            Shade(Root, 0.75f);
            var title = Ui.Text("Title", Root, "SETTINGS", Ui.Heavy, 64, Palette.Paper);
            Ui.Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 330), new Vector2(800, 90));
            title.characterSpacing = 30;
            menu = new MenuList(Root, new Vector2(0.5f, 0.5f), new Vector2(0, 250), 640, 62, 28, false);
            string Pct(float v) => Mathf.RoundToInt(v * 100) + "%";
            float Step(float v, int d) => Mathf.Clamp01(Mathf.Round((v + d * 0.1f) * 10f) / 10f);
            string OnOff(bool b) => b ? "On" : "Off";
            menu.Add("Master volume", null, () => Pct(save.master), d => { save.master = Step(save.master, d); apply(); });
            menu.Add("Music", null, () => Pct(save.music), d => { save.music = Step(save.music, d); apply(); });
            menu.Add("Effects", null, () => Pct(save.sfx), d => { save.sfx = Step(save.sfx, d); apply(); });
            menu.Add("Fullscreen", null, () => OnOff(save.fullscreen), d => { save.fullscreen = !save.fullscreen; apply(); });
            menu.Add("Screen shake", null, () => OnOff(save.shake), d => { save.shake = !save.shake; apply(); });
            menu.Add("Reduce flashing", null, () => OnOff(save.reduceFlashing), d => { save.reduceFlashing = !save.reduceFlashing; apply(); });
            menu.Add("Focus slow-motion", null, () => Pct(save.focus) + " speed", d => { save.focus = Mathf.Clamp(Mathf.Round((save.focus + d * 0.1f) * 10f) / 10f, 0.1f, 0.6f); apply(); });
            menu.Add("Back", onBack);
        }

        public override void Show() { base.Show(); menu.Selected = 0; }

        protected override void Tick(InputReader input, float dt, bool hasInput)
        {
            if (hasInput && input.Back) { Sfx.Play("ui_back"); onBack(); return; }
            menu.Update(input, dt, hasInput);
        }
    }

    // ==================================================================== level complete

    public sealed class CompleteScreen : MenuScreen
    {
        public readonly MenuList Menu;
        readonly TextMeshProUGUI title, time, par, best, medalText;
        readonly RectTransform stamp;
        readonly Image stampRing, stampDisc;
        Medal medal;
        bool stamped;

        public CompleteScreen(Transform canvas, Action onNext, Action onRetry, Action onLevels) : base(canvas, "Complete")
        {
            var band = Ui.Img("Band", Root, null, new Color(0.03f, 0.035f, 0.08f, 0.78f));
            Ui.Place(band.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(4000, 460));
            title = Ui.Text("Title", Root, "SETTLED", Ui.Heavy, 96, Palette.Gold);
            Ui.Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-120, 190), new Vector2(900, 110));
            title.characterSpacing = 30;
            time = Ui.Text("Time", Root, "", Ui.Heavy, 64, Palette.Paper, TextAlignmentOptions.Right);
            Ui.Place(time.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(1, 0.5f), new Vector2(40, 90), new Vector2(500, 80));
            par = Ui.Text("Par", Root, "", Ui.Semi, 26, new Color(0.8f, 0.84f, 0.95f, 0.85f), TextAlignmentOptions.Right);
            Ui.Place(par.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(1, 0.5f), new Vector2(40, 36), new Vector2(500, 34));
            par.characterSpacing = 6;
            best = Ui.Text("Best", Root, "", Ui.Semi, 22, Palette.Ice, TextAlignmentOptions.Right);
            Ui.Place(best.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(1, 0.5f), new Vector2(40, 2), new Vector2(500, 30));
            best.characterSpacing = 8;

            stamp = Ui.Rect("Stamp", Root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(210, 60), new Vector2(190, 190));
            stampDisc = Ui.Img("Disc", stamp, Ui.Circle, Color.clear);
            Ui.Fill(stampDisc.rectTransform);
            stampRing = Ui.Img("Ring", stamp, Ui.Ring, Color.clear);
            Ui.Place(stampRing.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(160, 160));
            medalText = Ui.Text("Medal", stamp, "", Ui.Heavy, 26, Palette.Ink);
            Ui.Fill(medalText.rectTransform);
            medalText.characterSpacing = 6;

            Menu = new MenuList(Root, new Vector2(0.5f, 0.5f), new Vector2(0, -70), 1000, 60, 28, false);
            Menu.Root.sizeDelta = new Vector2(1000, 60);
            var next = Menu.Add("Next", onNext);
            var retry = Menu.Add("Retry", onRetry);
            var levels = Menu.Add("Levels", onLevels);
            // lay the three buttons out in a row
            float w = 300;
            for (int i = 0; i < Menu.Items.Count; i++)
            {
                Menu.Items[i].Rt.anchoredPosition = new Vector2((i - 1) * (w + 20), 0);
                Menu.Items[i].Rt.sizeDelta = new Vector2(w, 52);
            }
        }

        public void Show(int ticks, int parTicks, int prevBest, bool isLast)
        {
            medal = SaveData.MedalFor(ticks, parTicks);
            time.text = Ui.Secs(ticks) + "s";
            par.text = parTicks > 0 ? $"PAR {Ui.Secs(parTicks)}s   ·   {(ticks - parTicks <= 0 ? "on par" : "+" + Ui.Secs(ticks - parTicks) + "s")}" : "";
            best.text = prevBest == 0 ? "FIRST CLEAR" : ticks < prevBest ? $"NEW BEST  (was {Ui.Secs(prevBest)})" : $"BEST {Ui.Secs(prevBest)}";
            Menu.Items[0].Label = isLast ? "Finish" : "Next";
            Menu.Selected = 0;
            stamped = false;
            stamp.localScale = Vector3.zero;
            Show();
        }

        protected override void Tick(InputReader input, float dt, bool hasInput)
        {
            title.rectTransform.localScale = Vector3.one * (1f + Mathf.Max(0f, 0.25f - Age) * 1.6f);
            float st = Age - 0.45f;
            if (st > 0f && !stamped) { stamped = true; Sfx.Play("stamp"); }
            if (st > 0f)
            {
                float k = Mathf.Clamp01(st / 0.18f);
                stamp.localScale = Vector3.one * Mathf.Lerp(2.4f, 1f, k * k);
                stamp.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(-30f, -12f, k));
                var c = LevelSelectScreen.MedalColor(medal);
                stampDisc.color = new Color(c.r, c.g, c.b, k);
                stampRing.color = new Color(Palette.Ink.r, Palette.Ink.g, Palette.Ink.b, 0.5f * k);
                medalText.text = medal == Medal.Gold ? "TIME\nTHIEF" : SaveData.MedalName(medal);
                medalText.color = new Color(Palette.Ink.r, Palette.Ink.g, Palette.Ink.b, k);
            }
            int prev = Menu.Selected;
            if (hasInput)
            {
                if (input.PressedDir == Dirs.E) Menu.Selected = (Menu.Selected + 1) % 3;
                if (input.PressedDir == Dirs.W) Menu.Selected = (Menu.Selected + 2) % 3;
                if (prev != Menu.Selected) Sfx.Play("ui_hover");
                if (input.Restart) { Menu.Items[1].Activate(); return; }
                if (input.Back) { Menu.Items[2].Activate(); return; }
            }
            Menu.Update(input, dt, hasInput && Age > 0.5f);
        }
    }

    // ==================================================================== chapter card & ending

    public sealed class ChapterCard : MenuScreen
    {
        readonly TextMeshProUGUI small, big, epigraph;
        readonly Image line;
        Action done;
        public ChapterCard(Transform canvas) : base(canvas, "ChapterCard")
        {
            Shade(Root, 0.9f);
            small = Ui.Text("Small", Root, "", Ui.Semi, 26, new Color(0.75f, 0.8f, 0.95f, 0.9f));
            Ui.Place(small.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(1200, 40));
            small.characterSpacing = 40;
            big = Ui.Text("Big", Root, "", Ui.Heavy, 120, Palette.Gold);
            Ui.Place(big.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 30), new Vector2(1600, 140));
            line = Ui.Img("Line", Root, null, Palette.Brass);
            Ui.Place(line.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -52), new Vector2(0, 2));
            epigraph = Ui.Text("Epigraph", Root, "", Ui.Light, 38, Palette.Paper);
            Ui.Place(epigraph.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -100), new Vector2(1600, 60));
            epigraph.fontStyle = FontStyles.Italic;
        }

        public void Show(int chapter, Action onDone)
        {
            var info = LevelCatalog.Chapters[Mathf.Clamp(chapter - 1, 0, LevelCatalog.Chapters.Length - 1)];
            small.text = $"CHAPTER {Hud.Roman(chapter)}";
            big.text = info.Title.ToUpperInvariant();
            epigraph.text = $"“{info.Epigraph}”";
            done = onDone;
            Show();
            Sfx.Play("chapter");
        }

        protected override float FadeSpeed => 3f;

        protected override void Tick(InputReader input, float dt, bool hasInput)
        {
            big.characterSpacing = Mathf.Lerp(80f, 40f, Mathf.Clamp01(Age / 2.5f));
            line.rectTransform.sizeDelta = new Vector2(Mathf.Lerp(0, 520, Mathf.SmoothStep(0, 1, Age / 0.9f)), 2);
            epigraph.alpha = Mathf.Clamp01((Age - 0.5f) * 2f);
            if (Target > 0f && (Age > 3.2f || (hasInput && Age > 0.5f && (input.AnyKey || input.Confirm))))
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
        Action done;

        public EndingScreen(Transform canvas) : base(canvas, "Ending")
        {
            var bg = Ui.Img("Dawn", Root, null, new Color(1f, 0.86f, 0.62f, 0.0f));
            Ui.Fill(bg.rectTransform);
            dawn = bg;
            Shade(Root, 0.55f);
            head = Ui.Text("Head", Root, "Account settled.", Ui.Heavy, 96, Palette.Gold);
            Ui.Place(head.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 220), new Vector2(1600, 120));
            sub = Ui.Text("Sub", Root, "Time well spent.", Ui.Light, 48, Palette.Paper);
            Ui.Place(sub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 130), new Vector2(1600, 70));
            sub.fontStyle = FontStyles.Italic;
            body = Ui.Text("Body", Root, "", Ui.Regular, 28, new Color(0.88f, 0.9f, 1f, 0.9f));
            Ui.Place(body.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -120), new Vector2(1600, 360));
            body.richText = true;
            body.lineSpacing = 12;
            foot = Ui.Text("Foot", Root, "press any key", Ui.Semi, 22, new Color(0.8f, 0.84f, 0.95f, 0.6f));
            Ui.Place(foot.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 50), new Vector2(800, 30));
            foot.characterSpacing = 12;
        }

        readonly Image dawn;

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

        protected override float FadeSpeed => 0.8f;

        protected override void Tick(InputReader input, float dt, bool hasInput)
        {
            dawn.color = new Color(1f, 0.82f, 0.58f, Mathf.Clamp01(Age / 4f) * 0.35f);
            head.alpha = Mathf.Clamp01(Age - 0.5f);
            sub.alpha = Mathf.Clamp01(Age - 1.6f);
            body.alpha = Mathf.Clamp01(Age - 2.8f);
            foot.alpha = Mathf.Clamp01(Age - 4f) * 0.6f;
            if (Target > 0f && Age > 4f && hasInput && (input.AnyKey || input.Confirm))
            {
                Hide();
                var d = done;
                done = null;
                d?.Invoke();
            }
        }
    }
}
