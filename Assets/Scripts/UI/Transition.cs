using System;
using BorrowedSeconds.Audio;
using BorrowedSeconds.View;
using UnityEngine;
using UnityEngine.UI;

namespace BorrowedSeconds.UI
{
    /// <summary>
    /// Screen-to-screen transition: a clock hand sweeps a dark dial across the screen, the swap
    /// happens while it is covered, and the same hand keeps sweeping to reveal the new screen.
    /// </summary>
    public sealed class Transition : MonoBehaviour
    {
        Image img;
        Material mat;
        float t = -1f, duration;
        Action midpoint;
        bool fired;
        public bool Busy => t >= 0f;

        public static Transition Create(Transform parent)
        {
            var canvas = Ui.MakeCanvas("TransitionCanvas", 100, parent);
            var tr = canvas.gameObject.AddComponent<Transition>();
            tr.img = Ui.Img("Wipe", canvas.transform, null, Color.white);
            Ui.Fill(tr.img.rectTransform);
            tr.mat = Mats.Instance("BS_UIWipe");
            tr.img.material = tr.mat;
            tr.img.raycastTarget = true;
            tr.img.enabled = false;
            return tr;
        }

        /// <summary>Covers the screen, runs <paramref name="mid"/>, then uncovers.</summary>
        public void Play(Action mid, float seconds = 0.9f)
        {
            if (Busy)
            {
                // already mid-sweep: run the swap at the next cover point instead of stacking
                var prev = midpoint;
                midpoint = () => { prev?.Invoke(); mid?.Invoke(); };
                return;
            }
            midpoint = mid;
            duration = seconds;
            t = 0f;
            fired = false;
            img.enabled = true;
            Sfx.Play("rotor_whoosh", 0.45f, 1.35f);
        }

        void Update()
        {
            if (t < 0f) return;
            t += Mathf.Min(Clock.Dt, 1f / 30f);
            float half = duration * 0.5f;
            float p;
            if (t < half) p = Ease.InOutCubic(t / half);
            else
            {
                if (!fired)
                {
                    fired = true;
                    var m = midpoint;
                    midpoint = null;
                    m?.Invoke();
                    Sfx.Play("tick", 0.6f, 0.9f);
                    t = half; // the swap may have taken a long frame; start the reveal fresh
                }
                p = 1f + Ease.InOutCubic((t - half) / half);
            }
            mat.SetFloat("_Progress", p);
            mat.SetFloat("_Aspect", Screen.height > 0 ? (float)Screen.width / Screen.height : 1.78f);
            if (t >= duration)
            {
                t = -1f;
                img.enabled = false;
            }
        }
    }
}
