using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using BorrowedSeconds.Audio;
using BorrowedSeconds.Sim;
using BorrowedSeconds.UI;
using BorrowedSeconds.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace BorrowedSeconds.Game
{
    /// <summary>
    /// -bsTrailer DIR -bsShotList FILE: plays a shot list (Tools/trailer/shots.json) and records every
    /// shot as its own frame-locked 60 fps clip, DIR/NN_name.video.mp4 plus its audio event log, with
    /// captions, title and end cards drawn by the game's own UI kit; DIR/clips.json lists them.
    /// Tools/trailer/make_trailer.sh cuts the clips together and scores them.
    /// -bsStills DIR -bsShotList FILE: plays the shots that list "stills" with the full HUD and no
    /// captions, and saves those moments as PNG screenshots.
    /// </summary>
    public sealed partial class GameRoot
    {
        float trailerBlur;
        bool stillsRun;

        IEnumerator Trailer(string dir, string listPath, bool stills, string only)
        {
            if (listPath == null || !File.Exists(listPath))
            {
                Debug.LogError("[Trailer] needs -bsShotList FILE");
                Application.Quit(2);
                yield break;
            }
            dir = Path.GetFullPath(dir);
            Directory.CreateDirectory(dir);
            stillsRun = stills;
            var shots = MiniJson.List((Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(listPath)), "shots");
            PrepareTrailerSave();
            ApplySettings();

            var ov = new GameObject("TrailerOverlay").AddComponent<TrailerOverlay>();
            ov.transform.SetParent(transform, false);
            ov.Build();
            ov.Suppress = stills;
            var rec = gameObject.AddComponent<ClipRecorder>();
            rec.Begin(60);
            yield return null;

            var clips = new List<object>();
            for (int n = 0; n < shots.Count; n++)
            {
                var s = (Dictionary<string, object>)shots[n];
                string name = MiniJson.Str(s, "name", "shot" + n);
                if (only != null && System.Array.IndexOf(only.Split(','), name) < 0) continue;
                if (stills ? MiniJson.List(s, "stills").Count == 0 : MiniJson.Bool(s, "stillsOnly", false)) continue;
                yield return SettleShot(ov, s);
                string clip = Path.Combine(dir, name);
                if (!stills) rec.StartClip(clip);
                yield return RunShot(s, ov, stills ? dir : null);
                if (!stills)
                    clips.Add(new Dictionary<string, object> { ["name"] = name, ["file"] = Path.GetFileName(clip), ["frames"] = rec.EndClip() });
                Debug.Log($"[Trailer] {name} done");
            }
            if (!stills)
                File.WriteAllText(Path.Combine(dir, "clips.json"), MiniJson.Serialize(new Dictionary<string, object> { ["fps"] = 60, ["clips"] = clips }));
            rec.Finish();
            yield return null;
            Application.Quit(rec.Ok ? 0 : 1);
        }

        /// <summary>
        /// A known in-memory save (never written): default settings, every chapter but the last
        /// settled with a mix of medals, so the Ledger and the medal coins have something to show.
        /// </summary>
        void PrepareTrailerSave()
        {
            Save.ReadOnly = true;
            Save.ids = new string[0];
            Save.best = new int[0];
            Save.learned = ~0;
            DefaultSettings();
            for (int i = 0; i < Catalog.Levels.Count - 4; i++)
            {
                var d = Catalog.Levels[i];
                int par = Catalog.SolutionFor(d)?.Par ?? 100;
                Save.Record(d.Id, par + (i % 5 == 3 ? 95 : i % 4 == 2 ? 45 : 6 + i % 3 * 4));
            }
        }

        /// <summary>Default settings, so every run looks and sounds the same whatever the player's save says.</summary>
        void DefaultSettings()
        {
            Save.master = 0.8f;
            Save.music = 0.7f;
            Save.sfx = 0.9f;
            Save.focus = 0.2f;
            Save.shake = true;
            Save.reduceFlashing = false;
            ApplySettings();
        }

        /// <summary>Clears the previous shot off screen before the next clip starts recording.</summary>
        IEnumerator SettleShot(TrailerOverlay ov, Dictionary<string, object> next)
        {
            DefaultSettings();
            ov.Clear();
            foreach (var m in new MenuScreen[] { title, levels, pause, settings, complete, card, ending }) m.Hide();
            if (Session != null)
            {
                Session.Paused = true;
                Session.ForcedAim = -1;
                Session.ForcedFocus = false;
                Session.Speed = 1f;
            }
            trailerBlur = Num(next, "blur", 0f); // already up when the next clip starts
            Hud.SetVisible(MiniJson.Bool(next, "hud", MiniJson.Str(next, "type", "level") == "level"));
            for (int i = 0; i < 30; i++) yield return null;
        }

        IEnumerator RunShot(Dictionary<string, object> s, TrailerOverlay ov, string stillsDir)
        {
            string type = MiniJson.Str(s, "type", "level");
            string levelId = MiniJson.Str(s, "level", "3-5");
            int li = Mathf.Max(0, Catalog.Levels.FindIndex(l => l.Id == levelId));
            bool levelShot = type == "level" || type == "chapter";
            bool hud = MiniJson.Bool(s, "hud", levelShot);
            Hud.TrailerMode = stillsDir == null && !MiniJson.Bool(s, "fullHud", false);
            trailerBlur = Num(s, "blur", 0f);
            promptDemo = MiniJson.Bool(s, "prompts", false);
            Save.learned = promptDemo ? MiniJson.Int(s, "learned", 0) : ~0;
            List<TimedAction> acts = null;

            void Backdrop(bool muted)
            {
                LoadLevel(li);
                acts = ReplayFor(Catalog.Levels[li], s);
                Session.Autoplay = acts;
                Session.AllowInput = false;
                Session.Muted = muted;
                Session.Seek(MiniJson.Int(s, "seek", 0));
                FrameShot(s);
            }

            switch (type)
            {
                case "chapter":
                    StartLevel(li, true);
                    acts = ReplayFor(Catalog.Levels[li], s);
                    Session.Autoplay = acts;
                    FrameShot(s);
                    break;
                case "title":
                case "end":
                    Backdrop(true);
                    State = Flow.Title;
                    ov.Logo(type == "end", MiniJson.Str(s, "tagline", ""), MiniJson.Str(s, "kicker", ""),
                        MiniJson.Str(s, "url", ""), MiniJson.Str(s, "footer", ""));
                    break;
                case "ledger":
                    Backdrop(true);
                    State = Flow.Levels;
                    levels.Show(MiniJson.Int(s, "select", 0));
                    break;
                case "titlescreen":
                    Backdrop(true);
                    title.Show();
                    State = Flow.Title;
                    Rig.ShiftX = 0.64f;
                    Rig.Zoom = 1.6f;
                    Rig.Snap();
                    break;
                case "ending":
                    Backdrop(true);
                    State = Flow.Ending;
                    int total = 0, par = 0;
                    foreach (var d in Catalog.Levels)
                    {
                        int p = Catalog.SolutionFor(d)?.Par ?? 0;
                        par += p;
                        total += p + 14;
                    }
                    ending.Show(total, par, 21, Catalog.Levels.Count, () => { });
                    break;
                default: // "level", or a menu over a level ("settings")
                    Backdrop(MiniJson.Bool(s, "muted", false));
                    State = Flow.Playing;
                    break;
            }
            if (type != "chapter") // the chapter card brings the HUD in itself when it closes
            {
                Hud.SetVisible(hud);
                Hud.SkipIntro();
            }

            var caps = MiniJson.List(s, "captions");
            var stamps = MiniJson.List(s, "stamps");
            var events = MiniJson.List(s, "events");
            var stillList = MiniJson.List(s, "stills");
            var aimWindows = MiniJson.List(s, "aim");
            var focusWindows = MiniJson.List(s, "focus");
            var slowWindows = MiniJson.List(s, "slow");
            var capState = new int[caps.Count];
            var stampState = new int[stamps.Count];
            var eventDone = new bool[events.Count];
            var stillDone = new bool[stillList.Count];
            float dur = Num(s, "dur", -1f), hold = Num(s, "hold", 0.8f), completeAfter = Num(s, "complete", -1f);
            int until = MiniJson.Int(s, "until", -1), aimLead = MiniJson.Int(s, "aimLead", 16);
            bool untilWin = MiniJson.Bool(s, "win", false), untilRewound = MiniJson.Bool(s, "death", false);
            float zoom0 = Rig.Zoom, zoomTo = Num(s, "zoomTo", zoom0), zoomTime = Num(s, "zoomTime", 4f);
            float t = 0f, endT = -1f, wonT = -1f;
            int frame = 0, durFrames = dur > 0f ? Mathf.RoundToInt(dur * 60f) : -1;
            if (Session != null && State != Flow.Card) Session.Paused = false;

            void HideAll()
            {
                if (MiniJson.Bool(s, "keep", false)) return;
                ov.HideCaption();
                ov.HideStamp();
            }

            while (true)
            {
                int tick = Session != null ? Session.Tick : 0;
                for (int i = 0; i < caps.Count; i++)
                {
                    var c = (Dictionary<string, object>)caps[i];
                    if (capState[i] == 0 && Due(c, "at", "tick", t, tick))
                    {
                        ov.Caption(MiniJson.Str(c, "kicker", ""), MiniJson.Str(c, "head", ""), MiniJson.Str(c, "sub", ""), MiniJson.Str(c, "pos", "tl"),
                            Num(c, "wrap", 700f));
                        capState[i] = 1;
                    }
                    else if (capState[i] == 1 && Due(c, "out", "outTick", t, tick))
                    {
                        ov.HideCaption();
                        capState[i] = 2;
                    }
                }
                for (int i = 0; i < stamps.Count; i++)
                {
                    var c = (Dictionary<string, object>)stamps[i];
                    if (stampState[i] == 0 && Due(c, "at", "tick", t, tick))
                    {
                        ov.Stamp(MiniJson.Str(c, "text", ""), MiniJson.Str(c, "sub", ""), MiniJson.Str(c, "band", "") == "hold");
                        stampState[i] = 1;
                    }
                    else if (stampState[i] == 1 && Due(c, "out", "outTick", t, tick))
                    {
                        ov.HideStamp();
                        stampState[i] = 2;
                    }
                }
                for (int i = 0; i < events.Count; i++)
                {
                    var e = (Dictionary<string, object>)events[i];
                    if (eventDone[i] || !Due(e, "at", "tick", t, tick)) continue;
                    eventDone[i] = true;
                    ShotEvent(MiniJson.Str(e, "do", ""), MiniJson.Int(e, "arg", 0));
                }

                if (Session != null && levelShot && State == Flow.Playing)
                {
                    Session.ForcedAim = AimAt(aimWindows, tick, acts, aimLead);
                    Session.ForcedFocus = InWindow(focusWindows, tick, out _);
                    float speed = InWindow(slowWindows, tick, out var w) ? Num(w, 2, 1f) : 1f;
                    Session.Speed = Mathf.MoveTowards(Session.Speed, speed, Clock.Dt * 3f);
                }
                if (Session != null && Session.State == LevelSession.Mode.Won && wonT < 0f) wonT = t;
                if (completeAfter >= 0f && wonT >= 0f && t >= wonT + completeAfter && State == Flow.Playing)
                {
                    State = Flow.Complete;
                    complete.Show(Session.Tick, Catalog.SolutionFor(Session.Def)?.Par ?? 0, 0, false);
                }
                if (zoomTo != zoom0 && stillsDir == null) Rig.Zoom = Mathf.Lerp(zoom0, zoomTo, Ease.InOutCubic(t / Mathf.Max(0.01f, zoomTime)));

                for (int i = 0; i < stillList.Count && stillsDir != null; i++)
                {
                    var st = (Dictionary<string, object>)stillList[i];
                    if (stillDone[i] || !Due(st, "at", "tick", t, tick)) continue;
                    stillDone[i] = true;
                    if (Session != null) Session.Paused = true;
                    yield return null;
                    yield return null;
                    string path = Path.Combine(stillsDir, MiniJson.Str(st, "name", "still" + i) + ".png");
                    ScreenCapture.CaptureScreenshot(path);
                    yield return null;
                    yield return null;
                    Debug.Log("[Trailer] still " + path);
                    if (Session != null) Session.Paused = false;
                }

                if (durFrames > 0)
                {
                    if (frame == durFrames - 30) HideAll();
                    if (frame >= durFrames) break;
                }
                else
                {
                    bool reached = (until >= 0 && tick >= until)
                                   || (untilWin && wonT >= 0f)
                                   || (untilRewound && Session != null && Session.Deaths > 0 && Session.State == LevelSession.Mode.Playing);
                    if (endT < 0f && reached)
                    {
                        endT = t;
                        HideAll();
                    }
                    if (endT >= 0f && t >= endT + hold) break;
                }
                if (t > 90f)
                {
                    Debug.LogWarning("[Trailer] shot timed out: " + MiniJson.Str(s, "name", ""));
                    break;
                }
                yield return null;
                frame++;
                t = frame / 60f;
            }
        }

        /// <summary>Scripted menu moves inside a shot.</summary>
        void ShotEvent(string what, int arg)
        {
            switch (what)
            {
                case "pause":
                    Pause();
                    break;
                case "settings":
                    OpenSettings(Flow.Paused);
                    break;
                case "select":
                    if (settings.Visible) { settings.Menu.Selected = arg; Sfx.Play("ui_hover"); }
                    else if (pause.Visible) { pause.Menu.Selected = arg; Sfx.Play("ui_hover"); }
                    else if (levels.Visible) levels.Select(arg);
                    break;
                case "adjust":
                    if (!settings.Visible) break;
                    var item = settings.Menu.Items[settings.Menu.Selected];
                    if (item.Adjust != null)
                    {
                        item.Adjust(arg);
                        item.Punch = 1f;
                        Sfx.Play("ui_tick");
                    }
                    else if (item.Toggle != null)
                    {
                        item.Activate?.Invoke();
                        item.Punch = 1f;
                        Sfx.Play("ui_click");
                    }
                    break;
            }
        }

        List<TimedAction> ReplayFor(LevelDef def, Dictionary<string, object> s)
        {
            var acts = Catalog.SolutionFor(def)?.Actions ?? new List<TimedAction>();
            if (MiniJson.Bool(s, "fail", false)) acts = FindDefault(def, acts) ?? acts;
            int shift = MiniJson.Int(s, "shift", 0);
            if (shift != 0) acts = acts.ConvertAll(a => new TimedAction(a.Tick + shift, a.Action));
            return acts;
        }

        /// <summary>
        /// Camera for a shot: zoom, sideways shift, a lean toward a tile ("lean": [x, y, amount]) and a
        /// world-space pan ("pan": [x, z]; positive z lowers the board on screen).
        /// </summary>
        void FrameShot(Dictionary<string, object> s)
        {
            if (stillsRun) s = new Dictionary<string, object>(); // screenshots keep the game's own framing
            Rig.Zoom = Num(s, "zoom", 1f);
            Rig.ShiftX = Num(s, "shiftX", 0f);
            Rig.Offset = Vector3.zero;
            var lean = MiniJson.List(s, "lean");
            var board = Session.Board;
            if (lean.Count >= 2)
            {
                float k = Num(lean, 2, 1f);
                var p = board.Pos(Num(lean, 0, 0f), Num(lean, 1, 0f));
                Rig.Offset = new Vector3(p.x - board.Bounds.center.x, 0f, p.z - board.Bounds.center.z) * k;
            }
            var pan = MiniJson.List(s, "pan");
            if (pan.Count >= 2) Rig.Offset += new Vector3(Num(pan, 0, 0f), 0f, Num(pan, 1, 0f));
            Rig.Frame(board.Bounds, true);
            Rig.Snap();
        }

        static int AimAt(List<object> windows, int tick, List<TimedAction> acts, int lead)
        {
            if (InWindow(windows, tick, out var w)) return Mathf.RoundToInt(Num(w, 2, -1f));
            return lead > 0 ? UpcomingBorrow(acts, tick, lead) : -1;
        }

        /// <summary>Whether <paramref name="tick"/> falls in one of the [from, to, ...] windows.</summary>
        static bool InWindow(List<object> windows, int tick, out List<object> hit)
        {
            foreach (var o in windows)
            {
                var w = (List<object>)o;
                if (tick >= Num(w, 0, 0f) && tick <= Num(w, 1, -1f)) { hit = w; return true; }
            }
            hit = null;
            return false;
        }

        /// <summary>A cue fires at a time in seconds ("at") or a simulation tick ("tick").</summary>
        static bool Due(Dictionary<string, object> c, string timeKey, string tickKey, float t, int tick)
        {
            if (c.ContainsKey(tickKey)) return tick >= MiniJson.Int(c, tickKey, int.MaxValue);
            if (c.ContainsKey(timeKey)) return t >= Num(c, timeKey, float.MaxValue);
            return timeKey == "at";
        }

        static float Num(Dictionary<string, object> d, string key, float def)
            => d.TryGetValue(key, out var v) && v != null ? (float)System.Convert.ToDouble(v, CultureInfo.InvariantCulture) : def;

        static float Num(List<object> l, int i, float def)
            => l != null && i < l.Count && l[i] != null ? (float)System.Convert.ToDouble(l[i], CultureInfo.InvariantCulture) : def;
    }

    /// <summary>
    /// The trailer's own layer, drawn with the game's UI kit: a caption card (top left), a stamped
    /// statement band (top centre) and the logo card used for the title and the end.
    /// </summary>
    public sealed class TrailerOverlay : MonoBehaviour
    {
        /// <summary>Stills mode: nothing of the trailer's own is drawn.</summary>
        public bool Suppress;

        const float CapX = 66f, CapY = -60f, CapPad = 24f, CapWrap = 820f, CapIndent = 24f;
        RectTransform capRt;
        CanvasGroup capGroup;
        Panel capPanel;
        Image capBar, capDiamond;
        TextMeshProUGUI kicker, head, sub;
        TextFx kickerFx, headFx, subFx;
        float capAge = -1f, capOutAt = -1f, capH, capSide = -1f;
        Vector2 capBase = new Vector2(CapX, CapY);
        string[] capPending;

        RectTransform stampRt;
        CanvasGroup stampGroup;
        Panel stampBand;
        TextMeshProUGUI stampText, stampSub;
        TextFx stampFx, stampSubFx;
        float stampAge = -1f, stampOutAt = -1f, bandAge;

        RectTransform logoRoot, lockup, rule, urlRoot;
        CanvasGroup logoGroup;
        Image shade, dawn, sun, emblemGlow;
        RawImage emblem;
        WatchStage watch;
        TextMeshProUGUI word1, word2, tagline, urlKicker, urlText, footer;
        TextFx fx1, fx2, fxTag, fxUrl;
        Panel urlPill;
        float logoAge = -1f, logoOutAt = -1f;
        bool endCard;

        public void Build()
        {
            var root = Ui.MakeCanvas("TrailerCanvas", 60, transform).transform;
            BuildLogo(root);
            BuildStamp(root);
            BuildCaption(root);
            Clear();
        }

        void BuildCaption(Transform root)
        {
            capRt = Ui.Rect("Caption", root, new Vector2(0, 1), new Vector2(0, 1), new Vector2(CapX, CapY), new Vector2(CapWrap, 200));
            capGroup = capRt.gameObject.AddComponent<CanvasGroup>();
            capPanel = new Panel("Panel", capRt, new Vector2(CapWrap, 200), Panel.Style.Card);
            capPanel.Rt.anchorMin = capPanel.Rt.anchorMax = new Vector2(0, 1);
            capPanel.Rt.pivot = new Vector2(0, 1);
            capPanel.SetFill(new Color(0.075f, 0.085f, 0.17f, 0.9f), new Color(0.035f, 0.04f, 0.09f, 0.9f));
            capBar = Ui.Img("Bar", capRt, null, Palette.Gold);
            Ui.Place(capBar.rectTransform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(4, 0));
            capDiamond = Ui.Img("Diamond", capRt, Ui.Diamond, Palette.Gold);
            Ui.Place(capDiamond.rectTransform, new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(CapIndent + 7, -13), new Vector2(14, 14));
            kicker = Ui.Text("Kicker", capRt, "", Ui.Semi, 21, Palette.Gold, TextAlignmentOptions.TopLeft);
            Ui.Place(kicker.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(CapIndent + 24, 0), new Vector2(CapWrap, 30));
            kicker.characterSpacing = 28;
            // with wide tracking, kerned pairs lose their spacing ("ROTO R S"); the kicker is all caps anyway
            var features = kicker.fontFeatures;
            features.Remove(UnityEngine.TextCore.OTL_FeatureTag.kern);
            kicker.fontFeatures = features;
            head = Ui.Text("Head", capRt, "", Ui.Heavy, 48, Palette.Paper, TextAlignmentOptions.TopLeft);
            Ui.Place(head.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(CapIndent - 2, -33), new Vector2(1400, 64));
            head.richText = true;
            sub = Ui.Text("Sub", capRt, "", Ui.Regular, 27, new Color(0.86f, 0.89f, 1f, 0.94f), TextAlignmentOptions.TopLeft);
            Ui.Place(sub.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(CapIndent, -98), new Vector2(CapWrap, 120));
            sub.textWrappingMode = TextWrappingModes.Normal;
            sub.richText = true;
            sub.lineSpacing = 4;
            kickerFx = TextFx.On(kicker, TextFx.Kind.Spread, 0.02f, 0.5f, 50f);
            headFx = TextFx.On(head, TextFx.Kind.Rise, 0.018f, 0.45f, 28f);
            subFx = TextFx.On(sub, TextFx.Kind.Rise, 0.005f, 0.4f, 14f);
        }

        void BuildStamp(Transform root)
        {
            stampRt = Ui.Rect("Stamp", root, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -150), new Vector2(1800, 150));
            stampGroup = stampRt.gameObject.AddComponent<CanvasGroup>();
            stampBand = new Panel("Band", stampRt, new Vector2(2400, 150), Panel.Style.Band);
            stampText = Ui.Text("Text", stampRt, "", Ui.Heavy, 80, Palette.Gold);
            Ui.Place(stampText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 16), new Vector2(1800, 100));
            stampText.characterSpacing = 24;
            stampSub = Ui.Text("Sub", stampRt, "", Ui.Semi, 26, Palette.Paper);
            Ui.Place(stampSub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -44), new Vector2(1800, 36));
            stampSub.characterSpacing = 10;
            stampSub.richText = true;
            stampFx = TextFx.On(stampText, TextFx.Kind.Stamp, 0.045f, 0.22f, 60f);
            stampSubFx = TextFx.On(stampSub, TextFx.Kind.Type, 0.014f, 0.2f);
        }

        void BuildLogo(Transform root)
        {
            logoRoot = Ui.Stretch("Logo", root);
            logoGroup = logoRoot.gameObject.AddComponent<CanvasGroup>();
            shade = Ui.Img("Shade", logoRoot, null, new Color(0.015f, 0.02f, 0.05f, 0.55f));
            Ui.Fill(shade.rectTransform);
            dawn = Ui.Img("Dawn", logoRoot, Ui.VGradient, new Color(1f, 0.72f, 0.42f, 0f));
            Ui.Fill(dawn.rectTransform);
            sun = Ui.Img("Sun", logoRoot, Ui.Glow, new Color(1f, 0.8f, 0.5f, 0f));
            Ui.Place(sun.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0, -520), new Vector2(2200, 1400));

            // the title screen's logo lockup, centred: pocket watch emblem, two words, rule, tagline
            lockup = Ui.Rect("Lockup", logoRoot, new Vector2(0.5f, 0.5f), new Vector2(0, 1), Vector2.zero, new Vector2(1000, 330));
            watch = WatchStage.Create(transform, 640);
            emblemGlow = Ui.Img("EmblemGlow", lockup, Ui.Glow, new Color(1f, 0.75f, 0.4f, 0f));
            Ui.Place(emblemGlow.rectTransform, new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(140, -150), new Vector2(520, 520));
            emblem = watch.Show(lockup, new Vector2(330, 330));
            Ui.Place(emblem.rectTransform, new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(140, -150), new Vector2(330, 330));
            word1 = Ui.Text("Borrowed", lockup, "BORROWED", Ui.Heavy, 116, Palette.Paper, TextAlignmentOptions.TopLeft);
            Ui.Place(word1.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(300, -14), new Vector2(900, 130));
            word1.characterSpacing = 12;
            fx1 = TextFx.On(word1, TextFx.Kind.Drop, 0.05f, 0.6f, 90f);
            word2 = Ui.Text("Seconds", lockup, "SECONDS", Ui.Heavy, 116, Palette.Ice, TextAlignmentOptions.TopLeft);
            Ui.Place(word2.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(300, -126), new Vector2(900, 130));
            word2.characterSpacing = 12;
            fx2 = TextFx.On(word2, TextFx.Kind.Stamp, 0.06f, 0.3f, 60f);
            fx2.ShimmerColor = Color.white;
            rule = Kit.Rule(lockup, 620, new Color(Palette.Brass.r, Palette.Brass.g, Palette.Brass.b, 0.9f));
            rule.anchorMin = rule.anchorMax = new Vector2(0, 1);
            rule.pivot = new Vector2(0, 0.5f);
            rule.anchoredPosition = new Vector2(306, -262);
            tagline = Ui.Text("Tag", lockup, "", Ui.Light, 30, new Color(0.86f, 0.89f, 1f, 0.92f), TextAlignmentOptions.TopLeft);
            Ui.Place(tagline.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(308, -284), new Vector2(1100, 40));
            fxTag = TextFx.On(tagline, TextFx.Kind.Type, 0.016f, 0.2f);

            urlRoot = Ui.Rect("Url", logoRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -230), new Vector2(1000, 200));
            urlKicker = Ui.Text("Kicker", urlRoot, "", Ui.Semi, 21, Palette.Gold);
            Ui.Place(urlKicker.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 62), new Vector2(1000, 30));
            urlKicker.characterSpacing = 30;
            urlPill = new Panel("Pill", urlRoot, new Vector2(760, 72), Panel.Style.Tag);
            urlPill.Rt.anchoredPosition = Vector2.zero;
            urlText = Ui.Text("Text", urlPill.Rt, "", Ui.Semi, 34, Palette.Ice);
            Ui.Fill(urlText.rectTransform);
            urlText.characterSpacing = 2;
            fxUrl = TextFx.On(urlText, TextFx.Kind.Type, 0.022f, 0.2f);
            footer = Ui.Text("Footer", urlRoot, "", Ui.Semi, 26, new Color(0.93f, 0.94f, 1f, 1f));
            Ui.Place(footer.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -78), new Vector2(1400, 36));
            footer.characterSpacing = 8;
            footer.richText = true;
        }

        public void Clear()
        {
            capAge = stampAge = logoAge = -1f;
            capPending = null;
            capGroup.alpha = stampGroup.alpha = logoGroup.alpha = 0f;
            logoRoot.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- caption card

        /// <summary>
        /// Shows a caption card (cross-fading from the current one) in a screen corner:
        /// "tl", "tr", "bl" or "br". It slides in from that side.
        /// </summary>
        public void Caption(string kickerText, string headText, string subText, string pos = "tl", float wrap = 700f)
        {
            if (Suppress) return;
            if (capAge >= 0f)
            {
                capPending = new[] { kickerText, headText, subText, pos, wrap.ToString(CultureInfo.InvariantCulture) };
                if (capOutAt < 0f) capOutAt = capAge;
                return;
            }
            bool right = pos.Length > 1 && pos[1] == 'r', bottom = pos.Length > 0 && pos[0] == 'b';
            var corner = new Vector2(right ? 1 : 0, bottom ? 0 : 1);
            capRt.anchorMin = capRt.anchorMax = capRt.pivot = corner;
            capSide = right ? 1f : -1f;
            capBase = new Vector2(right ? -CapX : CapX, bottom ? -CapY : CapY);
            kicker.text = (kickerText ?? "").ToUpperInvariant();
            head.text = headText ?? "";
            sub.text = subText ?? "";
            sub.rectTransform.sizeDelta = new Vector2(wrap, 120);
            head.ForceMeshUpdate();
            sub.ForceMeshUpdate();
            float subW = string.IsNullOrEmpty(sub.text) ? 0f : sub.GetRenderedValues(false).x;
            float subH = string.IsNullOrEmpty(sub.text) ? 0f : sub.preferredHeight;
            float w = Mathf.Max(kicker.GetPreferredValues(kicker.text).x + 24f, head.GetPreferredValues(head.text).x - 2f, subW);
            capH = 94f + (subH > 0f ? subH + 8f : -4f);
            capRt.sizeDelta = new Vector2(CapIndent + w, capH);
            capPanel.Rt.anchoredPosition = new Vector2(-CapPad, CapPad);
            capPanel.SetSize(new Vector2(CapIndent + w + CapPad * 2f, capH + CapPad * 2f - 6f));
            capAge = 0f;
            capOutAt = -1f;
        }

        public void HideCaption()
        {
            capPending = null;
            if (capAge >= 0f && capOutAt < 0f) capOutAt = capAge;
        }

        // ---------------------------------------------------------------- stamp band

        public void Stamp(string text, string subText, bool holdBand = false)
        {
            if (Suppress) return;
            stampText.text = text ?? "";
            stampSub.text = subText ?? "";
            bool two = !string.IsNullOrEmpty(stampSub.text);
            stampText.rectTransform.anchoredPosition = new Vector2(0, two ? 16 : 0);
            stampBand.SetSize(new Vector2(2400, two ? 160 : 126));
            stampAge = 0f;
            stampOutAt = -1f;
            bandAge = holdBand ? 10f : 0f;
        }

        public void HideStamp()
        {
            if (stampAge >= 0f && stampOutAt < 0f) stampOutAt = stampAge;
        }

        // ---------------------------------------------------------------- logo card

        public void Logo(bool end, string tag, string urlKick, string url, string foot)
        {
            if (Suppress) return;
            endCard = end;
            tagline.text = tag;
            urlKicker.text = urlKick.ToUpperInvariant();
            urlText.text = url;
            footer.text = foot;
            urlRoot.gameObject.SetActive(end && url.Length > 0);
            urlPill.SetSize(new Vector2(urlText.GetPreferredValues(url).x + 90f, 76));
            urlPill.Rt.anchoredPosition = Vector2.zero;
            // centre the lockup: the emblem reaches 25 px left of the lockup's origin
            float words = Mathf.Max(word1.GetPreferredValues("BORROWED").x, word2.GetPreferredValues("SECONDS").x,
                tagline.GetPreferredValues(tag).x + 8f, 620f);
            float width = 325f + words, scale = end ? 1.12f : 1.22f;
            lockup.localScale = Vector3.one * scale;
            lockup.anchoredPosition = new Vector2((-width * 0.5f + 25f) * scale, end ? 350f : 200f);
            logoRoot.gameObject.SetActive(true);
            logoAge = 0f;
            logoOutAt = -1f;
        }

        void Update()
        {
            float dt = Clock.Dt;
            UpdateCaption(dt);
            UpdateStamp(dt);
            UpdateLogo(dt);
        }

        void UpdateCaption(float dt)
        {
            if (capAge < 0f) return;
            capAge += dt;
            float a = capAge;
            float outT = capOutAt >= 0f ? Ease.Clamp((a - capOutAt) / 0.3f) : 0f;
            float inT = Ease.OutCubic(a / 0.55f);
            capGroup.alpha = Ease.Clamp(a * 6f) * (1f - Ease.InCubic(outT));
            capRt.anchoredPosition = capBase + new Vector2(capSide * ((1f - inT) * 80f + Ease.InCubic(outT) * 50f), 0f);
            capPanel.Reveal = Ease.OutCubic(a / 0.6f);
            capPanel.Sheen = -0.6f + (a - 0.45f) * 2.2f;
            capPanel.Glow = 0.2f + 0.6f * Mathf.Clamp01(1f - (a - 0.3f) / 0.9f);
            capPanel.Apply();
            capBar.rectTransform.sizeDelta = new Vector2(4f, capH * Ease.OutCubic((a - 0.1f) / 0.45f));
            capDiamond.rectTransform.localRotation = Quaternion.Euler(0, 0, (1f - Ease.OutBack(a / 0.6f)) * 180f + 45f);
            kickerFx.Age = a - 0.08f;
            headFx.Age = a - 0.18f;
            subFx.Age = a - 0.42f;
            if (outT >= 1f)
            {
                capAge = -1f;
                capGroup.alpha = 0f;
                var p = capPending;
                capPending = null;
                if (p != null) Caption(p[0], p[1], p[2], p[3], float.Parse(p[4], CultureInfo.InvariantCulture));
            }
        }

        void UpdateStamp(float dt)
        {
            if (stampAge < 0f) return;
            stampAge += dt;
            bandAge += dt;
            float a = stampAge, b = bandAge;
            float outT = stampOutAt >= 0f ? Ease.Clamp((a - stampOutAt) / 0.25f) : 0f;
            stampGroup.alpha = Ease.Clamp(b * 8f) * (1f - outT);
            stampBand.Rt.localScale = new Vector3(Ease.OutExpo(b / 0.35f), Mathf.Lerp(0.2f, 1f, Ease.OutBack(b / 0.3f)) * (1f - 0.4f * outT), 1f);
            stampBand.Sheen = -0.6f + (a - 0.3f) * 1.6f;
            stampBand.Apply();
            stampFx.Age = a - 0.06f;
            stampSubFx.Age = a - 0.4f;
            if (outT >= 1f) { stampAge = -1f; stampGroup.alpha = 0f; }
        }

        void UpdateLogo(float dt)
        {
            if (logoAge < 0f) return;
            logoAge += dt;
            float a = logoAge;
            float outT = logoOutAt >= 0f ? Ease.Clamp((a - logoOutAt) / 0.4f) : 0f;
            logoGroup.alpha = Ease.Clamp(a * 3f) * (1f - outT);
            shade.color = new Color(0.015f, 0.02f, 0.05f, endCard ? 0.62f : 0.5f);
            float rise = endCard ? Ease.InOutCubic(a / 4f) : 0f;
            dawn.color = new Color(1f, 0.72f, 0.42f, 0.3f * rise);
            sun.color = new Color(1f, 0.78f, 0.48f, 0.5f * rise);
            sun.rectTransform.anchoredPosition = new Vector2(0, Mathf.Lerp(-520f, -230f, rise));

            float e = Ease.Clamp(a / 1.1f), now = Clock.Now;
            watch.Scale = Mathf.Lerp(0.2f, 1f, Ease.OutBack(e, 1.4f));
            watch.Yaw = Mathf.Lerp(-200f, 0f, Ease.OutCubic(e)) + Mathf.Sin(now * 0.55f) * 16f;
            watch.Pitch = Mathf.Sin(now * 0.41f) * 7f - 4f;
            watch.Roll = Mathf.Sin(now * 0.33f) * 3f;
            // ten past ten, the classic watch-face time, with a ticking second hand
            float whole = Mathf.Floor(a), sec = whole + Mathf.Clamp01((a - whole) * 9f);
            watch.SecondDeg = watch.SmallDeg = sec * 6f;
            watch.MinuteDeg = 60f + sec * 0.1f;
            watch.HourDeg = 305f;
            emblem.color = new Color(1, 1, 1, Ease.OutCubic(a * 3f));
            emblemGlow.color = new Color(1f, 0.72f, 0.36f, 0.18f * Ease.OutCubic(e) * (0.85f + 0.15f * Mathf.Sin(now * 1.3f)));
            emblem.rectTransform.anchoredPosition = new Vector2(140, -150 + Mathf.Sin(now * 0.9f) * 5f);
            fx1.Age = a - 0.35f;
            fx2.Age = a - 0.8f;
            float sh = (a - 1.7f) * 12f - 2f;
            fx2.Shimmer = a > 1.7f && sh < 12f ? sh : -100f;
            rule.localScale = new Vector3(Ease.OutExpo((a - 1.15f) / 0.7f), 1, 1);
            fxTag.Age = a - 1.25f;
            if (endCard)
            {
                urlKicker.alpha = Ease.OutCubic((a - 1.9f) * 3f);
                float pop = Ease.OutBack((a - 2.0f) / 0.45f, 1.8f);
                urlPill.Rt.localScale = new Vector3(Mathf.Max(0f, pop), Mathf.Max(0f, pop), 1f);
                urlPill.Glow = 0.4f + 0.3f * Mathf.Sin(now * 2.5f);
                urlPill.Sheen = -0.6f + (a - 2.3f) * 1.8f;
                urlPill.Apply();
                fxUrl.Age = a - 2.15f;
                footer.alpha = Ease.OutCubic((a - 3.0f) * 2f) * 0.9f;
            }
            if (outT >= 1f) { logoAge = -1f; logoRoot.gameObject.SetActive(false); }
        }
    }

    /// <summary>
    /// Frame-locked multi-clip recorder: while a clip is open, every frame goes into its own ffmpeg
    /// pipe and the audio director's event log into the clip's .audio.log.
    /// </summary>
    public sealed class ClipRecorder : MonoBehaviour
    {
        Process ffmpeg;
        Stream pipe;
        StreamWriter audioLog;
        string videoPath;
        int fps, width, height;
        bool live, running;
        public bool Ok { get; private set; } = true;
        public int Frames { get; private set; }

        public void Begin(int framesPerSecond)
        {
            fps = framesPerSecond;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            Time.captureFramerate = fps;
            AudioListener.volume = 0f;
            running = true;
            StartCoroutine(Loop());
        }

        public void StartClip(string basePath)
        {
            videoPath = basePath + ".video.mp4";
            audioLog = new StreamWriter(basePath + ".audio.log");
            BorrowedSeconds.Audio.AudioDirector.Log = audioLog;
            Frames = 0;
            width = height = 0;
            live = true;
        }

        public int EndClip()
        {
            live = false;
            BorrowedSeconds.Audio.AudioDirector.Log = null;
            audioLog?.Dispose();
            audioLog = null;
            if (ffmpeg != null)
            {
                pipe.Dispose();
                ffmpeg.WaitForExit();
                Ok &= ffmpeg.ExitCode == 0;
                ffmpeg.Dispose();
                ffmpeg = null;
            }
            Debug.Log($"[Trailer] wrote {videoPath} ({Frames} frames)");
            return Frames;
        }

        public void Finish()
        {
            if (live) EndClip();
            running = false;
            Time.captureFramerate = 0;
        }

        IEnumerator Loop()
        {
            var eof = new WaitForEndOfFrame();
            while (running)
            {
                yield return eof;
                if (live) Grab();
            }
        }

        void Grab()
        {
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            if (ffmpeg == null) StartEncoder(tex);
            if (tex.width == width && tex.height == height)
            {
                if (Frames == 0) audioLog.WriteLine($"V {Clock.Now.ToString(CultureInfo.InvariantCulture)} {fps}");
                var data = tex.GetRawTextureData<byte>();
                pipe.Write(data.ToArray(), 0, data.Length);
                Frames++;
            }
            Destroy(tex);
        }

        void StartEncoder(Texture2D tex)
        {
            width = tex.width;
            height = tex.height;
            string fmt = tex.format switch
            {
                TextureFormat.RGB24 => "rgb24",
                TextureFormat.ARGB32 => "argb",
                TextureFormat.BGRA32 => "bgra",
                _ => "rgba",
            };
            // near-lossless 4:4:4 intermediates; the final cut is encoded once, by make_trailer.sh
            ffmpeg = Process.Start(new ProcessStartInfo
            {
                FileName = "nice",
                Arguments = $"-n 10 ffmpeg -y -loglevel error -f rawvideo -pix_fmt {fmt} -s {width}x{height} -r {fps} -i - " +
                            $"-vf vflip -c:v libx264 -preset fast -crf 10 -pix_fmt yuv444p \"{videoPath}\"",
                UseShellExecute = false,
                RedirectStandardInput = true,
            });
            pipe = ffmpeg.StandardInput.BaseStream;
        }
    }
}
