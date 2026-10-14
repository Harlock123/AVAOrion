using Avalonia;
using Avalonia.Media;
using AVAOrion.Game;
using AVAOrion.Rules;
using AVAOrion.World;

namespace AVAOrion.Views;

public sealed partial class GameCanvas
{
    /// <summary>
    /// A battle playing out: the two sides facing each other, every volley drawn as beams or missile
    /// trails in its round, ships winking out as they're destroyed, and the verdict at the end.
    /// </summary>
    private void DrawBattle(DrawingContext dc, Game.Panel p)
    {
        var w = Engine.World;
        var r = Engine.Reports[p.Tag];
        Box(dc, View, 0xFF03050C);
        using var clip = dc.PushClip(View.Deflate(3));
        Starfield(dc, View, r.Star.Length * 31, 160);
        double t = Engine.Clock - Engine.BattleStarted;
        int round = Math.Min(r.Rounds + 1, 1 + (int)(t / GameEngine.RoundTime));
        double within = t / GameEngine.RoundTime - (round - 1);
        bool done = round > r.Rounds;
        Text(dc, $"Battle at {r.Star}", View.X + 20, View.Y + 12, 32, Gold);
        Text(dc, done ? "The fight is over" : $"Round {round} of {r.Rounds}", View.Right - 20, View.Y + 20, 22, Dim, 2);
        // Where each group sits: two columns facing each other.
        var spots = new List<Point>[2];
        for (int side = 0; side < 2; side++)
        {
            var e = w.Empires[r.Empires[side]];
            double cx = side == 0 ? View.X + 170 : View.Right - 170;
            Text(dc, e.Monster ? "The monster" : $"The {e.Name}", cx, View.Y + 62, 24, e.Colour, 1);
            spots[side] = [];
            int n = r.Groups[side].Count;
            double top = View.Y + 120, step = Math.Min(70, (r.PlanetSide == side ? 330.0 : 440.0) / Math.Max(1, n));
            for (int g = 0; g < n; g++) spots[side].Add(new Point(cx, top + g * step + step / 2));
        }
        // Counts as of this moment: replay the kills up to now.
        var counts = new[] { r.Groups[0].Select(g => g.Start).ToArray(), r.Groups[1].Select(g => g.Start).ToArray() };
        int bases = r.BasesStart;
        var now = new List<Volley>();
        foreach (var byRound in r.Volleys.Where(v => v.Round <= round).GroupBy(v => v.Round))
        {
            var inRound = byRound.ToList();
            for (int i = 0; i < inRound.Count; i++)
            {
                var v = inRound[i];
                double at = inRound.Count <= 1 ? 0.2 : i / (double)inRound.Count * 0.8;
                if (v.Round == round && within < at) continue;
                if (v.Target < 0) bases = Math.Max(0, bases - v.Kills);
                else counts[1 - v.Side][v.Target] = Math.Max(r.Groups[1 - v.Side][v.Target].End, counts[1 - v.Side][v.Target] - v.Kills);
                if (v.Round == round && within - at < 0.35) now.Add(v);
            }
        }
        // The planet, on its owner's side.
        Point? planet = null;
        if (r.PlanetSide >= 0)
        {
            var star = w.Stars.First(s => s.Name == r.Star);
            double px = r.PlanetSide == 0 ? View.X + 170 : View.Right - 170, py = View.Bottom - 245;
            planet = new Point(px, py);
            Planet(dc, star, px, py, 50);
            Shadowed(dc, $"{bases} missile base{(bases == 1 ? "" : "s")}", px, py + 56, 18, bases > 0 ? Ink : Dim, 1);
        }
        // The groups.
        for (int side = 0; side < 2; side++)
        {
            var e = w.Empires[r.Empires[side]];
            for (int g = 0; g < r.Groups[side].Count; g++)
            {
                var grp = r.Groups[side][g];
                var at = spots[side][g];
                int left = counts[side][g];
                int dir = side == 0 ? 1 : -1;
                double alpha = left > 0 ? 1 : 0.25;
                using (dc.PushOpacity(alpha)) Ship(dc, grp.Hull, at.X + dir * 40, at.Y, dir, e.Colour, 1.6);
                string label = $"{left} {grp.Name}";
                Text(dc, Fit(label, 150, 19), at.X - dir * 30, at.Y - 10, 19, left > 0 ? Ink : Dim, side == 0 ? 2 : 0);
                if (left < grp.Start) Text(dc, $"-{grp.Start - left}", at.X - dir * 30, at.Y + 8, 17, Bad, side == 0 ? 2 : 0);
            }
            if (r.Retreated[side] && done) Text(dc, "withdrew", side == 0 ? View.X + 170 : View.Right - 170, View.Y + 86, 20, Warn, 1);
        }
        // This moment's volleys: beams and missile trails, flashes where they strike.
        foreach (var v in now)
        {
            Point from = v.Group < 0 ? planet ?? spots[v.Side][0] : spots[v.Side][v.Group];
            from = new Point(from.X + (v.Side == 0 ? 60 : -60), from.Y);
            Point to = v.Target < 0 ? planet ?? spots[1 - v.Side][0] : spots[1 - v.Side][v.Target];
            uint colour = w.Empires[r.Empires[v.Side]].Colour;
            if (v.Missile)
                dc.DrawLine(new Pen(Brush(0xFFFFE0A0), 2, new DashStyle([2, 5], 0)), from, to);
            else
            {
                dc.DrawLine(Pen(Sprites.Alpha(colour, 0.35), 6), from, to);
                dc.DrawLine(Pen(Sprites.Shade(colour, 1.5), 2), from, to);
            }
            if (v.Hits > 0)
            {
                double flash = 8 + Math.Min(18, v.Hits * 2);
                dc.DrawEllipse(Brush(0xC0FFF0C0), null, to, flash, flash);
                dc.DrawEllipse(Brush(0xFFFFFFFF), null, to, flash * 0.4, flash * 0.4);
            }
        }
        if (done)
        {
            var verdict = Engine.Verdict(r);
            var box = new Rect(View.X + 80, View.Bottom - 136, View.Width - 160, 46);
            dc.FillRectangle(Brush(0xD0000000), box);
            Text(dc, verdict, box.Center.X, box.Y + 10, 26, verdict.StartsWith("Defeat") ? Bad : Gold, 1);
        }
        var area = new Rect(View.X + 6, View.Bottom - 80, View.Width - 12, 74);
        var shown = new Game.Panel { Selected = p.Selected };
        foreach (var o in p.Options) shown.Options.Add(o);
        DrawPanel(dc, area, shown, frame: false);
    }
}
