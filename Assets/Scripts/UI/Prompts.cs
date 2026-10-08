using BorrowedSeconds.Game;
using BorrowedSeconds.Sim;
using BorrowedSeconds.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BorrowedSeconds.UI
{
    /// <summary>
    /// Onboarding without text walls: small key-cap pills that float over the thing they refer to
    /// (the pawn, a slider) and retire for good once the player has done what they describe.
    /// </summary>
    public sealed class Prompts : MonoBehaviour
    {
        public const int Move = 1, Borrow = 2, Debt = 4, Rewind = 8, Focus = 16;

        sealed class Pill
        {
            public RectTransform Rt;
            public CanvasGroup Group;
            public TextMeshProUGUI Text;
            public Panel Panel;
            public float Alpha;
        }

        Canvas canvas;
        Pill overPlayer, overObstacle;
        float rewindHint;   // seconds left to show the rewind prompt after an auto-rewind
        int steps;

        public static Prompts Create(Transform parent)
        {
            var go = new GameObject("Prompts");
            go.transform.SetParent(parent, false);
            var p = go.AddComponent<Prompts>();
            p.canvas = Ui.MakeCanvas("PromptCanvas", 11, go.transform);
            p.overPlayer = p.MakePill();
            p.overObstacle = p.MakePill();
            return p;
        }

        Pill MakePill()
        {
            var rt = Ui.Rect("Pill", canvas.transform, Vector2.zero, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(300, 52));
            var group = rt.gameObject.AddComponent<CanvasGroup>();
            var caret = Ui.Img("Caret", rt, Ui.Diamond, Palette.Gold);
            Ui.Place(caret.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0, -6), new Vector2(16, 16));
            var panel = new Panel("Bg", rt, new Vector2(300, 52), Panel.Style.Card);
            panel.Rt.anchoredPosition = Vector2.zero;
            panel.SetRim(Palette.Gold);
            var text = Ui.Text("Text", rt, "", Ui.Semi, 21, Palette.Paper);
            Ui.Fill(text.rectTransform);
            text.richText = true;
            group.alpha = 0f;
            return new Pill { Rt = rt, Group = group, Text = text, Panel = panel };
        }

        /// <summary>The pills' current text (the input bot reads the button names).</summary>
        public string PlayerText => overPlayer.Text.text;
        /// <summary>What the pill over the player is showing or fading in to (null: it's going); checks read it.</summary>
        public string PlayerNote { get; private set; }
        public string ObstacleText => overObstacle.Text.text;

        /// <summary>Call when a new level starts.</summary>
        public void ResetLevel()
        {
            steps = 0;
            rewindHint = 0f;
            overPlayer.Alpha = overObstacle.Alpha = 0f;
            overPlayer.Group.alpha = overObstacle.Group.alpha = 0f;
        }

        public void OnEvents(SaveData save, System.Collections.Generic.List<SimEvent> events)
        {
            foreach (var e in events)
            {
                if (e.Type == Ev.Step && ++steps >= 3) Learn(save, Move);
                if (e.Type == Ev.Borrow) Learn(save, Borrow);
                if (e.Type == Ev.PlayerThaw) Learn(save, Debt);
            }
        }

        public void OnAutoRewindDone() => rewindHint = 6f;

        static void Learn(SaveData save, int bit)
        {
            if ((save.learned & bit) != 0) return;
            save.learned |= bit;
            save.Save();
        }

        public void Tick(LevelSession s, SaveData save, InputReader input, bool active, float dt)
        {
            string playerText = null, obstacleText = null;
            int obstacle = -1;
            if (active && s != null && s.Def != null && s.State == LevelSession.Mode.Playing)
            {
                bool pad = input.UsingGamepad;
                if (input.Focus) Learn(save, Focus);
                if (input.Rewind) { Learn(save, Rewind); rewindHint = 0f; }
                rewindHint = Mathf.Max(0f, rewindHint - dt);
                var cur = s.Cur;
                int learned = save.learned;

                // on an exit that won't let you leave yet, say why (every attempt, not just until learned)
                string exit = ExitNote.Line(s.Def, cur);
                int colon = exit.IndexOf(':');
                if (colon > 0)
                    playerText = $"<color=#FFD27A>{exit.Substring(0, colon)}</color>{exit.Substring(colon)}";
                else if ((learned & Move) == 0)
                    playerText = pad ? $"<color=#FFD27A>{input.Pad.Stick}</color>  move" : $"<color=#FFD27A>{input.MoveKeysName()}</color>  or  <color=#FFD27A>arrows</color>  move";
                else if (rewindHint > 0f && (learned & Rewind) == 0)
                    playerText = $"Hold <color=#FFD27A>{(pad ? input.Pad.West : input.KeyName(KeyAction.Rewind))}</color>  rewind further";
                else if ((learned & Debt) == 0 && cur.Countdown > 0)
                    playerText = "Debt due: freeze where the <color=#55E0AE>ring</color> is safe";
                else if ((learned & Borrow) != 0 && (learned & Focus) == 0 && s.LoanAvailable && s.Aim >= 0)
                    playerText = $"{(input.FocusToggle ? "Press" : "Hold")} <color=#FFD27A>{(pad ? input.Pad.LeftTrigger : input.KeyName(KeyAction.Focus))}</color>  slow time to aim";

                // a pad always aims at something (the nearest obstacle until LB/RB cycle), so the prompt
                // goes over what it aims at; the mouse prompt waits until nothing is hovered
                if ((learned & Move) != 0 && (learned & Borrow) == 0 && s.LoanAvailable && (s.Aim < 0 || pad))
                {
                    obstacle = pad && s.Aim >= 0 && !s.Cur.IsObstacleFrozen(s.Def, s.Aim) ? s.Aim : NearestFree(s);
                    if (obstacle >= 0)
                        obstacleText = pad ? $"<color=#FFD27A>{input.Pad.South}</color>  freeze it  ·  <color=#FFD27A>{input.Pad.Shoulders}</color>  aim"
                                           : "<color=#FFD27A>Hover</color> + <color=#FFD27A>click</color>  freeze it";
                }
            }

            PlayerNote = playerText;
            var cam = Camera.main;
            Show(overPlayer, playerText, s != null && s.Board != null ? s.Board.Player.WorldPos + Vector3.up * 1.9f : Vector3.zero, cam, dt);
            // over an obstacle that is also aimed (always, on a pad), the pill sits above the HUD's aim tag
            float lift = obstacle >= 0 && s.Aim == obstacle ? 100f : 0f;
            Show(overObstacle, obstacleText, obstacle >= 0 ? s.Board.ObstacleCenter(obstacle) + Vector3.up * 1.1f : Vector3.zero, cam, dt, lift);
        }

        static int NearestFree(LevelSession s)
        {
            int best = -1;
            float bestD = float.MaxValue;
            var p = s.Board.Player.WorldPos;
            for (int i = 0; i < s.Def.ObstacleCount; i++)
            {
                if (s.Cur.IsObstacleFrozen(s.Def, i)) continue;
                float d = (s.Board.ObstacleCenter(i) - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        void Show(Pill pill, string text, Vector3 world, Camera cam, float dt, float lift = 0f)
        {
            bool on = text != null && cam != null;
            if (on && pill.Text.text != text)
            {
                pill.Text.text = text;
                pill.Rt.sizeDelta = new Vector2(pill.Text.preferredWidth + 44f, 52f);
                pill.Panel.SetSize(pill.Rt.sizeDelta);
                pill.Panel.Rt.anchoredPosition = Vector2.zero;
            }
            pill.Alpha = Mathf.MoveTowards(pill.Alpha, on ? 1f : 0f, dt * (on ? 3f : 5f));
            pill.Group.alpha = pill.Alpha;
            pill.Rt.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, Ease.OutBack(pill.Alpha, 2.4f));
            pill.Panel.Glow = 0.35f + 0.25f * Mathf.Sin(Clock.Now * 3f);
            pill.Panel.Apply();
            if (pill.Alpha <= 0f || cam == null) return;
            if (on)
            {
                var sp = cam.WorldToScreenPoint(world);
                float scale = canvas.GetComponent<RectTransform>().localScale.x;
                float bob = Mathf.Sin(Clock.Now * 3f) * 5f;
                var size = ((RectTransform)canvas.transform).rect.size;
                float half = pill.Rt.sizeDelta.x * 0.5f + 24f;
                float x = Mathf.Clamp(sp.x / scale, half, size.x - half);
                float y = Mathf.Clamp(sp.y / scale + lift + bob, 140f, size.y - 170f);
                pill.Rt.anchoredPosition = new Vector2(x, y);
            }
        }
    }
}
