using System.Collections.Generic;
using UnityEngine;

namespace BorrowedSeconds.Audio
{
    /// <summary>
    /// Pooled one-shot SFX and two-deck crossfading music, loaded from Resources/Audio.
    /// Clips named "name_1", "name_2"... are variants picked at random. The "out of time" muffle
    /// low-passes everything while the player is frozen.
    /// </summary>
    public sealed class AudioDirector : MonoBehaviour
    {
        static AudioDirector I;
        readonly Dictionary<string, List<AudioClip>> clips = new Dictionary<string, List<AudioClip>>();
        readonly List<AudioSource> pool = new List<AudioSource>();
        readonly List<AudioLowPassFilter> poolLp = new List<AudioLowPassFilter>();
        readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>();
        AudioSource[] decks;
        AudioLowPassFilter[] deckLp;
        int live;
        string track;
        float duck, deckFade = 1f;

        public float Master = 0.8f, Music = 0.7f, Effects = 0.9f;
        /// <summary>0..1: frozen ("out of time") muffling.</summary>
        public float Muffle;
        /// <summary>0..1: lighter muffling while focusing.</summary>
        public float Focus;
        /// <summary>Music pitch multiplier (rewind / frozen sag).</summary>
        public float MusicPitch = 1f;

        public static AudioDirector Create(Transform parent)
        {
            var go = new GameObject("Audio");
            go.transform.SetParent(parent, false);
            I = go.AddComponent<AudioDirector>();
            I.Build();
            return I;
        }

        void Build()
        {
            foreach (var c in Resources.LoadAll<AudioClip>("Audio"))
            {
                string n = c.name;
                int us = n.LastIndexOf('_');
                if (us > 0 && int.TryParse(n.Substring(us + 1), out _)) n = n.Substring(0, us);
                if (!clips.TryGetValue(n, out var list)) clips[n] = list = new List<AudioClip>();
                list.Add(c);
            }
            for (int i = 0; i < 24; i++)
            {
                var go = new GameObject("Sfx" + i);
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                pool.Add(src);
                var lp = go.AddComponent<AudioLowPassFilter>();
                lp.cutoffFrequency = 22000f;
                poolLp.Add(lp);
            }
            decks = new AudioSource[2];
            deckLp = new AudioLowPassFilter[2];
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject("Music" + i);
                go.transform.SetParent(transform, false);
                decks[i] = go.AddComponent<AudioSource>();
                decks[i].loop = true;
                decks[i].playOnAwake = false;
                decks[i].volume = 0f;
                deckLp[i] = go.AddComponent<AudioLowPassFilter>();
                deckLp[i].cutoffFrequency = 22000f;
            }
            Debug.Log($"[Audio] {clips.Count} sounds loaded");
        }

        public static void Play(string name, float volume = 1f, float pitch = 1f, bool world = true)
        {
            if (I == null || !I.clips.TryGetValue(name, out var list) || list.Count == 0) return;
            float now = Time.unscaledTime;
            if (I.lastPlayed.TryGetValue(name, out float t) && now - t < 0.03f) return;
            I.lastPlayed[name] = now;
            AudioSource src = null;
            int idx = -1;
            for (int i = 0; i < I.pool.Count; i++)
                if (!I.pool[i].isPlaying) { src = I.pool[i]; idx = i; break; }
            if (src == null) { idx = 0; src = I.pool[0]; }
            src.clip = list[Random.Range(0, list.Count)];
            src.volume = volume * I.Effects * I.Master;
            src.pitch = pitch;
            I.poolLp[idx].enabled = world;
            src.Play();
            if (volume >= 0.9f && world) I.duck = Mathf.Max(I.duck, 0.35f);
        }

        public static bool Has(string name) => I != null && I.clips.ContainsKey(name);

        /// <summary>Crossfades to a music loop (no-op if already playing).</summary>
        public void SetMusic(string name)
        {
            if (name == track) return;
            track = name;
            if (!clips.TryGetValue(name ?? "", out var list) || list.Count == 0) { deckFade = 0f; live = 1 - live; decks[live].Stop(); return; }
            live = 1 - live;
            decks[live].clip = list[0];
            decks[live].volume = 0f;
            decks[live].Play();
            deckFade = 0f;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            duck = Mathf.MoveTowards(duck, 0f, dt * 0.9f);
            deckFade = Mathf.MoveTowards(deckFade, 1f, dt / 1.6f);
            float musicVol = Music * Master * (1f - duck) * (1f - Muffle * 0.25f);
            decks[live].volume = musicVol * deckFade;
            decks[1 - live].volume = musicVol * (1f - deckFade);
            if (deckFade >= 1f && decks[1 - live].isPlaying) decks[1 - live].Stop();
            float cutoff = Mathf.Lerp(22000f, 600f, Muffle);
            cutoff = Mathf.Min(cutoff, Mathf.Lerp(22000f, 2400f, Focus));
            foreach (var lp in deckLp) lp.cutoffFrequency = cutoff;
            foreach (var d in decks) d.pitch = MusicPitch * Mathf.Lerp(1f, 0.94f, Muffle);
            float sfxCut = Mathf.Lerp(22000f, 1400f, Muffle);
            foreach (var lp in poolLp) lp.cutoffFrequency = sfxCut;
        }
    }

    /// <summary>Shorthand for one-shots.</summary>
    public static class Sfx
    {
        public static void Play(string name, float volume = 1f, float pitch = 1f) => AudioDirector.Play(name, volume, pitch, false);
        public static void World(string name, float volume = 1f, float pitch = 1f) => AudioDirector.Play(name, volume, pitch, true);
    }
}
