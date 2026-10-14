using AVAOrion.Rules;

namespace AVAOrion.World;

/// <summary>Something that happened which the player may need to hear about, or answer.</summary>
public enum Happen
{
    Tech, Contact, War, Peace, Treaty, Offer, Demand, Colonised, ColonyLost, ColonyTaken, Battle, Fallen,
    Council, CouncilResult, GameOver, Event, Vault, ShipBuilt,
}

public sealed record Event(Happen Kind, int Empire, string Text, int A = -1, int B = -1, int Star = -1);

/// <summary>
/// The rules of the galaxy, applied to a <see cref="WorldState"/>: colonies growing and building,
/// research, ships flying and fighting, landings, treaties and wars, the Galactic Council, and the
/// years going by. Nothing here knows about the screen: what the player should hear about is
/// posted as <see cref="Events"/>, <see cref="Notes"/> and <see cref="Battles"/>.
/// </summary>
public sealed partial class Sim
{
    public WorldState W { get; }
    public Rng Rng { get; }
    public List<Event> Events { get; } = [];
    public List<(string Text, uint Colour)> Notes { get; } = [];
    /// <summary>The fights the player was in (or saw) this turn, for showing.</summary>
    public List<BattleReport> Battles { get; } = [];
    public event Action<Sfx>? Sound;
    public int WarsDeclared { get; private set; }

    public Sim(WorldState w, Rng rng)
    {
        W = w;
        Rng = rng;
    }

    public Empire E(int id) => W.Empires[id];
    public bool IsPlayer(int e) => e == W.Player && W.Empires[e].Human;

    public void Play(Sfx s, int empire = -1)
    {
        if (empire < 0 || IsPlayer(empire)) Sound?.Invoke(s);
    }

    public void Tell(int empire, string text, uint colour = 0xFFE0E8F0)
    {
        if (empire < 0 || IsPlayer(empire)) Notes.Add((text, colour));
        if (Notes.Count > 200) Notes.RemoveRange(0, 100);
    }

    public void Post(Event e)
    {
        if (e.Empire < 0 || IsPlayer(e.Empire)) Events.Add(e);
    }

    // ───────────────────────────── a new galaxy ─────────────────────────────

    public static WorldState NewWorld(ulong seed, int race, int level, int rivals, GalaxySize size = GalaxySize.Medium, string? ruler = null)
    {
        var rng = new Rng(seed);
        var stars = GalaxyMaker.Make(rng, size, out double width, out double height);
        var w = new WorldState { Stars = stars, Width = width, Height = height, Size = size, Level = level, Seed = seed };
        var races = new List<int> { race };
        races.AddRange(Enumerable.Range(0, Races.All.Length).Where(r => r != race).OrderBy(_ => rng.Next(1000)).Take(Math.Clamp(rivals, 1, Races.All.Length - 1)));
        int n = races.Count + 1;
        var homes = GalaxyMaker.PickHomes(stars, rng, races.Count, width, height);
        for (int i = 0; i < races.Count; i++)
        {
            var e = NewEmpire(i, races[i], n, stars.Count, human: i == 0, ruler: i == 0 ? ruler : null);
            w.Empires.Add(e);
            GalaxyMaker.Settle(stars, homes[i], Races.All[races[i]], rng, homes);
        }
        var monsters = NewEmpire(races.Count, 0, n, stars.Count, human: false, ruler: "the Warden");
        monsters.Monster = true;
        monsters.Relations = Enumerable.Repeat(Relation.War, n).ToArray();
        w.Empires.Add(monsters);
        foreach (var e in w.Empires.Where(e => !e.Monster)) e.Relations[monsters.Id] = Relation.War;
        var sim = new Sim(w, rng);
        for (int i = 0; i < races.Count; i++) sim.Found(homes[i], i);
        sim.MakeVault(homes);
        foreach (var e in w.Empires.Where(e => !e.Monster)) sim.Survey(e.Id);
        w.Seed = rng.State;
        return w;
    }

    internal static Empire NewEmpire(int id, int race, int n, int stars, bool human, string? ruler)
    {
        var r = Races.All[race];
        var e = new Empire
        {
            Id = id, Race = race, Human = human, Ruler = string.IsNullOrWhiteSpace(ruler) ? r.RulerName : ruler!,
            Relations = new Relation[n], Treaties = new Treaty[n], Trade = new bool[n], Attitude = new int[n], Cooldown = new int[n],
            Betrayed = Enumerable.Repeat(-100, n).ToArray(), Explored = new bool[stars], Reserve = 50,
        };
        foreach (var t in Techs.Start) e.Known[Techs.Id(t)] = true;
        for (int i = 0; i < n; i++) e.Attitude[i] = (int)(20 - 40 * r.Aggression);
        // Studies lean the people's way.
        e.Alloc = [14, 14, 14, 14, 14, 14];
        foreach (var f in r.Leanings) e.Alloc[(int)f] += 8;
        int extra = 100 - e.Alloc.Sum();
        e.Alloc[(int)Field.Planetology] += extra;
        return e;
    }

