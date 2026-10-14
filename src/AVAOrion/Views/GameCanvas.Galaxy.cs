using Avalonia;
using Avalonia.Media;
using AVAOrion.Game;
using AVAOrion.Rules;
using AVAOrion.World;

namespace AVAOrion.Views;

public sealed partial class GameCanvas
{
    /// <summary>How the galaxy's parsecs map onto the view.</summary>
    private (double Scale, double X, double Y) Fit(WorldState w)
    {
        double s = Math.Min((View.Width - 40) / w.Width, (View.Height - 40) / w.Height);
        return (s, View.X + (View.Width - w.Width * s) / 2, View.Y + (View.Height - w.Height * s) / 2);
    }

    private static Point At(double x, double y, (double S, double X, double Y) fit) => new(fit.X + x * fit.S, fit.Y + y * fit.S);

    /// <summary>A starfield behind everything: fixed points of light that twinkle.</summary>
    private void Starfield(DrawingContext dc, Rect area, int seed, int count)
    {
        var rng = new Rng((ulong)(seed * 7 + 3));
        for (int i = 0; i < count; i++)
        {
            double x = area.X + rng.Next((int)area.Width), y = area.Y + rng.Next((int)area.Height);
            double tw = 0.45 + 0.35 * Math.Sin(Engine.Clock * (0.6 + i % 5 * 0.3) + i);
            uint c = i % 7 == 0 ? 0xFFB0C8FF : i % 11 == 0 ? 0xFFFFE0B0 : 0xFFFFFFFF;
            double size = i % 13 == 0 ? 2 : 1;
            dc.FillRectangle(Brush(Sprites.Alpha(c, tw)), new Rect(x, y, size, size));
        }
    }

