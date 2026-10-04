using UnityEngine;

namespace BorrowedSeconds.UI
{
    /// <summary>Easing curves (all map 0..1 to 0..1, clamped).</summary>
    public static class Ease
    {
        public static float Clamp(float t) => t < 0 ? 0 : t > 1 ? 1 : t;
        public static float OutCubic(float t) { t = Clamp(t); return 1 - (1 - t) * (1 - t) * (1 - t); }
        public static float OutQuint(float t) { t = Clamp(t); float u = 1 - t; return 1 - u * u * u * u * u; }
        public static float OutExpo(float t) { t = Clamp(t); return t >= 1 ? 1 : 1 - Mathf.Pow(2, -10 * t); }
        public static float InCubic(float t) { t = Clamp(t); return t * t * t; }
        public static float InOutCubic(float t) { t = Clamp(t); return t < 0.5f ? 4 * t * t * t : 1 - Mathf.Pow(-2 * t + 2, 3) / 2; }
        public static float OutBack(float t, float s = 1.70158f) { t = Clamp(t) - 1; return 1 + (s + 1) * t * t * t + s * t * t; }
        public static float OutElastic(float t)
        {
            t = Clamp(t);
            if (t <= 0 || t >= 1) return t;
            return Mathf.Pow(2, -10 * t) * Mathf.Sin((t * 10 - 0.75f) * (2 * Mathf.PI / 3)) + 1;
        }

        /// <summary>Progress of item <paramref name="index"/> in a staggered sequence.</summary>
        public static float Stagger(float age, int index, float delay, float step, float duration)
            => Clamp((age - delay - index * step) / duration);
    }

    /// <summary>A damped spring on one float (semi-implicit Euler, stable at UI frame rates).</summary>
    public struct Spring
    {
        public float Value, Velocity;
        public float Stiffness, Damping;

        public static Spring Make(float value, float stiffness = 260f, float damping = 22f)
            => new Spring { Value = value, Stiffness = stiffness, Damping = damping };

        public float Step(float target, float dt)
        {
            dt = Mathf.Min(dt, 1f / 30f);
            // sub-step so springs stay stable when frames are long
            for (int i = 0; i < 2; i++)
            {
                float h = dt * 0.5f;
                float a = (target - Value) * Stiffness - Velocity * Damping;
                Velocity += a * h;
                Value += Velocity * h;
            }
            return Value;
        }

        public void Kick(float velocity) => Velocity += velocity;
        public void Snap(float v) { Value = v; Velocity = 0; }
    }

    /// <summary>A damped spring on a Vector2.</summary>
    public struct Spring2
    {
        public Spring X, Y;
        public static Spring2 Make(Vector2 v, float stiffness = 260f, float damping = 22f)
            => new Spring2 { X = Spring.Make(v.x, stiffness, damping), Y = Spring.Make(v.y, stiffness, damping) };
        public Vector2 Step(Vector2 target, float dt) => new Vector2(X.Step(target.x, dt), Y.Step(target.y, dt));
        public Vector2 Value => new Vector2(X.Value, Y.Value);
        public Vector2 Velocity => new Vector2(X.Velocity, Y.Velocity);
        public void Snap(Vector2 v) { X.Snap(v.x); Y.Snap(v.y); }
    }
}
