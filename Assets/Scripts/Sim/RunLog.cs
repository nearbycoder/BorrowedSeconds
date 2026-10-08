using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BorrowedSeconds.Sim
{
    /// <summary>
    /// The actions of the run that survives the player's rewinds, one per tick, so a win can be
    /// saved and replayed as a ghost of the best run. Saved in the solver's form (tick, action),
    /// as text: "12:5,40:17" (only the ticks with an action).
    /// </summary>
    public sealed class RunLog
    {
        readonly List<int> acts = new List<int>();

        /// <summary>Ticks recorded so far (the next action belongs to this tick).</summary>
        public int Count => acts.Count;

        /// <summary>The action applied on <paramref name="tick"/>; ticks must come in order.</summary>
        public void Add(int tick, int action)
        {
            if (tick < acts.Count) TruncateTo(tick);
            while (acts.Count < tick) acts.Add(Act.None);
            acts.Add(action);
        }

        /// <summary>A rewind back to <paramref name="tick"/> drops the actions from that tick on.</summary>
        public void TruncateTo(int tick)
        {
            if (tick < 0) tick = 0;
            if (tick < acts.Count) acts.RemoveRange(tick, acts.Count - tick);
        }

        public List<TimedAction> Actions()
        {
            var list = new List<TimedAction>();
            for (int t = 0; t < acts.Count; t++) if (acts[t] != Act.None) list.Add(new TimedAction(t, acts[t]));
            return list;
        }

        public static string Encode(IList<TimedAction> actions)
        {
            var sb = new StringBuilder();
            foreach (var a in actions)
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append(a.Tick.ToString(CultureInfo.InvariantCulture)).Append(':').Append(a.Action.ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <summary>Null if the text isn't a run (damaged save): no ghost rather than a wrong one.</summary>
        public static List<TimedAction> Decode(string text)
        {
            if (text == null) return null;
            var list = new List<TimedAction>();
            if (text.Length == 0) return list;
            int last = -1;
            foreach (var part in text.Split(','))
            {
                int colon = part.IndexOf(':');
                if (colon <= 0
                    || !int.TryParse(part.Substring(0, colon), NumberStyles.None, CultureInfo.InvariantCulture, out int tick)
                    || !int.TryParse(part.Substring(colon + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int action)
                    || tick <= last) return null;
                list.Add(new TimedAction(tick, action));
                last = tick;
            }
            return list;
        }
    }
}
