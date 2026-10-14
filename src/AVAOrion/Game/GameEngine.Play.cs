using AVAOrion.Rules;
using AVAOrion.World;

namespace AVAOrion.Game;

/// <summary>What the star cursor is choosing a destination for.</summary>
public enum Aim { None, Fleet, Transports }

public sealed partial class GameEngine
{
    /// <summary>The star the cursor is on.</summary>
    public int Selected { get; private set; }
    /// <summary>When set, the cursor is choosing where something should go, from <see cref="AimFrom"/>.</summary>
    public Aim Aiming { get; private set; }
    public int AimFrom { get; private set; } = -1;
    private Fleet? _aimFleet;
    private Dictionary<int, int> _aimShips = [];
    private int _aimTroops;
    private readonly List<Action> _queue = [];
    /// <summary>The fights to show, and when the one on screen began.</summary>
    public List<BattleReport> Reports { get; } = [];
    public double BattleStarted { get; private set; }
    private Sfx _music = Sfx.MusicStop;
    private readonly List<string> _found = [];

    public Empire Me => World.Human;
    public Star Here => World.Stars[Selected];

    // ───────────────────────────── starting and ending turns ─────────────────────────────

    public void StartGame(WorldState w, bool fresh = true)
    {
        World = w;
        Sim = new Sim(w, new Rng(w.Seed == 0 ? 1 : w.Seed));
        Sim.Sound += Play;
        Ai = new Ai(Sim);
        Mode = Mode.Play;
        CloseAll();
        _queue.Clear();
        Reports.Clear();
        Aiming = Aim.None;
        Play(Sfx.MusicStop);
        _music = Sfx.MusicStop;
        Music();
        Selected = w.Stars.FirstOrDefault(s => s.Owner == w.Player && s.Capital)?.Id ?? w.Stars.FirstOrDefault(s => s.Owner == w.Player)?.Id ?? 0;
        _ = fresh;
        AfterAction();
    }

    /// <summary>The player's orders are given: if the Council sits this year, the player votes first.</summary>
    public void EndTurn()
    {
        if (Sim is null || World.Over) return;
        CloseAll();
        Aiming = Aim.None;
        if (Sim.CouncilDue && Me.Alive && Me.Human && !InDemo)
        {
            CouncilVote();
            return;
        }
        Advance(-1);
    }

    private void Advance(int vote)
    {
        var sim = Sim!;
        foreach (var e in World.Empires.Where(e => e.Id != World.Player)) Ai!.Play(e);
        sim.EndTurn(vote);
        // The player's fights first, then what they saw.
        foreach (var r in sim.Battles.OrderByDescending(r => r.Empires.Contains(World.Player)))
        {
            if (!r.Empires.Contains(World.Player) && Reports.Count >= 2) continue;
            var report = r;
            _queue.Add(() => ShowBattle(report));
        }
        Play(Sfx.Turn);
        Music();
        AfterAction();
    }

    private void Music()
    {
        if (Sim is null) return;
        var tune = World.Empires.Any(e => e.Alive && !e.Monster && Sim.AtWar(World.Player, e.Id)) ? Sfx.WarMusic : Sfx.SpaceMusic;
        if (tune == _music) return;
        _music = tune;
        Play(tune);
    }

    /// <summary>After anything the player does: news becomes panels, and the game may end.</summary>
    private void AfterAction()
    {
        if (Sim is null) return;
        foreach (var e in Sim.Events) Queue(e);
        Sim.Events.Clear();
        if (_found.Count > 0)
        {
            // All of a turn's discoveries on one page.
            var found = _found.ToList();
            _found.Clear();
            _queue.Insert(0, () =>
            {
                var p = new Panel { Title = found.Count == 1 ? "A discovery" : $"{found.Count} discoveries", Kind = found.Count > 2 ? PanelKind.Wide : PanelKind.Plain };
                foreach (var f in found) p.Say(f);
                p.Add("Continue", Pop);
                p.Back = Pop;
                Push(p);
            });
        }
        if (World.Over && Mode == Mode.Play && !InDemo)
        {
            _queue.RemoveAll(_ => true);
            GameOver();
            return;
        }
        if (Panels.Count == 0) AfterPanels();
    }

    private void AfterPanels()
    {
        if (Mode != Mode.Play || InDemo || Panels.Count > 0) return;
        if (_queue.Count > 0)
        {
            var next = _queue[0];
            _queue.RemoveAt(0);
            next();
            return;
        }
        if (_battleMusic)
        {
            _battleMusic = false;
            _music = Sfx.MusicStop;
            Music();
        }
    }

