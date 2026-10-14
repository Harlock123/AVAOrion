using AVAOrion.Rules;
using AVAOrion.World;

namespace AVAOrion.Tests;

/// <summary>The rules of the galaxy, one at a time, on a small row of stars.</summary>
public sealed class RulesTests
{
    // ───────────────────────────── the galaxy ─────────────────────────────

    [Fact]
    public void The_same_seed_makes_the_same_galaxy()
    {
        var a = Sim.NewWorld(42, 0, 1, 4);
        var b = Sim.NewWorld(42, 0, 1, 4);
        Assert.Equal(a.Stars.Select(s => (s.Name, s.X, s.Y, s.Planet, s.Size)), b.Stars.Select(s => (s.Name, s.X, s.Y, s.Planet, s.Size)));
        Assert.Equal(40, a.Stars.Count);
        for (int i = 0; i < a.Stars.Count; i++)
        for (int j = i + 1; j < a.Stars.Count; j++)
            Assert.True(a.Distance(a.Stars[i], a.Stars[j]) >= 2.4);
        Assert.Equal(a.Stars.Count, a.Stars.Select(s => s.Name).Distinct().Count());
    }

    [Fact]
    public void Every_people_starts_with_a_home_fleet_and_worlds_nearby()
    {
        var w = Sim.NewWorld(9, 2, 1, 5);
        var sim = new Sim(w, new Rng(1));
        foreach (var e in w.Empires.Where(e => !e.Monster))
        {
            var home = Assert.Single(w.Stars, s => s.Owner == e.Id);
            Assert.True(home.Capital);
            Assert.Equal(e.Kind.Home, home.Planet);
            Assert.Equal(50, home.Pop);
            var fleet = Assert.Single(w.Fleets, f => f.Owner == e.Id);
            Assert.Equal(5, fleet.Count);
            Assert.Contains(fleet.Ships, s => e.Design(s.Design)!.Colony);
            Assert.True(w.Stars.Count(s => s.Owner < 0 && s.HasPlanet && !Planets.Hostile(s.Planet) && w.Distance(s, home) <= 6.5) >= 2);
            foreach (var other in w.Stars.Where(s => s.Owner >= 0 && s.Owner != e.Id))
                Assert.True(w.Distance(home, other) > 6);
        }
        var vault = Assert.Single(w.Stars, s => s.Vault);
        Assert.Equal("Aeon", vault.Name);
        Assert.Contains(w.Fleets, f => f.At == vault.Id && w.Empires[f.Owner].Monster);
        Assert.True(sim.Votes(0) >= 1);
    }

    [Fact]
    public void Every_discovery_is_reachable_and_every_weapon_is_a_discovery()
    {
        var e = Sim.NewEmpire(0, 0, 2, 1, human: false, ruler: null);
        for (int guard = 0; guard < 500; guard++)
            foreach (var f in Enum.GetValues<Field>())
                foreach (var t in Sim.Available(e, f).Take(1))
                    e.Known[t.Id] = true;
        Assert.All(e.Known, Assert.True);
        Assert.All(Ships.Weapons, w => Assert.True(Techs.Get(w.Name).Effect == Effect.Weapon));
    }

    // ───────────────────────────── colonies ─────────────────────────────

    [Fact]
    public void A_colony_makes_more_with_factories_and_rich_minerals()
    {
        var (w, sim) = Sandbox.Make();
        var s = w.Stars[0];
        double bare = sim.Report(s).Gross;
        s.Factories = 100;
        double built = sim.Report(s).Gross;
        Assert.True(built > bare + 90);
        s.Minerals = Minerals.Rich;
        Assert.True(sim.Report(s).Gross > built * 1.5);
        // People can only run so many factories each.
        s.Factories = 1000;
        Assert.Equal(sim.Report(s).Working, s.Pop * Sim.Controls(w.Empires[0]));
    }

    [Fact]
    public void Sliders_always_add_up_to_a_hundred()
    {
        var (w, _) = Sandbox.Make();
        var s = w.Stars[0];
        Sim.SetSlider(s, Spend.Ships, 70);
        Assert.Equal(100, s.Sliders.Sum());
        Assert.Equal(70, s.Slider(Spend.Ships));
        Sim.SetSlider(s, Spend.Industry, 90);
        Assert.Equal(100, s.Sliders.Sum());
        Assert.Equal(90, s.Slider(Spend.Industry));
        Sim.SetSlider(s, Spend.Research, 0);
        Assert.Equal(100, s.Sliders.Sum());
        Assert.All(s.Sliders, v => Assert.InRange(v, 0, 100));
    }

