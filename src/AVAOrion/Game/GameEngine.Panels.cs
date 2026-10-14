using AVAOrion.Rules;
using AVAOrion.World;

namespace AVAOrion.Game;

public sealed partial class GameEngine
{
    public static string Years(double need, double rate) =>
        rate <= 0.01 ? "never" : need <= 0 ? "next year" : $"{Math.Ceiling(need / rate):0} year{(Math.Ceiling(need / rate) == 1 ? "" : "s")}";

    // ───────────────────────────── the menu ─────────────────────────────

    public void GameMenu()
    {
        var p = new Panel { Title = "Menu", Back = Pop };
        p.Add("Back to the galaxy", Pop);
        p.Add("Colonies (L)", ColonyList);
        p.Add("Research (R)", ResearchMenu);
        p.Add("Ship designs (D)", DesignsMenu);
        p.Add("Other peoples (P)", PeoplesMenu);
        p.Add("The state of the galaxy (G)", GalaxyReport);
        p.Add("The chronicle", JournalPanel);
        p.Add("Save the game", SaveMenu, !InDemo);
        p.Add("Load a game", LoadMenu, Saves.Any());
        p.Add("How to play", Help);
        p.Add("Retire now and see the score", RetireMenu);
        p.Add("Quit to the title", ShowTitle);
        Push(p);
    }

    // ───────────────────────────── a star ─────────────────────────────

    /// <summary>Enter on a star: your own colony's screen, or what is known of anyone else's star.</summary>
    public void OpenStar(Star s)
    {
        if (s.Owner == World.Player)
        {
            ColonyScreen(s);
            return;
        }
        var sim = Sim!;
        var p = new Panel { Title = s.Name, Kind = PanelKind.Colony, Tag = s.Id, Back = Pop };
        var mine = World.Fleets.FirstOrDefault(f => f.Owner == World.Player && f.At == s.Id && !f.IsTransport);
        if (mine is not null && sim.CanColonise(mine, out _))
            p.Add($"Found a colony on {s.Name}", () => { CloseAll(); sim.Colonise(mine); AfterAction(); });
        if (mine is not null) p.Add("Your fleet here (F)", () => { Pop(); FleetPanel(s); });
        p.Add("Close", Pop);
        Push(p);
    }

    public static string SpendWord(Spend s) => s switch { Spend.Ships => "Ships", Spend.Defense => "Defence", Spend.Industry => "Industry", Spend.Ecology => "Ecology", _ => "Research" };

    /// <summary>A colony: its people and factories, and its five spending sliders.</summary>
    public void ColonyScreen(Star s)
    {
        var sim = Sim!;
        Show(() =>
        {
            var p = new Panel { Title = s.Name, Kind = PanelKind.Colony, Tag = s.Id, Back = Pop };
            if (s.Owner != World.Player)
            {
                p.Add("Close", Pop);
                return p;
            }
            var r = sim.Report(s);
            foreach (var spend in Enum.GetValues<Spend>())
            {
                var which = spend;
                p.AddAdjust($"{SpendWord(spend)}: {s.Slider(spend)}%", d => Sim.SetSlider(s, which, s.Slider(which) + d * 5), note: SliderNote(s, spend, r));
            }
            var designs = Me.Active.ToList();
            var current = Me.Design(s.Building);
            p.AddAdjust($"Shipyard builds: {(current is null || current.Retired ? "nothing" : current.Name)}", d =>
            {
                if (designs.Count == 0) return;
                int i = designs.FindIndex(x => x.Id == s.Building);
                s.Building = designs[((i < 0 ? 0 : i + d) % designs.Count + designs.Count) % designs.Count].Id;
            }, note: current is null ? "Left and Right choose a design." : $"{current.Describe()} Costs {current.Cost} BC.");
            p.Add("Even out the ecology (just enough to clean up)", () => Sim.SetSlider(s, Spend.Ecology, sim.EcoNeed(s)),
                note: "Sets the ecology share to what cleaning up this colony's waste needs, plus a little for growth while it's young.");
            double hurry = current is null || current.Retired ? 0 : Math.Min(Me.Reserve, current.Cost - s.ShipBC);
            p.Add(hurry >= 1 ? $"Hurry the shipyard with {hurry:0} BC from the reserve" : "Hurry the shipyard (nothing to hurry, or no reserve)",
                () => { sim.Hurry(s); Say($"{s.Name}'s shipyard is hurried along.", 0xFFFFD860); }, hurry >= 1,
                $"The reserve holds {Me.Reserve:0} BC, from industry with nowhere to go and from trade.");
            p.Add("Send transports (T)", () => TransportPanel(s));
            p.Add("Close", Pop);
            p.Turn = d =>
            {
                var mine = World.Stars.Where(x => x.Owner == World.Player).ToList();
                if (mine.Count < 2) return;
                var next = mine[(mine.IndexOf(s) + d + mine.Count) % mine.Count];
                Selected = next.Id;
                Pop();
                ColonyScreen(next);
            };
            return p;
        });
    }