    private void DrawGalaxy(DrawingContext dc)
    {
        var w = Engine.World;
        var sim = Engine.Sim;
        dc.FillRectangle(Brush(0xFF02030A), View);
        if (sim is null || w.Stars.Count == 0) return;
        using var clip = dc.PushClip(View);
        int seed = (int)(w.Seed % 100000);
        dc.DrawImage(Sprites.NebulaBitmap(seed), new Rect(0, 0, Sprites.NebulaPixels, Sprites.NebulaPixels), View);
        Starfield(dc, View, seed, 260);
        var fit = Fit(w);
        var me = w.Human;
        // How far your ships can fly: a soft glow around each of your colonies.
        double range = Sim.Range(me) * fit.Scale;
        foreach (var s in w.Stars)
            if (s.Owner == w.Player)
                dc.DrawEllipse(Brush(Sprites.Alpha(me.Colour, 0.06)), null, At(s.X, s.Y, fit), range, range);
        // Lanes of your fleets in flight.
        foreach (var f in w.Fleets)
        {
            if (f.Dest < 0 || f.Owner != w.Player) continue;
            var dest = w.Stars[f.Dest];
            var pen = new Pen(Brush(Sprites.Alpha(me.Colour, 0.55)), 1.2, new DashStyle([3, 4], Engine.Clock * -6));
            dc.DrawLine(pen, At(f.X, f.Y, fit), At(dest.X, dest.Y, fit));
        }
        // The stars.
        foreach (var s in w.Stars)
        {
            var p = At(s.X, s.Y, fit);
            uint tone = Planets.StarTone(s.Colour);
            double core = s.Colour == StarColour.Brown ? 3 : s.Colour is StarColour.Blue or StarColour.White ? 4.5 : 4;
            double pulse = 1 + 0.08 * Math.Sin(Engine.Clock * 1.7 + s.Id);
            dc.DrawEllipse(Brush(Sprites.Alpha(tone, 0.18)), null, p, core * 3 * pulse, core * 3 * pulse);
            dc.DrawEllipse(Brush(Sprites.Alpha(tone, 0.35)), null, p, core * 1.7, core * 1.7);
            dc.DrawEllipse(Brush(Sprites.Shade(tone, 1.3)), null, p, core, core);
            if (s.Owner >= 0)
            {
                uint oc = w.Empires[s.Owner].Colour;
                dc.DrawEllipse(null, Pen(oc, 2), p, 9, 9);
                if (s.Capital) dc.DrawEllipse(null, Pen(Sprites.Alpha(oc, 0.6), 1), p, 12, 12);
            }
            if (s.Vault) dc.DrawEllipse(null, new Pen(Brush(0xFFE0B0FF), 1.2, new DashStyle([2, 3], Engine.Clock * 3)), p, 14, 14);
            uint name = s.Owner >= 0 ? Sprites.Shade(w.Empires[s.Owner].Colour, 1.15) : me.Explored[s.Id] ? 0xFFB8C4D8 : 0xFF6A7488;
            Shadowed(dc, s.Name, p.X, p.Y + 10, 16, name, 1);
        }
        // Fleets waiting at stars, each people's to one side.
        foreach (var group in w.Fleets.Where(f => f.At >= 0).GroupBy(f => f.At))
        {
            var star = w.Stars[group.Key];
            var p = At(star.X, star.Y, fit);
            int i = 0;
            foreach (var f in group.OrderBy(f => f.Owner != w.Player))
            {
                if (f.Owner != w.Player && !sim.Sees(w.Player, star.X, star.Y)) continue;
                var e = w.Empires[f.Owner];
                double fx = p.X + 14 + (i % 2) * 14, fy = p.Y - 12 - (i / 2) * 12;
                if (f.IsTransport) Capsule(dc, fx, fy, e.Colour);
                else Ship(dc, Biggest(e, f), fx, fy, 1, e.Colour, 0.7);
                i++;
            }
        }
        // Fleets in flight.
        foreach (var f in w.Fleets.Where(f => f.Dest >= 0))
        {
            if (f.Owner != w.Player && !sim.Sees(w.Player, f.X, f.Y)) continue;
            var e = w.Empires[f.Owner];
            var p = At(f.X, f.Y, fit);
            var dest = w.Stars[f.Dest];
            int dir = dest.X >= f.X ? 1 : -1;
            if (f.IsTransport) Capsule(dc, p.X, p.Y, e.Colour);
            else Ship(dc, Biggest(e, f), p.X, p.Y, dir, e.Colour, 0.75);
            if (f.Owner == w.Player) Text(dc, f.Eta.ToString(), p.X + 8, p.Y - 2, 15, 0xFFE0E8F0);
        }
        // The cursor, and where something is being sent.
        var here = At(Engine.Here.X, Engine.Here.Y, fit);
        if (Engine.Aiming != Aim.None && Engine.AimFrom >= 0)
        {
            var from = w.Stars[Engine.AimFrom];
            var (turns, ok) = Engine.AimInfo();
            uint col = ok ? Good : Bad;
            dc.DrawLine(new Pen(Brush(col), 2, new DashStyle([5, 4], Engine.Clock * -10)), At(from.X, from.Y, fit), here);
            if (Engine.Here != from) Shadowed(dc, ok ? $"{turns} year{(turns == 1 ? "" : "s")}" : "out of range", here.X, here.Y - 34, 18, col, 1);
        }
        double spin = Engine.Clock * 1.5;
        for (int k = 0; k < 4; k++)
        {
            double a = spin + k * Math.PI / 2;
            var p1 = new Point(here.X + Math.Cos(a) * 16, here.Y + Math.Sin(a) * 16);
            var p2 = new Point(here.X + Math.Cos(a + 0.5) * 16, here.Y + Math.Sin(a + 0.5) * 16);
            dc.DrawLine(Pen(Gold, 2), p1, p2);
        }
    }

    private static Hull Biggest(Empire e, Fleet f) => f.Ships.Select(s => e.Design(s.Design)?.Hull ?? Hull.Small).DefaultIfEmpty(Hull.Small).Max();

