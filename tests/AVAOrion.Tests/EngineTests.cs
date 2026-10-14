using System.Diagnostics;
using AVAOrion.Game;
using AVAOrion.Rules;
using AVAOrion.World;

namespace AVAOrion.Tests;

/// <summary>The game played through its keys, as a person plays it.</summary>
public sealed class EngineTests
{
    [Fact]
    public void A_new_game_from_the_title_by_keys()
    {
        var g = new GameEngine(4, Helpers.TempSaves());
        Assert.Equal(Mode.Title, g.Mode);
        g.Press("Enter");                       // Begin a new game
        Assert.Equal("Which people will you lead?", g.Top!.Title);
        g.Press("Down");
        g.Press("Down");
        g.Press("Enter");                       // the Siltari
        Assert.Equal("Hive-Mother Oss", g.Top!.Entry);
        g.Press("Enter");
        Assert.Equal("The galaxy", g.Top!.Title);
        g.Press("Enter");                       // Begin
        Assert.Equal(Mode.Play, g.Mode);
        Assert.Equal("Siltari", g.Me.Name);
        Assert.Equal(5, g.World.Empires.Count(e => !e.Monster));
        Assert.Equal(g.World.Stars.Single(s => s.Owner == g.World.Player).Id, g.Selected);
        g.Press("Enter");                       // the opening message
        Assert.Empty(g.Panels);
    }

    [Fact]
    public void The_cursor_steps_from_star_to_star()
    {
        var g = Helpers.NewGame(seed: 6);
        int start = g.Selected;
        var here = g.Here;
        g.Press("Right");
        if (g.Selected != start) Assert.True(g.Here.X > here.X);
        g.Press("Left");
        g.Press("Up");
        g.Press("Down");
        g.Press("C");
        Assert.Equal(start, g.Selected);
    }

    [Fact]
    public void Sending_a_scout_by_keys_and_ending_the_year()
    {
        var g = Helpers.NewGame(seed: 8);
        var home = g.Here;
        g.Press("F");
        Assert.StartsWith("Your fleet", g.Top!.Title);
        // Send just one scout: everything else to zero.
        foreach (var o in g.Top.Options.ToList())
        {
            int i = g.Top.Options.FindIndex(x => x.Label == o.Label);
            if (o.Adjust is null) continue;
            g.Top.Selected = i;
            for (int k = 0; k < 5; k++) g.Press("Left");
            if (o.Label.StartsWith("Scout")) g.Press("Right");
        }
        g.Top.Selected = g.Top.Options.FindIndex(o => o.Label.StartsWith("Choose"));
        g.Press("Enter");
        Assert.Equal(Aim.Fleet, g.Aiming);
        var target = g.World.Stars.Where(s => s != home && g.Sim!.InRange(g.World.Player, s)).OrderBy(s => g.World.Distance(s, home)).First();
        g.Select(target.Id);
        g.Press("Enter");
        Assert.Equal(Aim.None, g.Aiming);
        var going = Assert.Single(g.World.Fleets, f => f.Owner == g.World.Player && f.Dest == target.Id);
        Assert.Equal(1, going.Count);
        int year = g.World.Year;
        g.Press("E");
        Assert.Equal(year + 1, g.World.Year);
    }

    [Fact]
    public void The_colony_screen_moves_the_sliders()
    {
        var g = Helpers.NewGame(seed: 9);
        g.Press("Enter");
        Assert.Equal(PanelKind.Colony, g.Top!.Kind);
        var home = g.Here;
        int ships = home.Slider(Spend.Ships);
        g.Top.Selected = 0;
        g.Press("Right");
        Assert.Equal(ships + 5, home.Slider(Spend.Ships));
        Assert.Equal(100, home.Sliders.Sum());
        g.Top!.Selected = 5;
        g.Press("Right");
        Assert.NotEqual(-1, home.Building);
        g.Press("Esc");
        Assert.Empty(g.Panels);
    }

    [Fact]
    public void Research_designs_and_the_other_screens()
    {
        var g = Helpers.NewGame(seed: 12);
        g.Press("R");
        Assert.Equal("Research", g.Top!.Title);
        int before = g.Me.Alloc[0];
        g.Press("Right");
        Assert.Equal(before + 5, g.Me.Alloc[0]);
        Assert.Equal(100, g.Me.Alloc.Sum());
        g.Press("Enter");
        Assert.EndsWith("what next?", g.Top!.Title);
        g.Press("Enter");
        g.Press("Esc");
        g.Press("D");
        Assert.Equal("Ship designs", g.Top!.Title);
        g.Top.Selected = g.Top.Options.FindIndex(o => o.Label.StartsWith("Draw up"));
        g.Press("Enter");
        Assert.Equal(PanelKind.Design, g.Top!.Kind);
        Assert.NotNull(g.Draft);
        g.Press("Right");                       // a large hull
        Assert.Equal(Hull.Large, g.Draft!.Hull);
        g.Top!.Selected = g.Top.Options.FindIndex(o => o.Label.StartsWith("Fill"));
        g.Press("Enter");
        g.Top!.Selected = g.Top.Options.FindIndex(o => o.Label.StartsWith("Save"));
        g.Press("Enter");
        Assert.NotNull(g.Top!.Entry);
        g.Press("Enter");
        Assert.Contains(g.Me.Active, d => d.Hull == Hull.Large);
        g.CloseAll();
        foreach (var key in new[] { "P", "L", "G" })
        {
            g.Press(key);
            Assert.NotNull(g.Top);
            g.CloseAll();
        }
        g.Press("Esc");
        Assert.Equal("Menu", g.Top!.Title);
    }

    [Fact]
    public void Saving_and_loading_a_game()
    {
        var g = Helpers.NewGame(seed: 13);
        Helpers.AutoPlay(g, 20);
        g.World.Seed = g.Sim!.Rng.State;
        g.Saves.Save(3, g.World);
        int year = g.World.Year;
        Helpers.AutoPlay(g, 5);
        Assert.True(g.Load(3));
        Assert.Equal(year, g.World.Year);
        Assert.Equal(Mode.Play, g.Mode);
        Assert.StartsWith(Path.GetTempPath(), g.Saves.Folder);
    }

    [Fact]
    public void The_attract_demo_plays_itself_and_any_key_ends_it()
    {
        var g = new GameEngine(5, Helpers.TempSaves());
        for (int i = 0; i < 35 * 10; i++) g.Update(0.1);
        Assert.True(g.InDemo);
        int year = g.World.Year;
        for (int i = 0; i < 40; i++) g.Update(0.1);
        Assert.True(g.World.Year > year);
        g.Press("Space");
        Assert.False(g.InDemo);
        Assert.Equal(Mode.Title, g.Mode);
    }

    [Fact]
    public void A_whole_game_runs_to_its_end()
    {
        var g = Helpers.NewGame(seed: 14);
        var clock = Stopwatch.StartNew();
        Helpers.AutoPlay(g, 450);
        Assert.True(g.World.Over);
        Assert.Equal(Mode.Over, g.Mode);
        Assert.NotEmpty(g.World.Outcome);
        Assert.True(clock.Elapsed.TotalSeconds < 120, $"took {clock.Elapsed.TotalSeconds:0}s");
    }
}
