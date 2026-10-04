using System.Collections.Generic;
using BorrowedSeconds.Sim;
using UnityEngine;

namespace BorrowedSeconds.Game
{
    public sealed class LevelSolution
    {
        public int Par;
        public int Loans;
        public int Margin;
        public List<TimedAction> Actions = new List<TimedAction>();
    }

    public sealed class ChapterInfo
    {
        public string Title, Epigraph, Mechanic;
    }

    /// <summary>Loads levels.json and the solver's solutions.json from Resources.</summary>
    public sealed class LevelCatalog
    {
        public readonly List<LevelDef> Levels;
        public readonly Dictionary<string, LevelSolution> Solutions = new Dictionary<string, LevelSolution>();

        public static readonly ChapterInfo[] Chapters =
        {
            new ChapterInfo { Title = "Principal", Epigraph = "Every second you take, you give back.", Mechanic = "Sliders" },
            new ChapterInfo { Title = "Interest", Epigraph = "Light keeps no promises.", Mechanic = "Lasers" },
            new ChapterInfo { Title = "Momentum", Epigraph = "What goes around comes around.", Mechanic = "Rotors" },
            new ChapterInfo { Title = "Compound", Epigraph = "Pay it all back.", Mechanic = "Everything" },
            new ChapterInfo { Title = "Leverage", Epigraph = "Crystal turns everything back.", Mechanic = "Pens" },
            new ChapterInfo { Title = "Escrow", Epigraph = "Leave something behind to hold the door.", Mechanic = "Plates and gates" },
            new ChapterInfo { Title = "Exposure", Epigraph = "Every light casts a shadow.", Mechanic = "Lasers" },
            new ChapterInfo { Title = "Amortize", Epigraph = "Pay it back a little at a time.", Mechanic = "Two loans" },
            new ChapterInfo { Title = "Overdraft", Epigraph = "Due sooner than you think.", Mechanic = "Short terms" },
            new ChapterInfo { Title = "Maturity", Epigraph = "Some debts take their time.", Mechanic = "Long terms" },
            new ChapterInfo { Title = "Arbitrage", Epigraph = "Make the machine pay for itself.", Mechanic = "Rotors" },
            new ChapterInfo { Title = "Solvency", Epigraph = "Every account comes due.", Mechanic = "Everything" },
        };

        public LevelCatalog()
        {
            var json = Resources.Load<TextAsset>("Levels/levels");
            Levels = LevelDef.LoadAll(json.text);
            var sol = Resources.Load<TextAsset>("Levels/solutions");
            if (sol == null) return;
            var root = (Dictionary<string, object>)MiniJson.Parse(sol.text);
            foreach (var o in MiniJson.List(root, "levels"))
            {
                var d = (Dictionary<string, object>)o;
                var s = new LevelSolution
                {
                    Par = MiniJson.Int(d, "par", 0),
                    Loans = MiniJson.Int(d, "loans", 0),
                    Margin = MiniJson.Int(d, "margin", 0),
                };
                foreach (var a in MiniJson.List(d, "actions"))
                {
                    var pair = (List<object>)a;
                    s.Actions.Add(new TimedAction(System.Convert.ToInt32(pair[0]), System.Convert.ToInt32(pair[1])));
                }
                Solutions[MiniJson.Str(d, "id", "")] = s;
            }
        }

        public LevelSolution SolutionFor(LevelDef d) => Solutions.TryGetValue(d.Id, out var s) ? s : null;
        public int IndexOf(LevelDef d) => Levels.IndexOf(d);
    }
}
