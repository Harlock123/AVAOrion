using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AVAOrion.Game;
using AVAOrion.Rules;
using AVAOrion.World;

namespace AVAOrion.Views;

public sealed partial class GameCanvas
{
    // The layout: the galaxy on the left; the people's state, the chosen star and the news on the right.
    private static readonly Rect View = new(16, 16, 704, 704);
    private static readonly Rect Status = new(736, 16, 272, 160);
    private static readonly Rect StarBox = new(736, 184, 272, 292);
    private static readonly Rect Messages = new(736, 484, 272, 268);
    private static readonly Rect Bar = new(16, 726, 704, 26);

    private static readonly Typeface Retro = new(new FontFamily("avares://AVAOrion/Assets/Fonts#VT323"));
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width);
        return p;
    }

    private const uint Ground = 0xFF04060C, PanelFill = 0xFF0E1420, Frame = 0xFF7890B8, Ink = 0xFFE8ECF0, Gold = 0xFFFFD860,
        Dim = 0xFF8A96A4, Good = 0xFF60D080, Warn = 0xFFE0C040, Bad = 0xFFE05040, Blue = 0xFF80C0FF;

    private static FormattedText Fmt(string text, double size, uint colour) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));

    private static double Measure(string text, double size) => Fmt(text, size, Ink).Width;

    /// <summary>Text with its top-left corner at a point (or centred, or right-aligned, on it).</summary>
    private static void Text(DrawingContext dc, string text, double x, double y, double size, uint colour, int align = 0)
    {
        var ft = Fmt(text, size, colour);
        double left = align == 0 ? x : align == 1 ? x - ft.Width / 2 : x - ft.Width;
        dc.DrawText(ft, new Point(left, y));
    }

    private static void Shadowed(DrawingContext dc, string text, double x, double y, double size, uint colour, int align = 0)
    {
        Text(dc, text, x + 2, y + 2, size, 0xC0000000, align);
        Text(dc, text, x, y, size, colour, align);
    }

    /// <summary>Text shortened with an ellipsis to fit a width.</summary>
    private static string Fit(string text, double width, double size)
    {
        if (Measure(text, size) <= width) return text;
        while (text.Length > 1 && Measure(text + "...", size) > width) text = text[..^1];
        return text + "...";
    }

    private static readonly Dictionary<(string, double, double), List<string>> WrapCache = [];

    /// <summary>Splits text into lines that fit a width.</summary>
    private static List<string> Wrap(string text, double width, double size)
    {
        if (WrapCache.TryGetValue((text, width, size), out var cached)) return cached;
        var lines = new List<string>();
        var line = "";
        foreach (var word in text.Split(' '))
        {
            string tryLine = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && Measure(tryLine, size) > width)
            {
                lines.Add(line);
                line = word;
            }
            else line = tryLine;
        }
        if (line.Length > 0) lines.Add(line);
        if (WrapCache.Count > 3000) WrapCache.Clear();
        WrapCache[(text, width, size)] = lines;
        return lines;
    }

    private static void Box(DrawingContext dc, Rect r, uint fill = PanelFill, uint border = Frame)
    {
        dc.FillRectangle(Brush(fill), r);
        dc.DrawRectangle(Pen(border, 2), r.Deflate(1));
        dc.DrawRectangle(Pen(Sprites.Shade(border, 0.45), 1), r.Deflate(4));
    }

    private static void Image(DrawingContext dc, Bitmap b, double x, double y, double size, double alpha = 1)
    {
        if (alpha < 1)
            using (dc.PushOpacity(alpha))
                dc.DrawImage(b, new Rect(0, 0, 16, 16), new Rect(x, y, size, size));
        else dc.DrawImage(b, new Rect(0, 0, 16, 16), new Rect(x, y, size, size));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, 1.5) : null, g);
    }

    private static void ProgressBar(DrawingContext dc, Rect r, double f, uint colour)
    {
        dc.FillRectangle(Brush(0xFF26303A), r);
        dc.FillRectangle(Brush(colour), new Rect(r.X, r.Y, r.Width * Math.Clamp(f, 0, 1), r.Height));
        dc.DrawRectangle(Pen(0xFF4A5868, 1), r);
    }

    private void DrawScreen(DrawingContext dc)
    {
        dc.FillRectangle(Brush(Ground), new Rect(0, 0, ScreenWidth, ScreenHeight));
        if (Engine.Mode == Mode.Title)
        {
            DrawTitle(dc);
            return;
        }
        DrawPlay(dc);
        if (Engine.InDemo)
        {
            double pulse = 0.6 + 0.4 * Math.Sin(Engine.Clock * 4);
            var r = new Rect(168, 40, 400, 56);
            dc.FillRectangle(Brush(0xC0000000), r);
            Text(dc, "DEMO - PRESS ANY KEY", 368, 52, 34, Sprites.Shade(Gold, pulse), 1);
        }
    }

    private void DrawPlay(DrawingContext dc)
    {
        var top = Engine.Top;
        switch (top?.Kind)
        {
            case PanelKind.Colony: DrawColony(dc, top); break;
            case PanelKind.Battle: DrawBattle(dc, top); break;
            case PanelKind.Design: DrawDesign(dc, top); break;
            default: DrawGalaxy(dc); break;
        }
        DrawStatus(dc);
        DrawStarBox(dc);
        DrawMessages(dc, Engine.Log);
        DrawBar(dc);
        if (top is not null && top.Kind is not (PanelKind.Colony or PanelKind.Battle or PanelKind.Design)) DrawPanelFor(dc, top);
    }

    // ───────────────────────────── the side panels ─────────────────────────────

    private void DrawStatus(DrawingContext dc)
    {
        var w = Engine.World;
        var sim = Engine.Sim;
        if (sim is null) return;
        var me = w.Human;
        var r = Status;
        Box(dc, r);
        double x = r.X + 14, y = r.Y + 8;
        Text(dc, Fit($"The {me.Name}", r.Width - 28, 28), x, y, 28, me.Colour);
        y += 28;
        Text(dc, Fit(me.Ruler, r.Width - 28, 20), x, y, 20, Dim);
        y += 22;
        Text(dc, $"Year {w.Year}", x, y, 24, Ink);
        bool war = w.Empires.Any(e => e.Alive && !e.Monster && sim.AtWar(w.Player, e.Id));
        if (war) Text(dc, "AT WAR", r.Right - 14, y + 2, 20, Bad, 2);
        y += 26;
        double bc = 0, rp = 0;
        foreach (var s in w.Stars)
            if (s.Owner == w.Player)
            {
                var rep = sim.Report(s);
                bc += rep.Gross;
                rp += rep.RP;
            }
        Text(dc, $"Output {bc:0} BC   Research {rp:0}", x, y, 20, Gold);
        y += 22;
        Text(dc, $"Reserve {me.Reserve:0} BC   {w.Stars.Count(s => s.Owner == w.Player)} colonies", x, y, 20, Dim);
        y += 22;
        Text(dc, w.CouncilTurn >= 0 ? $"The Council sits in {WorldState.StartYear + w.CouncilTurn}" : "You have defied the Council", x, y, 19, Blue);
    }

    private void DrawStarBox(DrawingContext dc)
    {
        var r = StarBox;
        Box(dc, r);
        var w = Engine.World;
        var sim = Engine.Sim;
        if (sim is null || w.Stars.Count == 0) return;
        var s = Engine.Here;
        var me = w.Human;
        double x = r.X + 14, y = r.Y + 8;
        uint col = s.Owner >= 0 ? w.Empires[s.Owner].Colour : Ink;
        Text(dc, s.Name, x, y, 28, col);
        Text(dc, $"{s.Colour} star", r.Right - 14, y + 6, 18, Planets.StarTone(s.Colour), 2);
        y += 32;
        if (!me.Explored[s.Id])
        {
            Text(dc, "Not yet surveyed.", x, y, 20, Dim);
            y += 22;
        }
        else if (!s.HasPlanet)
        {
            Text(dc, "No planet worth the name.", x, y, 20, Dim);
            y += 22;
        }
        else
        {
            Planet(dc, s, r.Right - 52, y + 30, 32);
            Text(dc, $"{Planets.SizeNames[(int)s.Size]} {s.Kind.Name.ToLowerInvariant()}", x, y, 20, Ink);
            y += 21;
            var extras = new List<string>();
            if (s.Minerals != Minerals.Normal) extras.Add(Planets.MineralNames[(int)s.Minerals].ToLowerInvariant());
            if (s.Fertile) extras.Add("fertile");
            if (s.Artifacts) extras.Add("ancient ruins");
            if (extras.Count > 0) Text(dc, string.Join(", ", extras), x, y, 18, Gold);
            y += 20;
            if (s.Owner >= 0)
            {
                var owner = w.Empires[s.Owner];
                Text(dc, Fit($"The {owner.Name}' colony", r.Width - 90, 19), x, y, 19, owner.Colour);
                y += 20;
                if (s.Owner == w.Player || sim.Sees(w.Player, s.X, s.Y))
                {
                    var rep = s.Owner == w.Player ? sim.Report(s) : null;
                    Text(dc, $"{s.Pop:0}{(rep is null ? "" : $"/{rep.MaxPop:0}")} people, {s.Factories:0} factories", x, y, 19, Ink);
                    y += 20;
                    Text(dc, $"{s.Bases} missile base{(s.Bases == 1 ? "" : "s")}{(rep is null ? "" : $", {rep.Gross:0} BC")}", x, y, 19, Dim);
                    y += 20;
                }
            }
            else
            {
                bool can = Sim.CanSettle(me, s.Planet);
                Text(dc, can ? $"Room for {sim.MaxPop(s, me):0} of your people" : $"Needs {s.Kind.Needs}", x, y, 19, can ? Good : Warn);
                y += 20;
            }
        }
        double reach = sim.Reach(w.Player, s.X, s.Y);
        if (s.Owner != w.Player) Text(dc, $"{reach:0.0} parsecs from your colonies{(reach > Sim.Range(me) ? " (out of range)" : "")}", x, y, 18, reach > Sim.Range(me) ? Warn : Dim);
        y += 24;
        // Fleets here and on their way.
        dc.DrawLine(Pen(0xFF2A3A50, 1), new Point(x, y - 2), new Point(r.Right - 14, y - 2));
        foreach (var f in w.Fleets.Where(f => f.At == s.Id && (f.Owner == w.Player || sim.Sees(w.Player, s.X, s.Y))).Take(4))
        {
            var e = w.Empires[f.Owner];
            Ship(dc, f.Ships.Select(st => e.Design(st.Design)?.Hull ?? Hull.Small).DefaultIfEmpty(Hull.Small).Max(), x + 8, y + 10, 1, e.Colour, 0.8);
            string what = f.IsTransport ? $"{f.Troops} colonists" : string.Join(", ", f.Ships.Select(st => $"{st.Count} {e.Design(st.Design)?.Name}"));
            Text(dc, Fit(what, r.Width - 54, 18), x + 24, y, 18, f.Owner == w.Player ? Ink : e.Colour);
            y += 20;
        }
        int coming = w.Fleets.Count(f => f.Dest == s.Id && f.Owner == w.Player);
        if (coming > 0 && y < r.Bottom - 20) Text(dc, $"{coming} of your fleets on the way", x, y, 18, Blue);
    }

    private void DrawMessages(DrawingContext dc, IReadOnlyList<(string Text, uint Colour)> log)
    {
        var r = Messages;
        Box(dc, r);
        const double size = 19, lh = 19;
        int room = (int)((r.Height - 14) / lh);
        var lines = new List<(string, uint)>();
        for (int i = log.Count - 1; i >= 0 && lines.Count < room; i--)
        {
            var wrapped = Wrap(log[i].Text, r.Width - 20, size);
            for (int j = wrapped.Count - 1; j >= 0 && lines.Count < room; j--) lines.Add((wrapped[j], i >= log.Count - 3 ? log[i].Colour : Sprites.Shade(log[i].Colour, 0.65)));
        }
        lines.Reverse();
        for (int i = 0; i < lines.Count; i++) Text(dc, lines[i].Item1, r.X + 10, r.Y + 7 + i * lh, size, lines[i].Item2);
    }

    private void DrawBar(DrawingContext dc)
    {
        string hint;
        var top = Engine.Top;
        if (Engine.World.Over && top is null) hint = "The game is over. Arrows look around; Enter for the final reckoning.";
        else if (top is { Kind: PanelKind.Battle }) hint = "Enter to continue (the battle plays out by itself).";
        else if (top is not null && top.Entry is not null) hint = "Type, then Enter.";
        else if (top is not null) hint = top.Options.Any(o => o.Adjust is not null) || top.Turn is not null ? "Up/Down choose, Left/Right change, Enter picks, Esc goes back" : "Up/Down to choose, Enter picks, Esc goes back";
        else if (Engine.Aiming != Aim.None) hint = "Move the cursor to where they should go, then Enter. Esc cancels.";
        else hint = "Arrows: stars  Enter: open  F: fleet  T: transports  R: research  D: designs  E: end year";
        Text(dc, Fit(hint, Bar.Width, 20), Bar.X, Bar.Y + 2, 20, Dim);
    }

    // ───────────────────────────── panels ─────────────────────────────

    /// <summary>Where a panel goes over the play screen, by its kind.</summary>
    private void DrawPanelFor(DrawingContext dc, Game.Panel p)
    {
        switch (p.Kind)
        {
            case PanelKind.Big:
                DrawPanel(dc, View.Deflate(8), p);
                return;
            case PanelKind.Wide:
                DrawPanel(dc, new Rect(40, 150, 656, 400), p);
                return;
            default:
            {
                double h = Math.Min(560, 120 + PanelHeight(p, 532));
                DrawPanel(dc, new Rect(92, 368 - h / 2, 552, h), p);
                return;
            }
        }
    }

    /// <summary>Roughly how tall a plain panel's contents are, so small messages get small boxes.</summary>
    private static double PanelHeight(Game.Panel p, double width)
    {
        int lines = p.Text.Sum(t => Wrap(t, width - 36, 22).Count) + Math.Min(p.Options.Count, 10);
        if (p.Entry is not null) lines += 2;
        if (p.Options.Any(o => o.Note is not null)) lines += 2;
        return lines * 23 + 10;
    }

    /// <summary>A panel: title, wrapped paragraphs, a typing line or the options (scrolled to keep the choice in view), and the chosen option's note.</summary>
    private void DrawPanel(DrawingContext dc, Rect r, Game.Panel p, bool frame = true)
    {
        if (frame) Box(dc, r, 0xF0141A22);
        double x = r.X + 18, width = r.Width - 36, y = r.Y + 10;
        const double size = 22, lh = 23;
        if (p.Title.Length > 0)
        {
            Text(dc, Fit(p.Title, width, 28), x, y, 28, Gold);
            y += 34;
        }
        string? note = p.Options.Count > 0 && p.Selected < p.Options.Count ? p.Options[p.Selected].Note : null;
        double noteSpace = note is null ? 0 : lh * Math.Min(3, Wrap(note, width, 20).Count) + 8;
        int optionRows = Math.Min(Math.Max(1, p.Options.Count), 8);
        double textBottom = p.Entry is not null ? r.Bottom - 48 : r.Bottom - 12 - noteSpace - lh * optionRows - 6;
        var all = p.Text.SelectMany(t => Wrap(t, width, size)).ToList();
        int fit = Math.Max(1, (int)((textBottom - y) / lh));
        foreach (var line in all.Take(fit))
        {
            uint col = line.StartsWith('>') ? Blue : Ink;
            Text(dc, line.StartsWith('>') ? line[1..].TrimStart() : line, x, y, size, col);
            y += lh;
        }
        if (p.Entry is not null)
        {
            double ey = r.Bottom - 42;
            dc.FillRectangle(Brush(0xFF0A0E14), new Rect(x - 4, ey, width + 8, 32));
            string cursor = Engine.Clock % 1 < 0.5 ? "_" : " ";
            Text(dc, $"{p.Prompt} {p.Entry}{cursor}", x + 4, ey + 2, 26, Gold);
            return;
        }
        y += 6;
        int room = Math.Max(1, (int)((r.Bottom - 10 - noteSpace - y) / lh));
        int first = Math.Clamp(p.Selected - room / 2, 0, Math.Max(0, p.Options.Count - room));
        for (int i = first; i < Math.Min(p.Options.Count, first + room); i++)
        {
            var o = p.Options[i];
            bool sel = i == p.Selected;
            if (sel) dc.FillRectangle(Brush(0xFF2A3A4E), new Rect(x - 6, y, width + 12, lh));
            bool live = o.Act is not null || o.Adjust is not null;
            uint col = !o.Enabled ? 0xFF5A6470 : sel ? Gold : live ? Ink : Dim;
            string num = !live ? "" : i < 9 ? $"{i + 1}. " : "   ";
            string label = o.Adjust is not null && sel ? $"< {o.Label} >" : o.Label;
            Text(dc, Fit(num + label, width, size), x, y, size, col);
            y += lh;
        }
        if (first > 0) Text(dc, "more above", r.Right - 18, r.Y + 12, 20, Dim, 2);
        if (first + room < p.Options.Count) Text(dc, "more below", r.Right - 18, r.Bottom - 24 - noteSpace, 20, Dim, 2);
        if (note is not null)
        {
            double ny = r.Bottom - noteSpace - 2;
            dc.DrawLine(Pen(0xFF3A4858, 1), new Point(x, ny - 4), new Point(x + width, ny - 4));
            foreach (var line in Wrap(note, width, 20).Take(3))
            {
                Text(dc, line, x, ny, 20, Blue);
                ny += lh;
            }
        }
    }
}

internal static class PanelExtensions
{
    public static T Also<T>(this T value, Action<T> act)
    {
        act(value);
        return value;
    }
}
