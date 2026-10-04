using System;
using System.Collections.Generic;
using BorrowedSeconds.Sim;
using BorrowedSeconds.View;
using UnityEngine;

namespace BorrowedSeconds.Game
{
    /// <summary>
    /// Runs one level: a fixed 20 Hz simulation driven by buffered input, rendered with a
    /// one-tick lookahead so motion never trails input. Keeps every tick for instant rewind.
    /// </summary>
    public sealed class LevelSession : MonoBehaviour
    {
        public enum Mode { Intro, Playing, Dying, Rewinding, Won }

        public const float TickDt = 1f / Rules.TicksPerSecond;
        public LevelDef Def { get; private set; }
        public SimState Cur { get; private set; }
        public SimState Look { get; private set; }
        public BoardView Board { get; private set; }
        public GhostPreview Ghost { get; private set; }
        public Mode State { get; private set; } = Mode.Intro;
        public int Aim { get; private set; } = -1;
        public float Alpha { get; private set; }
        public bool Focusing { get; private set; }
        public int Deaths { get; private set; }
        public int RewindTicksUsed { get; private set; }
        public float RewindPos => rewindPos;
        public float IntroTime = 0.6f;
        /// <summary>Simulation speed while focusing (a setting).</summary>
        public static float FocusScale = 0.2f;

        public event Action<SimState, List<SimEvent>> Events;
        public event Action Won;
        public event Action<int> Died;
        public event Action<bool> RewindChanged;
        public event Action<int> BorrowDenied;

        public List<TimedAction> Autoplay;   // when set, input is ignored and these actions drive the sim
        public bool AllowInput = true;
        public bool Paused;
        /// <summary>Attract-mode playback behind menus: no sounds or freeze tint.</summary>
        public bool Muted;
        public float Speed = 1f;
        /// <summary>Aim shown while input is disabled (the demo reel previews scripted borrows).</summary>
        public int ForcedAim = -1;

        readonly List<SimState> history = new List<SimState>();
        readonly Stack<SimState> pool = new Stack<SimState>();
        InputReader input;
        float acc, hitstop, stateTimer, rewindPos, rewindSpeed;
        int queuedDir = -1, queuedBorrow = -1, cycleIndex = -1, autoplayCursor;
        bool manualRewind, pointerAim;
        float focusBlend;
        Camera cam;

        public void Init(LevelDef def, InputReader inputReader, Camera camera)
        {
            Def = def;
            input = inputReader;
            cam = camera;
            Board = new GameObject("Board").AddComponent<BoardView>();
            Board.transform.SetParent(transform, false);
            Board.Build(def);
            Cur = Simulation.Create(def);
            Look = Cur.Clone();
            Simulation.Step(def, Look, Act.None);
            history.Add(Cur.Clone());
            Ghost = new GhostPreview(Board);
            State = Mode.Intro;
            stateTimer = 0f;
            Board.Render(Cur, Look, 0f, 0f);
        }

        public float FocusBlend => focusBlend;
        public int Tick => Cur.Tick;
        public bool LoanAvailable => !Cur.LoanActive && (Def.LoanLimit < 0 || Cur.Loans < Def.LoanLimit);

        public void Hitstop(float seconds) => hitstop = Mathf.Max(hitstop, seconds);

        void Update()
        {
            float dt = Mathf.Min(Clock.Dt, 0.1f);
            stateTimer += dt;
            if (Def == null) return;

            switch (State)
            {
                case Mode.Intro:
                    if (stateTimer >= IntroTime) { State = Mode.Playing; stateTimer = 0; }
                    Board.Render(Cur, Look, 0f, dt);
                    return;
                case Mode.Dying:
                    Board.Render(Cur, Cur, 0f, dt);
                    Ghost.Refresh(Cur, -1, false);
                    Ghost.Render(dt, Time.time);
                    if (stateTimer > 0.85f) BeginRewind(false);
                    return;
                case Mode.Rewinding:
                    UpdateRewind(dt);
                    return;
                case Mode.Won:
                    Board.Render(Cur, Cur, 0f, dt);
                    return;
            }

            bool canControl = AllowInput && Autoplay == null;
            if (canControl)
            {
                UpdateAim();
                if (input.Rewind && history.Count > 1) { BeginRewind(true); UpdateRewind(dt); return; }
                if (input.PressedDir >= 0) queuedDir = input.PressedDir;
                if (input.Borrow) RequestBorrow();
                Focusing = input.Focus;
            }
            else
            {
                Focusing = false;
                Aim = ForcedAim;
            }
            focusBlend = Mathf.MoveTowards(focusBlend, Focusing ? 1f : 0f, dt * 6f);
            Board.SetHighlight(LoanAvailable ? Aim : -1);

            float scale = Mathf.Lerp(1f, FocusScale, focusBlend) * Speed;
            if (Paused) scale = 0f;
            if (hitstop > 0f)
            {
                hitstop -= dt;
                scale = 0f;
            }
            acc += dt * scale;
            int guard = 0;
            while (acc >= TickDt && State == Mode.Playing && guard++ < 8)
            {
                acc -= TickDt;
                DoTick();
            }
            if (acc > TickDt) acc = TickDt;
            Alpha = State == Mode.Playing ? acc / TickDt : 0f;
            Board.Render(Cur, State == Mode.Playing ? Look : Cur, Alpha, dt);
            if (Aim != lastAim) { lastAim = Aim; RefreshGhost(); }
            Ghost.Render(dt, Time.time);
        }

        int lastAim = -1;

