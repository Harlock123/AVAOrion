using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAOrion.Game;
using AVAOrion.Rules;
using AVAOrion.Views;
using AVAOrion.World;

namespace AVAOrion.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set ORION_SHOTS to a
/// folder to save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas) Open(GameEngine g)
    {
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("ORION_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAOrion-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    [AvaloniaFact]
    public void Title_and_a_new_game()
    {
        var g = new GameEngine(11, Helpers.TempSaves());
        var (w, c) = Open(g);
        c.Tick(2.5);
        Shot(w, c, "title");
        g.Press("Enter");
        g.Top!.Selected = 3;
        Shot(w, c, "peoples");
        g.Press("Enter");
        g.Press("Enter");
        Shot(w, c, "galaxy-options");
        g.Press("Enter");
        Shot(w, c, "start");
    }

    [AvaloniaFact]
    public void A_growing_empire()
    {
        var g = Helpers.NewGame(seed: 5, race: 0);
        var (w, c) = Open(g);
        Helpers.AutoPlay(g, 70);
        var home = g.World.Stars.First(s => s.Owner == g.World.Player && s.Capital);
        g.Select(home.Id);
        Shot(w, c, "galaxy");
        g.Press("Enter");
        Shot(w, c, "colony");
        g.CloseAll();
        g.ResearchMenu();
        Shot(w, c, "research");
        g.CloseAll();
        g.DesignsMenu();
        g.Top!.Selected = g.Top.Options.Count - 2;
        g.Press("Enter");
        Shot(w, c, "design");
        g.CloseAll();
        g.PeoplesMenu();
        Shot(w, c, "peoples-met");
        g.CloseAll();
        g.GalaxyReport();
        Shot(w, c, "report");
        g.CloseAll();
        var fleet = g.World.Fleets.FirstOrDefault(f => f.Owner == g.World.Player && f.At >= 0 && !f.IsTransport);
        if (fleet is not null)
        {
            g.Select(fleet.At);
            g.Press("F");
            Shot(w, c, "fleet");
            g.Press("Down");
            while (g.Top is not null && !g.Top.Options[g.Top.Selected].Label.StartsWith("Choose")) g.Press("Down");
            g.Press("Enter");
            g.Step(1, 0);
            Shot(w, c, "aim");
            g.Press("Esc");
        }
    }

    [AvaloniaFact]
    public void A_battle_playing_out()
    {
        var g = Helpers.NewGame(seed: 7, race: 1);
        var (w, c) = Open(g);
        var sim = g.Sim!;
        var me = g.Me;
        var them = g.World.Empires.First(e => e.Id != me.Id && !e.Monster);
        var star = g.World.Stars.First(s => s.Owner == them.Id);
        Sandbox.Know(me, "Mass Driver", "Deflector Shield I", "Battle Computer Mk I");
        var cruiser = Designer.Add(me, Designer.Warship(me, Hull.Large));
        var mine = sim.NewFleet(me.Id, star);
        Sim.AddShips(mine, cruiser.Id, 4);
        Sim.AddShips(mine, me.Designs.First(d => d.Name == "Fighter").Id, 6);
        var theirs = sim.FleetAt(them.Id, star);
        Sim.AddShips(theirs, them.Designs.First(d => d.Name == "Destroyer").Id, 5);
        star.Bases = 3;
        sim.Meet(me.Id, them.Id);
        sim.DeclareWar(me.Id, them.Id);
        var report = sim.Fight(star, them.Id, me.Id);
        g.CloseAll();
        g.ShowBattle(report);
        c.Tick(GameEngine.RoundTime * 1.5);
        Shot(w, c, "battle");
        c.Tick(GameEngine.RoundTime * 12);
        Shot(w, c, "battle-end");
    }
}