    /// <summary>What a slider's share is buying, in words.</summary>
    public string SliderNote(Star s, Spend spend, ColonyReport r)
    {
        var sim = Sim!;
        switch (spend)
        {
            case Spend.Ships:
                var d = Me.Design(s.Building);
                if (d is null || d.Retired) return $"{r.Ships:0} BC a year, with nothing to build: half goes to the reserve.";
                return $"{r.Ships:0} BC a year towards a {d.Name} ({d.Cost} BC): {(r.Ships * 1 >= d.Cost ? $"{Math.Floor(r.Ships / d.Cost):0} a year" : Years(d.Cost - s.ShipBC, r.Ships))}.";
            case Spend.Defense:
                return $"{r.Defense:0} BC a year: a missile base every {Years(Sim.BaseCost, r.Defense)}. {s.Bases} base{(s.Bases == 1 ? "" : "s")} now.";
            case Spend.Industry:
                return s.Factories >= r.MaxFactories - 0.5
                    ? $"Every factory the people can run is built ({r.MaxFactories:0}). Industry spending goes to the reserve at half value."
                    : $"{r.Industry:0} BC a year: {r.Industry / Sim.FactoryCost(Me):0.0} factories a year at {Sim.FactoryCost(Me):0} BC each.";
            case Spend.Ecology:
                return r.Ecology >= r.Cleanup
                    ? $"{r.Ecology:0} BC a year: {r.Cleanup:0.0} cleans the waste, the rest grows the colony."
                    : $"Only {r.Ecology:0} BC for {r.Cleanup:0.0} of waste: the planet is being poisoned, and will hold fewer people.";
            default:
                return $"{r.Research:0} BC a year: {r.RP:0} research points{(s.Artifacts ? " (doubled by the ruins of an old people)" : "")}.";
        }
    }

    public void ColonyList()
    {
        var sim = Sim!;
        Show(() =>
        {
            var p = new Panel { Title = "Your colonies", Kind = PanelKind.Big, Back = Pop };
            var mine = World.Stars.Where(s => s.Owner == World.Player).OrderByDescending(s => s.Pop).ToList();
            if (mine.Count == 0) p.Say("You have no colonies.");
            foreach (var s in mine)
            {
                var star = s;
                var r = sim.Report(s);
                var d = Me.Design(s.Building);
                p.Add($"{s.Name}: {s.Pop:0}/{r.MaxPop:0} people, {r.Gross:0} BC, {(d is null ? "no shipyard" : d.Name)}", () => { Selected = star.Id; ColonyScreen(star); },
                    note: $"{s.Factories:0} factories, {s.Bases} bases. Ships {s.Slider(Spend.Ships)}%, defence {s.Slider(Spend.Defense)}%, industry {s.Slider(Spend.Industry)}%, ecology {s.Slider(Spend.Ecology)}%, research {s.Slider(Spend.Research)}%.");
            }
            p.Add("Close", Pop);
            return p;
        });
    }

    // ───────────────────────────── research ─────────────────────────────