    private bool _battleMusic;

    private void Queue(Event e)
    {
        switch (e.Kind)
        {
            case Happen.Tech:
                _found.Add(e.Text);
                break;
            case Happen.Contact:
                _queue.Add(() => Tell("First contact", e.Text));
                break;
            case Happen.War:
                _queue.Add(() => Tell("War!", e.Text));
                break;
            case Happen.Demand:
                _queue.Add(() => DemandPanel(e));
                break;
            case Happen.Offer:
                _queue.Add(() => OfferPanel(e));
                break;
            case Happen.Colonised or Happen.ColonyLost or Happen.ColonyTaken or Happen.Fallen or Happen.Event or Happen.Vault or Happen.Battle:
            {
                string title = e.Kind switch
                {
                    Happen.Colonised => "A new colony",
                    Happen.ColonyLost => "A colony lost",
                    Happen.ColonyTaken => "Victory",
                    Happen.Fallen => "The end of a people",
                    Happen.Vault => "The Vault of Aeon",
                    Happen.Battle => "Invasion",
                    _ => "News from the stars",
                };
                _queue.Add(() =>
                {
                    if (e.Star >= 0) Selected = e.Star;
                    Tell(title, e.Text);
                });
                break;
            }
            case Happen.CouncilResult:
                _queue.Add(() => CouncilResult(e));
                break;
        }
    }

    private void GameOver()
    {
        Mode = Mode.Over;
        CloseAll();
        Play(Sfx.MusicStop);
        Play(World.Winner == World.Player ? Sfx.Victory : Sfx.Defeat);
        ShowOver();
    }

    public void ShowOver()
    {
        var p = new Panel { Title = World.Winner == World.Player ? "Triumph" : "The end of the game", Kind = PanelKind.Wide };
        p.Say(World.Outcome);
        foreach (var e in World.Empires.Where(e => !e.Monster && (e.Alive || e.Id == World.Player)).OrderByDescending(e => Sim!.Score(e)))
            p.Say($"> The {e.Name}{(e.Id == World.Player ? " (you)" : "")}: {Sim!.Score(e)} points{(e.Alive ? "" : ", fallen")}.");
        p.Add("Look over the galaxy", () => CloseAll());
        p.Add("Back to the title", ShowTitle);
        Push(p);
    }

    // ───────────────────────────── battles ─────────────────────────────

    public void ShowBattle(BattleReport r)
    {
        Reports.Add(r);
        BattleStarted = Clock;
        if (!_battleMusic)
        {
            _battleMusic = true;
            Play(Sfx.BattleMusic);
        }
        var p = new Panel { Title = $"Battle at {r.Star}", Kind = PanelKind.Battle, Tag = Reports.Count - 1, Back = Pop };
        p.Add("Continue", Pop);
        Push(p);
    }

    /// <summary>Seconds each round of a battle takes to play out on screen.</summary>
    public const double RoundTime = 0.9;

    private int _soundedRound;

    /// <summary>While a battle plays out, each round sounds its guns, and kills sound their explosions.</summary>
    private void BattleSounds()
    {
        if (Top is not { Kind: PanelKind.Battle } p || p.Tag >= Reports.Count) return;
        var r = Reports[p.Tag];
        int round = 1 + (int)((Clock - BattleStarted) / RoundTime);
        if (Clock - BattleStarted < 0.05) _soundedRound = 0;
        if (round <= _soundedRound || round > r.Rounds) return;
        _soundedRound = round;
        var volleys = r.Volleys.Where(v => v.Round == round).ToList();
        if (volleys.Any(v => !v.Missile)) Play(Sfx.Laser);
        if (volleys.Any(v => v.Missile)) Play(Sfx.Missile);
        if (volleys.Any(v => v.Kills > 0)) Play(Sfx.Explosion);
        else if (volleys.Any(v => v.Hits > 0)) Play(Sfx.Shield);
    }

    /// <summary>The line under a battle, once it has played out.</summary>
    public string Verdict(BattleReport r)
    {
        int me = Array.IndexOf(r.Empires, World.Player);
        string Name(int side) => World.Empires[r.Empires[side]].Name;
        if (r.Retreated[0] || r.Retreated[1])
        {
            int fled = r.Retreated[0] ? 0 : 1;
            return me < 0 ? $"The {Name(fled)} withdrew." : fled == me ? "Our ships withdrew." : $"The {Name(fled)} withdrew.";
        }
        if (r.Winner < 0) return "Neither side could finish the other.";
        string pop = r.PopLost > 0 ? $" {r.PopLost:0} of the colony's people died in the bombardment." : "";
        if (me < 0) return $"The {Name(r.Winner)} won.{pop}";
        return (r.Winner == me ? "Victory!" : "Defeat.") + pop;
    }

