using System.Collections;
using System.Collections.Generic;
using System.IO;
using BorrowedSeconds.Audio;
using BorrowedSeconds.Sim;
using BorrowedSeconds.UI;
using BorrowedSeconds.View;
using UnityEngine;

namespace BorrowedSeconds.Game
{
    /// <summary>
    /// Bootstraps the whole game at runtime (the scene only needs a camera) and owns the flow
    /// between title, level select, levels and menus.
    /// Command line: -bsLevel N, -bsCapture DIR [-bsOnly ID] [-bsShots t1,t2], -bsMenus DIR.
    /// </summary>
    public sealed class GameRoot : MonoBehaviour
    {
        public enum Flow { Title, Levels, Card, Playing, Paused, Complete, Settings, Ending }

        public static GameRoot I { get; private set; }
        public LevelCatalog Catalog { get; private set; }
        public readonly InputReader Input = new InputReader();
        public Camera Cam { get; private set; }
        public CameraRig Rig { get; private set; }
        public WorldEnvironment Env { get; private set; }
        public LevelSession Session { get; private set; }
        public Fx Fx { get; private set; }
        public Hud Hud { get; private set; }
        public AudioDirector Audio { get; private set; }
        public SaveData Save { get; private set; }
        public int LevelIndex { get; private set; }
        public Flow State { get; private set; }

        Canvas menuCanvas;
        TitleScreen title;
        LevelSelectScreen levels;
        PauseScreen pause;
        SettingsScreen settings;
        CompleteScreen complete;
        ChapterCard card;
        EndingScreen ending;
        Flow settingsReturn;
        float wonAt = -1f, attractTimer;
        int attractIndex;
        bool capturing;
        static readonly string[] AttractLevels = { "1-5", "3-5", "2-4", "4-1", "3-2" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I == null) new GameObject("Game").AddComponent<GameRoot>();
        }