    public void ResearchMenu()
    {
        var sim = Sim!;
        Show(() =>
        {
            var p = new Panel { Title = "Research", Kind = PanelKind.Big, Back = Pop };
            double rp = World.Stars.Where(s => s.Owner == World.Player).Sum(s => sim.Report(s).RP);
            p.Say($"Your colonies make {rp:0} research points a year, shared between the six fields. Left and Right change a field's share; Enter chooses what it studies.");
            for (int f = 0; f < 6; f++)
            {
                int field = f;
                var target = Me.Target[f] >= 0 ? Techs.All[Me.Target[f]] : null;
                if (target is null || Me.Known[target.Id])
                {
                    Me.Target[f] = sim.PickTech(Me, (Field)f);
                    target = Me.Target[f] >= 0 ? Techs.All[Me.Target[f]] : null;
                }
                string study = target is null ? "nothing left" : $"{target.Name} ({Me.Progress[f]:0}/{sim.TechCost(Me, target)}, {Years(sim.TechCost(Me, target) - Me.Progress[f], rp * Me.Alloc[f] / 100)})";
                p.Options.Add(new Opt($"{Techs.FieldNames[f]} {Me.Alloc[f]}%: {study}", () => ChooseTech((Field)field), d => Allot(field, d * 5),
                    Note: target?.About ?? "Everything in this field is known."));
            }
            p.Add("Close", Pop);
            return p;
        });
    }

    /// <summary>Moves research share into (or out of) one field, from (or to) the others in proportion.</summary>
    private void Allot(int field, int delta)
    {
        var a = Me.Alloc;
        delta = Math.Clamp(delta, -a[field], 100 - a[field]);
        a[field] += delta;
        int left = -delta;
        for (int guard = 0; guard < 200 && left != 0; guard++)
        {
            int step = Math.Sign(left);
            var pick = Enumerable.Range(0, 6).Where(i => i != field && (step > 0 || a[i] > 0)).OrderBy(i => step > 0 ? a[i] : -a[i]).FirstOrDefault(-1);
            if (pick < 0) break;
            a[pick] += step;
            left -= step;
        }
    }

    private void ChooseTech(Field f)
    {
        var sim = Sim!;
        var p = new Panel { Title = $"{Techs.FieldNames[(int)f]}: what next?", Kind = PanelKind.Big, Back = Pop };
        var options = Sim.Available(Me, f);
        p.Say($"Your best in this field is level {Sim.FieldLevel(Me, f)}. Anything up to six levels beyond can be studied; the higher, the dearer.");
        foreach (var t in options)
        {
            var tech = t;
            p.Add($"{t.Name} (level {t.Level}, {sim.TechCost(Me, t)} RP){(Me.Target[(int)f] == t.Id ? " - now" : "")}", () =>
            {
                Me.Target[(int)f] = tech.Id;
                Pop();
            }, note: t.About);
        }
        if (options.Count == 0) p.Say("Nothing is left to learn here.");
        p.Add("Close", Pop);
        p.Selected = Math.Max(0, options.FindIndex(t => t.Id == Me.Target[(int)f]));
        Push(p);
    }

    // ───────────────────────────── other peoples ─────────────────────────────

    public static string Mood(int attitude) => attitude switch { >= 40 => "friendly", >= 10 => "cordial", >= -10 => "wary", >= -40 => "cold", _ => "hostile" };

    public void PeoplesMenu()
    {
        Show(() =>
        {
            var p = new Panel { Title = "Other peoples", Kind = PanelKind.Big, Back = Pop };
            var met = World.Empires.Where(e => e.Id != World.Player && !e.Monster && e.Alive && Sim!.Met(World.Player, e.Id)).ToList();
            if (met.Count == 0) p.Say("Your ships have met no other people yet.");
            foreach (var e in met)
            {
                var them = e;
                string terms = Sim!.AtWar(World.Player, e.Id) ? "at war" : e.Treaties[World.Player] == Treaty.Alliance ? "allied" : e.Treaties[World.Player] == Treaty.NonAggression ? "pact" : "at peace";
                p.Add($"The {e.Name}: {terms}{(e.Trade[World.Player] ? ", trading" : "")}, {Mood(e.Attitude[World.Player])}", () => Parley(them),
                    note: $"{e.Ruler}. {e.Kind.Nature} {World.Stars.Count(s => s.Owner == e.Id)} colonies, {e.TechCount} discoveries.");
            }
            p.Add("Close", Pop);
            return p;
        });
    }

