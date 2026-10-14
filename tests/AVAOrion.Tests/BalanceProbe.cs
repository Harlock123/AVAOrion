using System.Diagnostics;
using AVAOrion.Rules;
using AVAOrion.World;

namespace AVAOrion.Tests;

/// <summary>
/// Plays whole games between computer rulers and prints how they went: colonies, population,
/// discoveries, fleets, wars, councils. Set ORION_BALANCE to run it.
/// </summary>
public sealed class BalanceProbe(ITestOutputHelper output)
{
    [Fact]
    public void Whole_games_between_computer_rulers()
    {
        if (Environment.GetEnvironmentVariable("ORION_BALANCE") is null) return;
        int first = int.Parse(Environment.GetEnvironmentVariable("ORION_FIRST") ?? "0");
        int games = int.Parse(Environment.GetEnvironmentVariable("ORION_GAMES") ?? "2");
        for (int g = first; g < games; g++)
        {
            var w = Sim.NewWorld((ulong)(500 + g * 31), g % 8, 1, 5);
            foreach (var e in w.Empires) e.Human = false;
            var sim = new Sim(w, new Rng((ulong)(3 + g)));
            var ai = new Ai(sim);
            var clock = Stopwatch.StartNew();
            while (!w.Over)
            {
                foreach (var e in w.Empires) ai.Play(e);
                sim.EndTurn();
                sim.Events.Clear();
                if (w.Turn % 50 == 0 || w.Over)
                {
                    output.WriteLine($"game {g} year {w.Year} ({clock.Elapsed.TotalSeconds:0.0}s) fleets {w.Fleets.Count} wars {sim.WarsDeclared}");
                    foreach (var e in w.Empires.Where(e => e.Alive && !e.Monster))
                    {
                        var cols = w.Stars.Where(s => s.Owner == e.Id).ToList();
                        int ships = w.Fleets.Where(f => f.Owner == e.Id).Sum(f => f.Count);
                        output.WriteLine($"   {e.Name,-9} colonies {cols.Count,2} pop {cols.Sum(s => s.Pop),5:0} fact {cols.Sum(s => s.Factories),5:0} techs {e.TechCount,2} ships {ships,3} power {ai.Might(e.Id),6:0} kills {e.Kills} losses {e.Losses} votes {sim.Votes(e.Id)} colships {w.Fleets.Where(f => f.Owner == e.Id).Sum(f => f.Ships.Where(st => e.Design(st.Design)?.Colony == true).Sum(st => st.Count))} targets {w.Stars.Count(s => s.Owner < 0 && s.HasPlanet && Sim.CanSettle(e, s.Planet) && sim.InRange(e.Id, s))} wars {string.Join(",", w.Empires.Where(o => o.Alive && !o.Monster && sim.AtWar(e.Id, o.Id)).Select(o => o.Name))} best {string.Join("/", Enumerable.Range(0, 6).Select(f => Sim.FieldLevel(e, (Field)f)))}");
                    }
                }
            }
            output.WriteLine($"game {g}: {w.Outcome} ({w.Year})");
        }
    }
}
