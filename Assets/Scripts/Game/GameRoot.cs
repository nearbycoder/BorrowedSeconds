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
        public enum Flow { Title, Levels, Card, Playing, Paused, Complete, Settings, Ending, Watching, HowTo }

        public static GameRoot I { get; private set; }
        public LevelCatalog Catalog { get; private set; }
        public readonly InputReader Input = new InputReader();
        readonly Rumble rumble = new Rumble();
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
        ControlsScreen controls;
        DisplayScreen displayPage;
        HowToPlayScreen howto;
        CompleteScreen complete;
        ChapterCard card;
        EndingScreen ending;
        Flow settingsReturn, howtoReturn;
        float wonAt = -1f, attractTimer;
        int attractIndex;
        bool capturing;
        static readonly string[] ScriptedFlags = { "-bsCapture", "-bsMenus", "-bsDemo", "-bsInputBot", "-bsChecks", "-bsTrailer", "-bsStills", "-bsFps", "-bsFidelityShots" };
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
            // known before the first ApplySettings, so a scripted run never resizes its window
            var argv = System.Environment.GetCommandLineArgs();
            capturing = System.Array.Exists(ScriptedFlags, f => System.Array.IndexOf(argv, f) >= 0);
            // -bsFidelity 0..3: a scripted run at another Graphics fidelity step (they keep High otherwise)
            if (int.TryParse(Arg(argv, "-bsFidelity"), out int fid)) fidelityArg = GraphicsFidelity.Clamp(fid);

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
            pause.SetGhostOn(Save.bestGhost);
            UnityEngine.InputSystem.InputSystem.onDeviceChange += OnDeviceChange;
        }

        void OnDestroy()
        {
            UnityEngine.InputSystem.InputSystem.onDeviceChange -= OnDeviceChange;
            rumble.Stop();
        }

        void OnApplicationQuit() => rumble.Stop();

        /// <summary>A rumble pulse on the pad in use, while a level is really being played (not the
        /// title's replays, Watch solution or a scripted run) and Controller vibration is on.</summary>
        void Buzz(float low, float high, float seconds)
        {
            if (!Save.vibration || !Input.UsingGamepad || State != Flow.Playing || Session == null || Session.Muted) return;
            if (capturing && !botDrivesFlow) return;
            rumble.Pulse(UnityEngine.InputSystem.Gamepad.current, low, high, seconds);
        }

        /// <summary>A pad unplugged or out of battery mid-level pauses it, as losing focus does, and
        /// the hints go back to the keyboard if no pad is left.</summary>
        void OnDeviceChange(UnityEngine.InputSystem.InputDevice device, UnityEngine.InputSystem.InputDeviceChange change)
        {
            if (!(device is UnityEngine.InputSystem.Gamepad)) return;
            if (change != UnityEngine.InputSystem.InputDeviceChange.Removed && change != UnityEngine.InputSystem.InputDeviceChange.Disconnected) return;
            if (rumble.Active == device) rumble.Stop();
            bool padLeft = false;
            foreach (var g in UnityEngine.InputSystem.Gamepad.all) padLeft |= g != device && g.enabled;
            if (!padLeft) Input.UsingGamepad = false;
            FocusLost(botDrivesFlow); // the input bot unplugs its virtual pad on purpose
        }

        void BuildMenus()
        {
            menuCanvas = Ui.MakeCanvas("MenuCanvas", 20, transform);
            Wipe = Transition.Create(transform);
            var root = menuCanvas.transform;
            title = new TitleScreen(root, () => Go(Continue), () => Go(() => ShowLevels(Save.lastLevel), 0.7f), () => OpenHowTo(Flow.Title), () => OpenSettings(Flow.Title), Quit,
                () => Save.ids.Length == 0 ? "Begin" : Save.finished ? "Replay" : "Continue");
            levels = new LevelSelectScreen(root, Catalog, Save, i => Go(() => StartLevel(i, true)), () => Go(ShowTitle, 0.7f));
            pause = new PauseScreen(root, Resume, () => { pause.Hide(); Go(() => StartLevel(LevelIndex, false), 0.6f); }, ToggleClue, WatchFromPause, ToggleBestRun,
                () => { pause.Hide(); Go(() => ShowLevels(LevelIndex)); }, () => OpenSettings(Flow.Paused), () => OpenHowTo(Flow.Paused), () => { pause.Hide(); Go(ShowTitle); });
            settings = new SettingsScreen(root, Save, () => { ApplySettings(); Save.Save(); }, OpenDisplay, CloseSettings, OpenControls, EraseProgress);
            displayPage = new DisplayScreen(root, Save, () => { ApplySettings(); Save.Save(); }, () => { ApplyDisplay(true); Save.Save(); }, CloseDisplay);
            controls = new ControlsScreen(root, () => Input.Keys, BindKey, ResetKeys, CloseControls);
            howto = new HowToPlayScreen(root, CloseHowTo);
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
            Input.Keys = KeyBindings.Load(Save.keys);
            Input.FocusToggle = Save.focusToggle && !capturing;
            // switching vibration on gives one short pulse, so you know it works
            if (Save.vibration && !vibrationWas && Input.UsingGamepad && (!capturing || botDrivesFlow))
                rumble.Pulse(UnityEngine.InputSystem.Gamepad.current, 0.35f, 0.35f, 0.15f);
            vibrationWas = Save.vibration;
            if (Session != null) ShowControlHints(true);
            LevelSession.GameSpeed = capturing ? 1f : Mathf.Clamp(Save.speed, 0.5f, 1f);
            SetHudScale(capturing && !forceHudScale ? hudScaleArg : Save.hudScale);
            if (!capturing || forceDisplay) DisplayOptions.ApplyRenderScale(Save.renderScale);
            GraphicsFidelity.Apply(capturing && !forceDisplay ? fidelityArg : Save.fidelity, Cam, Env.Sun, Env);
            ApplyDisplay(false);
        }

        /// <summary>The Graphics fidelity step a scripted run uses (-bsFidelity; High by default).</summary>
        int fidelityArg = GraphicsFidelity.Default;

        bool vibrationWas = true;
        /// <summary>Set by the ready-hold check: scripted runs otherwise start the clock at once.</summary>
        bool forceReadyHold;
        /// <summary>Scripted runs keep a 100 % HUD unless started with -bsHudScale (or the HUD-size
        /// check sets forceHudScale; it must not use forceDisplay, which would apply the run's save
        /// to the window too).</summary>
        float hudScaleArg = 1f;
        bool forceHudScale;

        /// <summary>HUD size: the HUD scales its groups and the camera re-frames the board clear of them.</summary>
        void SetHudScale(float scale)
        {
            float k = HudLayout.Sizes[HudLayout.Step(scale)];
            if (Mathf.Approximately(k, HudLayout.Scale)) return;
            HudLayout.Scale = k;
            if (Session != null) Rig.Frame(Session.Board.Bounds, false, Session.Board.TileTops);
        }

        /// <summary>Set by the display check: scripted runs otherwise keep the window and render
        /// scale they were started with.</summary>
        bool forceDisplay;

        /// <summary>Fullscreen or the saved window size (<see cref="DisplayOptions.Apply"/>).</summary>
        void ApplyDisplay(bool chosen)
        {
            if (Application.isEditor || (capturing && !forceDisplay)) return;
            DisplayOptions.Apply(Save, chosen);
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
            if (capturing) { Input.Keys = KeyBindings.DefaultKeys(); Input.FocusToggle = false; } // scripted runs never use the player's bindings
            // scripted runs play at full speed unless asked (-bsSpeed checks that slow play is still exact)
            LevelSession.GameSpeed = float.TryParse(Arg(args, "-bsSpeed"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float speed)
                ? Mathf.Clamp(speed, 0.1f, 4f) : capturing ? 1f : LevelSession.GameSpeed;
            // -bsHudScale 1.5: a scripted run with a larger HUD (the autopilot logs the framing it gets)
            if (float.TryParse(Arg(args, "-bsHudScale"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float hudScale))
            {
                hudScaleArg = hudScale;
                SetHudScale(hudScale);
            }
            // -bsRenderScale 0.5: the fps probe measures a lower render resolution
            if (float.TryParse(Arg(args, "-bsRenderScale"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float renderScale))
                DisplayOptions.ApplyRenderScale(renderScale);
            string fidelityShots = Arg(args, "-bsFidelityShots");
            if (fidelityShots != null)
            {
                capturing = true;
                Save.ReadOnly = true;
                StartCoroutine(FidelityTour(fidelityShots));
                return;
            }
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
                StartCoroutine(Checks(checks, Arg(args, "-bsOnly")));
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
            clueLevel = -1;
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
            clueLevel = -1;
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
            if (clueLevel != index) clueLevel = -1; // a clue lasts while you stay on its level, restarts included
            ApplyClue();
            LoadBestRun();
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

        // ---------------------------------------------------------------- the clue

        /// <summary>The level whose clue is showing (-1: none). Leaving the level drops it.</summary>
        int clueLevel = -1;
        static readonly Clue NoClue = new Clue(-1, -1, -1, -1);
        /// <summary>The step showing (one per loan of the route) and all of them.</summary>
        Clue clue = NoClue;
        List<Clue> clueSteps = new List<Clue>();
        int clueStep = -2;
        bool clueLoanOut;

        /// <summary>Pause > Show a clue / Hide the clue: toggles the clue and goes back to the level.</summary>
        void ToggleClue()
        {
            clueLevel = clueLevel == LevelIndex ? -1 : LevelIndex;
            Sfx.Play(clueLevel >= 0 ? "ui_click" : "ui_back");
            Resume();
            ApplyClue();
        }

        /// <summary>Marks the clue on the board, labels it and spells it out in the tip (none while
        /// watching the solution, which shows everything anyway).</summary>
        void ApplyClue()
        {
            if (Session == null) return;
            var sol = Catalog.SolutionFor(Session.Def);
            bool show = clueLevel == LevelIndex && State != Flow.Watching && Session.Autoplay == null && sol != null;
            clueSteps = show ? Clue.Steps(Session.Def, sol.Actions) : new List<Clue>();
            clueSteps.RemoveAll(c => !c.Valid);
            clueStep = -2; // not a step: the board and HUD take whatever comes next
            UpdateClueStep();
            pause.SetClueShown(clueSteps.Count > 0);
            RefreshTip(false);
        }

        /// <summary>The clue follows the loans taken (rewinds included): the step's obstacle is marked
        /// until you borrow, its debt tile until you thaw from that loan, then the next step.</summary>
        void UpdateClueStep()
        {
            if (Session == null) return;
            int step = Clue.StepFor(Session.Cur, clueSteps.Count);
            bool loanOut = Clue.LoanOut(Session.Cur);
            if (step == clueStep && loanOut == clueLoanOut) return;
            bool moved = step != clueStep;
            clueStep = step;
            clueLoanOut = loanOut;
            clue = step >= 0 ? clueSteps[step] : NoClue;
            int obstacle = clue.Valid && !loanOut ? clue.Obstacle : -1;
            Session.Board.SetClue(obstacle, clue.Tile);
            Hud.SetClue(obstacle, clue.Tile, Clue.ObstacleLabel(step));
            if (moved && clue.Valid) RefreshTip(false);
        }

        // ---------------------------------------------------------------- race your best

        /// <summary>A ghost of the saved run on a settled level (null: none).</summary>
        BestRunView bestRun;
        /// <summary>Set by the best-ghost check: scripted runs otherwise never show the ghost.</summary>
        bool forceBestRun;
        /// <summary>The best-run ghost is on screen (checks read it).</summary>
        public bool BestRunShowing => bestRun != null && bestRun.Showing;

        void LoadBestRun()
        {
            bestRun = null;
            if (Session == null || !Save.Cleared(Session.Def.Id)) return;
            var run = RunLog.Decode(Save.Run(Session.Def.Id, out _));
            if (run == null) return;
            var view = new BestRunView(Session.Board, run);
            if (view.Valid) bestRun = view;
        }

        /// <summary>Pause > Best-run ghost: On/Off (saved).</summary>
        void ToggleBestRun()
        {
            Save.bestGhost = !Save.bestGhost;
            Save.Save();
            pause.SetGhostOn(Save.bestGhost);
        }

        void RenderBestRun()
        {
            if (bestRun == null || Session == null) return;
            bool show = Save.bestGhost && !Session.Muted && (!capturing || forceBestRun)
                && (State == Flow.Playing || State == Flow.Paused || State == Flow.Complete);
            // the ghost keeps to the level's clock, rewinds included
            float pos = Session.State == LevelSession.Mode.Rewinding ? Session.RewindPos : Session.Tick + Session.Alpha;
            int tick = Mathf.FloorToInt(pos);
            bestRun.Render(tick, pos - tick, show, Mathf.Min(Clock.Dt, 0.05f), Time.time);
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
            Session.Viewer = true; // Focus slows the replay, Rewind scrubs it back, each borrow is aimed first
            Hud.Watching = true;
            tipOpen = true;
            ApplyClue(); // off while watching
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
            var pad = Input.Pad;
            string hint = Input.UsingGamepad && !string.IsNullOrEmpty(def.HintPad) ? pad.Fill(def.HintPad) : def.Hint;
            string text = tipOpen || string.IsNullOrEmpty(def.Hint)
                ? hint
                : $"Stuck? Press <color=#FFD27A>{(Input.UsingGamepad ? pad.Select : Input.KeyName(KeyAction.Hint))}</color> for a hint.";
            if (State == Flow.Watching)
                text = "<color=#7CF4FF>The solver's route, at par.</color> Each borrow is aimed just before it happens. "
                    + $"{(Input.FocusToggle ? "Press" : "Hold")} <color=#FFD27A>{(Input.UsingGamepad ? pad.LeftTrigger : Input.KeyName(KeyAction.Focus))}</color> to slow it, "
                    + $"hold <color=#FFD27A>{(Input.UsingGamepad ? pad.West : Input.KeyName(KeyAction.Rewind))}</color> to rewind, "
                    + $"<color=#FFD27A>{(Input.UsingGamepad ? pad.East : "Esc")}</color> to stop watching.";
            else if (attemptDeaths >= NudgeAfterDeaths && !clue.Valid)
                text += (text.Length > 0 ? "\n" : "") + "<size=20><color=#C9D3F0>Still stuck? Pause for <b>a clue</b>, or <b>Watch solution</b>.</color></size>";
            else if (attemptDeaths >= NudgeAfterDeaths)
                text += (text.Length > 0 ? "\n" : "") + "<size=20><color=#C9D3F0>Still stuck? Pause and choose <b>Watch solution</b>.</color></size>";
            if (State != Flow.Watching && clue.Valid)
                text += (text.Length > 0 ? "\n" : "") + $"<color=#FFD27A><b>Clue:</b></color> {clue.Words(def, clueStep, clueSteps.Count)}";
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
            rumble.Stop();
            Input.ReleaseFocus();
            Hud.RestartHold = -1f;
            restartArmed = false;
            Session.AllowInput = false;
            pause.Show();
            Sfx.Play("ui_click");
        }

        // the game keeps running in the background (runInBackground), so a real-time level must
        // not: alt-tabbing away mid-level opens the pause menu instead of letting the debt fall due
        void OnApplicationFocus(bool focus) { AppFocus(focus); if (!focus) { rumble.Stop(); FocusLost(realFocusCheck); } }
        void OnApplicationPause(bool paused) { AppFocus(!paused); if (paused) { rumble.Stop(); FocusLost(realFocusCheck); } }

        /// <summary>Set by the real-focus check (inside the private compositor only): a scripted run
        /// reacts to the compositor's own focus changes as a player's game would.</summary>
        bool realFocusCheck;

        // Settings > Mute in background: the whole mix (AudioListener.volume) fades out while the
        // window is in the background and back in when it returns. Scripted runs leave the listener
        // alone (the demo and trailer recorders silence it themselves) unless the check drives it.
        bool appFocused = true;
        /// <summary>Set by the background-mute check: lets a scripted run drive the listener.</summary>
        bool forceBackgroundMute;
        /// <summary>Seconds the mix takes to fade out or back in.</summary>
        const float BackgroundFade = 0.25f;
        void AppFocus(bool focused) => appFocused = focused;

        void FadeBackgroundMute()
        {
            if (capturing && !forceBackgroundMute) return;
            float target = Save.muteBackground && !appFocused ? 0f : 1f;
            AudioListener.volume = Mathf.MoveTowards(AudioListener.volume, target, Time.unscaledDeltaTime / BackgroundFade);
        }

        /// <summary>Pauses a level in play when the window loses focus (scripted runs only when forced).</summary>
        void FocusLost(bool force)
        {
            if (capturing && !force) return;
            if (State == Flow.Playing && Session != null && Session.State != LevelSession.Mode.Won && !pendingComplete.HasValue) Pause();
        }

        // the frame the pause menu closed: its Esc/Start press must not reopen it in the same Update
        int resumedFrame = -1;

        void Resume()
        {
            resumedFrame = Time.frameCount;
            Input.ReleaseFocus(); // a Focus press in the menu must not come back as slow motion
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

        /// <summary>How to play, from the title or the pause menu.</summary>
        void OpenHowTo(Flow from)
        {
            howtoReturn = from;
            State = Flow.HowTo;
            title.Hide();
            pause.Hide();
            howto.Show();
        }

        void CloseHowTo()
        {
            howto.Hide();
            State = howtoReturn;
            if (State == Flow.Title) title.Show();
            else if (State == Flow.Paused) pause.Show();
        }

        void OpenControls()
        {
            settings.Hide();
            controls.Show();
        }

        void CloseControls()
        {
            controls.Hide();
            settings.Show();
            settings.Menu.Selected = SettingsScreen.ControlsRow;
        }

        void OpenDisplay()
        {
            settings.Hide();
            displayPage.Show();
        }

        void CloseDisplay()
        {
            displayPage.Hide();
            settings.Show();
            settings.Menu.Selected = SettingsScreen.DisplayRow;
        }

        void BindKey(KeyAction action, UnityEngine.InputSystem.Key key)
        {
            KeyBindings.Assign(Input.Keys, action, key);
            Save.keys = KeyBindings.Save(Input.Keys);
            Save.Save();
            if (Session != null) { ShowControlHints(); RefreshTip(false); }
        }

        void ResetKeys()
        {
            Input.Keys = KeyBindings.DefaultKeys();
            Save.keys = new string[0];
            Save.Save();
            if (Session != null) { ShowControlHints(); RefreshTip(false); }
        }

        /// <summary>Settings > Erase progress (after its second press): a fresh Ledger, the onboarding
        /// prompts back, settings and key bindings kept.</summary>
        void EraseProgress()
        {
            Save.EraseProgress();
            Save.Save();
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
            Input.ReleaseFocus();
            if (Session.Autoplay != null && State != Flow.Playing) return;
            var def = Session.Def;
            int prev = Save.Best(def.Id);
            Save.Record(def.Id, Session.Tick, RunLog.Encode(Session.Run.Actions()));
            if (LevelIndex + 1 < Catalog.Levels.Count) Save.lastLevel = LevelIndex + 1;
            if (!capturing) Save.Save();
            pendingComplete = (Session.Tick, Catalog.SolutionFor(def)?.Par ?? 0, prev);
        }

        (int ticks, int par, int prev)? pendingComplete;
        int lastCountdown;

        void Update()
        {
            Input.Poll();
            rumble.Tick();
            FadeBackgroundMute();
            float dt = Mathf.Min(Clock.Dt, 0.05f); // menus: a loading hitch must not skip their intros
            bool top(MenuScreen s) => s.Visible && TopScreen() == s && (Wipe == null || !Wipe.Busy);
            Cursor.visible = !Input.UsingGamepad;
            Cursors.Set(State == Flow.Playing && Session != null && Session.Aim >= 0 && Session.LoanAvailable && Session.State == LevelSession.Mode.Playing
                ? Cursors.Kind.Aim : Cursors.Kind.Arrow);
            Prompts.Tick(Session, Save, Input, State == Flow.Playing && Session != null && !Session.Muted && (promptDemo || (!capturing && Session.Autoplay == null)), dt);
            Env.MenuBlur = Mathf.Max(Mathf.Max(Mathf.Max(levels.BlurNow, pause.BlurNow), trailerBlur), Mathf.Max(Mathf.Max(Mathf.Max(settings.BlurNow, Mathf.Max(controls.BlurNow, displayPage.BlurNow)), Mathf.Max(complete.BlurNow, howto.BlurNow)), Mathf.Max(card.BlurNow, ending.BlurNow)));
            Hud.Dim = Mathf.Max(Mathf.Max(pause.BlurNow, howto.BlurNow), Mathf.Max(complete.BlurNow, Mathf.Max(settings.BlurNow, Mathf.Max(controls.BlurNow, displayPage.BlurNow))));
            title.Update(Input, dt, top(title));
            levels.Update(Input, dt, top(levels));
            pause.Update(Input, dt, top(pause));
            settings.Update(Input, dt, top(settings));
            controls.Update(Input, dt, top(controls));
            displayPage.Update(Input, dt, top(displayPage));
            howto.Update(Input, dt, top(howto));
            complete.Update(Input, dt, top(complete));
            card.Update(Input, dt, top(card));
            ending.Update(Input, dt, top(ending));

            if (Session == null) return;
            UpdateClueStep();
            RenderBestRun();
            if (Input.UsingGamepad != hintsForPad || (hintsForPad && Input.Pad != hintsPad)) ShowControlHints();
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
            if (capturing && !botDrivesFlow) return;
            switch (State)
            {
                case Flow.Title:
                case Flow.Levels:
                    attractTimer += dt;
                    if ((Session.State == LevelSession.Mode.Won && Clock.Now - wonAt > 2.5f) || attractTimer > 40f) LoadAttract();
                    break;
                case Flow.Playing:
                    if (Input.Pause && Time.frameCount != resumedFrame) { Pause(); break; }
                    if (Input.Hint) ToggleTip();
                    if (Session.State != LevelSession.Mode.Won && UpdateRestart(dt)) { Sfx.Play("ui_back"); StartLevel(LevelIndex, false); break; }
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

        // restart is instant in a level's first seconds; later it must be held, so a stray key can't
        // throw away a long attempt
        const int QuickRestartTicks = 3 * Rules.TicksPerSecond;
        const float RestartHoldSeconds = 0.6f;
        float restartHeld;
        bool restartArmed;

        bool UpdateRestart(float dt)
        {
            if (Input.Restart && Session.Tick < QuickRestartTicks) { restartArmed = false; Hud.RestartHold = -1f; return true; }
            if (Input.Restart) { restartArmed = true; restartHeld = 0f; } // a fresh press, not a hold carried over from a restart
            // real time, not the clamped menu dt: a hold must take 0.6 s even at a low frame rate
            if (restartArmed && Input.RestartHeld) restartHeld += Mathf.Min(Clock.Dt, 0.25f);
            else restartArmed = false;
            Hud.RestartLabel = $"HOLD {(Input.UsingGamepad ? Input.Pad.North : Input.KeyName(KeyAction.Restart))} TO RESTART".ToUpperInvariant();
            Hud.RestartHold = restartArmed ? restartHeld / RestartHoldSeconds : -1f;
            if (!restartArmed || restartHeld < RestartHoldSeconds) return false;
            restartArmed = false;
            Hud.RestartHold = -1f;
            return true;
        }

        MenuScreen TopScreen()
        {
            MenuScreen best = null;
            int order = -1;
            foreach (var s in new MenuScreen[] { title, levels, pause, settings, controls, displayPage, howto, complete, card, ending })
                if (s.Visible && s.Root.GetSiblingIndex() > order) { order = s.Root.GetSiblingIndex(); best = s; }
            return best;
        }

        public void LoadLevel(int index)
        {
            if (Session != null) Destroy(Session.gameObject);
            rumble.Stop();
            if (State == Flow.Watching) State = Flow.Playing; // a new session ends any solution replay
            pendingComplete = null;
            Input.ReleaseFocus();
            LevelIndex = index;
            var def = Catalog.Levels[index];
            Session = new GameObject("Level " + def.Id).AddComponent<LevelSession>();
            Session.Init(def, Input, Cam);
            clue = NoClue; // StartLevel puts a clue back on its own level
            clueSteps = new List<Clue>();
            clueStep = -2;
            bestRun = null; // and the best-run ghost
            Session.WaitForStart = !capturing || forceReadyHold; // the board can be read before the clock runs
            Session.Events += OnSimEvents;
            Prompts.ResetLevel();
            Session.Events += (st, evs) => { if ((promptDemo || !capturing) && !Session.Muted) Prompts.OnEvents(Save, evs); };
            Session.Died += _ => diedSinceRewind = true;
            attemptDeaths = 0;
            Hud.Watching = false;
            Hud.RestartHold = -1f;
            restartArmed = false;
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
                if (!Session.Muted) Sfx.Stinger("death");
                Buzz(0.9f, 0.9f, 0.35f);
                // say what did it, and whether the player thawed into it or it ran into them
                string why = DeathReport.Describe(Session.Def, Session.Cur.DeathCause, Session.DeathThawed);
                Hud.Banner("DEFAULTED", why.Length > 0 ? why + "  ·  rewinding…" : "rewinding…", Palette.Danger, why.Length > 0 ? 1.5f : 0.7f);
            };
            Session.RewindChanged += on => { if (on && !Session.Muted) Sfx.Play("rewind", 0.8f); };
            Session.Won += OnWon;
            Session.BorrowDenied += _ => { Rig.Shake(0.06f); if (!Session.Muted) Sfx.Play("denied", 0.7f); };
            Rig.Frame(Session.Board.Bounds, true, Session.Board.TileTops);
            Hud.Bind(Session, Catalog, Save.Best(def.Id));
            tipOpen = !def.Spoiler;
            ShowControlHints();
            RefreshTip(true);
        }

        bool hintsForPad;
        PadNames hintsPad;

        void ShowControlHints(bool force = false)
        {
            bool changed = force || hintsForPad != Input.UsingGamepad || hintsPad != Input.Pad;
            string focus = Input.FocusToggle ? "focus on/off" : "focus";
            hintsForPad = Input.UsingGamepad;
            hintsPad = Input.Pad;
            var p = Input.Pad;
            Hud.ReadyLabel = hintsForPad ? $"move or press {p.South} to start" : "move or click to start";
            if (State == Flow.Watching)
                Hud.SetHints(hintsForPad
                    ? $"<b>{p.LeftTrigger}</b> slow     <b>{p.West}</b> rewind     <b>{p.East}</b> stop watching"
                    : $"<b>{Input.KeyName(KeyAction.Focus)}</b> slow     <b>{Input.KeyName(KeyAction.Rewind)}</b> rewind     <b>Esc</b> stop watching");
            else
                Hud.SetHints(hintsForPad
                    ? $"<b>{p.Stick}</b> move     <b>{p.Shoulders}</b> aim     <b>{p.South}</b> borrow     <b>{p.LeftTrigger}</b> {focus}     <b>{p.West}</b> rewind     <b>{p.North}</b> restart     <b>{p.Select}</b> hint     <b>{p.Start}</b> pause"
                    : $"<b>{Input.MoveKeysName()}</b> move     <b>Click</b> borrow     <b>{Input.KeyName(KeyAction.Focus)}</b> {focus}     <b>{Input.KeyName(KeyAction.Rewind)}</b> rewind     "
                      + $"<b>{Input.KeyName(KeyAction.Restart)}</b> restart     <b>{Input.KeyName(KeyAction.Hint)}</b> hint     <b>Esc</b> pause");
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
                        if (loud) Sfx.Stinger("borrow");
                        Buzz(0.2f, 0.5f, 0.1f);
                        break;
                    case Ev.Due:
                        Env.PulseFreeze();
                        Rig.Shake(0.18f);
                        Rig.Punch(0.5f);
                        Fx.Freeze(board.At(s.P));
                        Env.Ripple(board.At(s.P, 0.5f), 0.7f);
                        if (loud) Sfx.Stinger("freeze");
                        Buzz(0.5f, 0.25f, 0.18f);
                        break;
                    case Ev.PlayerThaw:
                        Rig.Shake(0.12f);
                        Fx.Thaw(board.At(s.P));
                        if (loud) Sfx.Stinger("thaw");
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
                        if (loud) Sfx.Stinger("lock_latch");
                        Buzz(0.15f, 0.45f, 0.12f);
                        break;
                    case Ev.ExitOpen:
                        Fx.ExitOpen(board.At(def.Exit));
                        if (loud) Sfx.Stinger("exit_open", 0.8f);
                        break;
                    case Ev.Win:
                        Env.Flash(0.8f);
                        Rig.Punch(-0.6f);
                        Fx.Win(board.At(def.Exit));
                        if (loud) Sfx.Stinger("win");
                        Buzz(0.35f, 0.7f, 0.3f);
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
            bool frameOnly = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-bsFrameOnly") >= 0;
            for (int i = 0; i < Catalog.Levels.Count; i++)
            {
                var def = Catalog.Levels[i];
                if (only != null && def.Id != only) continue;
                var sol = Catalog.SolutionFor(def);
                if (sol == null) { log.Add($"SKIP {def.Id} (no solution)"); continue; }
                LoadLevel(i);
                if (frameOnly)
                {
                    // -bsFrameOnly: just the framing each level gets (screen shapes, HUD sizes), no play
                    log.Add($"FRAME {def.Id} {def.Name} hud={Rig.HudOverlap} pullback={Rig.HudPullback:0.000}");
                    if (Rig.HudOverlap > 0) fail++; else pass++;
                    yield return null;
                    continue;
                }
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
                var run = new RunWatch(Session);
                int dueShots = 0;
                Session.Events += (st, evs) =>
                {
                    foreach (var e in evs) if (e.Type == Ev.Due && dueShots++ < 3) wanted.Add(st.Tick + 8);
                };
                while (Session.State != LevelSession.Mode.Won && !Session.Cur.Dead && run.Alive())
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
                log.Add($"{(won ? "PASS" : "FAIL")} {def.Id} {def.Name} tick={Session.Tick} dead={Session.Cur.Dead} frame: hud={Rig.HudOverlap} pullback={Rig.HudPullback:0.000}" + (won ? "" : $" ({run.Why(Session)})"));
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
            OpenHowTo(Flow.Paused);
            yield return Burst("07_howto", 0.3f, 1.2f);
            CloseHowTo();
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
