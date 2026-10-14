using AVAOrion.Rules;
using AVAOrion.World;

namespace AVAOrion.Game;

public enum Mode { Title, Play, Over }

/// <summary>
/// The whole game, apart from drawing it: the title and a new galaxy, the turns, the player's orders
/// to colonies and fleets, research and ship design, dealings with other peoples, battles to watch,
/// and the computer rulers giving their orders in between. Keys arrive by name ("Up", "Enter", "F"),
/// so the tests and the attract demo play exactly as a person does.
/// </summary>
public sealed partial class GameEngine
{
    public const double DemoAfter = 30;
    public const double DemoLength = 75;

    public Mode Mode { get; private set; } = Mode.Title;
    public WorldState World { get; private set; } = new();
    public Sim? Sim { get; private set; }
    public Ai? Ai { get; private set; }
    public Rng Rng { get; private set; }
    public SaveStore Saves { get; }
    public List<Panel> Panels { get; } = [];
    public Panel? Top => Panels.Count > 0 ? Panels[^1] : null;
    public double Clock { get; private set; }
    public double Idle { get; private set; }
    public bool InDemo { get; private set; }
    private double _demoTime, _demoStep;
    public event Action<Sfx>? Sound;
    public event Action? QuitRequested;

    /// <summary>The news log: the simulation's notes for the player.</summary>
    public IReadOnlyList<(string Text, uint Colour)> Log => Sim?.Notes ?? (IReadOnlyList<(string, uint)>)[];

    public GameEngine(int seed, SaveStore? saves = null)
    {
        Rng = new Rng((ulong)seed * 2654435761UL + 1);
        Saves = saves ?? new SaveStore();
        ShowTitle();
    }

    public void Play(Sfx s) => Sound?.Invoke(s);

    public void Say(string text, uint colour = 0xFFE8E0D0) => Sim?.Notes.Add((text, colour));

    // ───────────────────────────── the clock ─────────────────────────────

    public void Update(double dt)
    {
        Clock += dt;
        if (InDemo)
        {
            _demoTime += dt;
            _demoStep -= dt;
            if (_demoStep <= 0)
            {
                _demoStep = 0.35;
                DemoRound();
            }
            if (_demoTime > DemoLength || World.Over) EndDemo();
            return;
        }
        if (Mode == Mode.Title && Panels.Count == 1)
        {
            Idle += dt;
            if (Idle > DemoAfter) StartDemo();
        }
        BattleSounds();
    }

    // ───────────────────────────── panels ─────────────────────────────

    public Panel Push(Panel p)
    {
        Panels.Add(p);
        return p;
    }

    public Panel Show(Func<Panel> make)
    {
        var p = make();
        p.Make = make;
        return Push(p);
    }

    public void Pop()
    {
        if (Panels.Count > 0) Panels.RemoveAt(Panels.Count - 1);
        Refresh();
        if (Panels.Count == 0) AfterPanels();
    }

    public void CloseAll() => Panels.Clear();

    public void Refresh()
    {
        if (Top is not { Make: { } make } old) return;
        var p = make();
        p.Make = make;
        p.Selected = Math.Clamp(old.Selected, 0, Math.Max(0, p.Options.Count - 1));
        Panels[^1] = p;
    }

    /// <summary>A message with a Continue button.</summary>
    public void Tell(string title, string text, Action? then = null, PanelKind kind = PanelKind.Plain)
    {
        var p = new Panel { Title = title, Kind = kind }.Say(text);
        p.Add("Continue", () => { Pop(); then?.Invoke(); });
        p.Back = () => { Pop(); then?.Invoke(); };
        Push(p);
    }

    // ───────────────────────────── keys ─────────────────────────────

    /// <summary>A key goes down.</summary>
    public void Press(string key)
    {
        if (InDemo)
        {
            EndDemo();
            return;
        }
        Input(key);
    }

    internal void Input(string key)
    {
        Idle = 0;
        if (Top is { } p)
        {
            PanelKey(p, key);
            return;
        }
        if (Mode is Mode.Play or Mode.Over) PlayKey(key);
    }

    /// <summary>Typed text, for names.</summary>
    public void Type(string text)
    {
        if (Top is not { Entry: not null } p) return;
        foreach (char ch in text)
            if ((char.IsLetterOrDigit(ch) || ch is ' ' or '\'' or '-' or '.') && p.Entry!.Length < 20)
                p.Entry += ch;
    }

    private void PanelKey(Panel p, string key)
    {
        if (p.Entry is not null)
        {
            switch (key)
            {
                case "Enter":
                    p.OnEntry?.Invoke(p.Entry.Trim());
                    return;
                case "Back":
                    if (p.Entry.Length > 0) p.Entry = p.Entry[..^1];
                    return;
                case "Esc":
                    p.Back?.Invoke();
                    return;
            }
            return;
        }
        var opts = p.Options;
        switch (key)
        {
            case "Up":
                if (opts.Count > 0) { p.Selected = (p.Selected + opts.Count - 1) % opts.Count; Play(Sfx.Menu); }
                return;
            case "Down":
                if (opts.Count > 0) { p.Selected = (p.Selected + 1) % opts.Count; Play(Sfx.Menu); }
                return;
            case "PageUp":
                if (opts.Count > 0) { p.Selected = Math.Max(0, p.Selected - 8); Play(Sfx.Menu); }
                return;
            case "PageDown":
                if (opts.Count > 0) { p.Selected = Math.Min(opts.Count - 1, p.Selected + 8); Play(Sfx.Menu); }
                return;
            case "Left" or "Right":
            {
                int d = key == "Left" ? -1 : 1;
                if (opts.Count > 0 && opts[p.Selected].Adjust is { } adj) { adj(d); Play(Sfx.Menu); Refresh(); }
                else if (p.Turn is { } turn) { turn(d); Play(Sfx.Menu); }
                return;
            }
            case "Enter" or "Space":
                Choose(p, p.Selected);
                return;
            case "Esc" or "Back":
                if (p.Back is { } back) { back(); Play(Sfx.Menu); }
                return;
        }
        if (key.Length == 1 && char.IsDigit(key[0]) && key[0] != '0')
        {
            int n = key[0] - '1';
            if (n < opts.Count) { p.Selected = n; Choose(p, n); }
        }
    }

    private void Choose(Panel p, int index)
    {
        if (index < 0 || index >= p.Options.Count) return;
        var o = p.Options[index];
        if (!o.Enabled || o.Act is null) return;
        Play(Sfx.Pick);
        o.Act();
        if (Top == p) Refresh();
    }
}
