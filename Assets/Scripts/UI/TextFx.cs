using TMPro;
using UnityEngine;

namespace BorrowedSeconds.UI
{
    /// <summary>
    /// Per-character animation for a TextMeshPro label. The owner drives <see cref="Age"/> (seconds
    /// since the intro started; negative hides the text) and optionally <see cref="Shimmer"/>.
    ///   Drop   letters fall in from above and settle with an overshoot
    ///   Stamp  letters slam down from large scale
    ///   Rise   letters slide up and fade in
    ///   Spread letters fly in from wide spacing towards the centre
    ///   Type   typewriter reveal with a bright cursor flash on each new letter
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    public sealed class TextFx : MonoBehaviour
    {
        public enum Kind { None, Drop, Stamp, Rise, Spread, Type }

        public Kind Intro = Kind.Drop;
        public float Age = -1f;
        public float Step = 0.045f;      // seconds between letters
        public float Duration = 0.5f;    // per-letter animation length
        public float Distance = 60f;     // pixels of travel / scale for stamps
        /// <summary>Shimmer band position in characters (negative = off).</summary>
        public float Shimmer = -100f;
        public Color ShimmerColor = new Color(1f, 1f, 1f, 1f);
        public float Wave;               // idle bob amplitude in pixels

        TMP_Text text;
        public bool Finished => text != null && Age > Step * Mathf.Max(1, text.textInfo.characterCount) + Duration;

        void Awake() => text = GetComponent<TMP_Text>();

        public static TextFx On(TMP_Text t, Kind kind, float step = 0.045f, float duration = 0.5f, float distance = 60f)
        {
            var fx = t.gameObject.GetComponent<TextFx>() ?? t.gameObject.AddComponent<TextFx>();
            fx.text = t;
            fx.Intro = kind;
            fx.Step = step;
            fx.Duration = duration;
            fx.Distance = distance;
            return fx;
        }

        void LateUpdate()
        {
            if (text == null) return;
            text.ForceMeshUpdate();
            var info = text.textInfo;
            int count = info.characterCount;
            if (count == 0) return;
            float time = Clock.Now;
            for (int i = 0; i < count; i++)
            {
                var ch = info.characterInfo[i];
                if (!ch.isVisible) continue;
                int mi = ch.materialReferenceIndex, vi = ch.vertexIndex;
                var verts = info.meshInfo[mi].vertices;
                var cols = info.meshInfo[mi].colors32;
                Vector3 c = (verts[vi] + verts[vi + 2]) * 0.5f;
                float t = Intro == Kind.None ? 1f : Ease.Clamp((Age - i * Step) / Duration);
                Vector3 offset = Vector3.zero;
                float scale = 1f, alpha = 1f, rot = 0f, flash = 0f;
                switch (Intro)
                {
                    case Kind.Drop:
                        offset.y = (1f - Ease.OutBack(t, 2.2f)) * Distance;
                        alpha = Ease.OutCubic(t * 2.5f);
                        rot = (1f - Ease.OutCubic(t)) * (i % 2 == 0 ? 12f : -12f);
                        break;
                    case Kind.Stamp:
                        scale = Mathf.Lerp(1f + Distance / 30f, 1f, Ease.OutCubic(t));
                        alpha = Ease.Clamp(t * 4f);
                        flash = t > 0f && t < 1f ? 1f - t : 0f;
                        break;
                    case Kind.Rise:
                        offset.y = -(1f - Ease.OutQuint(t)) * Distance;
                        alpha = Ease.OutCubic(t);
                        break;
                    case Kind.Spread:
                        float mid = (count - 1) * 0.5f;
                        offset.x = (1f - Ease.OutQuint(t)) * (i - mid) * Distance * 0.5f;
                        alpha = Ease.OutCubic(t);
                        break;
                    case Kind.Type:
                        alpha = t > 0f ? 1f : 0f;
                        flash = t > 0f ? Mathf.Clamp01(1f - t * 2.5f) : 0f;
                        break;
                }
                if (Age < 0f) alpha = 0f;
                if (Wave > 0f) offset.y += Mathf.Sin(time * 2.2f + i * 0.45f) * Wave;
                float sh = Shimmer > -50f ? Mathf.Exp(-Mathf.Pow((i - Shimmer) / 1.6f, 2f)) : 0f;
                var rotQ = Quaternion.Euler(0, 0, rot);
                for (int k = 0; k < 4; k++)
                {
                    var v = verts[vi + k] - c;
                    v = rotQ * (v * scale);
                    verts[vi + k] = c + v + offset;
                    var col = (Color)cols[vi + k];
                    col = Color.Lerp(col, ShimmerColor, Mathf.Max(sh * 0.85f, flash * 0.7f));
                    col.a *= alpha;
                    cols[vi + k] = col;
                }
            }
            for (int m = 0; m < info.meshInfo.Length; m++)
            {
                info.meshInfo[m].mesh.vertices = info.meshInfo[m].vertices;
                info.meshInfo[m].mesh.colors32 = info.meshInfo[m].colors32;
                text.UpdateGeometry(info.meshInfo[m].mesh, m);
            }
        }
    }
}