    private void Parley(Empire them)
    {
        var sim = Sim!;
        var ai = Ai!;
        int me = World.Player;
        Show(() =>
        {
            var p = new Panel { Title = $"{them.Ruler} of the {them.Name}", Back = Pop };
            bool war = sim.AtWar(me, them.Id);
            p.Say($"{them.Kind.Nature} {them.Kind.Gift} They are {Mood(them.Attitude[me])} towards you.");
            void Answer(bool yes, string ok, string no) => Tell(yes ? "Agreed" : "Refused", yes ? ok : no);
            if (war)
                p.Add("Propose peace", () =>
                {
                    bool yes = ai.WouldMakePeace(them, me);
                    if (yes) sim.MakePeace(me, them.Id);
                    else them.Cooldown[me] = Math.Max(them.Cooldown[me], 3);
                    Answer(yes, $"{them.Ruler} accepts. The war is over.", $"\"Not while our fleets have the upper hand,\" says {them.Ruler}.");
                });
            else
            {
                if (them.Treaties[me] == Treaty.None)
                    p.Add("Propose a non-aggression pact", () =>
                    {
                        bool yes = ai.WouldSign(them, me, Treaty.NonAggression);
                        if (yes) sim.Sign(me, them.Id, Treaty.NonAggression);
                        Answer(yes, $"{them.Ruler} signs the pact.", $"\"We do not trust you yet,\" says {them.Ruler}.");
                    });
                if (them.Treaties[me] != Treaty.Alliance)
                    p.Add("Propose an alliance", () =>
                    {
                        bool yes = ai.WouldSign(them, me, Treaty.Alliance);
                        if (yes) sim.Sign(me, them.Id, Treaty.Alliance);
                        Answer(yes, $"{them.Ruler} clasps your hand: allies.", $"\"An alliance must be earned,\" says {them.Ruler}.");
                    });
                if (!them.Trade[me])
                    p.Add("Propose a trade agreement", () =>
                    {
                        bool yes = ai.WouldSign(them, me, Treaty.None, trade: true);
                        if (yes) sim.SignTrade(me, them.Id);
                        Answer(yes, "The merchants of both peoples will grow rich.", $"\"We have no need of your goods,\" says {them.Ruler}.");
                    });
                p.Add("Exchange discoveries", () => Exchange(them));
                p.Add("Declare war", () =>
                {
                    var c = new Panel { Title = "War?", Back = Pop };
                    c.Say(them.Treaties[me] != Treaty.None ? $"You have a treaty with the {them.Name}. Breaking it will make every people trust you less." : $"Go to war with the {them.Name}?");
                    c.Add("Declare war", () => { Pop(); sim.DeclareWar(me, them.Id); Music(); AfterAction(); });
                    c.Add("Keep the peace", Pop);
                    Push(c);
                });
            }
            p.Add($"Send a gift of 50 BC (reserve {Me.Reserve:0})", () =>
            {
                Me.Reserve -= 50;
                them.Reserve += 50;
                them.Attitude[me] = Math.Min(100, them.Attitude[me] + 10);
                Say($"{them.Ruler} accepts your gift.", 0xFF60D080);
            }, Me.Reserve >= 50);
            p.Add("Leave", Pop);
            return p;
        });
    }

