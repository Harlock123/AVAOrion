using AVAOrion.Game;
using AVAOrion.Rules;
using AVAOrion.World;

namespace AVAOrion.Tests;

internal static class Helpers
{
    /// <summary>A folder for saved games that isn't the user's own.</summary>
    public static SaveStore TempSaves() =>
        new(Path.Combine(Path.GetTempPath(), "avaorion-tests-" + Guid.NewGuid().ToString("N")));

    /// <summary>A game begun as a player would, with the opening message read.</summary>
    public static GameEngine NewGame(int seed = 3, int race = 0, int level = 1, int rivals = 4, GalaxySize size = GalaxySize.Medium)
    {
        var g = new GameEngine(seed, TempSaves());
        g.Begin(seed, race, level, rivals, size, "Ruler Ash");
        g.CloseAll();
        return g;
    }

    /// <summary>Lets the computer play the player's people for a number of years, ending each turn as a person would.</summary>
    public static void AutoPlay(GameEngine g, int years, bool accept = true)
    {
        for (int i = 0; i < years && !g.World.Over; i++)
        {
            g.CloseAll();
            g.Ai!.Play(g.Me);
            g.EndTurn();
            if (g.Top?.Title == "The Galactic Council") g.Press("Enter");
            for (int guard = 0; guard < 20 && g.Top is not null && !g.World.Over; guard++)
            {
                var accept1 = g.Top.Options.FindIndex(o => o.Label.StartsWith("Accept"));
                if (accept1 >= 0 && accept) { g.Top.Selected = accept1; g.Press("Enter"); break; }
                g.CloseAll();
            }
        }
        if (!g.World.Over) g.CloseAll();
    }
}

internal static class Sandbox
{
    /// <summary>
    /// A small galaxy for testing one rule at a time: a row of stars a few parsecs apart, the player
    /// at the first and one other people at the last, each with a home colony and nothing else.
    /// </summary>
    public static (WorldState W, Sim Sim) Make()
    {
        var w = new WorldState { Width = 40, Height = 20, Level = 1 };
        for (int i = 0; i < 8; i++)
            w.Stars.Add(new Star { Id = i, Name = $"Star{i}", X = 4 + i * 4, Y = 10, Colour = StarColour.Yellow, Planet = PlanetType.Terran, Size = PlanetSize.Medium });
        int n = 3;
        w.Empires.Add(Sim.NewEmpire(0, 0, n, w.Stars.Count, human: true, ruler: null));
        w.Empires.Add(Sim.NewEmpire(1, 1, n, w.Stars.Count, human: false, ruler: null));
        var m = Sim.NewEmpire(2, 0, n, w.Stars.Count, human: false, ruler: "the Warden");
        m.Monster = true;
        m.Relations = [Relation.War, Relation.War, Relation.War];
        w.Empires.Add(m);
        w.Empires[0].Relations[2] = w.Empires[1].Relations[2] = Relation.War;
        foreach (var e in w.Empires.Take(2)) Designer.Starting(e);
        Colony(w, 0, 0, 50);
        Colony(w, 7, 1, 50);
        w.Stars[0].Capital = w.Stars[7].Capital = true;
        var sim = new Sim(w, new Rng(5));
        return (w, sim);
    }

    public static Star Colony(WorldState w, int star, int owner, double pop, double factories = 0)
    {
        var s = w.Stars[star];
        s.Owner = owner;
        s.Pop = pop;
        s.Factories = factories;
        s.Building = w.Empires[owner].Designs.First(d => d.Armed).Id;
        w.Empires[owner].Explored[star] = true;
        return s;
    }

    public static ShipDesign Design(Empire e, string name) => e.Designs.First(d => d.Name == name);

    public static Fleet Fleet(WorldState w, Sim sim, int owner, int star, string design, int count)
    {
        var f = sim.NewFleet(owner, w.Stars[star]);
        Sim.AddShips(f, Design(w.Empires[owner], design).Id, count);
        return f;
    }

    public static void Know(Empire e, params string[] techs)
    {
        foreach (var t in techs) e.Known[Techs.Id(t)] = true;
    }
}