    [Fact]
    public void Industry_builds_factories_and_shipyards_build_ships()
    {
        var (w, sim) = Sandbox.Make();
        var s = w.Stars[0];
        s.Sliders = [0, 0, 100, 0, 0];
        double before = s.Factories;
        sim.RunColony(s);
        Assert.True(s.Factories > before);
        var fighter = Sandbox.Design(w.Empires[0], "Fighter");
        s.Building = fighter.Id;
        s.Sliders = [100, 0, 0, 0, 0];
        s.ShipBC = fighter.Cost;
        sim.RunColony(s);
        Assert.Contains(w.Fleets, f => f.Owner == 0 && f.At == 0 && f.Ships.Any(x => x.Design == fighter.Id));
    }

    [Fact]
    public void Uncleaned_waste_poisons_a_world_and_ecology_grows_it()
    {
        var (w, sim) = Sandbox.Make();
        var s = w.Stars[0];
        s.Factories = 100;
        s.Sliders = [0, 0, 0, 0, 100];
        double room = sim.MaxPop(s, w.Empires[0]);
        for (int i = 0; i < 10; i++) sim.RunColony(s);
        Assert.True(sim.MaxPop(s, w.Empires[0]) < room);
        var young = Sandbox.Colony(w, 3, 0, 5);
        young.Sliders = [0, 0, 0, 100, 0];
        double pop = young.Pop;
        sim.RunColony(young);
        Assert.True(young.Pop > pop + 0.5);
    }

    [Fact]
    public void Defence_spending_builds_missile_bases()
    {
        var (w, sim) = Sandbox.Make();
        var s = w.Stars[0];
        s.Sliders = [0, 100, 0, 0, 0];
        s.BaseBC = Sim.BaseCost - 1;
        sim.RunColony(s);
        Assert.True(s.Bases >= 1);
    }

    [Fact]
    public void Hostile_worlds_need_the_right_discovery_except_for_the_hardy()
    {
        var e = Sim.NewEmpire(0, 0, 2, 1, human: true, ruler: null);
        Assert.True(Sim.CanSettle(e, PlanetType.Terran));
        Assert.False(Sim.CanSettle(e, PlanetType.Barren));
        Sandbox.Know(e, "Barren Environment");
        Assert.True(Sim.CanSettle(e, PlanetType.Dead));
        Assert.False(Sim.CanSettle(e, PlanetType.Toxic));
        var quorn = Sim.NewEmpire(1, Races.All.First(r => r.Hardy).Id, 2, 1, human: false, ruler: null);
        Assert.True(Sim.CanSettle(quorn, PlanetType.Barren));
        Assert.False(Sim.CanSettle(e, PlanetType.None));
    }

    // ───────────────────────────── fleets ─────────────────────────────

    [Fact]
    public void Fleets_fly_at_their_speed_within_fuel_range()
    {
        var (w, sim) = Sandbox.Make();
        var f = Sandbox.Fleet(w, sim, 0, 0, "Fighter", 2);
        Assert.Null(sim.Send(f, w.Stars[3])); // 12 parsecs: out of range
        var go = sim.Send(f, w.Stars[1])!;
        Assert.Equal(2, go.Eta); // 4 parsecs at 2.5 a turn
        sim.EndTurn();
        Assert.True(go.Moving);
        sim.EndTurn();
        Assert.Equal(1, go.At);
        Assert.True(w.Empires[0].Explored[1]);
        // Scouts with reserve tanks fly further.
        var scout = Sandbox.Fleet(w, sim, 0, 0, "Scout", 1);
        Assert.True(sim.Tanked(scout));
        Assert.NotNull(sim.Send(scout, w.Stars[2]));
    }

    [Fact]
    public void Part_of_a_fleet_can_be_sent_on_its_own()
    {
        var (w, sim) = Sandbox.Make();
        var f = Sandbox.Fleet(w, sim, 0, 0, "Fighter", 5);
        var go = sim.Send(f, w.Stars[1], new Dictionary<int, int> { [f.Ships[0].Design] = 2 })!;
        Assert.NotSame(f, go);
        Assert.Equal(2, go.Count);
        Assert.Equal(3, f.Count);
        Assert.Equal(0, f.At);
    }