        void RefreshGhost()
        {
            if (State == Mode.Playing) Ghost.Refresh(Cur, Aim, LoanAvailable);
            else Ghost.Refresh(Cur.Dead ? Cur : history[0], -1, false);
        }

        // ---------------------------------------------------------------- input → actions

        void UpdateAim()
        {
            int hovered = -1;
            if (cam != null && !input.UsingGamepad)
                hovered = Board.Pick(cam.ScreenPointToRay(input.Pointer));
            if (input.PointerMoved) pointerAim = true;
            if (input.CycleNext || input.CyclePrev)
            {
                pointerAim = false;
                int n = Def.ObstacleCount;
                if (n > 0)
                {
                    if (cycleIndex < 0) cycleIndex = Nearest();
                    else cycleIndex = (cycleIndex + (input.CycleNext ? 1 : n - 1)) % n;
                }
            }
            if (pointerAim && hovered >= 0) Aim = hovered;
            else if (!pointerAim && cycleIndex >= 0) Aim = cycleIndex;
            else if (input.UsingGamepad) Aim = cycleIndex >= 0 ? cycleIndex : Nearest();
            else Aim = hovered;
        }

        int Nearest()
        {
            int best = -1;
            float bestD = float.MaxValue;
            var p = Board.Player.WorldPos;
            for (int i = 0; i < Def.ObstacleCount; i++)
            {
                float d = (Board.ObstacleCenter(i) - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        void RequestBorrow()
        {
            int target = Aim;
            if (target < 0 && !pointerAim) target = Nearest();
            if (target < 0) return;
            if (!LoanAvailable || Cur.IsObstacleFrozen(Def, target))
            {
                BorrowDenied?.Invoke(target);
                return;
            }
            queuedBorrow = target;
        }

        void DoTick()
        {
            int act = Act.None;
            if (Autoplay != null)
            {
                while (autoplayCursor < Autoplay.Count && Autoplay[autoplayCursor].Tick < Cur.Tick) autoplayCursor++;
                if (autoplayCursor < Autoplay.Count && Autoplay[autoplayCursor].Tick == Cur.Tick) act = Autoplay[autoplayCursor++].Action;
            }
            else if (Cur.CanActNext)
            {
                if (queuedBorrow >= 0)
                {
                    act = Act.Borrow(queuedBorrow);
                    queuedBorrow = -1;
                }
                else if (queuedDir >= 0)
                {
                    act = Act.Move(queuedDir);
                    queuedDir = -1;
                }
                else if (input.HeldDir >= 0 && AllowInput && Simulation.CanEnter(Def, Cur, Def.Neighbor(Cur.P, input.HeldDir)))
                {
                    act = Act.Move(input.HeldDir);
                }
            }
            if (Cur.PFrozen > 1) queuedDir = -1; // don't stack moves while frozen

            Simulation.Step(Def, Cur, act);
            var snap = pool.Count > 0 ? pool.Pop() : new SimState(Def);
            snap.CopyFrom(Cur);
            history.Add(snap);
            Look.CopyFrom(Cur);
            Simulation.Step(Def, Look, Act.None);

            RefreshGhost();
            if (Cur.Events.Count > 0) Events?.Invoke(Cur, Cur.Events);
            if (Cur.Dead)
            {
                State = Mode.Dying;
                stateTimer = 0f;
                Deaths++;
                Died?.Invoke(Cur.DeathCause);
            }
            else if (Cur.Won)
            {
                State = Mode.Won;
                stateTimer = 0f;
                Won?.Invoke();
            }
        }

        // ---------------------------------------------------------------- rewind

        void BeginRewind(bool manual)
        {
            manualRewind = manual;
            State = Mode.Rewinding;
            rewindPos = history.Count - 1;
            rewindSpeed = manual ? 30f : 40f;
            queuedBorrow = -1;
            queuedDir = -1;
            RewindChanged?.Invoke(true);
        }

        void UpdateRewind(float dt)
        {
            Ghost.Visible = false;
            Ghost.Render(dt, Time.time);
            int target;
            if (manualRewind)
            {
                target = 0;
                rewindSpeed = Mathf.Min(rewindSpeed + dt * 40f, 120f);
                if (!input.Rewind || !AllowInput) { EndRewind(Mathf.CeilToInt(rewindPos)); return; }
            }
            else
            {
                int deathTick = history.Count - 1;
                target = Mathf.Max(0, deathTick - 2 * Rules.TicksPerSecond);
                rewindSpeed = Mathf.Min(rewindSpeed + dt * 80f, 90f);
            }
            float before = rewindPos;
            rewindPos = Mathf.Max(target, rewindPos - rewindSpeed * dt);
            RewindTicksUsed += Mathf.Max(0, Mathf.FloorToInt(before) - Mathf.FloorToInt(rewindPos));
            int lo = Mathf.FloorToInt(rewindPos);
            int hi = Mathf.Min(lo + 1, history.Count - 1);
            Board.Render(history[lo], history[hi], rewindPos - lo, dt);
            if (!manualRewind && rewindPos <= target + 0.001f) EndRewind(target);
        }

        void EndRewind(int at)
        {
            at = Mathf.Clamp(at, 0, history.Count - 1);
            for (int k = history.Count - 1; k > at; k--)
            {
                pool.Push(history[k]);
                history.RemoveAt(k);
            }
            Cur.CopyFrom(history[at]);
            Cur.Events.Clear();
            Look.CopyFrom(Cur);
            Simulation.Step(Def, Look, Act.None);
            acc = 0f;
            State = Mode.Playing;
            stateTimer = 0f;
            Ghost.Visible = true;
            RefreshGhost();
            RewindChanged?.Invoke(false);
        }
    }
}