    /// <summary>A people's home: a big colony with factories, and its first ships.</summary>
    private void Found(Star home, int empire)
    {
        var e = W.Empires[empire];
        home.Owner = empire;
        home.Pop = 50;
        home.Factories = 40;
        home.Bases = 1;
        home.Capital = true;
        home.Sliders = [10, 0, 50, 10, 30];
        e.Explored[home.Id] = true;
        Designer.Starting(e);
        home.Building = e.Designs.First(d => d.Name == "Destroyer").Id;
        var fleet = NewFleet(empire, home);
        AddShips(fleet, e.Designs.First(d => d.Name == "Scout").Id, 2);
        AddShips(fleet, e.Designs.First(d => d.Name == "Fighter").Id, 2);
        AddShips(fleet, e.Designs.First(d => d.Colony).Id, 1);
    }

    /// <summary>The Vault of Aeon: the star nearest the galaxy's heart that is far from every home, and its guardian.</summary>
    private void MakeVault(List<Star> homes)
    {
        var centre = (X: W.Width / 2, Y: W.Height / 2);
        var vault = W.Stars.Where(s => !homes.Contains(s) && homes.All(h => W.Distance(h, s) > 8))
            .OrderBy(s => WorldState.Distance(s.X, s.Y, centre.X, centre.Y)).FirstOrDefault();
        if (vault is null) return;
        vault.Name = "Aeon";
        vault.Vault = true;
        vault.Planet = PlanetType.Terran;
        vault.Size = PlanetSize.Huge;
        vault.Minerals = Minerals.UltraRich;
        vault.Artifacts = true;
        vault.Fertile = true;
        vault.Colour = StarColour.White;
        var m = W.Monsters!;
        var warden = new ShipDesign
        {
            Id = m.NextDesign++, Name = "the Warden", Hull = Hull.Huge, Warp = 0, Armor = 4, Shield = 7, Computer = 5, Ecm = 3,
            Mounts = [new Mount(Ships.Weapon("Phase Lance").Id, 6), new Mount(Ships.Weapon("Scourge Missile").Id, 4)],
        };
        m.Designs.Add(warden);
        AddShips(NewFleet(m.Id, vault), warden.Id, 1);
    }

    // ───────────────────────────── fleets, made and merged ─────────────────────────────

    public Fleet NewFleet(int owner, Star at)
    {
        var f = new Fleet { Id = W.NextId++, Owner = owner, At = at.Id, X = at.X, Y = at.Y };
        W.Fleets.Add(f);
        return f;
    }

    public static void AddShips(Fleet f, int design, int count)
    {
        var s = f.Ships.FirstOrDefault(x => x.Design == design);
        if (s is null) f.Ships.Add(new Stack { Design = design, Count = count });
        else s.Count += count;
    }

    /// <summary>The fleet of a people waiting at a star (not a transport), made if need be.</summary>
    public Fleet FleetAt(int owner, Star star)
    {
        var f = W.Fleets.FirstOrDefault(x => x.Owner == owner && x.At == star.Id && !x.IsTransport);
        return f ?? NewFleet(owner, star);
    }

    /// <summary>Joins up a people's fleets waiting at the same star.</summary>
    public void Merge(Star star)
    {
        foreach (var group in W.Fleets.Where(f => f.At == star.Id && !f.IsTransport).GroupBy(f => f.Owner).ToList())
        {
            var list = group.ToList();
            if (list.Count < 2) continue;
            var keep = list[0];
            foreach (var other in list.Skip(1))
            {
                foreach (var s in other.Ships) AddShips(keep, s.Design, s.Count);
                W.Fleets.Remove(other);
            }
        }
        W.Fleets.RemoveAll(f => f.Count == 0 && !f.IsTransport);
    }

    // ───────────────────────────── what a people knows ─────────────────────────────

    /// <summary>The best value of an effect a people knows (or the given default).</summary>
    public static double Best(Empire e, Effect effect, double none = 0)
    {
        double best = none;
        for (int i = 0; i < Techs.Count; i++)
            if (e.Known[i] && Techs.All[i].Effect == effect) best = Math.Max(best, Techs.All[i].Value);
        return best;
    }

