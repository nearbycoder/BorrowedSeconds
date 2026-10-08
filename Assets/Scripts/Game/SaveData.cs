using System;
using UnityEngine;

namespace BorrowedSeconds.Game
{
    public enum Medal { None, Bronze, Silver, Gold }

    /// <summary>Progress and settings, persisted as JSON in PlayerPrefs.</summary>
    [Serializable]
    public sealed class SaveData
    {
        const string Key = "bs.save.v1";

        public string[] ids = new string[0];
        public int[] best = new int[0];        // best clear in ticks per level id, 0 = never cleared
        public int lastLevel;
        public bool finished;
        public int learned;                     // onboarding prompts retired (UI.Prompts bits)

        public float master = 0.8f, music = 0.7f, sfx = 0.9f, focus = 0.2f;
        public float speed = 1f;                // game-speed assist; medals count sim ticks, so they stay fair
        public string[] keys = new string[0];   // keyboard bindings by KeyAction (KeyBindings); empty = defaults
        public bool fullscreen = true, shake = true, reduceFlashing;
        public bool focusToggle;                // a press turns Focus on and the next turns it off, instead of holding
        public bool vibration = true;           // rumble pulses on the gamepad in use (Game.Rumble)
        public bool muteBackground = true;      // the mix fades out while the window is in the background
        public int windowW, windowH;            // window size when not fullscreen (DisplayOptions); 0 = pick one
        public float renderScale = 1f;          // the 3D scene's render resolution (DisplayOptions.RenderScales)
        public float hudScale = 1f;             // HUD size: 1, 1.25 or 1.5 (View.HudLayout)
        public int fidelity = GraphicsFidelity.Default; // Graphics fidelity: 0 Low .. 3 Ultra; saves from before it load as High
        public string[] runs = new string[0];   // a run's actions per level id (Sim.RunLog text), "" = none; the best-run ghost replays it
        public int[] runTicks = new int[0];     // that run's time in ticks
        public bool bestGhost = true;           // a settled level shows a ghost of the saved run (pause menu)

        public static SaveData Load()
        {
            try
            {
                var json = PlayerPrefs.GetString(Key, "");
                if (!string.IsNullOrEmpty(json)) return JsonUtility.FromJson<SaveData>(json) ?? new SaveData();
            }
            catch (Exception e) { Debug.LogWarning("[Save] reset: " + e.Message); }
            return new SaveData();
        }

        /// <summary>Set for scripted runs (capture, menu tour, demo) so they never overwrite the player's save.</summary>
        [NonSerialized] public bool ReadOnly;

        public void Save()
        {
            if (ReadOnly) return;
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(this));
            PlayerPrefs.Save();
        }

        public int Best(string id)
        {
            int i = Array.IndexOf(ids, id);
            return i >= 0 ? best[i] : 0;
        }

        /// <summary>Records a clear; returns true if it beat the previous best. <paramref name="run"/> (the
        /// clear's actions, Sim.RunLog text) is kept with a new best, or with any clear if none is kept yet.</summary>
        public bool Record(string id, int ticks, string run = null)
        {
            int i = Array.IndexOf(ids, id);
            if (i < 0)
            {
                Array.Resize(ref ids, ids.Length + 1);
                Array.Resize(ref best, best.Length + 1);
                i = ids.Length - 1;
                ids[i] = id;
            }
            // saves from before round 10 have no runs: the arrays catch up with ids here
            if (runs == null || runs.Length < ids.Length) { runs ??= new string[0]; int n = runs.Length; Array.Resize(ref runs, ids.Length); for (int k = n; k < runs.Length; k++) runs[k] = ""; }
            if (runTicks == null || runTicks.Length < ids.Length) { runTicks ??= new int[0]; Array.Resize(ref runTicks, ids.Length); }
            bool better = best[i] == 0 || ticks < best[i];
            if (run != null && (better || string.IsNullOrEmpty(runs[i]))) { runs[i] = run; runTicks[i] = ticks; }
            if (!better) return false;
            best[i] = ticks;
            return true;
        }

        /// <summary>The kept run for a level (Sim.RunLog text) and its time, or null if none.</summary>
        public string Run(string id, out int ticks)
        {
            int i = Array.IndexOf(ids, id);
            ticks = 0;
            if (i < 0 || runs == null || i >= runs.Length || string.IsNullOrEmpty(runs[i])) return null;
            ticks = runTicks != null && i < runTicks.Length ? runTicks[i] : 0;
            return runs[i];
        }

        public bool Cleared(string id) => Best(id) > 0;

        /// <summary>Settings > Erase progress: medals, best times, where to continue and the retired
        /// onboarding prompts go; settings and key bindings stay.</summary>
        public void EraseProgress()
        {
            ids = new string[0];
            best = new int[0];
            runs = new string[0];
            runTicks = new int[0];
            lastLevel = 0;
            finished = false;
            learned = 0;
        }

        public static Medal MedalFor(int ticks, int par)
        {
            if (ticks <= 0) return Medal.None;
            if (par <= 0) return Medal.Bronze;
            if (ticks <= par + 20) return Medal.Gold;
            if (ticks <= par + 80) return Medal.Silver;
            return Medal.Bronze;
        }

        public static string MedalName(Medal m) => m switch
        {
            Medal.Gold => "TIME THIEF",
            Medal.Silver => "SILVER",
            Medal.Bronze => "BRONZE",
            _ => "",
        };
    }
}
