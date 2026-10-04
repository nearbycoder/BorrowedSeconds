using System.Collections;
using System.Collections.Generic;
using System.IO;
using BorrowedSeconds.Sim;
using BorrowedSeconds.View;
using UnityEngine;

namespace BorrowedSeconds.Game
{
    /// <summary>
    /// Bootstraps the whole game at runtime (the scene only needs a camera) and owns the flow
    /// between levels. Command line: -bsLevel N, -bsCapture DIR [-bsOnly ID] [-bsShots t1,t2].
    /// </summary>
    public sealed class GameRoot : MonoBehaviour
    {
        public static GameRoot I { get; private set; }
        public LevelCatalog Catalog { get; private set; }
        public readonly InputReader Input = new InputReader();
        public Camera Cam { get; private set; }
        public CameraRig Rig { get; private set; }
        public WorldEnvironment Env { get; private set; }
        public LevelSession Session { get; private set; }
        public int LevelIndex { get; private set; }

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
        }

        void Start()
        {
            var args = System.Environment.GetCommandLineArgs();
            string capture = Arg(args, "-bsCapture");
            int level = int.TryParse(Arg(args, "-bsLevel"), out int lv) ? Mathf.Clamp(lv - 1, 0, Catalog.Levels.Count - 1) : 0;
            if (capture != null)
            {
                StartCoroutine(Autopilot(capture, Arg(args, "-bsOnly"), Arg(args, "-bsShots")));
                return;
            }
            LoadLevel(level);
        }

        static string Arg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        void Update()
        {
            Input.Poll();
            if (Session == null) return;
            Env.FrozenAmount = Mathf.MoveTowards(Env.FrozenAmount, Session.Cur.PFrozen > 0 && !Session.Cur.Dead ? 1f : 0f, Time.unscaledDeltaTime * 4f);
            Env.RewindAmount = Mathf.MoveTowards(Env.RewindAmount, Session.State == LevelSession.Mode.Rewinding ? 1f : 0f, Time.unscaledDeltaTime * 6f);
            Env.FocusAmount = Session.FocusBlend;
            if (Session.Autoplay != null) return;
            if (Input.Restart) LoadLevel(LevelIndex);
            if (Session.State == LevelSession.Mode.Won && (Input.Confirm || Input.AnyKey))
                LoadLevel((LevelIndex + 1) % Catalog.Levels.Count);
        }

        public void LoadLevel(int index)
        {
            if (Session != null) Destroy(Session.gameObject);
            LevelIndex = index;
            var def = Catalog.Levels[index];
            Session = new GameObject("Level " + def.Id).AddComponent<LevelSession>();
            Session.Init(def, Input, Cam);
            Session.Events += OnSimEvents;
            Session.Died += _ => { Env.PulseDeath(); Rig.Shake(0.5f); };
            Rig.Frame(Session.Board.Bounds, true);
        }

        void OnSimEvents(SimState s, List<SimEvent> events)
        {
            foreach (var e in events)
            {
                switch (e.Type)
                {
                    case Ev.Borrow:
                        Env.PulseBorrow();
                        Rig.Punch(0.9f);
                        Session.Hitstop(0.08f);
                        break;
                    case Ev.Due:
                        Env.PulseFreeze();
                        Rig.Shake(0.18f);
                        Rig.Punch(0.5f);
                        break;
                    case Ev.PlayerThaw:
                        Rig.Shake(0.12f);
                        break;
                    case Ev.LockLatch:
                        Env.Flash(0.6f);
                        Rig.Shake(0.15f);
                        break;
                    case Ev.Win:
                        Env.Flash(0.8f);
                        Rig.Punch(-0.6f);
                        break;
                }
            }
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
                        Debug.Log($"[Autopilot] cam pos={Cam.transform.position} rot={Cam.transform.rotation.eulerAngles} fov={Cam.fieldOfView} aspect={Cam.aspect} bounds={Session.Board.Bounds} sun={Env.Sun.transform.forward}");
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
    }
}
