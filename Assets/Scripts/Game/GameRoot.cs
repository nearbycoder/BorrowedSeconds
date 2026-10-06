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
    /// Command line: -bsLevel N, -bsCapture DIR [-bsOnly ID] [-bsShots t1,t2], -bsMenus DIR, -bsDemo FILE.mp4, -bsChecks DIR,
    /// -bsTrailer DIR / -bsStills DIR -bsShotList FILE [-bsOnly SHOT].
    /// </summary>
    public sealed partial class GameRoot : MonoBehaviour
    {
        public enum Flow { Title, Levels, Card, Playing, Paused, Complete, Settings, Ending, Watching }

        public static GameRoot I { get; private set; }
        public LevelCatalog Catalog { get; private set; }
        public readonly InputReader Input = new InputReader();
        public Camera Cam { get; private set; }
        public CameraRig Rig { get; private set; }
        public WorldEnvironment Env { get; private set; }
        public LevelSession Session { get; private set; }
        public Fx Fx { get; private set; }
        public Hud Hud { get; private set; }
        public Prompts Prompts { get; private set; }
        public Transition Wipe { get; private set; }
        bool diedSinceRewind, promptDemo, tipOpen;
        int attemptDeaths;
        /// <summary>Defaults in one attempt before the tip points at Watch solution.</summary>
        const int NudgeAfterDeaths = 3;
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
            Prompts = Prompts.Create(transform);
            Audio = AudioDirector.Create(transform);
            BuildMenus();
            ApplySettings();
        }

        void BuildMenus()
        {
            menuCanvas = Ui.MakeCanvas("MenuCanvas", 20, transform);
            Wipe = Transition.Create(transform);
            var root = menuCanvas.transform;
            title = new TitleScreen(root, () => Go(Continue), () => Go(() => ShowLevels(Save.lastLevel), 0.7f), () => OpenSettings(Flow.Title), Quit,
                () => Save.ids.Length == 0 ? "Begin" : Save.finished ? "Replay" : "Continue");
            levels = new LevelSelectScreen(root, Catalog, Save, i => Go(() => StartLevel(i, true)), () => Go(ShowTitle, 0.7f));
            pause = new PauseScreen(root, Resume, () => { pause.Hide(); Go(() => StartLevel(LevelIndex, false), 0.6f); }, WatchFromPause,
                () => { pause.Hide(); Go(() => ShowLevels(LevelIndex)); }, () => OpenSettings(Flow.Paused), () => { pause.Hide(); Go(ShowTitle); });
            settings = new SettingsScreen(root, Save, () => { ApplySettings(); Save.Save(); }, CloseSettings);
            complete = new CompleteScreen(root, () => Go(Next), () => { complete.Hide(); Go(() => StartLevel(LevelIndex, false), 0.6f); },
                () => { complete.Hide(); Go(() => ShowLevels(LevelIndex)); }, amount => Rig.Shake(amount));
            card = new ChapterCard(root);
            ending = new EndingScreen(root);
        }

        /// <summary>Runs a screen change behind the clock-hand wipe (directly during scripted runs).</summary>
        void Go(System.Action change, float seconds = 0.85f)
        {
            if (capturing || Wipe == null) { change(); return; }
            if (Wipe.Busy) return; // one screen change at a time
            Wipe.Play(change, seconds);
        }

        void ApplySettings()
        {
            Audio.Master = Save.master;
            Audio.Music = Save.music;
            Audio.Effects = Save.sfx;
            Rig.ShakeEnabled = Save.shake;
            Env.ReduceFlashing = Save.reduceFlashing;
            LevelSession.FocusScale = Save.focus;
            LevelSession.GameSpeed = capturing ? 1f : Mathf.Clamp(Save.speed, 0.5f, 1f);
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
            string demo = Arg(args, "-bsDemo");
            string bot = Arg(args, "-bsInputBot");
            string checks = Arg(args, "-bsChecks");
            string trailer = Arg(args, "-bsTrailer"), stills = Arg(args, "-bsStills");
            capturing = capture != null || menus != null || demo != null || bot != null || trailer != null || stills != null || checks != null;
            promptDemo = System.Array.IndexOf(args, "-bsPrompts") >= 0;
            if (promptDemo) Save.learned = 0;
            Save.ReadOnly = capturing;
            // scripted runs play at full speed unless asked (-bsSpeed checks that slow play is still exact)
            LevelSession.GameSpeed = float.TryParse(Arg(args, "-bsSpeed"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float speed)
                ? Mathf.Clamp(speed, 0.1f, 4f) : capturing ? 1f : LevelSession.GameSpeed;
            string fps = Arg(args, "-bsFps");
            if (fps != null)
            {
                capturing = true;
                Save.ReadOnly = true;
                StartCoroutine(FpsProbe(fps));
                return;
            }
            if (bot != null)
            {
                StartCoroutine(InputBot(bot));
                return;
            }
            if (checks != null)
            {
                StartCoroutine(Checks(checks));
                return;
            }
            if (demo != null)
            {
                StartCoroutine(DemoReel(demo));
                return;
            }
            if (trailer != null || stills != null)
            {
                StartCoroutine(Trailer(trailer ?? stills, Arg(args, "-bsShotList"), stills != null, Arg(args, "-bsOnly")));
                return;
            }
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
            Rig.ShiftX = 0.64f;
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
            // each volume of four chapters: two on loop A, two on loop B, its last level on the finale loop
            if (index == Catalog.Levels.Count - 1) return "music_finale";
            bool lastOfVolume = chapter % 4 == 0 && (index + 1 >= Catalog.Levels.Count || Catalog.Levels[index + 1].Chapter != chapter);
            if (lastOfVolume) return "music_finale";
            return (chapter - 1) % 4 < 2 ? "music_a" : "music_b";
        }

        void WatchFromPause()
        {
            pause.Hide();
            Go(() => StartWatch(LevelIndex), 0.6f);
        }

        /// <summary>
        /// Plays the solver's solution for a level, then hands it back fresh. Watching records no
        /// time and unlocks nothing (OnWon ignores replays outside Flow.Playing).
        /// </summary>
        void StartWatch(int index)
        {
            StartLevel(index, false);
            var sol = Catalog.SolutionFor(Session.Def);
            if (sol == null) return;
            State = Flow.Watching;
            Session.Autoplay = sol.Actions;
            Session.AllowInput = false;
            Hud.Watching = true;
            tipOpen = true;
            RefreshTip(true);
            ShowControlHints();
        }

        void StopWatching()
        {
            if (Wipe != null && Wipe.Busy) return;
            Go(() => StartLevel(LevelIndex, false), 0.6f);
        }

        void UpdateWatch()
        {
            if (Input.Pause || Input.Back || Input.Restart) { Sfx.Play("ui_back"); StopWatching(); }
            else if (Session.State == LevelSession.Mode.Won && Clock.Now - wonAt > 1.6f) StopWatching();
        }

        /// <summary>The tip panel: the level's hint, folded behind H on levels where it gives the trick
        /// away, plus a pointer to Watch solution after a few defaults.</summary>
        void RefreshTip(bool slideIn)
        {
            var def = Session.Def;
            string text = tipOpen || string.IsNullOrEmpty(def.Hint)
                ? def.Hint
                : Input.UsingGamepad ? "Stuck? Press <color=#FFD27A>Select</color> for a hint." : "Stuck? Press <color=#FFD27A>H</color> for a hint.";
            if (State == Flow.Watching) text = "<color=#7CF4FF>The solver's route, at par.</color>  " + (Input.UsingGamepad ? "<color=#FFD27A>B</color>" : "<color=#FFD27A>Esc</color>") + " to stop watching.";
            else if (attemptDeaths >= NudgeAfterDeaths)
                text += (text.Length > 0 ? "\n" : "") + "<size=20><color=#C9D3F0>Still stuck? Pause and choose <b>Watch solution</b>.</color></size>";
            if (slideIn) Hud.SetTip(text); else Hud.SetTipText(text);
        }

        void ToggleTip()
        {
            if (string.IsNullOrEmpty(Session.Def.Hint)) return;
            tipOpen = !tipOpen;
            Sfx.Play(tipOpen ? "ui_click" : "ui_back", 0.7f);
            RefreshTip(false);
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

        // the game keeps running in the background (runInBackground), so a real-time level must
        // not: alt-tabbing away mid-level opens the pause menu instead of letting the debt fall due
        void OnApplicationFocus(bool focus) { if (!focus) FocusLost(false); }
        void OnApplicationPause(bool paused) { if (paused) FocusLost(false); }

        /// <summary>Pauses a level in play when the window loses focus (scripted runs only when forced).</summary>
        void FocusLost(bool force)
        {
            if (capturing && !force) return;
            if (State == Flow.Playing && Session != null && Session.State != LevelSession.Mode.Won && !pendingComplete.HasValue) Pause();
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
            wonAt = Clock.Now;
            if (Session.Autoplay != null && State != Flow.Playing) return;
            var def = Session.Def;
            int prev = Save.Best(def.Id);
            Save.Record(def.Id, Session.Tick);
            if (LevelIndex + 1 < Catalog.Levels.Count) Save.lastLevel = LevelIndex + 1;
            if (!capturing) Save.Save();
            pendingComplete = (Session.Tick, Catalog.SolutionFor(def)?.Par ?? 0, prev);
        }

        (int ticks, int par, int prev)? pendingComplete;
        int lastCountdown;

        void Update()
        {
            Input.Poll();
            float dt = Mathf.Min(Clock.Dt, 0.05f); // menus: a loading hitch must not skip their intros
            bool top(MenuScreen s) => s.Visible && TopScreen() == s && (Wipe == null || !Wipe.Busy);
            Cursor.visible = !Input.UsingGamepad;
            Cursors.Set(State == Flow.Playing && Session != null && Session.Aim >= 0 && Session.LoanAvailable && Session.State == LevelSession.Mode.Playing
                ? Cursors.Kind.Aim : Cursors.Kind.Arrow);
            Prompts.Tick(Session, Save, Input, State == Flow.Playing && Session != null && !Session.Muted && (promptDemo || (!capturing && Session.Autoplay == null)), dt);
            Env.MenuBlur = Mathf.Max(Mathf.Max(Mathf.Max(levels.BlurNow, pause.BlurNow), trailerBlur), Mathf.Max(Mathf.Max(settings.BlurNow, complete.BlurNow), Mathf.Max(card.BlurNow, ending.BlurNow)));
            Hud.Dim = Mathf.Max(pause.BlurNow, Mathf.Max(complete.BlurNow, settings.BlurNow));
            title.Update(Input, dt, top(title));
            levels.Update(Input, dt, top(levels));
            pause.Update(Input, dt, top(pause));
            settings.Update(Input, dt, top(settings));
            complete.Update(Input, dt, top(complete));
            card.Update(Input, dt, top(card));
            ending.Update(Input, dt, top(ending));

            if (Session == null) return;
            if (Input.UsingGamepad != hintsForPad) ShowControlHints();
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

            if (State == Flow.Watching) UpdateWatch(); // only ever entered from the pause menu
            if (capturing) return;
            switch (State)
            {
                case Flow.Title:
                case Flow.Levels:
                    attractTimer += dt;
                    if ((Session.State == LevelSession.Mode.Won && Clock.Now - wonAt > 2.5f) || attractTimer > 40f) LoadAttract();
                    break;
                case Flow.Playing:
                    if (Input.Pause) { Pause(); break; }
                    if (Input.Hint) ToggleTip();
                    if (Input.Restart && Session.State != LevelSession.Mode.Won) { Sfx.Play("ui_back"); StartLevel(LevelIndex, false); break; }
                    if (pendingComplete.HasValue && Clock.Now - wonAt > 1.1f)
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
            if (State == Flow.Watching) State = Flow.Playing; // a new session ends any solution replay
            pendingComplete = null;
            LevelIndex = index;
            var def = Catalog.Levels[index];
            Session = new GameObject("Level " + def.Id).AddComponent<LevelSession>();
            Session.Init(def, Input, Cam);
            Session.Events += OnSimEvents;
            Prompts.ResetLevel();
            Session.Events += (st, evs) => { if ((promptDemo || !capturing) && !Session.Muted) Prompts.OnEvents(Save, evs); };
            Session.Died += _ => diedSinceRewind = true;
            attemptDeaths = 0;
            Hud.Watching = false;
            Session.Died += _ => { if (++attemptDeaths == NudgeAfterDeaths && State == Flow.Playing) RefreshTip(false); };
            Session.RewindChanged += on =>
            {
                if (!on && diedSinceRewind) Prompts.OnAutoRewindDone();
                if (!on) diedSinceRewind = false;
            };
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
            tipOpen = !def.Spoiler;
            ShowControlHints();
            RefreshTip(true);
        }

        bool hintsForPad;

        void ShowControlHints()
        {
            bool changed = hintsForPad != Input.UsingGamepad;
            hintsForPad = Input.UsingGamepad;
            if (State == Flow.Watching)
                Hud.SetHints(hintsForPad ? "<b>B</b> stop watching" : "<b>Esc</b> stop watching");
            else
                Hud.SetHints(hintsForPad
                    ? "<b>Stick</b> move     <b>LB/RB</b> aim     <b>A</b> borrow     <b>LT</b> focus     <b>X</b> rewind     <b>Y</b> restart     <b>Select</b> hint     <b>Start</b> pause"
                    : "<b>WASD</b> move     <b>Click</b> borrow     <b>Shift</b> focus     <b>Z</b> rewind     <b>R</b> restart     <b>H</b> hint     <b>Esc</b> pause");
            if (changed && Session != null) RefreshTip(false); // the folded tip names the device's hint key
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
                        Env.Ripple(board.ObstacleCenter(e.A), 1f);
                        if (loud) Sfx.Play("borrow");
                        break;
                    case Ev.Due:
                        Env.PulseFreeze();
                        Rig.Shake(0.18f);
                        Rig.Punch(0.5f);
                        Fx.Freeze(board.At(s.P));
                        Env.Ripple(board.At(s.P, 0.5f), 0.7f);
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
                        board.PulseLinks(e.A);
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
                float timeout = Time.realtimeSinceStartup + sol.Par * LevelSession.TickDt * 2f / LevelSession.GameSpeed + 10f;
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
            // frame-locked at 30 fps so captured animation timing is exact, whatever the real frame rate
            Directory.CreateDirectory(dir);
            Time.captureFramerate = 30;
            Cursors.Export(dir);
            IEnumerator Wait(float seconds)
            {
                int n = Mathf.RoundToInt(seconds * 30f);
                for (int i = 0; i < n; i++) yield return null;
            }
            IEnumerator Burst(string name, params float[] times)
            {
                float t = 0f;
                foreach (var at in times)
                {
                    yield return Wait(at - t);
                    t = at;
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"{name}_{Mathf.RoundToInt(at * 100):000}.png"));
                    yield return null;
                    t += 1f / 30f;
                }
            }
            ShowTitle();
            yield return Burst("01_title", 0.2f, 0.5f, 0.8f, 1.1f, 1.5f, 2.0f, 3.2f);
            if (Save.ids.Length == 0)
            {
                // fake a bit of progress (in memory only; the save is read-only here) so the ledger shows medals
                for (int i = 0; i < 7; i++)
                {
                    var d = Catalog.Levels[i];
                    int par = Catalog.SolutionFor(d)?.Par ?? 100;
                    Save.Record(d.Id, par + i * 15);
                }
            }
            Wipe.Play(() => ShowLevels(6), 0.9f);
            yield return Burst("02_wipe", 0.15f, 0.3f, 0.45f, 0.6f, 0.75f);
            yield return Burst("03_levels", 0.2f, 0.45f, 0.8f, 1.6f);
            StartLevel(5, true);
            Session.Autoplay = Catalog.SolutionFor(Session.Def)?.Actions;
            yield return Burst("04_card", 0.3f, 0.8f, 1.4f, 2.6f);
            yield return Wait(1.4f);
            yield return Burst("05_play", 0.15f, 0.5f, 1.0f, 1.45f, 1.8f, 2.6f);
            Pause();
            yield return Burst("06_pause", 0.1f, 0.25f, 0.45f, 1.0f);
            OpenSettings(Flow.Paused);
            yield return Burst("07_settings", 0.25f, 1.0f);
            CloseSettings();
            Resume();
            int guard = 0;
            while (Session.State != LevelSession.Mode.Won && guard++ < 30 * 30) yield return null;
            yield return Wait(0.1f);
            complete.Show(Session.Tick, Catalog.SolutionFor(Session.Def)?.Par ?? 0, 0, false);
            State = Flow.Complete;
            yield return Burst("08_complete", 0.25f, 0.6f, 1.0f, 1.2f, 1.4f, 1.7f, 2.6f);
            complete.Hide();
            State = Flow.Ending;
            Hud.SetVisible(false);
            ending.Show(9000, 8200, 7, 20, ShowTitle);
            yield return Burst("09_ending", 0.4f, 1.2f, 2.4f, 3.5f, 6f);
            Time.captureFramerate = 0;
            Application.Quit(0);
        }
    }
}
