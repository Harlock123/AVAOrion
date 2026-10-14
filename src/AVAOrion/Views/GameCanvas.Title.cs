using Avalonia;
using Avalonia.Media;
using AVAOrion.Game;
using AVAOrion.Rules;

namespace AVAOrion.Views;

public sealed partial class GameCanvas
{
    private void DrawTitle(DrawingContext dc)
    {
        DrawTitleArt(dc);
        var top = Engine.Top;
        if (top is null) return;
        if (Engine.Panels.Count == 1 && top.Title == "AVAOrion")
        {
            var menu = new Game.Panel().Also(p =>
            {
                foreach (var o in top.Options) p.Options.Add(o);
                p.Selected = top.Selected;
            });
            DrawPanel(dc, new Rect(342, 480, 340, 130), menu);
            Text(dc, "Arrows and Enter to choose.", 512, 700, 22, 0xFFD8E0F0, 1);
            Text(dc, "An original game of the stars. Every people, world and ship here is new.", 512, 726, 20, 0xFFA8B4C8, 1);
            return;
        }
        DrawPanel(dc, top.Kind == PanelKind.Big ? new Rect(112, 110, 800, 630) : new Rect(162, 380, 700, 350), top);
    }

    /// <summary>The title: a ringed world hanging in a nebula, and a fleet crossing in front of it.</summary>
    private void DrawTitleArt(DrawingContext dc)
    {
        var all = new Rect(0, 0, ScreenWidth, ScreenHeight);
        dc.FillRectangle(Brush(0xFF02030A), all);
        dc.DrawImage(Sprites.NebulaBitmap(1991), new Rect(0, 0, Sprites.NebulaPixels, Sprites.NebulaPixels), all);
        Starfield(dc, all, 7, 420);
        // A far yellow sun, and the great ringed world.
        dc.DrawEllipse(Brush(0x30FFE070), null, new Point(860, 140), 60, 60);
        dc.DrawEllipse(Brush(0xFFFFF0B0), null, new Point(860, 140), 16, 16);
        var c = new Point(300, 420);
        var ringBack = new Pen(Brush(0x90C8B090), 10);
        var ringFront = new Pen(Brush(0xD0E0C8A0), 10);
        dc.DrawGeometry(null, ringBack, Arc(c, 300, 70, Math.PI, 2 * Math.PI));
        dc.DrawImage(Sprites.PlanetBitmap(PlanetType.Ocean, 42), new Rect(0, 0, Sprites.PlanetPixels, Sprites.PlanetPixels), new Rect(c.X - 180, c.Y - 180, 360, 360));
        dc.DrawGeometry(null, ringFront, Arc(c, 300, 70, 0, Math.PI));
        // A small moon, and the fleet crossing the sky.
        dc.DrawImage(Sprites.PlanetBitmap(PlanetType.Barren, 7), new Rect(0, 0, Sprites.PlanetPixels, Sprites.PlanetPixels), new Rect(560, 250, 60, 60));
        double run = Engine.Clock * 40 % 1400 - 200;
        uint[] colours = [0xFFE8C850, 0xFF60C8E8, 0xFF4090E0];
        for (int i = 0; i < 6; i++)
        {
            double x = run + i * 70 - (i % 2) * 30, y = 330 + i * 26 + Math.Sin(Engine.Clock + i) * 4;
            Ship(dc, i == 0 ? Hull.Huge : i < 3 ? Hull.Large : Hull.Medium, x, y, 1, colours[i % 3], 2.2 - i * 0.15);
        }
        Shadowed(dc, "AVAORION", 512, 50, 124, 0xFFFFE070, 1);
        Shadowed(dc, "A Galaxy Waiting to Be Won", 512, 172, 36, 0xFFD8E8FF, 1);
    }

    private static StreamGeometry Arc(Point c, double rx, double ry, double from, double to)
    {
        var g = new StreamGeometry();
        using var ctx = g.Open();
        ctx.BeginFigure(new Point(c.X + Math.Cos(from) * rx, c.Y + Math.Sin(from) * ry), false);
        for (int i = 1; i <= 40; i++)
        {
            double a = from + (to - from) * i / 40;
            ctx.LineTo(new Point(c.X + Math.Cos(a) * rx, c.Y + Math.Sin(a) * ry));
        }
        ctx.EndFigure(false);
        return g;
    }
}
