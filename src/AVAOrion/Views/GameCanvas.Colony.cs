using Avalonia;
using Avalonia.Media;
using AVAOrion.Game;
using AVAOrion.Rules;
using AVAOrion.World;

namespace AVAOrion.Views;

public sealed partial class GameCanvas
{
    /// <summary>A star close up: its planet, what's known of it, and (for your colony) where its production goes.</summary>
    private void DrawColony(DrawingContext dc, Game.Panel p)
    {
        var w = Engine.World;
        var sim = Engine.Sim!;
        var s = w.Stars[p.Tag];
        var me = w.Human;
        Box(dc, View, 0xFF060A14);
        using (dc.PushClip(View.Deflate(3))) Starfield(dc, View, s.Id + 11, 120);
        double x0 = View.X + 20, y0 = View.Y + 12;
        uint col = s.Owner >= 0 ? w.Empires[s.Owner].Colour : Ink;
        Text(dc, s.Name, x0, y0, 34, col);
        Text(dc, $"{s.Colour} star", x0 + Measure(s.Name, 34) + 14, y0 + 10, 20, Planets.StarTone(s.Colour));
        if (s.Owner == w.Player) Text(dc, "Left/Right: other colonies", View.Right - 18, y0 + 10, 20, Dim, 2);
        bool known = me.Explored[s.Id];
        double py = y0 + 46;
        if (known && s.HasPlanet)
        {
            dc.DrawEllipse(Brush(Sprites.Alpha(Planets.StarTone(s.Colour), 0.08)), null, new Point(x0 + 100, py + 100), 120, 120);
            Planet(dc, s, x0 + 100, py + 100, 92);
        }
        double tx = x0 + 230, ty = py;
        if (!known)
        {
            Text(dc, "No ship of yours has surveyed this star.", tx, ty, 22, Dim);
        }
        else if (!s.HasPlanet)
        {
            Text(dc, "Only dust and comets circle this star.", tx, ty, 22, Dim);
        }
        else
        {
            Text(dc, $"{Planets.SizeNames[(int)s.Size]} {s.Kind.Name} world", tx, ty, 26, Gold);
            ty += 28;
            foreach (var line in Wrap(s.Kind.About, View.Right - 20 - tx, 20))
            {
                Text(dc, line, tx, ty, 20, Dim);
                ty += 20;
            }
            ty += 6;
            var extras = new List<string>();
            if (s.Minerals != Minerals.Normal) extras.Add($"{Planets.MineralNames[(int)s.Minerals]} in minerals ({Planets.MineralFactor(s.Minerals):0.##}x production)");
            if (s.Fertile) extras.Add("Fertile: half as many people again");
            if (s.Artifacts) extras.Add("Ruins of an ancient people: research doubled");
            if (s.Vault) extras.Add("The Vault of Aeon: whoever settles it learns the old secrets");
            foreach (var e in extras)
            {
                Text(dc, e, tx, ty, 20, Blue);
                ty += 21;
            }
            if (s.Owner == w.Player)
            {
                var r = sim.Report(s);
                ty += 4;
                Text(dc, $"People {s.Pop:0.0} of {r.MaxPop:0}" + (s.Waste > 0.5 ? $" (pollution has cost {s.Waste:0})" : ""), tx, ty, 22, Ink);
                ProgressBar(dc, new Rect(tx, ty + 24, 300, 8), s.Pop / Math.Max(1, r.MaxPop), Good);
                ty += 38;
                Text(dc, $"Factories {s.Factories:0} of {r.MaxFactories:0} the people can run", tx, ty, 22, Ink);
                ProgressBar(dc, new Rect(tx, ty + 24, 300, 8), s.Factories / Math.Max(1, r.MaxFactories), 0xFFD0A060);
                ty += 38;
                Text(dc, $"{s.Bases} missile base{(s.Bases == 1 ? "" : "s")}   Output {r.Gross:0} BC a year", tx, ty, 22, Gold);
            }
            else if (s.Owner >= 0)
            {
                var owner = w.Empires[s.Owner];
                Text(dc, $"A colony of the {owner.Name}.", tx, ty, 22, owner.Colour);
                ty += 24;
                if (sim.Sees(w.Player, s.X, s.Y)) Text(dc, $"About {s.Pop:0} people, {s.Factories:0} factories, {s.Bases} bases.", tx, ty, 20, Ink);
            }
            else
            {
                bool can = Sim.CanSettle(me, s.Planet);
                Text(dc, can ? $"Unclaimed. Room for {sim.MaxPop(s, me):0} of your people." : $"Unclaimed. Your people need {s.Kind.Needs} to live here.", tx, ty, 22, can ? Good : Warn);
            }
        }
        // The sliders, as bars of what each share buys.
        double by = View.Y + 300;
        if (s.Owner == w.Player)
        {
            var r = sim.Report(s);
            double[] bc = [r.Ships, r.Defense, r.Industry, r.Ecology, r.Research];
            uint[] colours = [0xFF80A8E8, 0xFFE07060, 0xFFD0A060, 0xFF60C070, 0xFF9AD0FF];
            for (int i = 0; i < 5; i++)
            {
                var spend = (Spend)i;
                double y = by + i * 30;
                bool sel = p.Selected == i;
                Text(dc, GameEngine.SpendWord(spend), x0, y, 22, sel ? Gold : Ink);
                var bar = new Rect(x0 + 110, y + 6, 330, 14);
                dc.FillRectangle(Brush(0xFF161E2C), bar);
                dc.FillRectangle(Brush(colours[i]), new Rect(bar.X, bar.Y, bar.Width * s.Sliders[i] / 100.0, bar.Height));
                if (spend == Spend.Ecology && r.Gross > 0)
                {
                    double need = Math.Min(1, r.Cleanup / r.Gross);
                    dc.DrawLine(Pen(0xFFFFFFFF, 2), new Point(bar.X + bar.Width * need, bar.Y - 3), new Point(bar.X + bar.Width * need, bar.Bottom + 3));
                }
                dc.DrawRectangle(Pen(sel ? Gold : 0xFF3A4A60, 1), bar);
                Text(dc, $"{s.Sliders[i]}%", x0 + 452, y, 22, Ink);
                Text(dc, $"{bc[i]:0.0} BC", View.Right - 20, y, 22, Dim, 2);
            }
            by += 156;
        }
        var area = new Rect(View.X + 6, s.Owner == w.Player ? by : View.Bottom - 160, View.Width - 12, s.Owner == w.Player ? View.Bottom - by - 4 : 156);
        dc.DrawLine(Pen(0xFF2A3A50, 1), new Point(area.X + 12, area.Y), new Point(area.Right - 12, area.Y));
        var shown = new Game.Panel { Selected = p.Selected };
        foreach (var o in p.Options) shown.Options.Add(o);
        DrawPanel(dc, area, shown, frame: false);
    }
}