    // ───────────────────────────── ships and planets ─────────────────────────────

    /// <summary>A ship seen from above, pointing left or right: bigger hulls are longer, wider and more elaborate.</summary>
    private static void Ship(DrawingContext dc, Hull hull, double x, double y, int dir, uint colour, double scale)
    {
        double u = 5 * scale * (1 + (int)hull * 0.45), d = dir;
        Point P(double px, double py) => new(x + px * u * d, y + py * u);
        uint body = colour, dark = Sprites.Shade(colour, 0.55), light = Sprites.Shade(colour, 1.35);
        const uint edge = 0xFF080810;
        switch (hull)
        {
            case Hull.Small:
                Poly(dc, body, edge, P(1.4, 0), P(-1, -0.9), P(-0.5, 0), P(-1, 0.9));
                break;
            case Hull.Medium:
                Poly(dc, dark, edge, P(-0.3, -0.4), P(-1.2, -1.1), P(-0.9, -0.2));
                Poly(dc, dark, edge, P(-0.3, 0.4), P(-1.2, 1.1), P(-0.9, 0.2));
                Poly(dc, body, edge, P(1.6, 0), P(0.6, -0.45), P(-1.2, -0.4), P(-1.2, 0.4), P(0.6, 0.45));
                break;
            case Hull.Large:
                Poly(dc, dark, edge, P(0.4, -0.3), P(-0.6, -1.3), P(-1.2, -1.3), P(-0.9, -0.3));
                Poly(dc, dark, edge, P(0.4, 0.3), P(-0.6, 1.3), P(-1.2, 1.3), P(-0.9, 0.3));
                Poly(dc, body, edge, P(1.7, 0), P(1.0, -0.5), P(-1.3, -0.55), P(-1.5, 0), P(-1.3, 0.55), P(1.0, 0.5));
                dc.DrawEllipse(Brush(light), null, P(0.5, 0), u * 0.25, u * 0.2);
                break;
            default:
                Poly(dc, dark, edge, P(1.0, -0.5), P(-0.2, -1.4), P(-1.4, -1.4), P(-1.1, -0.5));
                Poly(dc, dark, edge, P(1.0, 0.5), P(-0.2, 1.4), P(-1.4, 1.4), P(-1.1, 0.5));
                Poly(dc, body, edge, P(1.8, 0), P(1.3, -0.6), P(-1.4, -0.7), P(-1.7, -0.3), P(-1.7, 0.3), P(-1.4, 0.7), P(1.3, 0.6));
                dc.FillRectangle(Brush(light), new Rect(Math.Min(P(-0.6, -0.2).X, P(0.6, -0.2).X), P(0, -0.2).Y, u * 1.2, u * 0.4));
                break;
        }
        // Engine glow at the back.
        dc.DrawEllipse(Brush(0xC0FFC060), null, P(-1.25 - (int)hull * 0.12, 0), u * 0.18, u * 0.18);
    }

    private static void Capsule(DrawingContext dc, double x, double y, uint colour)
    {
        dc.DrawRectangle(Brush(Sprites.Shade(colour, 0.8)), Pen(0xFF080810, 1), new Rect(x - 5, y - 3, 10, 6), 3, 3);
        dc.FillRectangle(Brush(0xFFE0E8F0), new Rect(x - 2, y - 1, 4, 2));
    }

    /// <summary>A star's planet as a lit sphere, centred on a point.</summary>
    private static void Planet(DrawingContext dc, Star s, double cx, double cy, double radius)
    {
        if (!s.HasPlanet) return;
        dc.DrawImage(Sprites.PlanetBitmap(s.Planet, s.Id * 7 + 1), new Rect(0, 0, Sprites.PlanetPixels, Sprites.PlanetPixels),
            new Rect(cx - radius, cy - radius, radius * 2, radius * 2));
    }
}
