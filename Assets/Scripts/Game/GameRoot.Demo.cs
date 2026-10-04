using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using BorrowedSeconds.Sim;
using BorrowedSeconds.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace BorrowedSeconds.Game
{
    /// <summary>
    /// -bsDemo FILE: plays a scripted reel (title, ledger, a tour of levels from the solver's
    /// replays, a default and rewind, the finale and ending) and records it frame-locked at 60 fps
    /// to FILE.video.mp4 plus FILE.audio.log. Run it through Tools/demo/record.sh.
    /// </summary>
    public sealed partial class GameRoot
    {
        IEnumerator DemoReel(string outPath)
        {
            outPath = Path.GetFullPath(outPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            var ov = new GameObject("DemoOverlay").AddComponent<DemoOverlay>();
            ov.transform.SetParent(transform, false);
            ov.Build();
            ov.SetFade(1f);

            // mid-game progress so the ledger has medals and unlocks to show
            for (int i = 0; i < 12; i++)
            {
                var d = Catalog.Levels[i];
                int par = Catalog.SolutionFor(d)?.Par ?? 100;
                Save.Record(d.Id, par + (i % 4 == 3 ? 70 : i % 3 == 2 ? 30 : 6));
            }

            var rec = gameObject.AddComponent<DemoRecorder>();
            rec.Begin(outPath, 60);
            yield return null;

            ShowTitle();
            ov.FadeTo(0f, 0.8f);
            yield return Hold(6f);
            yield return Cut(ov, () => ShowLevels(11));
            yield return Hold(3.4f);

            yield return Cut(ov, null);
            yield return PlayLevel(ov, "1-1", true,
                "Freeze any moving obstacle for <color=#7CF4FF>three seconds</color>.",
                "…but the time is borrowed. When it comes due, <color=#7CF4FF>you</color> freeze.", true);

            var def12 = Catalog.Levels.Find(l => l.Id == "1-2");
            var fail = def12 != null ? FindDefault(def12, Catalog.SolutionFor(def12).Actions) : null;
            if (fail != null)
            {
                yield return Cut(ov, null);
                yield return PlayLevel(ov, "1-2", false,
                    "Frozen, hazards pass straight through you.",
                    null, false, fail, "Thaw inside one and you <color=#FF4F64>default</color>. Time rewinds.");
            }

            yield return Cut(ov, null);
            yield return PlayLevel(ov, "1-5", false,
                "The trick is planning <color=#FFD27A>where</color> the debt lands.",
                "Frozen, you still weigh on a dial. The slider passes right through.", false);

            yield return Cut(ov, null);
            yield return PlayLevel(ov, "2-4", false,
                "Lasers blink on a cycle and flicker before they fire.",
                "Pay the debt inside the beam: light can't touch a frozen you.", false);

            yield return Cut(ov, null);
            yield return PlayLevel(ov, "3-2", false,
                "Rotors sweep in quarter turns.",
                "Their arms sweep straight through you while you hold the dial.", false);

            yield return Cut(ov, null);
            yield return PlayLevel(ov, "4-5", false,
                "Settlement: two loans, two dials, every hazard.",
                "20 levels, each proven solvable by an exhaustive solver.", true);

            yield return Cut(ov, () =>
            {
                complete.Hide();
                State = Flow.Ending;
                Hud.SetVisible(false);
                Audio.SetMusic("music_title");
                int total = 0, par = 0;
                foreach (var d in Catalog.Levels)
                {
                    int p = Catalog.SolutionFor(d)?.Par ?? 0;
                    par += p;
                    total += p + 22;
                }
                ending.Show(total, par, 14, Catalog.Levels.Count, () => { });
            });
            yield return Hold(6.5f);
            ov.FadeTo(1f, 1.2f);
            yield return Hold(1.4f);
            rec.End();
            Application.Quit(rec.Ok ? 0 : 1);
        }

        IEnumerator PlayLevel(DemoOverlay ov, string id, bool card, string intro, string onFreeze, bool showComplete,
            List<TimedAction> actions = null, string onDeath = null)
        {
            int idx = Catalog.Levels.FindIndex(l => l.Id == id);
            if (idx < 0) yield break;
            StartLevel(idx, card);
            Hud.SetTip("");
            var sol = Catalog.SolutionFor(Session.Def);
            Session.Autoplay = actions ?? sol.Actions;
            if (!card) Session.IntroTime = 0.9f;
            ov.FadeTo(0f, 0.35f);
            while (State == Flow.Card) yield return null;
            ov.Caption(intro);

            bool froze = false;
            float limit = Time.realtimeSinceStartup + 120f;
            while (Session.State != LevelSession.Mode.Won && Time.realtimeSinceStartup < limit)
            {
                Session.ForcedAim = UpcomingBorrow(Session.Autoplay, Session.Tick, 16);
                if (!froze && Session.Cur.PFrozen > 0)
                {
                    froze = true;
                    if (onFreeze != null) ov.Caption(onFreeze);
                }
                if (Session.Deaths > 0)
                {
                    Session.ForcedAim = -1;
                    if (onDeath != null) ov.Caption(onDeath);
                    while (Session.State != LevelSession.Mode.Playing && Time.realtimeSinceStartup < limit) yield return null;
                    yield return Hold(0.6f);
                    ov.Caption(null);
                    yield break;
                }
                yield return null;
            }
            Session.ForcedAim = -1;
            if (showComplete)
            {
                yield return Hold(1.1f);
                ov.Caption(null);
                State = Flow.Complete;
                complete.Show(Session.Tick, sol.Par, 0, idx >= Catalog.Levels.Count - 1);
                yield return Hold(3f);
            }
            else yield return Hold(1f);
            ov.Caption(null);
        }

        IEnumerator Cut(DemoOverlay ov, System.Action between)
        {
            ov.FadeTo(1f, 0.3f);
            yield return Hold(0.32f);
            between?.Invoke();
            if (between != null) ov.FadeTo(0f, 0.35f);
        }

        static IEnumerator Hold(float seconds)
        {
            for (float t = 0f; t < seconds; t += Clock.Dt) yield return null;
        }

        static int UpcomingBorrow(List<TimedAction> acts, int tick, int lead)
        {
            if (acts == null) return -1;
            foreach (var a in acts)
                if (a.Tick >= tick && a.Tick - tick <= lead && Act.IsBorrow(a.Action)) return Act.BorrowTarget(a.Action);
            return -1;
        }

        /// <summary>
        /// The level's solution with everything after the first borrow delayed, searched for a
        /// variant where the player dies on thawing (falls back to any death).
        /// </summary>
        static List<TimedAction> FindDefault(LevelDef def, List<TimedAction> sol)
        {
            int bi = sol.FindIndex(a => Act.IsBorrow(a.Action));
            if (bi < 0) return null;
            List<TimedAction> fallback = null;
            for (int shift = 4; shift <= 80; shift += 2)
            {
                var acts = new List<TimedAction>();
                for (int k = 0; k < sol.Count; k++)
                    acts.Add(k > bi ? new TimedAction(sol[k].Tick + shift, sol[k].Action) : sol[k]);
                var s = Simulation.Create(def);
                int c = 0;
                for (int t = 0; t < 800 && !s.Dead && !s.Won; t++)
                {
                    int act = Act.None;
                    while (c < acts.Count && acts[c].Tick < s.Tick) c++;
                    if (c < acts.Count && acts[c].Tick == s.Tick) act = acts[c++].Action;
                    bool frozen = s.PFrozen > 0;
                    Simulation.Step(def, s, act);
                    if (s.Dead)
                    {
                        if (frozen) return acts;
                        fallback ??= acts;
                    }
                }
            }
            return fallback;
        }
    }

    /// <summary>Caption pill and fade-to-black for the demo reel.</summary>
    public sealed class DemoOverlay : MonoBehaviour
    {
        Image fade, pill;
        TextMeshProUGUI text;
        CanvasGroup capGroup;
        float fadeTarget, fadeSpeed = 1f, capTarget;
        string pending;

        public void Build()
        {
            var c = Ui.MakeCanvas("DemoCanvas", 60, transform);
            pill = Ui.Img("Caption", c.transform, Ui.Pill, new Color(0.04f, 0.05f, 0.11f, 0.82f));
            Ui.Place(pill.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -112f), new Vector2(900f, 66f));
            capGroup = pill.gameObject.AddComponent<CanvasGroup>();
            capGroup.alpha = 0f;
            text = Ui.Text("Text", pill.transform, "", Ui.Semi, 30f, new Color(0.96f, 0.94f, 0.9f));
            Ui.Fill(text.rectTransform);
            fade = Ui.Img("Fade", c.transform, null, Color.black);
            Ui.Fill(fade.rectTransform);
        }

        public void SetFade(float a)
        {
            fadeTarget = a;
            fade.color = new Color(0f, 0f, 0f, a);
        }

        public void FadeTo(float a, float seconds)
        {
            fadeTarget = a;
            fadeSpeed = 1f / Mathf.Max(0.01f, seconds);
        }

        /// <summary>Cross-fades to a new caption (null hides it).</summary>
        public void Caption(string s)
        {
            pending = s;
            capTarget = 0f;
        }

        void Update()
        {
            float dt = Clock.Dt;
            var col = fade.color;
            col.a = Mathf.MoveTowards(col.a, fadeTarget, dt * fadeSpeed);
            fade.color = col;

            if (capGroup.alpha <= 0.001f && pending != null)
            {
                text.text = pending;
                pending = null;
                text.ForceMeshUpdate();
                pill.rectTransform.sizeDelta = new Vector2(text.preferredWidth + 84f, 66f);
                capTarget = 1f;
            }
            capGroup.alpha = Mathf.MoveTowards(capGroup.alpha, capTarget, dt * 4f);
        }
    }

    /// <summary>
    /// Frame-locked recorder: raw frames go into an ffmpeg pipe (FILE.video.mp4) and the audio
    /// director's event log into FILE.audio.log; Tools/demo/record.sh mixes and muxes them.
    /// </summary>
    public sealed class DemoRecorder : MonoBehaviour
    {
        Process ffmpeg;
        Stream pipe;
        StreamWriter audioLog;
        string videoPath;
        int fps, width, height;
        bool running;
        public bool Ok { get; private set; }
        public int Frames { get; private set; }

        public void Begin(string path, int framesPerSecond)
        {
            fps = framesPerSecond;
            videoPath = path + ".video.mp4";
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            Time.captureFramerate = fps;
            AudioListener.volume = 0f;
            audioLog = new StreamWriter(path + ".audio.log");
            BorrowedSeconds.Audio.AudioDirector.Log = audioLog;
            running = true;
            StartCoroutine(Loop());
        }

        IEnumerator Loop()
        {
            var eof = new WaitForEndOfFrame();
            while (running)
            {
                yield return eof;
                if (!running) break;
                Grab();
            }
        }

        void Grab()
        {
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            if (ffmpeg == null) StartEncoder(tex);
            if (tex.width == width && tex.height == height)
            {
                if (Frames == 0) audioLog.WriteLine($"V {Clock.Now.ToString(System.Globalization.CultureInfo.InvariantCulture)} {fps}");
                var data = tex.GetRawTextureData<byte>();
                pipe.Write(data.ToArray(), 0, data.Length);
                Frames++;
            }
            Destroy(tex);
            if (Frames % 600 == 0) Debug.Log($"[Demo] frame {Frames} t={Clock.Now:0.00} dt={Clock.Dt:0.0000}");
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
            Debug.Log($"[Demo] encoding {width}x{height} {tex.format} @ {fps} fps");
            ffmpeg = Process.Start(new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-y -loglevel error -f rawvideo -pix_fmt {fmt} -s {width}x{height} -r {fps} -i - " +
                            "-vf \"vflip,scale=trunc(iw/2)*2:trunc(ih/2)*2\" -c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p " +
                            $"\"{videoPath}\"",
                UseShellExecute = false,
                RedirectStandardInput = true,
            });
            pipe = ffmpeg.StandardInput.BaseStream;
        }

        public void End()
        {
            running = false;
            BorrowedSeconds.Audio.AudioDirector.Log = null;
            audioLog.Dispose();
            if (ffmpeg == null) return;
            pipe.Dispose();
            ffmpeg.WaitForExit();
            Ok = ffmpeg.ExitCode == 0;
            Debug.Log($"[Demo] wrote {videoPath} ({Frames} frames, ok={Ok})");
        }
    }
}