    // ───────────────────────────── the cursor ─────────────────────────────

    private static (double Dx, double Dy)? Direction(string key) => key switch
    {
        "Up" or "8" => (0, -1),
        "Down" or "2" => (0, 1),
        "Left" or "4" => (-1, 0),
        "Right" or "6" => (1, 0),
        "Home" or "7" => (-0.7, -0.7),
        "PageUp" or "9" => (0.7, -0.7),
        "End" or "1" => (-0.7, 0.7),
        "PageDown" or "3" => (0.7, 0.7),
        _ => null,
    };

    /// <summary>Moves the cursor to the nearest star roughly in a direction.</summary>
    public void Step(double dx, double dy)
    {
        var from = Here;
        Star? best = null;
        double bestScore = double.MaxValue;
        foreach (var s in World.Stars)
        {
            if (s == from) continue;
            double vx = s.X - from.X, vy = s.Y - from.Y, d = Math.Sqrt(vx * vx + vy * vy);
            double cos = (vx * dx + vy * dy) / (d * Math.Sqrt(dx * dx + dy * dy));
            if (cos < 0.45) continue;
            double score = d * (2.2 - cos);
            if (score < bestScore)
            {
                bestScore = score;
                best = s;
            }
        }
        if (best is not null)
        {
            Selected = best.Id;
            Play(Sfx.Menu);
        }
    }

    public void Select(int star) => Selected = Math.Clamp(star, 0, World.Stars.Count - 1);

    // ───────────────────────────── the player's keys ─────────────────────────────

    private void PlayKey(string key)
    {
        if (Sim is null) return;
        if (Direction(key) is { } d)
        {
            Step(d.Dx, d.Dy);
            return;
        }
        if (World.Over)
        {
            if (key is "Esc" or "Enter") ShowOver();
            return;
        }
        if (Aiming != Aim.None)
        {
            if (key == "Esc") { Aiming = Aim.None; Selected = AimFrom; return; }
            if (key is "Enter" or "Space") Confirm();
            return;
        }
        switch (key)
        {
            case "Enter": OpenStar(Here); break;
            case "Esc": GameMenu(); break;
            case "E": EndTurn(); break;
            case "F": FleetPanel(Here); break;
            case "T": TransportPanel(Here); break;
            case "Tab": NextFleet(); break;
            case "C": NextColony(); break;
            case "R": ResearchMenu(); break;
            case "D": DesignsMenu(); break;
            case "P": PeoplesMenu(); break;
            case "L": ColonyList(); break;
            case "G": GalaxyReport(); break;
            case "B":
                if (Reports.Count > 0) ShowBattle(Reports[^1]);
                break;
        }
        AfterAction();
    }

    private void NextFleet()
    {
        var stars = World.Fleets.Where(f => f.Owner == World.Player && f.At >= 0 && !f.IsTransport).Select(f => f.At).Distinct().OrderBy(i => i).ToList();
        if (stars.Count == 0) { Say("You have no fleets waiting at a star.", 0xFFE0C040); return; }
        Selected = stars.FirstOrDefault(i => i > Selected, stars[0]);
    }

    private void NextColony()
    {
        var stars = World.Stars.Where(s => s.Owner == World.Player).Select(s => s.Id).ToList();
        if (stars.Count == 0) return;
        Selected = stars.FirstOrDefault(i => i > Selected, stars[0]);
    }

    // ───────────────────────────── sending things ─────────────────────────────

    private void BeginAim(Aim aim, Star from)
    {
        Aiming = aim;
        AimFrom = from.Id;
        CloseAll();
    }

    /// <summary>How long the thing being aimed would take to reach the star under the cursor (0 if it can't).</summary>
    public (int Turns, bool InRange) AimInfo()
    {
        var sim = Sim!;
        var to = Here;
        if (Aiming == Aim.Fleet && _aimFleet is not null)
        {
            bool ok = sim.CanSend(_aimFleet, to, out _);
            return (sim.TravelTurns(_aimFleet, to), ok);
        }
        if (Aiming == Aim.Transports)
        {
            var probe = new Fleet { Owner = World.Player, Troops = 1, X = World.Stars[AimFrom].X, Y = World.Stars[AimFrom].Y };
            return (sim.TravelTurns(probe, to), sim.InRange(World.Player, to));
        }
        return (0, false);
    }

