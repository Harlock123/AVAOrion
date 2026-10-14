using Avalonia;
using Avalonia.Media;
using AVAOrion.Game;
using AVAOrion.Rules;
using AVAOrion.World;

namespace AVAOrion.Views;

public sealed partial class GameCanvas
{
    /// <summary>The design room: the ship as drawn up so far, how full it is, what it costs and what it can do.</summary>
    private void DrawDesign(DrawingContext dc, Game.Panel p)
    {
        Box(dc, View, 0xFF060A14);
        var d = Engine.Draft;
        double x0 = View.X + 20, y0 = View.Y + 12;
        Text(dc, p.Title, x0, y0, 32, Gold);
        if (d is not null)
        {
            var me = Engine.Me;
            // The ship itself, in a pool of light.
            var c = new Point(View.X + 170, View.Y + 150);
            dc.DrawEllipse(Brush(Sprites.Alpha(me.Colour, 0.08)), null, c, 130, 90);
            Ship(dc, d.Hull, c.X, c.Y, 1, me.Colour, 6 - (int)d.Hull * 0.9);
            double tx = View.X + 340, ty = View.Y + 64;
            Text(dc, Fit(d.Name, View.Right - 20 - tx, 26), tx, ty, 26, me.Colour);
            ty += 32;
            int used = d.SpaceUsed;
            Text(dc, $"Space {used} of {d.Info.Space}", tx, ty, 22, d.Fits ? Ink : Bad);
            ProgressBar(dc, new Rect(tx, ty + 24, 320, 10), (double)used / d.Info.Space, d.Fits ? Good : Bad);
            ty += 42;
            Text(dc, $"Cost {d.Cost} BC", tx, ty, 22, Gold);
            ty += 26;
            Text(dc, $"Hull {d.Hp} hp, {Ships.ArmorNames[d.Armor].ToLowerInvariant()} armour", tx, ty, 20, Ink);
            ty += 22;
            int evasion = d.Info.Defense + (int)Sim.Best(me, Rules.Effect.Inertial) + me.Kind.Defense;
            Text(dc, $"Aim {d.Computer + me.Kind.Attack}   Evasion {evasion}   Shield {d.Shield}   ECM {d.Ecm}", tx, ty, 20, Ink);
            ty += 22;
            Text(dc, $"Speed {1.5 + Math.Max(1, Sim.Warp(me)):0.0} parsecs a year", tx, ty, 20, Dim);
            ty += 22;
            foreach (var m in d.Mounts)
            {
                var w = Ships.Weapons[m.Weapon];
                Text(dc, $"{m.Count} x {w.Name}: {w.Min}{(w.Max != w.Min ? "-" + w.Max : "")} damage{(w.Shots > 1 ? $" x{w.Shots}" : "")}{(w.Missile ? $", {w.Ammo} salvoes" : "")}", tx, ty, 19, Blue);
                ty += 20;
            }
            if (d.Colony) { Text(dc, "Colony pod", tx, ty, 19, Good); ty += 20; }
            if (d.Tanks) { Text(dc, "Reserve tanks: +3 parsecs range", tx, ty, 19, Good); }
        }
        var area = new Rect(View.X + 6, View.Y + 300, View.Width - 12, View.Height - 306);
        dc.DrawLine(Pen(0xFF2A3A50, 1), new Point(area.X + 12, area.Y), new Point(area.Right - 12, area.Y));
        var shown = new Game.Panel { Selected = p.Selected };
        foreach (var o in p.Options) shown.Options.Add(o);
        DrawPanel(dc, area, shown, frame: false);
    }
}
