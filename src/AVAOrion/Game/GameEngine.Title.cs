using AVAOrion.Rules;
using AVAOrion.World;

namespace AVAOrion.Game;

public sealed partial class GameEngine
{
    public static readonly string[] LevelNames = ["Cadet", "Captain", "Commodore", "Admiral"];

    private static readonly string[] LevelNotes =
    [
        "Rivals research slowly and are slow to anger. A gentle galaxy.",
        "Rivals as quick as you.",
        "Rivals learn faster than you, and warlike ones strike sooner.",
        "Rivals learn much faster, and they will come for you. For old hands.",
    ];

    private int _race;
    private string _ruler = "";
    private int _level = 1;
    private int _rivals = 4;
    private GalaxySize _size = GalaxySize.Medium;

    public void ShowTitle()
    {
        Mode = Mode.Title;
        InDemo = false;
        Idle = 0;
        Sim = null;
        Ai = null;
        Aiming = Aim.None;
        World = new WorldState();
        CloseAll();
        _queue.Clear();
        Reports.Clear();
        Play(Sfx.MusicStop);
        Play(Sfx.TitleTune);
        var p = new Panel { Title = "AVAOrion" };
        p.Add("Begin a new game", NewGame);
        p.Add("Continue a saved game", LoadMenu, Saves.Any());
        p.Add("How to play", Help);
        p.Add("Quit", () => QuitRequested?.Invoke());
        Push(p);
    }

    private void NewGame()
    {
        var p = new Panel { Title = "Which people will you lead?", Kind = PanelKind.Big, Back = Pop };
        p.Say("Each people has a gift, a homeworld of its own kind, and a temper its rivals will know it by.");
        foreach (var race in Races.All)
        {
            var r = race;
            p.Add($"The {r.Name}, under {r.RulerName}", () =>
            {
                _race = r.Id;
                Pop();
                NameRuler();
            }, note: r.Gift);
        }
        p.Selected = _race;
        Push(p);
    }

    private void NameRuler()
    {
        var race = Races.All[_race];
        var p = new Panel { Title = $"The ruler of the {race.Name}", Entry = race.RulerName, Prompt = "Your name:", Back = () => { Pop(); NewGame(); } };
        p.Say("What will the histories call you? (Backspace to change it, Enter when it's right.)");
        p.OnEntry = name =>
        {
            if (name.Length == 0) return;
            _ruler = name;
            Pop();
            GalaxyOptions();
        };
        Push(p);
    }

    private void GalaxyOptions()
    {
        Show(() =>
        {
            var p = new Panel { Title = "The galaxy", Back = () => { Pop(); NameRuler(); } };
            p.Say("Left and Right change each choice. Then begin.");
            p.AddAdjust($"Galaxy: {_size} ({GalaxyMaker.StarCount(_size)} stars)", d => _size = (GalaxySize)Math.Clamp((int)_size + d, 0, 2));
            p.AddAdjust($"Rival peoples: {_rivals}", d => _rivals = Math.Clamp(_rivals + d, 1, 7), note: "How many other peoples share the galaxy with you.");
            p.AddAdjust($"Difficulty: {LevelNames[_level]}", d => _level = Math.Clamp(_level + d, 0, 3), note: LevelNotes[_level]);
            p.Add("Begin", () => Begin(Environment.TickCount));
            p.Selected = 3;
            return p;
        });
    }

    /// <summary>Makes the galaxy and starts the game.</summary>
    public void Begin(int seed, int? race = null, int? level = null, int? rivals = null, GalaxySize? size = null, string? ruler = null)
    {
        var w = Sim.NewWorld((ulong)(uint)seed * 7919UL + 13, race ?? _race, level ?? _level, rivals ?? _rivals, size ?? _size, ruler ?? _ruler);
        CloseAll();
        StartGame(w);
        var me = w.Human;
        w.Note($"{me.Ruler} takes the throne of the {me.Name}.");
        Tell($"The {me.Name}", $"The year {w.Year}. {me.Ruler}, the stars are open to us at last. Your scouts can explore, and your colony ship stands ready to settle a new world: " +
                              "press F to give your fleet orders. Enter opens a star (your home's spending, or what's known of anyone else's). E ends the year. Esc for the menu and \"How to play\".", kind: PanelKind.Wide);
    }

    // ───────────────────────────── help ─────────────────────────────

    public void Help()
    {
        var p = new Panel { Title = "How to play", Kind = PanelKind.Big, Back = Pop };
        p.Say("Lead your people out among the stars from 2300: explore, settle new worlds, research, design warships, and deal with the peoples you meet. Win when the Galactic Council elects you High Sovereign of the stars (two thirds of the votes, by population), or by conquering every rival.");
        p.Say("The galaxy: the arrow keys move the cursor from star to star. Enter opens a star; F gives orders to your fleet there; T sends colonists (to your own colonies, or to invade an enemy's once its orbit is clear). Tab finds your next fleet, C your next colony. E ends the year.");
        p.Say("Colonies: five sliders share out each colony's production between ships, defence (missile bases), industry (factories), ecology (cleaning waste and growing) and research. Factories need people to run them; waste left uncleaned poisons the world.");
        p.Say("Research (R): six fields studied side by side; set each field's share and choose what it studies. Ship designs (D): fit hulls with armour, shields, computers, jammers and weapons. Other peoples (P): treaties, trade, war and peace. G shows the state of the galaxy and the Council's votes.");
        p.Say("Battles play out by themselves when fleets of peoples at war meet: watch them, or press Enter to skip. A side hopelessly outgunned withdraws. Ships can only fly to stars within fuel range of your colonies.");
        p.Add("Close", Pop);
        Push(p);
    }

    // ───────────────────────────── the attract demo ─────────────────────────────

    public void StartDemo()
    {
        int seed = (int)(Clock * 1000) + 23;
        var w = Sim.NewWorld((ulong)seed, seed % Races.All.Length, 1, 4, GalaxySize.Medium);
        w.Human.Human = false;
        StartGame(w);
        CloseAll();
        InDemo = true;
        _demoTime = 0;
        _demoStep = 0.5;
    }

    private void DemoRound()
    {
        if (Sim is null || Ai is null || World.Over) return;
        foreach (var e in World.Empires) Ai.Play(e);
        Sim.EndTurn();
        Sim.Events.Clear();
        var fight = Sim.Battles.FirstOrDefault();
        if (fight is not null) Selected = World.Stars.FindIndex(s => s.Name == fight.Star);
        else if (World.Turn % 4 == 0)
        {
            var mine = World.Stars.Where(s => s.Owner >= 0).ToList();
            if (mine.Count > 0) Selected = mine[Sim.Rng.Next(mine.Count)].Id;
        }
    }

    public void EndDemo()
    {
        InDemo = false;
        ShowTitle();
    }
}