    private void Exchange(Empire them)
    {
        var theirs = Enumerable.Range(0, Techs.Count).Where(t => them.Known[t] && !Me.Known[t]).ToList();
        var mine = Enumerable.Range(0, Techs.Count).Where(t => Me.Known[t] && !them.Known[t]).ToList();
        if (theirs.Count == 0 || mine.Count == 0)
        {
            Tell("No exchange", theirs.Count == 0 ? $"The {them.Name} know nothing you don't." : $"You know nothing the {them.Name} don't.");
            return;
        }
        int give = theirs.OrderBy(t => Techs.All[t].Level).First();
        int want = mine.OrderByDescending(t => Techs.All[t].Level).First();
        if (!Ai!.WouldTrade(them, give, want))
        {
            Tell("No exchange", $"\"Our learning is our own,\" says {them.Ruler}.");
            return;
        }
        var p = new Panel { Title = "An exchange of discoveries", Back = Pop };
        p.Say($"{them.Ruler} offers {Techs.All[give].Name} for {Techs.All[want].Name}.");
        p.Add("Agree", () =>
        {
            Pop();
            Sim!.Learn(Me, give, $"the {them.Name}");
            Sim.Learn(them, want, null);
            them.Attitude[World.Player] = Math.Min(100, them.Attitude[World.Player] + 5);
            AfterAction();
        });
        p.Add("Decline", Pop);
        Push(p);
    }

    private void DemandPanel(Event e)
    {
        var them = World.Empires[e.A];
        var p = new Panel { Title = $"{them.Ruler} of the {them.Name}" };
        p.Say(e.Text);
        int bc = e.B, tech = e.Star;
        p.Add(tech >= 0 ? $"Give them {Techs.All[tech].Name}" : $"Pay {bc} BC", () =>
        {
            Pop();
            if (tech >= 0) Sim!.Learn(them, tech, null);
            else
            {
                double pay = Math.Min(bc, Me.Reserve);
                Me.Reserve -= pay;
                them.Reserve += pay;
            }
            them.Attitude[World.Player] = Math.Min(100, them.Attitude[World.Player] + 15);
        });
        p.Add("Refuse", () =>
        {
            Pop();
            them.Attitude[World.Player] -= 20;
            if (Sim!.Rng.Next(100) < 30 + them.Kind.Aggression * 60) Sim.DeclareWar(them.Id, World.Player);
            AfterAction();
        });
        Push(p);
    }

    private void OfferPanel(Event e)
    {
        var them = World.Empires[e.A];
        var p = new Panel { Title = $"Envoys from the {them.Name}" };
        p.Say(e.Text);
        p.Add("Make peace", () => { Pop(); Sim!.MakePeace(World.Player, them.Id); Music(); });
        p.Add("Fight on", () => { Pop(); them.Attitude[World.Player] -= 5; });
        Push(p);
    }

    // ───────────────────────────── the Council ─────────────────────────────

    private void CouncilVote()
    {
        var sim = Sim!;
        var (a, b) = sim.Candidates();
        var ea = World.Empires[a];
        var eb = World.Empires[b];
        var p = new Panel { Title = "The Galactic Council", Kind = PanelKind.Wide };
        p.Say($"The Council of all the peoples sits to choose a High Sovereign of the stars. The candidates are the {ea.Name} ({sim.Votes(a)} votes) and the {eb.Name} ({sim.Votes(b)} votes). Two thirds of all the votes elects.");
        p.Say($"You have {sim.Votes(World.Player)} vote{(sim.Votes(World.Player) == 1 ? "" : "s")}. How will you cast them?");
        p.Add($"For the {ea.Name}{(a == World.Player ? " (yourselves)" : "")}", () => { CloseAll(); Advance(a); });
        p.Add($"For the {eb.Name}{(b == World.Player ? " (yourselves)" : "")}", () => { CloseAll(); Advance(b); });
        p.Add("Abstain", () => { CloseAll(); Advance(-1); });
        Push(p);
    }