    /// <summary>The smallest value of an effect a people knows (for costs and waste), or the default.</summary>
    public static double Least(Empire e, Effect effect, double none)
    {
        double best = none;
        for (int i = 0; i < Techs.Count; i++)
            if (e.Known[i] && Techs.All[i].Effect == effect) best = Math.Min(best, Techs.All[i].Value);
        return best;
    }

    public static double Sum(Empire e, Effect effect)
    {
        double sum = 0;
        for (int i = 0; i < Techs.Count; i++)
            if (e.Known[i] && Techs.All[i].Effect == effect) sum += Techs.All[i].Value;
        return sum;
    }

    public static int Warp(Empire e) => (int)Best(e, Effect.Warp, 1);
    public static double Range(Empire e) => Best(e, Effect.Range, 5);
    public static double Scanner(Empire e) => Best(e, Effect.Scanner, 2);
    public static int Controls(Empire e) => (int)Best(e, Effect.Controls, 2);
    public static double FactoryCost(Empire e) => Least(e, Effect.FactoryCost, 10);
    public static int ArmorTier(Empire e) => (int)Best(e, Effect.Armor, 0);
    public static int ShieldTier(Empire e) => (int)Best(e, Effect.Shield, 0);
    public static int ComputerTier(Empire e) => (int)Best(e, Effect.Computer, 0);
    public static int EcmTier(Empire e) => (int)Best(e, Effect.Ecm, 0);
    public static double Ground(Empire e) => Sum(e, Effect.Ground) / 100 + e.Kind.Ground;
    public static int FieldLevel(Empire e, Field f)
    {
        int best = 0;
        for (int i = 0; i < Techs.Count; i++)
            if (e.Known[i] && Techs.All[i].Field == f) best = Math.Max(best, Techs.All[i].Level);
        return best;
    }

    /// <summary>The weapons a people can fit.</summary>
    public static IEnumerable<Weapon> Arsenal(Empire e) =>
        Ships.Weapons.Where(w => e.Known[Techs.Id(w.Name)]);

    /// <summary>Whether a people could live on a planet of this kind.</summary>
    public static bool CanSettle(Empire e, PlanetType t)
    {
        if (t == PlanetType.None) return false;
        var needs = Planets.Kind(t).Needs;
        if (needs is null) return true;
        if (e.Kind.Hardy && t is PlanetType.Barren or PlanetType.Dead) return true;
        return e.Known[Techs.Id(needs)];
    }

    // ───────────────────────────── distance and sight ─────────────────────────────

    /// <summary>How far a star is from the nearest of a people's colonies.</summary>
    public double Reach(int empire, double x, double y)
    {
        double best = double.MaxValue;
        foreach (var s in W.Stars)
            if (s.Owner == empire) best = Math.Min(best, WorldState.Distance(s.X, s.Y, x, y));
        return best;
    }

    /// <summary>Whether a fleet of a people may fly to a star: within fuel range of one of its colonies.</summary>
    public bool InRange(int empire, Star star, bool tanks = false) =>
        Reach(empire, star.X, star.Y) <= Range(W.Empires[empire]) + (tanks ? 3 : 0) + 0.01;

    /// <summary>A fleet's speed in parsecs a turn: the slowest ship's warp.</summary>
    public double Speed(Fleet f)
    {
        var e = W.Empires[f.Owner];
        if (f.IsTransport) return 1.5 + Warp(e);
        int warp = f.Ships.Select(s => e.Design(s.Design)?.Warp ?? 1).DefaultIfEmpty(1).Min();
        return 1.5 + Math.Max(1, warp);
    }

    public int TravelTurns(Fleet f, Star to) => Math.Max(1, (int)Math.Ceiling(WorldState.Distance(f.X, f.Y, to.X, to.Y) / Speed(f)));

    /// <summary>Whether a people can see a point right now: near one of its colonies or fleets.</summary>
    public bool Sees(int empire, double x, double y)
    {
        var e = W.Empires[empire];
        double scan = Scanner(e);
        foreach (var s in W.Stars)
            if (s.Owner == empire && WorldState.Distance(s.X, s.Y, x, y) <= scan) return true;
        foreach (var f in W.Fleets)
            if (f.Owner == empire && WorldState.Distance(f.X, f.Y, x, y) <= 1.6) return true;
        return false;
    }

    /// <summary>Surveys whatever a people's colonies and fleets can see, and meets whoever is there.</summary>
    public void Survey(int empire)
    {
        var e = W.Empires[empire];
        if (e.Monster || !e.Alive) return;
        foreach (var s in W.Stars)
        {
            bool visited = W.Fleets.Any(f => f.Owner == empire && f.At == s.Id);
            if (visited || (Scanner(e) >= 4 && Sees(empire, s.X, s.Y))) e.Explored[s.Id] = true;
            if (s.Owner >= 0 && s.Owner != empire && (visited || Sees(empire, s.X, s.Y))) Meet(empire, s.Owner);
        }
        foreach (var f in W.Fleets)
            if (f.Owner != empire && Sees(empire, f.X, f.Y)) Meet(empire, f.Owner);
    }