        void Awake()
        {
            I = this;
            DontDestroyOnLoad(gameObject);
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = 144;
            Catalog = new LevelCatalog();
            Save = SaveData.Load();

            Cam = Camera.main;
            if (Cam == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                Cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            Env = new GameObject("Environment").AddComponent<WorldEnvironment>();
            Env.transform.SetParent(transform, false);
            Env.Build(Cam);
            Rig = Cam.gameObject.GetComponent<CameraRig>() ?? Cam.gameObject.AddComponent<CameraRig>();
            Rig.Init(Cam);
            Fx = new GameObject("Fx").AddComponent<Fx>();
            Fx.transform.SetParent(transform, false);
            Fx.Build();
            Hud = Hud.Create(transform);
            Audio = AudioDirector.Create(transform);
            BuildMenus();
            ApplySettings();
        }

        void BuildMenus()
        {
            menuCanvas = Ui.MakeCanvas("MenuCanvas", 20, transform);
            var root = menuCanvas.transform;
            title = new TitleScreen(root, Continue, () => ShowLevels(Save.lastLevel), () => OpenSettings(Flow.Title), Quit,
                () => Save.ids.Length == 0 ? "Begin" : Save.finished ? "Replay" : "Continue");
            levels = new LevelSelectScreen(root, Catalog, Save, i => StartLevel(i, true), ShowTitle);
            pause = new PauseScreen(root, Resume, () => { pause.Hide(); StartLevel(LevelIndex, false); },
                () => { pause.Hide(); ShowLevels(LevelIndex); }, () => OpenSettings(Flow.Paused), () => { pause.Hide(); ShowTitle(); });
            settings = new SettingsScreen(root, Save, () => { ApplySettings(); Save.Save(); }, CloseSettings);
            complete = new CompleteScreen(root, Next, () => { complete.Hide(); StartLevel(LevelIndex, false); },
                () => { complete.Hide(); ShowLevels(LevelIndex); });
            card = new ChapterCard(root);
            ending = new EndingScreen(root);
        }

        void ApplySettings()
        {
            Audio.Master = Save.master;
            Audio.Music = Save.music;
            Audio.Effects = Save.sfx;
            Rig.ShakeEnabled = Save.shake;
            Env.ReduceFlashing = Save.reduceFlashing;
            LevelSession.FocusScale = Save.focus;
            if (!Application.isEditor && !capturing)
            {
                var mode = Save.fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
                if (UnityEngine.Screen.fullScreenMode != mode) UnityEngine.Screen.fullScreenMode = mode;
            }
        }

        void Start()
        {
            var args = System.Environment.GetCommandLineArgs();
            string capture = Arg(args, "-bsCapture");
            string menus = Arg(args, "-bsMenus");
            capturing = capture != null || menus != null;
            if (capture != null)
            {
                StartCoroutine(Autopilot(capture, Arg(args, "-bsOnly"), Arg(args, "-bsShots")));
                return;
            }
            if (menus != null)
            {
                StartCoroutine(MenuTour(menus));
                return;
            }
            if (int.TryParse(Arg(args, "-bsLevel"), out int lv))
            {
                StartLevel(Mathf.Clamp(lv - 1, 0, Catalog.Levels.Count - 1), false);
                return;
            }
            ShowTitle();
        }

        static string Arg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        // ---------------------------------------------------------------- flow

        void ShowTitle()
        {
            State = Flow.Title;
            levels.Hide();
            title.Show();
            Hud.SetVisible(false);
            Rig.ShiftX = 0.56f;
            Rig.Zoom = 1.6f;
            Audio.SetMusic("music_title");
            LoadAttract();
        }

        void LoadAttract()
        {
            string id = AttractLevels[attractIndex % AttractLevels.Length];
            attractIndex++;
            int idx = Catalog.Levels.FindIndex(l => l.Id == id);
            var sol = idx >= 0 ? Catalog.SolutionFor(Catalog.Levels[idx]) : null;
            if (sol == null) idx = 0;
            LoadLevel(idx);
            Session.AllowInput = false;
            Session.Autoplay = sol?.Actions;
            Session.IntroTime = 1.2f;
            Session.Muted = true;
            attractTimer = 0f;
        }

        void ShowLevels(int focus)
        {
            State = Flow.Levels;
            title.Hide();
            levels.Show(focus);
            Hud.SetVisible(false);
            Rig.ShiftX = 0f;
            Rig.Zoom = 1.15f;
            Audio.SetMusic("music_title");
            if (Session == null || Session.Autoplay == null) LoadAttract();
            Session.AllowInput = false;
        }

        void Continue()
        {
            int i = Mathf.Clamp(Save.lastLevel, 0, Catalog.Levels.Count - 1);
            while (i > 0 && !levels.Unlocked(i)) i--;
            if (Save.finished) i = 0;
            StartLevel(i, true);
        }

        void Quit()
        {
            Save.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>Loads a level for play, with the chapter card first when entering a chapter.</summary>
        void StartLevel(int index, bool withCard)
        {
            title.Hide();
            levels.Hide();
            complete.Hide();
            pause.Hide();
            Rig.ShiftX = 0f;
            Rig.Zoom = 1f;
            LoadLevel(index);
            Save.lastLevel = index;
            Save.Save();
            var def = Catalog.Levels[index];
            Audio.SetMusic(MusicFor(def.Chapter, index));
            bool firstInChapter = index == 0 || Catalog.Levels[index - 1].Chapter != def.Chapter;
            if (withCard && firstInChapter)
            {
                State = Flow.Card;
                Session.AllowInput = false;
                Session.IntroTime = float.MaxValue;
                Hud.SetVisible(false);
                card.Show(def.Chapter, () =>
                {
                    State = Flow.Playing;
                    Session.IntroTime = 0.35f;
                    Session.AllowInput = true;
                    Hud.SetVisible(true);
                });
            }
            else
            {
                State = Flow.Playing;
                Hud.SetVisible(true);
            }
        }

        string MusicFor(int chapter, int index)
        {
            if (index == Catalog.Levels.Count - 1) return "music_finale";
            return chapter <= 2 ? "music_a" : "music_b";
        }

        void Pause()
        {
            if (State != Flow.Playing || Session == null) return;
            State = Flow.Paused;
            Session.Paused = true;
            Session.AllowInput = false;
            pause.Show();
            Sfx.Play("ui_click");
        }

        void Resume()
        {
            pause.Hide();
            State = Flow.Playing;
            Session.Paused = false;
            Session.AllowInput = true;
        }

        void OpenSettings(Flow from)
        {
            settingsReturn = from;
            State = Flow.Settings;
            title.Hide();
            pause.Hide();
            settings.Show();
        }

        void CloseSettings()
        {
            settings.Hide();
            State = settingsReturn;
            if (State == Flow.Title) title.Show();
            else if (State == Flow.Paused) pause.Show();
        }

        void Next()
        {
            complete.Hide();
            if (LevelIndex >= Catalog.Levels.Count - 1)
            {
                State = Flow.Ending;
                Hud.SetVisible(false);
                Save.finished = true;
                Save.Save();
                int total = 0, par = 0, golds = 0;
                foreach (var d in Catalog.Levels)
                {
                    int b = Save.Best(d.Id), p = Catalog.SolutionFor(d)?.Par ?? 0;
                    total += b;
                    par += p;
                    if (SaveData.MedalFor(b, p) == Medal.Gold) golds++;
                }
                Audio.SetMusic("music_title");
                ending.Show(total, par, golds, Catalog.Levels.Count, ShowTitle);
                return;
            }
            StartLevel(LevelIndex + 1, true);
        }

        void OnWon()
        {
            wonAt = Time.unscaledTime;
            if (Session.Autoplay != null && State != Flow.Playing) return;
            var def = Session.Def;
            int prev = Save.Best(def.Id);
            Save.Record(def.Id, Session.Tick);
            if (LevelIndex + 1 < Catalog.Levels.Count) Save.lastLevel = LevelIndex + 1;
            Save.Save();
            pendingComplete = (Session.Tick, Catalog.SolutionFor(def)?.Par ?? 0, prev);
        }

        (int ticks, int par, int prev)? pendingComplete;
        int lastCountdown;

        void Update()
        {
            Input.Poll();
            float dt = Time.unscaledDeltaTime;
            bool top(MenuScreen s) => s.Visible && TopScreen() == s;
            title.Update(Input, dt, top(title));
            levels.Update(Input, dt, top(levels));
            pause.Update(Input, dt, top(pause));
            settings.Update(Input, dt, top(settings));
            complete.Update(Input, dt, top(complete));
            card.Update(Input, dt, top(card));
            ending.Update(Input, dt, top(ending));

            if (Session == null) return;
            var s = Session.Cur;
            Env.FrozenAmount = Mathf.MoveTowards(Env.FrozenAmount, s.PFrozen > 0 && !s.Dead && !Session.Muted ? 1f : 0f, dt * 4f);
            Env.RewindAmount = Mathf.MoveTowards(Env.RewindAmount, Session.State == LevelSession.Mode.Rewinding ? 1f : 0f, dt * 6f);
            Env.FocusAmount = Session.FocusBlend;
            Audio.Muffle = Env.FrozenAmount;
            Audio.Focus = Session.FocusBlend;
            Audio.MusicPitch = Mathf.Lerp(1f, 0.8f, Env.RewindAmount);
            int cd = s.Countdown;
            if (cd != lastCountdown && cd > 0 && cd <= 60 && !Session.Muted && Session.State == LevelSession.Mode.Playing)
            {
                // debt warning ticks speed up through the last three seconds
                int every = cd > 40 ? 10 : cd > 20 ? 5 : 2;
                if (cd % every == 0) Sfx.Play("tick", cd > 20 ? 0.35f : 0.55f, cd > 20 ? 1f : 1.25f);
            }
            lastCountdown = cd;

            if (capturing) return;
            switch (State)
            {
                case Flow.Title:
                case Flow.Levels:
                    attractTimer += dt;
                    if ((Session.State == LevelSession.Mode.Won && Time.unscaledTime - wonAt > 2.5f) || attractTimer > 40f) LoadAttract();
                    break;
                case Flow.Playing:
                    if (Input.Pause) { Pause(); break; }
                    if (Input.Restart && Session.State != LevelSession.Mode.Won) { Sfx.Play("ui_back"); StartLevel(LevelIndex, false); break; }
                    if (pendingComplete.HasValue && Time.unscaledTime - wonAt > 1.1f)
                    {
                        var (t, p, prev) = pendingComplete.Value;
                        pendingComplete = null;
                        State = Flow.Complete;
                        complete.Show(t, p, prev, LevelIndex >= Catalog.Levels.Count - 1);
                    }
                    break;
            }
        }

        MenuScreen TopScreen()
        {
            MenuScreen best = null;
            int order = -1;
            foreach (var s in new MenuScreen[] { title, levels, pause, settings, complete, card, ending })
                if (s.Visible && s.Root.GetSiblingIndex() > order) { order = s.Root.GetSiblingIndex(); best = s; }
            return best;
        }

        public void LoadLevel(int index)
        {
            if (Session != null) Destroy(Session.gameObject);
            pendingComplete = null;
            LevelIndex = index;
            var def = Catalog.Levels[index];
            Session = new GameObject("Level " + def.Id).AddComponent<LevelSession>();
            Session.Init(def, Input, Cam);
            Session.Events += OnSimEvents;
            Session.Died += _ =>
            {
                Env.PulseDeath();
                Rig.Shake(0.5f);
                Fx.Death(Session.Board.Player.WorldPos);
                if (!Session.Muted) Sfx.Play("death");
                Hud.Banner("DEFAULTED", "rewinding…", Palette.Danger, 0.7f);
            };
            Session.RewindChanged += on => { if (on && !Session.Muted) Sfx.Play("rewind", 0.8f); };
            Session.Won += OnWon;
            Session.BorrowDenied += _ => { Rig.Shake(0.06f); if (!Session.Muted) Sfx.Play("denied", 0.7f); };
            Rig.Frame(Session.Board.Bounds, true);
            Hud.Bind(Session, Catalog);
            Hud.SetHints("<b>WASD</b> move     <b>Click</b> borrow     <b>Shift</b> focus     <b>Z</b> rewind     <b>R</b> restart     <b>Esc</b> pause");
            Hud.SetTip(def.Hint);
        }

        void OnSimEvents(SimState s, List<SimEvent> events)
        {
            var board = Session.Board;
            var def = Session.Def;
            bool loud = !Session.Muted;
            foreach (var e in events)
            {
                switch (e.Type)
                {
                    case Ev.Borrow:
                        Env.PulseBorrow();
                        Rig.Punch(0.9f);
                        Session.Hitstop(0.08f);
                        Fx.Borrow(board.At(s.P, 0.7f), board.ObstacleCenter(e.A));
                        if (loud) Sfx.Play("borrow");
                        break;
                    case Ev.Due:
                        Env.PulseFreeze();
                        Rig.Shake(0.18f);
                        Rig.Punch(0.5f);
                        Fx.Freeze(board.At(s.P));
                        if (loud) Sfx.Play("freeze");
                        break;
                    case Ev.PlayerThaw:
                        Rig.Shake(0.12f);
                        Fx.Thaw(board.At(s.P));
                        if (loud) Sfx.Play("thaw");
                        break;
                    case Ev.ObstacleThaw:
                        Fx.ObstacleThaw(board.ObstacleCenter(e.A));
                        if (loud) Sfx.World("obstacle_thaw", 0.7f);
                        break;
                    case Ev.Step:
                        Fx.Dust(board.At(e.A));
                        if (loud) Sfx.World("step", 0.55f, Random.Range(0.92f, 1.08f));
                        break;
                    case Ev.Bump:
                        Fx.Bump(board.At(e.A) + BoardView.DirVec(e.B) * 0.4f);
                        Rig.Shake(0.04f);
                        if (loud) Sfx.World("bump", 0.6f);
                        break;
                    case Ev.SliderBounce:
                        if (loud) Sfx.World("slider_thunk", 0.35f, Random.Range(0.9f, 1.1f));
                        break;
                    case Ev.RotorSweep:
                        if (loud) Sfx.World("rotor_whoosh", 0.3f, Random.Range(0.95f, 1.05f));
                        break;
                    case Ev.RotorReverse:
                        if (loud) Sfx.World("rotor_clank", 0.45f);
                        break;
                    case Ev.LaserFire:
                        if (loud) Sfx.World("laser_zap", 0.35f, Random.Range(0.95f, 1.05f));
                        break;
                    case Ev.PlateDown:
                        Fx.Plate(board.At(def.Plates[e.A].Tile), true);
                        if (loud) Sfx.World("plate", 0.7f);
                        break;
                    case Ev.PlateUp:
                        if (loud) Sfx.World("plate", 0.4f, 0.8f);
                        break;
                    case Ev.GateOpen:
                    case Ev.GateClose:
                        if (loud) Sfx.World("gate", 0.5f, e.Type == Ev.GateOpen ? 1.1f : 0.9f);
                        break;
                    case Ev.LockCharge:
                        Fx.LockTick(board.At(def.Locks[e.A]));
                        if (loud) Sfx.World("lock_tick", 0.6f, Mathf.Pow(2f, PentatonicStep(e.B) / 12f));
                        break;
                    case Ev.LockReset:
                        if (loud) Sfx.World("lock_reset", 0.5f);
                        break;
                    case Ev.LockLatch:
                        Env.Flash(0.6f);
                        Rig.Shake(0.15f);
                        Fx.Latch(board.At(def.Locks[e.A]));
                        if (loud) Sfx.Play("lock_latch");
                        break;
                    case Ev.ExitOpen:
                        Fx.ExitOpen(board.At(def.Exit));
                        if (loud) Sfx.Play("exit_open", 0.8f);
                        break;
                    case Ev.Win:
                        Env.Flash(0.8f);
                        Rig.Punch(-0.6f);
                        Fx.Win(board.At(def.Exit));
                        if (loud) Sfx.Play("win");
                        break;
                }
            }
        }

        static int PentatonicStep(int k)
        {
            int[] scale = { 0, 2, 4, 7, 9 };
            return scale[k % 5] + 12 * (k / 5);
        }

        // ---------------------------------------------------------------- autopilot / capture

        IEnumerator Autopilot(string dir, string only, string shots)
        {
            Directory.CreateDirectory(dir);
            var log = new List<string>();
            var shotTicks = new List<int>();
            if (shots != null)
                foreach (var p in shots.Split(','))
                    if (int.TryParse(p, out int t)) shotTicks.Add(t);
            int pass = 0, fail = 0;
            for (int i = 0; i < Catalog.Levels.Count; i++)
            {
                var def = Catalog.Levels[i];
                if (only != null && def.Id != only) continue;
                var sol = Catalog.SolutionFor(def);
                if (sol == null) { log.Add($"SKIP {def.Id} (no solution)"); continue; }
                LoadLevel(i);
                State = Flow.Playing;
                Hud.SetVisible(true);
                Session.Autoplay = sol.Actions;
                Session.IntroTime = 0.4f;
                var wanted = new SortedSet<int>(shotTicks);
                if (shots == null)
                {
                    wanted.Add(1);
                    foreach (var a in sol.Actions) if (Act.IsBorrow(a.Action)) { wanted.Add(a.Tick + 4); wanted.Add(a.Tick + 30); }
                    wanted.Add(Mathf.Max(1, sol.Par - 2));
                }
                float timeout = Time.realtimeSinceStartup + sol.Par * LevelSession.TickDt * 2f + 10f;
                int dueShots = 0;
                Session.Events += (st, evs) =>
                {
                    foreach (var e in evs) if (e.Type == Ev.Due && dueShots++ < 3) wanted.Add(st.Tick + 8);
                };
                while (Session.State != LevelSession.Mode.Won && !Session.Cur.Dead && Time.realtimeSinceStartup < timeout)
                {
                    if (wanted.Count > 0 && Session.Tick >= wanted.Min)
                    {
                        int t = wanted.Min;
                        wanted.Remove(t);
                        Session.Paused = true;
                        yield return null;
                        yield return null;
                        ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"{def.Id}_t{Session.Tick:0000}.png"));
                        yield return null;
                        yield return null;
                        Session.Paused = false;
                    }
                    yield return null;
                }
                bool won = Session.State == LevelSession.Mode.Won;
                yield return new WaitForSecondsRealtime(0.6f);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"{def.Id}_win.png"));
                yield return null;
                yield return null;
                if (won) pass++; else fail++;
                log.Add($"{(won ? "PASS" : "FAIL")} {def.Id} {def.Name} tick={Session.Tick} dead={Session.Cur.Dead}");
                Debug.Log("[Autopilot] " + log[log.Count - 1]);
            }
            log.Add($"done pass={pass} fail={fail}");
            File.WriteAllLines(Path.Combine(dir, "autopilot.log"), log);
            Debug.Log("[Autopilot] done");
            yield return new WaitForSecondsRealtime(0.3f);
            Application.Quit(fail == 0 ? 0 : 1);
        }

        /// <summary>Screenshots every menu screen (for visual review), then quits.</summary>
        IEnumerator MenuTour(string dir)
        {
            Directory.CreateDirectory(dir);
            IEnumerator Shot(string name, float wait)
            {
                yield return new WaitForSecondsRealtime(wait);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
                yield return null;
                yield return null;
            }
            ShowTitle();
            yield return Shot("01_title", 3.5f);
            bool fake = Save.ids.Length == 0;
            if (fake)
            {
                // fake a bit of progress so the ledger shows medals
                for (int i = 0; i < 7; i++)
                {
                    var d = Catalog.Levels[i];
                    int par = Catalog.SolutionFor(d)?.Par ?? 100;
                    Save.Record(d.Id, par + i * 15);
                }
            }
            ShowLevels(6);
            yield return Shot("02_levels", 1.2f);
            StartLevel(5, true);
            Session.Autoplay = Catalog.SolutionFor(Session.Def)?.Actions;
            yield return Shot("03_chapter_card", 1.6f);
            yield return new WaitForSecondsRealtime(2.5f);
            yield return Shot("04_playing", 2.5f);
            Pause();
            yield return Shot("05_pause", 0.8f);
            OpenSettings(Flow.Paused);
            yield return Shot("06_settings", 0.8f);
            CloseSettings();
            Resume();
            float until = Time.realtimeSinceStartup + 30f;
            while (Session.State != LevelSession.Mode.Won && Time.realtimeSinceStartup < until) yield return null;
            yield return new WaitForSecondsRealtime(0.1f);
            complete.Show(Session.Tick, Catalog.SolutionFor(Session.Def)?.Par ?? 0, 0, false);
            State = Flow.Complete;
            yield return Shot("07_complete", 1.4f);
            complete.Hide();
            State = Flow.Ending;
            ending.Show(9000, 8200, 7, 20, ShowTitle);
            yield return Shot("08_ending", 6f);
            if (fake) PlayerPrefs.DeleteAll();
            Application.Quit(0);
        }
    }
}