    [Fact]
    public void A_colony_ship_founds_a_colony()
    {
        var (w, sim) = Sandbox.Make();
        var f = Sandbox.Fleet(w, sim, 0, 1, "Colony Ship", 1);
        Assert.True(sim.CanColonise(f, out _));
        Assert.True(sim.Colonise(f));
        Assert.Equal(0, w.Stars[1].Owner);
        Assert.Equal(Sim.ColonistsPerShip, w.Stars[1].Pop);
        Assert.DoesNotContain(f, w.Fleets);
        w.Stars[2].Planet = PlanetType.Toxic;
        var g = Sandbox.Fleet(w, sim, 0, 2, "Colony Ship", 1);
        Assert.False(sim.CanColonise(g, out string why));
        Assert.Contains("Toxic Environment", why);
    }

    [Fact]
    public void Transports_swell_a_colony_of_ones_own()
    {
        var (w, sim) = Sandbox.Make();
        var small = Sandbox.Colony(w, 1, 0, 5);
        var t = sim.SendTransports(w.Stars[0], small, 10, out _)!;
        Assert.Equal(40, w.Stars[0].Pop);
        for (int i = 0; i < t.Eta + 1; i++) sim.EndTurn();
        Assert.True(small.Pop >= 15);
        Assert.DoesNotContain(t, w.Fleets);
    }

    // ───────────────────────────── war ─────────────────────────────

    [Fact]
    public void The_stronger_fleet_wins_and_the_weaker_withdraws_or_dies()
    {
        var (w, sim) = Sandbox.Make();
        sim.Meet(0, 1);
        sim.DeclareWar(0, 1);
        var star = w.Stars[3];
        Sandbox.Fleet(w, sim, 0, 3, "Destroyer", 8);
        Sandbox.Fleet(w, sim, 1, 3, "Fighter", 2);
        var r = sim.Fight(star, 0, 1);
        Assert.Equal(0, r.Winner);
        Assert.True(r.Groups[1].Sum(g => g.End) < 2 || r.Retreated[1]);
        Assert.DoesNotContain(w.Fleets, f => f.Owner == 1 && f.At == 3);
        Assert.NotEmpty(r.Volleys);
    }

    [Fact]
    public void Shields_turn_aside_weak_weapons()
    {
        var (w, sim) = Sandbox.Make();
        sim.Meet(0, 1);
        sim.DeclareWar(0, 1);
        var e1 = w.Empires[1];
        var wall = Designer.Add(e1, new ShipDesign { Name = "Wall", Hull = Hull.Large, Shield = 5, Armor = 0, Mounts = [new Mount(0, 1)] });
        var f = sim.NewFleet(1, w.Stars[3]);
        Sim.AddShips(f, wall.Id, 1);
        var attackers = sim.NewFleet(0, w.Stars[3]);
        Sim.AddShips(attackers, Sandbox.Design(w.Empires[0], "Fighter").Id, 10);
        // Lasers do 1 to 4; a class 5 shield stops them all (the missiles get through).
        var r = sim.Fight(w.Stars[3], 0, 1);
        Assert.All(r.Volleys.Where(v => v.Side == 0 && !v.Missile), v => Assert.Equal(0, v.Hits));
    }

    [Fact]
    public void Bases_defend_a_colony_and_an_undefended_one_is_bombed()
    {
        var (w, sim) = Sandbox.Make();
        sim.Meet(0, 1);
        sim.DeclareWar(0, 1);
        var home = w.Stars[7];
        home.Bases = 0;
        double pop = home.Pop;
        Sandbox.Fleet(w, sim, 0, 7, "Destroyer", 10);
        var r = sim.Fight(home, 1, 0);
        Assert.Equal(1, r.Winner);
        Assert.True(home.Pop < pop);
        Assert.Equal(0, r.PlanetSide);
    }

    [Fact]
    public void Soldiers_take_an_enemy_colony_and_its_secrets()
    {
        var (w, sim) = Sandbox.Make();
        sim.Meet(0, 1);
        sim.DeclareWar(0, 1);
        var target = w.Stars[7];
        target.Pop = 5;
        Sandbox.Know(w.Empires[1], "Deflector Shield I");
        Assert.True(sim.Invade(target, 0, 60));
        Assert.Equal(0, target.Owner);
        Assert.True(w.Empires[0].Known[Techs.Id("Deflector Shield I")]);
        Assert.False(w.Empires[1].Alive);
        Assert.Equal(0, w.Winner);
    }