    // ───────────────────────────── war and peace ─────────────────────────────

    public bool AtWar(int a, int b) => a != b && W.Empires[a].Relations[b] == Relation.War;
    public bool Met(int a, int b) => a == b || W.Empires[a].Relations[b] != Relation.None;

    public void Meet(int a, int b)
    {
        if (a == b) return;
        var ea = W.Empires[a];
        var eb = W.Empires[b];
        if (ea.Monster || eb.Monster || ea.Relations[b] != Relation.None) return;
        ea.Relations[b] = eb.Relations[a] = Relation.Peace;
        int other = IsPlayer(a) ? b : IsPlayer(b) ? a : -1;
        if (other < 0) return;
        var o = W.Empires[other];
        Post(new Event(Happen.Contact, W.Player, $"Your ships have met the {o.Name}, led by {o.Ruler}. {o.Kind.Nature}", other));
        W.Note($"First contact with the {o.Name}.");
        Play(Sfx.Horn);
    }

    public void DeclareWar(int a, int b)
    {
        if (a == b || AtWar(a, b)) return;
        var ea = W.Empires[a];
        var eb = W.Empires[b];
        bool broke = ea.Treaties[b] != Treaty.None;
        ea.Relations[b] = eb.Relations[a] = Relation.War;
        ea.Treaties[b] = eb.Treaties[a] = Treaty.None;
        ea.Trade[b] = eb.Trade[a] = false;
        ea.Cooldown[b] = eb.Cooldown[a] = 10;
        eb.Attitude[a] = Math.Max(-100, eb.Attitude[a] - (broke ? 50 : 30));
        if (broke) eb.Betrayed[a] = W.Turn;
        WarsDeclared++;
        foreach (var o in W.Empires)
            if (broke && o.Id != a && o.Id != b && !o.Monster && Met(o.Id, a)) o.Attitude[a] = Math.Max(-100, o.Attitude[a] - 10);
        if (IsPlayer(b)) Post(new Event(Happen.War, b, $"{ea.Ruler} of the {ea.Name} has declared war on you!", a));
        else if (IsPlayer(a)) Tell(a, $"You are at war with the {eb.Name}.", 0xFFE05040);
        else if (Met(W.Player, a) && Met(W.Player, b)) Tell(-1, $"The {ea.Name} have gone to war with the {eb.Name}.", 0xFFE0A040);
        if (IsPlayer(a) || IsPlayer(b)) W.Note($"War between the {ea.Name} and the {eb.Name}.");
    }

    public void MakePeace(int a, int b)
    {
        if (!AtWar(a, b)) return;
        var ea = W.Empires[a];
        var eb = W.Empires[b];
        ea.Relations[b] = eb.Relations[a] = Relation.Peace;
        ea.Attitude[b] = Math.Max(ea.Attitude[b], 0);
        eb.Attitude[a] = Math.Max(eb.Attitude[a], 0);
        if (IsPlayer(a) || IsPlayer(b))
        {
            var o = IsPlayer(a) ? eb : ea;
            Tell(W.Player, $"Peace with the {o.Name}.", 0xFF60D080);
            W.Note($"Peace with the {o.Name}.");
        }
    }

    public void Sign(int a, int b, Treaty t)
    {
        var ea = W.Empires[a];
        var eb = W.Empires[b];
        if (AtWar(a, b)) MakePeace(a, b);
        ea.Treaties[b] = eb.Treaties[a] = t;
        ea.Attitude[b] = Math.Min(100, ea.Attitude[b] + 10);
        eb.Attitude[a] = Math.Min(100, eb.Attitude[a] + 10);
        if (IsPlayer(a) || IsPlayer(b))
        {
            var o = IsPlayer(a) ? eb : ea;
            string what = t == Treaty.Alliance ? "an alliance" : "a non-aggression pact";
            Tell(W.Player, $"You have signed {what} with the {o.Name}.", 0xFF60D080);
            W.Note($"Signed {what} with the {o.Name}.");
        }
    }

    public void SignTrade(int a, int b)
    {
        W.Empires[a].Trade[b] = W.Empires[b].Trade[a] = true;
        if (IsPlayer(a) || IsPlayer(b)) Tell(W.Player, $"A trade agreement with the {W.Empires[IsPlayer(a) ? b : a].Name}.", 0xFF60D080);
    }
}
