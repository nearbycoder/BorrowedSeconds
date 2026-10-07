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
        public int windowW, windowH;            // window size when not fullscreen (DisplayOptions); 0 = pick one
        public float renderScale = 1f;          // the 3D scene's render resolution (DisplayOptions.RenderScales)

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

        /// <summary>Records a clear; returns true if it beat the previous best.</summary>
        public bool Record(string id, int ticks)
        {
            int i = Array.IndexOf(ids, id);
            if (i < 0)
            {
                Array.Resize(ref ids, ids.Length + 1);
                Array.Resize(ref best, best.Length + 1);
                i = ids.Length - 1;
                ids[i] = id;
            }
            if (best[i] != 0 && best[i] <= ticks) return false;
            best[i] = ticks;
            return true;
        }

        public bool Cleared(string id) => Best(id) > 0;

        /// <summary>Settings > Erase progress: medals, best times, where to continue and the retired
        /// onboarding prompts go; settings and key bindings stay.</summary>
        public void EraseProgress()
        {
            ids = new string[0];
            best = new int[0];
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