    private void CouncilResult(Event e)
    {
        var p = new Panel { Title = "The Council's verdict", Kind = PanelKind.Wide };
        foreach (var line in e.Text.Split('\n')) p.Say(line);
        if (e.A >= 0 && e.Empire == World.Player)
        {
            int winner = e.A;
            p.Add($"Accept the {World.Empires[winner].Name}' rule (the game ends)", () => { CloseAll(); Sim!.Accept(winner); AfterAction(); });
            p.Add("Defy the Council: every people will declare war on you", () => { CloseAll(); Sim!.Defy(); Music(); AfterAction(); });
        }
        else
        {
            p.Add("Continue", Pop);
            p.Back = Pop;
        }
        Push(p);
    }

    // ───────────────────────────── reports ─────────────────────────────

    public void GalaxyReport()
    {
        var sim = Sim!;
        var p = new Panel { Title = "The state of the galaxy", Kind = PanelKind.Big, Back = Pop };
        p.Say(World.CouncilTurn >= 0 ? $"The year {World.Year}. The Galactic Council next sits in {WorldState.StartYear + World.CouncilTurn}." : $"The year {World.Year}. You have defied the Council: only conquest decides now.");
        var known = World.Empires.Where(e => !e.Monster && e.Alive && (e.Id == World.Player || sim.Met(World.Player, e.Id))).OrderByDescending(e => sim.Votes(e.Id)).ToList();
        foreach (var e in known)
            p.Add($"{(e.Id == World.Player ? "You, the " : "The ")}{e.Name}: {World.Stars.Count(s => s.Owner == e.Id)} colonies, {sim.TotalPop(e.Id):0} people, {sim.Votes(e.Id)} votes, {e.TechCount} discoveries", null);
        int unknown = World.Empires.Count(e => !e.Monster && e.Alive && !known.Contains(e));
        if (unknown > 0) p.Say($"> {unknown} other people{(unknown == 1 ? "" : "s")} you have not met.");
        if (World.Stars.FirstOrDefault(s => s.Vault) is { } vault)
            p.Say(vault.Owner >= 0 ? $"> The Vault of Aeon is held by the {World.Empires[vault.Owner].Name}." : "> The Warden still guards the Vault of Aeon.");
        p.Add("Close", Pop);
        Push(p);
    }

    public void JournalPanel()
    {
        var p = new Panel { Title = "The chronicle", Kind = PanelKind.Big, Back = Pop };
        if (World.Journal.Count == 0) p.Say("Nothing written yet.");
        foreach (var n in Enumerable.Reverse(World.Journal)) p.Add(n, null);
        p.Add("Close", Pop);
        Push(p);
    }

    private void RetireMenu()
    {
        var p = new Panel { Title = "Retire?", Back = Pop };
        p.Say("Step down now and let history weigh the peoples as they stand.");
        p.Add("Retire", () =>
        {
            CloseAll();
            Sim!.End(-1, $"{Me.Ruler} steps down in {World.Year}, and the histories weigh the {Me.Name}' place among the stars.");
            AfterAction();
        });
        p.Add("Carry on", Pop);
        Push(p);
    }

    // ───────────────────────────── saving ─────────────────────────────

    private void SaveMenu()
    {
        Show(() =>
        {
            var p = new Panel { Title = "Save", Back = Pop };
            for (int s = 1; s <= SaveStore.Slots; s++)
            {
                int slot = s;
                p.Add(Saves.Describe(slot), () =>
                {
                    World.Seed = Sim!.Rng.State;
                    Saves.Save(slot, World);
                    Say($"Saved in slot {slot}.");
                    Pop();
                });
            }
            p.Add("Cancel", Pop);
            return p;
        });
    }

    public void LoadMenu()
    {
        Show(() =>
        {
            var p = new Panel { Title = "Load", Back = Pop };
            for (int s = 1; s <= SaveStore.Slots; s++)
            {
                int slot = s;
                p.Add(Saves.Describe(slot), () => Load(slot), Saves.Load(slot) is not null);
            }
            p.Add("Cancel", Pop);
            return p;
        });
    }

    public bool Load(int slot)
    {
        var w = Saves.Load(slot);
        if (w is null) return false;
        InDemo = false;
        StartGame(w, fresh: false);
        Say("The game continues.");
        return true;
    }
}
