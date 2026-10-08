using System.Collections.Generic;
using BorrowedSeconds.Game;
using BorrowedSeconds.Sim;
using BorrowedSeconds.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BorrowedSeconds.UI
{
    /// <summary>In-level HUD: level title, run clock vs par, the pocket-watch loan dial, aim tag, hints, banners.</summary>
    public sealed class Hud : MonoBehaviour
    {
        Canvas canvas;
        CanvasGroup group;
        TextMeshProUGUI chapterText, numberText, nameText, timeText, parText, speedText;
        // pocket watch
        RectTransform watch;
        Image watchFill, watchGlow, watchFace;
        TextMeshProUGUI watchBig, watchSmall, termText;
        Image[] pips = new Image[0];
        // aim tag
        RectTransform aimTag;
        TextMeshProUGUI aimText;
        CanvasGroup aimGroup;
        // banner
        TextMeshProUGUI banner, bannerSub;
        CanvasGroup bannerGroup;
        float bannerT = 10f, bannerDur;
        // hints: two rows above 100 % HUD size (hintRow2 is the upper one), in one scaled root
        RectTransform hintRoot, hintRow, hintRow2;
        bool hintsTwoRows;
        CanvasGroup hintGroup;
        TextMeshProUGUI hintText, tipText;
        CanvasGroup tipGroup;
        RectTransform tipBarRt, tipRt;
        Panel tipPanel;
        string hintSource;
        TextMeshProUGUI focusText;
        CanvasGroup focusGroup;
        TextMeshProUGUI rewindText;
        CanvasGroup rewindGroup;
        Panel focusTag, rewindTag;
        RectTransform rewindDial, focusRoot, rewindRoot;

        LevelSession session;
        float watchPulse, shownAlpha;
        // medal pace: on a level already settled, a coin by the clock shows the best medal still in reach
        Image paceCoin;
        Medal paceMedal;
        float paceFlip = 1f;
        int parTicks, bestTicks;
        /// <summary>The medal coin by the clock is showing (checks read it).</summary>
        public bool PaceShown => paceCoin != null && paceCoin.gameObject.activeSelf;
        /// <summary>The medal the pace coin shows (checks read it).</summary>
        public Medal PaceMedal => paceMedal;
        /// <summary>The line under the clock, e.g. "PAR 8.15 · BEST 9.20" (checks read it).</summary>
        public string ParLine => parText.text;
        /// <summary>0..1: how much a modal menu (pause, complete) covers the play screen.</summary>
        public float Dim;
        /// <summary>Trailer framing: keeps the pocket watch, loan terms, tags and banners; hides the
        /// level title, clock, key hints and tip (the trailer's captions take their place).</summary>
        public bool TrailerMode;
        /// <summary>The solver's solution is playing: the top tag reads SOLUTION instead of FOCUS.</summary>
        public bool Watching;
        /// <summary>Hold-to-restart progress 0..1, or below 0 when no restart is being held.</summary>
        public float RestartHold = -1f;
        /// <summary>The restart tag's text, e.g. "HOLD R TO RESTART".</summary>
        public string RestartLabel = "HOLD R TO RESTART";
        RectTransform restartRoot, restartFill;
        CanvasGroup restartGroup;
        // the clue's two world labels: over the route's first borrow and over its first debt's tile
        RectTransform clueObstacleTag, clueTileTag;
        CanvasGroup clueObstacleGroup, clueTileGroup;
        int clueObstacle = -1, clueTile = -1;
        /// <summary>The clue's labels are on (checks read it).</summary>
        public bool ClueShown => clueObstacle >= 0 || clueTile >= 0;
        // the level waits at tick 0 for the first move (LevelSession.Waiting)
        RectTransform readyRoot;
        CanvasGroup readyGroup;
        TextMeshProUGUI readyText;
        Panel readyTag;
        /// <summary>The waiting tag's second line, e.g. "move or click to start" (GameRoot names the device's keys).</summary>
        public string ReadyLabel = "move or borrow to start";
        /// <summary>The waiting tag is showing (checks read it).</summary>
        public bool ReadyShown => readyGroup != null && readyGroup.alpha > 0.5f;
        public string ReadyText => readyText.text;
        TextMeshProUGUI restartText;
        // 3D pocket watch + level title intro + banner band
        WatchStage watch3d;
        RawImage watchImg;
        Spring watchRoll = Spring.Make(0f, 160f, 9f);
        RectTransform titleGroup, bannerRootRt, timeRoot;
        Panel introBand, bannerBand, aimPanel;
        TextFx nameFx, numberFx, bannerFx, bannerSubFx;
        float introT = 99f;
        int lastLoans;
        bool wasFrozen;
        string termLine = "";
        float hintWidth;
        /// <summary>The thaw forecast the HUD is spelling out this frame (None when there is none).</summary>
        public GhostPreview.Verdict Forecast { get; private set; }
        /// <summary>The term line under the watch as shown (the forecast while a debt runs).</summary>
        public string TermText => termText.text;
        public string AimText => aimText.text;

        /// <summary>The forecast's verdict in words, in the ring's colour (rich text; empty for None).</summary>
        public static string VerdictText(GhostPreview.Verdict v) => v switch
        {
            GhostPreview.Verdict.Safe => "<color=#55E0AE><b>SAFE</b></color>",
            GhostPreview.Verdict.Charges => "<color=#FFD27A><b>SAFE</b>, DIAL LATCHES</color>",
            GhostPreview.Verdict.Soon => "<color=#FFB547><b>HIT</b> JUST AFTER</color>",
            GhostPreview.Verdict.Lethal => "<color=#FF4F64><b>LETHAL</b></color>",
            _ => "",
        };

        public static Hud Create(Transform parent)
        {
            var go = new GameObject("HUD");
            go.transform.SetParent(parent, false);
            var h = go.AddComponent<Hud>();
            h.Build();
            return h;
        }

        void Build()
        {
            canvas = Ui.MakeCanvas("HudCanvas", 10, transform);
            group = canvas.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            var root = canvas.transform;
            var ink = Palette.Paper;
            var dim = new Color(0.78f, 0.82f, 0.95f, 0.75f);

            // ---- top left: title
            introBand = new Panel("IntroBand", root, new Vector2(2400, 190), Panel.Style.Band);
            introBand.Image.color = new Color(1, 1, 1, 0);
            var tl = Ui.Rect("TopLeft", root, new Vector2(0, 1), new Vector2(0, 1), new Vector2(56, -40), new Vector2(700, 160));
            titleGroup = tl;
            chapterText = Ui.Text("Chapter", tl, "", Ui.Semi, 20, dim, TextAlignmentOptions.TopLeft);
            Ui.Place(chapterText.rectTransform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(700, 30));
            chapterText.characterSpacing = 14;
            numberText = Ui.Text("Number", tl, "", Ui.Heavy, 58, Palette.Gold, TextAlignmentOptions.TopLeft);
            Ui.Place(numberText.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -28), new Vector2(160, 70));
            nameText = Ui.Text("Name", tl, "", Ui.Semi, 40, ink, TextAlignmentOptions.TopLeft);
            Ui.Place(nameText.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(118, -38), new Vector2(600, 60));
            nameFx = TextFx.On(nameText, TextFx.Kind.Stamp, 0.035f, 0.22f, 50f);
            numberFx = TextFx.On(numberText, TextFx.Kind.Drop, 0.08f, 0.5f, 60f);

            // ---- top right: clock
            var tr = timeRoot = Ui.Rect("TopRight", root, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-56, -40), new Vector2(400, 120));
            timeText = Ui.Text("Time", tr, "0.00", Ui.Heavy, 54, ink, TextAlignmentOptions.TopRight);
            Ui.Place(timeText.rectTransform, new Vector2(1, 1), new Vector2(1, 1), Vector2.zero, new Vector2(400, 64));
            parText = Ui.Text("Par", tr, "", Ui.Semi, 22, dim, TextAlignmentOptions.TopRight);
            Ui.Place(parText.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(0, -66), new Vector2(400, 30));
            parText.characterSpacing = 8;
            paceCoin = Ui.Img("PaceCoin", tr, null, Color.white);
            Ui.Place(paceCoin.rectTransform, new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-200, -34), new Vector2(46, 46));
            paceCoin.gameObject.SetActive(false);
            speedText = Ui.Text("Speed", tr, "", Ui.Semi, 18, Palette.Ice, TextAlignmentOptions.TopRight);
            Ui.Place(speedText.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(0, -96), new Vector2(400, 26));
            speedText.characterSpacing = 8;

            // ---- bottom centre: pocket watch
            watch = Ui.Rect("Watch", root, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 46), new Vector2(170, 170));
            watchGlow = Ui.Img("Glow", watch, Ui.Glow, new Color(Palette.Ice.r, Palette.Ice.g, Palette.Ice.b, 0f));
            Ui.Place(watchGlow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(320, 320));
            watch3d = WatchStage.Create(transform, 512);
            watchImg = watch3d.Show(watch, new Vector2(272, 272));
            watch3d.MainHands = false; // the ice ring is the countdown; hands would cross the digits
            watch3d.SmallHand = false; // nor the sub-dial's hand, which sat right under the READY / DUE IN label
            Ui.Place(watchImg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(272, 272));
            watchFace = null;
            watchFill = Ui.Img("Fill", watch, Ui.Ring, Palette.Ice);
            Ui.Place(watchFill.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(132, 132));
            watchFill.type = Image.Type.Filled;
            watchFill.fillMethod = Image.FillMethod.Radial360;
            watchFill.fillOrigin = (int)Image.Origin360.Top;
            watchFill.fillClockwise = false;
            watchBig = Ui.Text("Big", watch, "", Ui.Heavy, 40, ink);
            Ui.Place(watchBig.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 6), new Vector2(160, 60));
            watchSmall = Ui.Text("Small", watch, "", Ui.Semi, 16, dim);
            Ui.Place(watchSmall.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -30), new Vector2(160, 24));
            watchSmall.characterSpacing = 3; // "LOAN READY" must fit inside the ring
            termText = Ui.Text("Term", root, "", Ui.Semi, 18, dim);
            Ui.Place(termText.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 6), new Vector2(640, 26));
            termText.textWrappingMode = TextWrappingModes.NoWrap;
            termText.characterSpacing = 10;

            // ---- aim tag
            aimTag = Ui.Rect("AimTag", root, Vector2.zero, new Vector2(0.5f, 0), Vector2.zero, new Vector2(270, 88));
            aimGroup = aimTag.gameObject.AddComponent<CanvasGroup>();
            aimPanel = new Panel("Bg", aimTag, new Vector2(270, 88), Panel.Style.Tag);
            aimPanel.Rt.anchoredPosition = Vector2.zero;
            aimPanel.Glow = 0.5f;
            aimPanel.Apply();
            aimText = Ui.Text("Text", aimTag, "", Ui.Semi, 20, Palette.Ice);
            Ui.Fill(aimText.rectTransform);
            aimText.richText = true;

            // ---- hints
            hintRoot = Ui.Rect("Hints", root, new Vector2(0, 0), new Vector2(0, 0), new Vector2(56, 34), new Vector2(760, 40));
            hintGroup = hintRoot.gameObject.AddComponent<CanvasGroup>();
            hintRow = Ui.Rect("Row", hintRoot, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(760, 40));
            hintRow2 = Ui.Rect("Row2", hintRoot, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 44), new Vector2(760, 40));
            hintText = Ui.Text("Text", root, "", Ui.Regular, 20, dim, TextAlignmentOptions.BottomLeft);
            Ui.Fill(hintText.rectTransform);
            hintText.richText = true;
            hintText.enabled = false; // drawn as keycaps instead

            var tip = Ui.Rect("Tip", root, new Vector2(0, 0), new Vector2(0, 0), new Vector2(56, 84), new Vector2(620, 120));
            tipGroup = tip.gameObject.AddComponent<CanvasGroup>();
            tipRt = tip;
            tipPanel = new Panel("TipPanel", tip, new Vector2(660, 100), Panel.Style.Card);
            tipPanel.Rt.anchorMin = tipPanel.Rt.anchorMax = new Vector2(0, 0);
            tipPanel.Rt.pivot = new Vector2(0, 0);
            tipPanel.SetFill(new Color(0.07f, 0.08f, 0.16f, 0.82f), new Color(0.035f, 0.04f, 0.09f, 0.82f));
            var tipBar = Ui.Img("Bar", tip, null, Palette.Gold);
            Ui.Place(tipBar.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(4, 0));
            tipText = Ui.Text("Text", tip, "", Ui.Regular, 24, Palette.Paper, TextAlignmentOptions.BottomLeft);
            Ui.Fill(tipText.rectTransform);
            tipText.rectTransform.offsetMin = new Vector2(20, 0);
            tipText.richText = true;
            tipText.textWrappingMode = TextWrappingModes.Normal;
            tipText.lineSpacing = 6;
            tipBarRt = tipBar.rectTransform;

            focusRoot = Ui.Rect("FocusTag", root, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -64), new Vector2(240, 46));
            focusGroup = focusRoot.gameObject.AddComponent<CanvasGroup>();
            focusTag = new Panel("Tag", focusRoot, new Vector2(240, 46), Panel.Style.Tag);
            focusTag.Rt.anchoredPosition = Vector2.zero;
            focusText = Ui.Text("Focus", focusRoot, "FOCUS", Ui.Semi, 22, Palette.Ice);
            Ui.Fill(focusText.rectTransform);
            focusText.characterSpacing = 30;
            rewindRoot = Ui.Rect("RewindTag", root, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -64), new Vector2(300, 52));
            rewindGroup = rewindRoot.gameObject.AddComponent<CanvasGroup>();
            rewindTag = new Panel("Tag", rewindRoot, new Vector2(300, 52), Panel.Style.Tag);
            rewindTag.Rt.anchoredPosition = Vector2.zero;
            var dial = Ui.Img("Dial", rewindRoot, Ui.Ticks, Palette.Ice);
            rewindDial = dial.rectTransform;
            Ui.Place(rewindDial, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(38, 0), new Vector2(38, 38));
            var dialRim = Ui.Img("Rim", rewindDial, Ui.ThinRing, Palette.Ice);
            Ui.Fill(dialRim.rectTransform);
            var hand = Ui.Img("Hand", rewindDial, null, Palette.Ice);
            Ui.Place(hand.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(2.5f, 15));
            rewindText = Ui.Text("Rewind", rewindRoot, "REWIND", Ui.Heavy, 26, Palette.Ice);
            Ui.Fill(rewindText.rectTransform);
            rewindText.rectTransform.offsetMin = new Vector2(40, 0);
            rewindText.characterSpacing = 22;

            // ---- the clue's labels (Show a clue, in the pause menu)
            clueObstacleTag = ClueLabel("ClueObstacle", root, "FREEZE FIRST", out clueObstacleGroup);
            clueTileTag = ClueLabel("ClueTile", root, "DEBT HERE", out clueTileGroup);

            // ---- the waiting tag (the focus/rewind tags' spot: either starts the clock or follows it)
            readyRoot = Ui.Rect("ReadyTag", root, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -70), new Vector2(420, 64));
            readyGroup = readyRoot.gameObject.AddComponent<CanvasGroup>();
            readyGroup.alpha = 0f;
            readyTag = new Panel("Tag", readyRoot, new Vector2(420, 64), Panel.Style.Tag);
            readyTag.Rt.anchoredPosition = Vector2.zero;
            readyText = Ui.Text("Text", readyRoot, "", Ui.Semi, 22, Palette.Gold);
            Ui.Fill(readyText.rectTransform);
            readyText.richText = true;
            readyText.lineSpacing = -8;

            // ---- hold-to-restart tag (below the focus/rewind tags)
            restartRoot = Ui.Rect("RestartTag", root, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -124), new Vector2(360, 50));
            restartGroup = restartRoot.gameObject.AddComponent<CanvasGroup>();
            restartGroup.alpha = 0f;
            var restartTag = new Panel("Tag", restartRoot, new Vector2(360, 50), Panel.Style.Tag);
            restartTag.Rt.anchoredPosition = Vector2.zero;
            restartText = Ui.Text("Text", restartRoot, "", Ui.Semi, 19, Palette.Coral);
            Ui.Place(restartText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 4), new Vector2(340, 30));
            restartText.characterSpacing = 12;
            var track = Ui.Img("Track", restartRoot, Ui.Pill, new Color(0.02f, 0.025f, 0.06f, 0.95f));
            Ui.Place(track.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), new Vector2(0, 9), new Vector2(300, 5));
            var fill = Ui.Img("Fill", restartRoot, Ui.Pill, Palette.Coral);
            restartFill = fill.rectTransform;
            Ui.Place(restartFill, new Vector2(0.5f, 0), new Vector2(0f, 0.5f), new Vector2(-150, 9), new Vector2(0, 5));

            // ---- banner
            var bannerRoot = Ui.Rect("Banner", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 160), new Vector2(1200, 200));
            bannerRootRt = bannerRoot;
            bannerGroup = bannerRoot.gameObject.AddComponent<CanvasGroup>();
            bannerBand = new Panel("Band", bannerRoot, new Vector2(2400, 176), Panel.Style.Band);
            banner = Ui.Text("Title", bannerRoot, "", Ui.Heavy, 96, Palette.Gold);
            Ui.Place(banner.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(1200, 120));
            banner.characterSpacing = 24;
            bannerSub = Ui.Text("Sub", bannerRoot, "", Ui.Semi, 26, ink);
            Ui.Place(bannerSub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -52), new Vector2(1200, 40));
            bannerSub.characterSpacing = 8;
            bannerFx = TextFx.On(banner, TextFx.Kind.Stamp, 0.045f, 0.2f, 60f);
            bannerSubFx = TextFx.On(bannerSub, TextFx.Kind.Type, 0.015f, 0.2f);
            bannerGroup.alpha = 0;
        }

        /// <param name="best">The saved best time in ticks, 0 if the level was never settled.</param>
        public void Bind(LevelSession s, LevelCatalog catalog, int best = 0)
        {
            clueObstacle = clueTile = -1;
            bestTicks = best;
            paceMedal = Medal.None;
            session = s;
            var d = s.Def;
            var ch = LevelCatalog.Chapters[Mathf.Clamp(d.Chapter - 1, 0, LevelCatalog.Chapters.Length - 1)];
            chapterText.text = $"CHAPTER {Roman(d.Chapter)}  ·  {ch.Title.ToUpperInvariant()}";
            int inChapter = 1;
            foreach (var l in catalog.Levels) { if (l == d) break; if (l.Chapter == d.Chapter) inChapter++; }
            numberText.text = $"{d.Chapter}-{inChapter}";
            nameText.text = d.Name;
            var sol = catalog.SolutionFor(d);
            parTicks = sol?.Par ?? 0;
            parText.text = ParText(false);
            termLine = $"LOAN 3.0s  ·  TERM {Ui.Secs1(d.Term)}s" + (d.LoanLimit >= 0 ? $"  ·  {d.LoanLimit} LOAN{(d.LoanLimit == 1 ? "" : "S")}" : "");
            termText.text = termLine;
            foreach (var p in pips) if (p != null) Destroy(p.gameObject);
            pips = new Image[Mathf.Max(0, d.LoanLimit)];
            for (int i = 0; i < pips.Length; i++)
            {
                pips[i] = Ui.Img("Pip", watch, Ui.Diamond, Palette.Ice);
                Ui.Place(pips[i].rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2((i - (pips.Length - 1) * 0.5f) * 24f, 14f), new Vector2(16, 16));
            }
            bannerT = 10f;
            introT = 0f;
            lastLoans = s.Cur.Loans;
            wasFrozen = false;
        }

        RectTransform ClueLabel(string name, Transform root, string text, out CanvasGroup g)
        {
            var rt = Ui.Rect(name, root, Vector2.zero, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(170, 38));
            g = rt.gameObject.AddComponent<CanvasGroup>();
            g.alpha = 0f;
            var caret = Ui.Img("Caret", rt, Ui.Diamond, Palette.Gold);
            Ui.Place(caret.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0, -5), new Vector2(13, 13));
            var panel = new Panel("Bg", rt, new Vector2(170, 38), Panel.Style.Tag);
            panel.Rt.anchoredPosition = Vector2.zero;
            panel.SetRim(Palette.Gold);
            var t = Ui.Text("Text", rt, text, Ui.Semi, 17, Palette.Gold);
            Ui.Fill(t.rectTransform);
            t.characterSpacing = 10;
            return rt;
        }

        /// <summary>The clue's labels: over the obstacle to freeze (FREEZE FIRST, or FREEZE NEXT on a
        /// later loan) and over the tile where that loan's debt falls due (-1: none).</summary>
        public void SetClue(int obstacle, int tile, string obstacleLabel = "FREEZE FIRST")
        {
            clueObstacle = obstacle;
            clueTile = tile;
            var t = clueObstacleTag.GetComponentInChildren<TextMeshProUGUI>();
            if (t.text != obstacleLabel) t.text = obstacleLabel;
        }

        /// <summary>The obstacle label's words (checks read them).</summary>
        public string ClueObstacleLabel => clueObstacleTag.GetComponentInChildren<TextMeshProUGUI>().text;

        void PlaceClueLabel(RectTransform rt, CanvasGroup g, bool show, Vector3 world, float dt)
        {
            g.alpha = Mathf.MoveTowards(g.alpha, show ? 1f : 0f, dt * 6f);
            if (g.alpha <= 0f) return;
            var sp = Camera.main.WorldToScreenPoint(world);
            float scale = canvas.GetComponent<RectTransform>().localScale.x;
            rt.anchoredPosition = new Vector2(sp.x / scale, sp.y / scale);
            rt.localScale = Vector3.one * HudLayout.Scale;
        }

        string ParText(bool pace) => parTicks <= 0 ? "" : pace ? $"PAR {Ui.Secs(parTicks)}  ·  BEST {Ui.Secs(bestTicks)}" : $"PAR {Ui.Secs(parTicks)}";

        /// <summary>
        /// Medal pace, only on a level you've settled before (a first run stays about the puzzle) and
        /// never while a solution or attract replay plays: the line under the clock adds your best,
        /// and the coin flips from gold to silver at par + 1 s and to bronze at par + 4 s.
        /// </summary>
        void UpdatePace(int tick, float dt)
        {
            bool pace = bestTicks > 0 && parTicks > 0 && !session.Muted && !Watching && !TrailerMode;
            string line = ParText(pace);
            if (parText.text != line) parText.text = line;
            if (paceCoin.gameObject.activeSelf != pace) paceCoin.gameObject.SetActive(pace);
            if (!pace) return;
            var m = SaveData.MedalFor(Mathf.Max(1, tick), parTicks);
            if (m != paceMedal)
            {
                if (paceMedal != Medal.None) paceFlip = 0f; // a flip as gold or silver slips away (or comes back on a rewind)
                paceMedal = m;
                paceCoin.sprite = Kit.CoinSmall(m);
            }
            paceFlip = Mathf.Min(1f, paceFlip + dt / 0.35f);
            float sx = Mathf.Abs(Mathf.Cos(paceFlip * Mathf.PI)), punch = 1f + 0.25f * Mathf.Sin(paceFlip * Mathf.PI);
            paceCoin.rectTransform.localScale = new Vector3(Mathf.Max(0.08f, sx) * punch, punch, 1f);
            // left of the clock's digits (monospaced at 0.6 em of its 54-unit font)
            float digits = Ui.Secs(tick).Length * 0.6f * 54f;
            paceCoin.rectTransform.anchoredPosition = new Vector2(-digits - 34f, -34f);
        }

        public static string Roman(int n)
        {
            if (n <= 0 || n >= 40) return n.ToString();
            string[] tens = { "", "X", "XX", "XXX" }, ones = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" };
            return tens[n / 10] + ones[n % 10];
        }

        /// <summary>The banner's second line as set (checks read it).</summary>
        public string BannerSub => bannerSub.text;

        public void Banner(string title, string sub, Color color, float duration)
        {
            banner.text = title;
            banner.color = color;
            bannerSub.text = sub;
            bannerT = 0f;
            bannerDur = duration;
        }

        /// <summary>Showing the HUD (re)starts the level-title intro, so it plays after a chapter card too.</summary>
        public void SetVisible(bool v)
        {
            if (v && shownAlpha < 0.5f) introT = -0.3f; // let whatever was on screen (chapter card) clear first
            shownAlpha = v ? 1f : 0f;
        }

        /// <summary>Settles the level-title intro at once (shots that open mid-level).</summary>
        public void SkipIntro() => introT = 10f;

        /// <summary>Control hints as "&lt;b&gt;Key&lt;/b&gt; label" pairs, drawn as brass keycaps.</summary>
        public void SetHints(string text)
        {
            if (text == hintSource) return;
            hintSource = text;
            LayoutHints();
        }

        /// <summary>One row of keycaps, or above 100 % HUD size two (the first half on top), so the
        /// larger hints still end short of the watch.</summary>
        void LayoutHints()
        {
            hintsTwoRows = HudLayout.TwoRows;
            string text = hintSource ?? "";
            if (!hintsTwoRows)
            {
                hintWidth = Kit.Keycaps(hintRow, text);
                Kit.Keycaps(hintRow2, "");
                return;
            }
            var pairs = System.Text.RegularExpressions.Regex.Matches(text, @"<b>.*?</b>[^<]*");
            var top = new System.Text.StringBuilder();
            var bottom = new System.Text.StringBuilder();
            for (int i = 0; i < pairs.Count; i++) (i < (pairs.Count + 1) / 2 ? top : bottom).Append(pairs[i].Value);
            hintWidth = Mathf.Max(Kit.Keycaps(hintRow2, top.ToString()), Kit.Keycaps(hintRow, bottom.ToString()));
        }

        /// <summary>The level's one-line teaching tip, shown above the control hints.</summary>
        public void SetTip(string text)
        {
            tipText.text = text ?? "";
            tipGroup.alpha = 0f;
        }

        /// <summary>Swaps the tip's text in place (no slide-in), e.g. when the player unfolds a hint.</summary>
        public void SetTipText(string text) => tipText.text = text ?? "";
        /// <summary>The control-hint row as last set ("&lt;b&gt;Key&lt;/b&gt; label" pairs).</summary>
        public string HintsText => hintSource;

        // ---- measurements for the HUD-size check, in screen pixels
        float CanvasScale => ((RectTransform)canvas.transform).localScale.x;
        /// <summary>The numbers behind the measurements (canvas scale, hint row scale and width).</summary>
        public string HintDebug => $"canvas {CanvasScale:0.000}, row scale {hintRoot.localScale.x:0.000}, row width {hintWidth:0}, room {HudLayout.Room(((RectTransform)canvas.transform).rect.width):0}";
        /// <summary>Key-hint rows in use (two above 100 % HUD size).</summary>
        public int HintRows => hintRow2.childCount > 0 ? 2 : 1;
        /// <summary>The key hints' label size (18 units) on screen.</summary>
        public float HintLabelPx => 18f * CanvasScale * hintRoot.localScale.x;
        /// <summary>The line under the watch (18 units) on screen.</summary>
        public float TermPx => 18f * CanvasScale * termText.rectTransform.localScale.x;
        /// <summary>The tip (24 units) on screen.</summary>
        public float TipPx => 24f * CanvasScale * tipRt.localScale.x;

        /// <summary>Screen-space boxes of the bottom HUD that must not meet: the hint rows (their
        /// keycaps), the tip panel, the watch's keep-out box and the line under it. Returns the
        /// pairs that overlap.</summary>
        public List<string> BottomOverlaps()
        {
            Rect World(RectTransform rt)
            {
                var c = new Vector3[4];
                rt.GetWorldCorners(c);
                return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
            }
            Rect Content(RectTransform row)
            {
                bool any = false;
                var r = new Rect();
                foreach (RectTransform child in row)
                {
                    var w = World(child);
                    r = any ? Rect.MinMaxRect(Mathf.Min(r.xMin, w.xMin), Mathf.Min(r.yMin, w.yMin), Mathf.Max(r.xMax, w.xMax), Mathf.Max(r.yMax, w.yMax)) : w;
                    any = true;
                }
                return r;
            }
            float s = CanvasScale, cw = Screen.width;
            var wb = HudLayout.WatchBox;
            var boxes = new List<(string name, Rect r)>
            {
                ("hints", Content(hintRow)),
                ("watch", Rect.MinMaxRect(cw * 0.5f + wb.x * s, 46f * s, cw * 0.5f + wb.z * s, wb.w * s)),
            };
            if (hintRow2.childCount > 0) boxes.Add(("hints2", Content(hintRow2)));
            if (tipGroup.alpha > 0.5f) boxes.Add(("tip", World(tipPanel.Rt)));
            float tw = termText.preferredWidth * termText.rectTransform.localScale.x * s;
            var term = World(termText.rectTransform);
            boxes.Add(("term", Rect.MinMaxRect(cw * 0.5f - tw * 0.5f, term.yMin, cw * 0.5f + tw * 0.5f, term.yMax)));
            var hits = new List<string>();
            for (int i = 0; i < boxes.Count; i++)
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    if (boxes[i].name == "watch" && boxes[j].name == "term") continue; // the line sits under the watch's case by design
                    if (boxes[i].r.Overlaps(boxes[j].r)) hits.Add($"{boxes[i].name}/{boxes[j].name}");
                }
            return hits;
        }
        public string TipText => tipText.text;

        void LateUpdate()
        {
            float dt = Mathf.Min(Clock.Dt, 0.05f); // a loading hitch must not skip the animations
            group.alpha = Mathf.MoveTowards(group.alpha, shownAlpha * (1f - 0.75f * Dim), dt * 4f);
            if (session == null || session.Def == null) return;
            var s = session.Cur;
            var d = session.Def;
            timeText.text = $"<mspace=0.6em>{Ui.Secs(s.Tick)}</mspace>";
            UpdatePace(s.Tick, dt);
            string speedTag = LevelSession.GameSpeed < 0.999f && !session.Muted ? $"SPEED {Mathf.RoundToInt(LevelSession.GameSpeed * 100)}%" : "";
            if (speedText.text != speedTag) speedText.text = speedTag;
            timeText.richText = true;
            AnimateIntro(dt);
            // the key hints end short of the pocket watch: long labels or rebound key names shrink the row
            float k = HudLayout.Scale, cw = ((RectTransform)canvas.transform).rect.width;
            if (hintsTwoRows != HudLayout.TwoRows) LayoutHints();
            hintRoot.localScale = Vector3.one * Mathf.Min(k, HudLayout.Room(cw) / Mathf.Max(1f, hintWidth));
            timeRoot.localScale = Vector3.one * k;
            termText.rectTransform.localScale = Vector3.one * k;
            restartRoot.localScale = Vector3.one * k;
            restartRoot.anchoredPosition = new Vector2(0, -64f - 60f * k);
            tipRt.localScale = Vector3.one * k;
            tipRt.sizeDelta = new Vector2(HudLayout.TipWidth(cw), 120f);
            if (titleGroup.gameObject.activeSelf == TrailerMode)
                foreach (var rt in new[] { titleGroup, timeRoot, hintRoot, tipRt, introBand.Rt }) rt.gameObject.SetActive(!TrailerMode);

            // pocket watch
            string big, small;
            float fill;
            Color col;
            bool frozen = s.PFrozen > 0 && !s.Dead;
            bool noCredit = d.LoanLimit >= 0 && s.Loans >= d.LoanLimit;
            if (frozen)
            {
                big = Ui.Secs1(s.PFrozen);
                small = "REPAYING";
                fill = s.PFrozen / (float)Rules.FreezeTicks;
                col = Palette.Ice;
            }
            else if (s.Countdown > 0 || s.Pending)
            {
                big = Ui.Secs1(s.Countdown);
                small = "DUE IN";
                fill = s.Countdown / (float)d.Term;
                col = s.Countdown <= Rules.TicksPerSecond ? Color.Lerp(Palette.Ice, Palette.Danger, 0.6f) : Palette.Ice;
            }
            else if (noCredit)
            {
                big = "—";
                small = "NO CREDIT";
                fill = 0f;
                col = Palette.Coral;
            }
            else
            {
                big = "3.0";
                small = "LOAN READY";
                fill = 1f;
                col = Palette.Gold;
            }
            watchBig.text = big;
            watchSmall.text = small;
            watchFill.fillAmount = Mathf.Lerp(watchFill.fillAmount, fill, 1f - Mathf.Exp(-25f * dt));
            watchFill.color = col;
            bool urgent = !frozen && s.Countdown > 0 && s.Countdown <= Rules.TicksPerSecond;
            watchPulse = urgent ? Mathf.Abs(Mathf.Sin(Clock.Now * 12f)) : Mathf.MoveTowards(watchPulse, 0f, dt * 3f);
            watch.localScale = Vector3.one * (1f + watchPulse * 0.06f) * HudLayout.WatchScale;
            watchGlow.color = new Color(col.r, col.g, col.b, (frozen ? 0.35f : 0.12f) + watchPulse * 0.3f);
            // the 3D watch: its second hand counts the debt down to twelve; frost while repaying
            if (s.Loans > lastLoans) watchRoll.Kick(260f);
            if (frozen && !wasFrozen) watchRoll.Kick(-200f);
            lastLoans = s.Loans;
            wasFrozen = frozen;
            float t = Clock.Now;
            watch3d.HourDeg = 304f;
            watch3d.MinuteDeg = 48f;
            float secFrac = frozen ? s.PFrozen / (float)Rules.FreezeTicks : s.Countdown > 0 ? s.Countdown / (float)d.Term : 0f;
            watch3d.SecondDeg = Mathf.LerpAngle(watch3d.SecondDeg, secFrac * 360f, 1f - Mathf.Exp(-20f * dt));
            watch3d.SmallDeg = Mathf.Floor(s.Tick / (float)Rules.TicksPerSecond) * 6f;
            watch3d.Frost = Mathf.MoveTowards(watch3d.Frost, frozen ? 1f : 0f, dt * 4f);
            watch3d.Roll = watchRoll.Step(0f, dt) * 0.08f + (urgent ? Mathf.Sin(t * 40f) * 2.5f : 0f);
            watch3d.Yaw = Mathf.Sin(t * 0.7f) * 7f;
            watch3d.Pitch = -6f + Mathf.Sin(t * 0.5f) * 3f;
            for (int i = 0; i < pips.Length; i++) pips[i].color = i < d.LoanLimit - s.Loans ? Palette.Ice : new Color(1, 1, 1, 0.18f);

            // the thaw forecast in words, so it never rests on the ring's colour alone
            Forecast = session.State == LevelSession.Mode.Playing ? session.Ghost.Result : GhostPreview.Verdict.None;
            string forecast = VerdictText(Forecast);
            bool debt = !frozen && (s.Countdown > 0 || s.Pending);
            string term = debt && forecast.Length > 0 ? "THAW HERE:  <size=21>" + forecast + "</size>" : termLine;
            if (termText.text != term) termText.text = term;

            // aim tag
            int aim = session.Aim;
            bool showAim = aim >= 0 && session.LoanAvailable && session.State == LevelSession.Mode.Playing && !s.IsObstacleFrozen(d, aim);
            aimGroup.alpha = Mathf.MoveTowards(aimGroup.alpha, showAim ? 1f : 0f, dt * 10f);
            aimTag.localScale = Vector3.one * Mathf.Lerp(0.7f, 1f, Ease.OutBack(aimGroup.alpha, 2.2f)) * HudLayout.Scale;
            aimPanel.Sheen = Mathf.Repeat(Clock.Now * 0.6f, 3f) - 0.8f;
            aimPanel.Apply();
            if (showAim)
            {
                var cam = Camera.main;
                var world = session.Board.ObstacleCenter(aim) + Vector3.up * 0.9f;
                var sp = cam.WorldToScreenPoint(world);
                var scale = canvas.GetComponent<RectTransform>().localScale.x;
                aimTag.anchoredPosition = new Vector2(sp.x / scale, sp.y / scale);
                aimText.text = $"<b>FREEZE 3.0s</b>\n<size=15><color=#C9D3F0>repay in {Ui.Secs1(d.Term)}s</color></size>"
                    + (forecast.Length > 0 ? $"\n<size=16>{forecast}</size>" : "");
            }

            // the clue's labels; the aim tag takes the obstacle's spot while it's aimed
            bool playing = session.State == LevelSession.Mode.Playing || session.State == LevelSession.Mode.Intro;
            PlaceClueLabel(clueObstacleTag, clueObstacleGroup, playing && clueObstacle >= 0 && !(showAim && aim == clueObstacle) && !s.IsObstacleFrozen(d, Mathf.Max(0, clueObstacle)),
                clueObstacle >= 0 ? session.Board.ObstacleCenter(clueObstacle) + Vector3.up * 0.9f : Vector3.zero, dt);
            PlaceClueLabel(clueTileTag, clueTileGroup, playing && clueTile >= 0,
                clueTile >= 0 ? session.Board.At(clueTile) + Vector3.up * 0.35f : Vector3.zero, dt);

            bool showTip = tipText.text.Length > 0 && session.State != LevelSession.Mode.Won;
            // the tip slides in once the title has docked, on a panel sized to its text
            float tipIn = showTip ? Ease.OutCubic((introT - 1.9f) / 0.6f) : 0f;
            tipGroup.alpha = Mathf.MoveTowards(tipGroup.alpha, tipIn, dt * 3f);
            tipRt.anchoredPosition = new Vector2(56 - (1f - tipGroup.alpha) * 40f, HudLayout.TipY);
            if (showTip)
            {
                float h = tipText.preferredHeight;
                tipBarRt.sizeDelta = new Vector2(4, h);
                tipPanel.SetSize(new Vector2(Mathf.Min(tipRt.sizeDelta.x + 40f, tipText.preferredWidth + 52f), h + 28f));
                tipPanel.Rt.anchoredPosition = new Vector2(-14, -14);
                tipPanel.Apply();
            }

            float tagBlend = Watching ? 1f : session.FocusBlend;
            string tagText = Watching ? "SOLUTION" : "FOCUS";
            if (focusText.text != tagText) focusText.text = tagText;
            focusGroup.alpha = tagBlend * (1f - rewindGroup.alpha); // the rewind tag takes the same spot
            focusRoot.localScale = Vector3.one * Mathf.Lerp(0.8f, 1f, Ease.OutBack(tagBlend, 2f)) * HudLayout.Scale;
            focusTag.Glow = 0.35f + 0.25f * Mathf.Sin(Clock.Now * 4f);
            focusTag.Apply();
            rewindGroup.alpha = Mathf.MoveTowards(rewindGroup.alpha, session.State == LevelSession.Mode.Rewinding ? 1f : 0f, dt * 8f);
            restartGroup.alpha = Mathf.MoveTowards(restartGroup.alpha, RestartHold >= 0f ? 1f : 0f, dt * (RestartHold >= 0f ? 12f : 4f));
            if (RestartHold >= 0f) restartFill.sizeDelta = new Vector2(300f * Mathf.Clamp01(RestartHold), 5f);
            if (restartText.text != RestartLabel) restartText.text = RestartLabel;
            rewindRoot.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, Ease.OutBack(rewindGroup.alpha, 2f)) * HudLayout.Scale;
            bool ready = session.Waiting && session.State == LevelSession.Mode.Playing && !session.Paused && !TrailerMode;
            readyGroup.alpha = Mathf.MoveTowards(readyGroup.alpha, ready ? 1f : 0f, dt * (ready ? 5f : 8f));
            readyRoot.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, Ease.OutBack(readyGroup.alpha, 2f)) * HudLayout.Scale;
            string readyLine = $"<b><cspace=0.3em>TIME WAITS</cspace></b>\n<size=17><color=#C9D3F0>{ReadyLabel}</color></size>";
            if (readyText.text != readyLine) readyText.text = readyLine;
            readyTag.Glow = 0.3f + 0.2f * Mathf.Sin(Clock.Now * 2.5f);
            readyTag.Apply();
            rewindDial.localRotation = Quaternion.Euler(0, 0, Clock.Now * 540f); // hands run backwards
            rewindTag.Glow = 0.5f;
            rewindTag.Apply();

            bannerT += dt;
            float a = bannerT > bannerDur ? Mathf.Max(0, 1f - (bannerT - bannerDur) / 0.35f) : 1f;
            bannerGroup.alpha = bannerT < 0f ? 0f : a;
            bannerBand.Rt.localScale = new Vector3(Ease.OutExpo(bannerT / 0.35f), Mathf.Lerp(0.2f, 1f, Ease.OutBack(bannerT / 0.3f)), 1f);
            bannerBand.Sheen = -2f;
            bannerBand.Apply();
            bannerFx.Age = bannerT - 0.08f;
            bannerSubFx.Age = bannerT - 0.35f;
        }

        /// <summary>Level start: the title stamps in large at centre over a band, then docks top-left.</summary>
        void AnimateIntro(float dt)
        {
            introT += dt;
            var size = ((RectTransform)canvas.transform).rect.size;
            Vector2 corner = new Vector2(56, -40);
            Vector2 centre = new Vector2(size.x * 0.5f - 330f * HudLayout.Scale, -size.y * 0.5f + 80f);
            float fly = Ease.InOutCubic((introT - 1.25f) / 0.55f);
            titleGroup.anchoredPosition = Vector2.Lerp(centre, corner, fly);
            titleGroup.localScale = Vector3.one * Mathf.Lerp(1.45f, 1f, fly) * HudLayout.Scale;
            nameFx.Age = introT - 0.25f;
            numberFx.Age = introT - 0.05f;
            chapterText.alpha = Ease.OutCubic((introT - 0.5f) * 3f) * 0.9f;
            float band = introT < 1.25f ? Ease.OutExpo(introT / 0.35f) : 1f - Ease.OutCubic((introT - 1.25f) / 0.4f);
            introBand.Rt.anchoredPosition = new Vector2(0, 80f);
            introBand.Rt.localScale = new Vector3(Ease.OutExpo(introT / 0.4f), 1f, 1f);
            introBand.Image.color = new Color(1, 1, 1, Mathf.Clamp01(band));
            introBand.Sheen = -2f;
            introBand.Apply();
            // the watch rises into place and the key hints fade up once the title has settled
            float rise = Ease.OutBack((introT - 1.35f) / 0.6f, 1.4f);
            watch.anchoredPosition = new Vector2(0, Mathf.LerpUnclamped(-340f, 46f + HudLayout.WatchLift, rise));
            hintGroup.alpha = Ease.OutCubic((introT - 1.7f) / 0.5f);
            termText.alpha = 0.75f * Ease.OutCubic((introT - 1.75f) / 0.5f);
            timeRoot.anchoredPosition = new Vector2(Mathf.Lerp(260f, -56f, Ease.OutCubic((introT - 1.5f) / 0.5f)), -40f);
        }
    }
}