    private void Confirm()
    {
        var sim = Sim!;
        var to = Here;
        if (Aiming == Aim.Fleet && _aimFleet is not null)
        {
            if (!sim.CanSend(_aimFleet, to, out string why))
            {
                Say(why, 0xFFE0A040);
                Play(Sfx.Bump);
                return;
            }
            var go = sim.Send(_aimFleet, to, _aimShips);
            if (go is not null)
            {
                Say($"{go.Count} ship{(go.Count == 1 ? "" : "s")} bound for {to.Name}: {go.Eta} year{(go.Eta == 1 ? "" : "s")}.", 0xFFC8D8F0);
                Play(Sfx.Warp);
            }
        }
        else if (Aiming == Aim.Transports)
        {
            var from = World.Stars[AimFrom];
            var f = sim.SendTransports(from, to, _aimTroops, out string why);
            if (f is null)
            {
                Say(why, 0xFFE0A040);
                Play(Sfx.Bump);
                return;
            }
            Say($"{_aimTroops} colonists embark at {from.Name} for {to.Name}: {f.Eta} year{(f.Eta == 1 ? "" : "s")}.", 0xFFC8D8F0);
        }
        Aiming = Aim.None;
        Selected = AimFrom;
    }

    /// <summary>The fleet panel: the player's ships at a star, how many of each to send, and where.</summary>
    public void FleetPanel(Star star)
    {
        var sim = Sim!;
        var fleet = World.Fleets.FirstOrDefault(f => f.Owner == World.Player && f.At == star.Id && !f.IsTransport);
        if (fleet is null)
        {
            var coming = World.Fleets.Where(f => f.Owner == World.Player && f.Dest == star.Id).ToList();
            Say(coming.Count > 0 ? $"{coming.Sum(f => Math.Max(f.Count, f.Troops > 0 ? 1 : 0))} of your ships or transports are bound for {star.Name}." : $"You have no ships at {star.Name}.", 0xFFE0C040);
            return;
        }
        _aimShips = fleet.Ships.ToDictionary(s => s.Design, s => s.Count);
        Show(() =>
        {
            var p = new Panel { Title = $"Your fleet at {star.Name}", Back = Pop };
            if (!World.Fleets.Contains(fleet) || fleet.At != star.Id) { p.Add("Close", Pop); return p; }
            foreach (var st in fleet.Ships.ToList())
            {
                var design = Me.Design(st.Design)!;
                int id = st.Design;
                int n = _aimShips.GetValueOrDefault(id);
                p.AddAdjust($"{design.Name}: send {n} of {st.Count}", d => _aimShips[id] = Math.Clamp(n + d, 0, st.Count), note: design.Describe());
            }
            p.Add("Choose where they go", () =>
            {
                if (_aimShips.Values.Sum() == 0) { Say("Choose at least one ship to send.", 0xFFE0A040); return; }
                _aimFleet = fleet;
                BeginAim(Aim.Fleet, star);
            }, note: "Then move the cursor to a star and press Enter (Esc to cancel).");
            if (sim.CanColonise(fleet, out _))
                p.Add($"Found a colony on {star.Name}", () =>
                {
                    CloseAll();
                    sim.Colonise(fleet);
                    AfterAction();
                }, note: $"{Planets.SizeNames[(int)star.Size]} {star.Kind.Name.ToLowerInvariant()} world: room for {sim.MaxPop(star, Me):0} people.");
            p.Add("Close", Pop);
            return p;
        });
    }

    public void TransportPanel(Star star)
    {
        if (star.Owner != World.Player)
        {
            Say("Transports leave from your own colonies.", 0xFFE0C040);
            return;
        }
        int spare = Math.Max(0, (int)star.Pop - 1);
        if (spare < 1) { Say($"{star.Name} has no one to spare.", 0xFFE0C040); return; }
        _aimTroops = Math.Max(1, spare / 2);
        Show(() =>
        {
            var p = new Panel { Title = $"Transports from {star.Name}", Back = Pop };
            p.Say("Colonists can swell another of your colonies, or land on an enemy's to take it. Enemy bases and warships shoot transports down: clear them first.");
            p.AddAdjust($"Colonists: {_aimTroops} of {spare}", d => _aimTroops = Math.Clamp(_aimTroops + d * Math.Max(1, spare / 10), 1, spare));
            p.Add("Choose where they go", () => BeginAim(Aim.Transports, star), note: "Then move the cursor to a star and press Enter.");
            p.Add("Close", Pop);
            return p;
        });
    }
}