    [Fact]
    public void Peoples_meet_go_to_war_and_sign_treaties()
    {
        var (w, sim) = Sandbox.Make();
        Assert.False(sim.Met(0, 1));
        var scout = Sandbox.Fleet(w, sim, 0, 7, "Scout", 1);
        sim.Survey(0);
        Assert.True(sim.Met(0, 1));
        Assert.Contains(sim.Events, e => e.Kind == Happen.Contact);
        sim.Sign(0, 1, Treaty.NonAggression);
        Assert.Equal(Treaty.NonAggression, w.Empires[1].Treaties[0]);
        sim.DeclareWar(0, 1);
        Assert.True(sim.AtWar(1, 0));
        Assert.Equal(Treaty.None, w.Empires[1].Treaties[0]);
        Assert.True(w.Empires[1].Betrayed[0] == w.Turn);
        sim.MakePeace(1, 0);
        Assert.False(sim.AtWar(0, 1));
        _ = scout;
    }

    // ───────────────────────────── knowledge and the end ─────────────────────────────

    [Fact]
    public void Research_discovers_and_moves_on()
    {
        var (w, sim) = Sandbox.Make();
        var e = w.Empires[0];
        e.Alloc = [0, 0, 0, 0, 100, 0];
        int t = sim.PickTech(e, Field.Propulsion);
        e.Target[(int)Field.Propulsion] = t;
        e.Progress[(int)Field.Propulsion] = sim.TechCost(e, Techs.All[t]);
        sim.EndTurn();
        Assert.True(e.Known[t]);
        Assert.Contains(sim.Events, ev => ev.Kind == Happen.Tech);
        Assert.NotEqual(t, e.Target[(int)Field.Propulsion]);
        Assert.True(Techs.Cost(20) > Techs.Cost(10) * 3);
    }

    [Fact]
    public void Better_technology_makes_better_designs()
    {
        var e = Sim.NewEmpire(0, 0, 2, 1, human: true, ruler: null);
        var early = Designer.Warship(e, Hull.Large);
        Sandbox.Know(e, "Fusion Beam", "Deflector Shield III", "Battle Computer Mk III", "Ferrocrystal Plating", "Fusion Drive");
        var late = Designer.Warship(e, Hull.Large);
        Assert.True(early.Fits && late.Fits);
        Assert.True(late.Power > early.Power * 2);
        Assert.Equal(4, late.Warp);
        Assert.Contains(late.Mounts, m => Ships.Weapons[m.Weapon].Name == "Fusion Beam");
        var colony = Designer.ColonyShip(e);
        Assert.True(colony.Fits);
        Assert.True(colony.Colony);
        for (int i = 0; i < 8; i++) Designer.Add(e, Designer.Warship(e, Hull.Small));
        Assert.True(e.Active.Count() <= 6);
    }

    [Fact]
    public void The_Council_elects_with_two_thirds_of_the_votes()
    {
        var (w, sim) = Sandbox.Make();
        sim.Meet(0, 1);
        w.Empires[1].Attitude[0] = 50;
        w.Stars[0].Pop = 200;
        w.CouncilTurn = w.Turn;
        Assert.True(sim.CouncilDue);
        var (a, _) = sim.Candidates();
        Assert.Equal(0, a);
        sim.EndTurn(humanVote: 0);
        Assert.Equal(0, w.Winner);
        Assert.Contains("High Sovereign", w.Outcome);
    }

    [Fact]
    public void Defying_the_Council_means_war_with_everyone()
    {
        var (w, sim) = Sandbox.Make();
        sim.Meet(0, 1);
        sim.Defy();
        Assert.True(sim.AtWar(1, 0));
        Assert.Equal(-1, w.CouncilTurn);
        Assert.False(sim.CouncilDue);
    }

    [Fact]
    public void A_galaxy_saves_and_loads_whole()
    {
        var w = Sim.NewWorld(21, 3, 1, 4);
        foreach (var e in w.Empires) e.Human = false;
        var sim = new Sim(w, new Rng(2));
        var ai = new Ai(sim);
        for (int t = 0; t < 40; t++)
        {
            foreach (var e in w.Empires) ai.Play(e);
            sim.EndTurn();
        }
        var back = WorldState.FromJson(w.ToJson());
        Assert.Equal(w.Turn, back.Turn);
        Assert.Equal(w.Stars.Select(s => (s.Owner, Math.Round(s.Pop, 3), s.Bases)), back.Stars.Select(s => (s.Owner, Math.Round(s.Pop, 3), s.Bases)));
        Assert.Equal(w.Fleets.Select(f => (f.Owner, f.Count, f.At, f.Dest)), back.Fleets.Select(f => (f.Owner, f.Count, f.At, f.Dest)));
        Assert.Equal(w.Empires.Select(e => e.TechCount), back.Empires.Select(e => e.TechCount));
        Assert.Equal(w.Empires.Select(e => e.Designs.Count), back.Empires.Select(e => e.Designs.Count));
    }
}
