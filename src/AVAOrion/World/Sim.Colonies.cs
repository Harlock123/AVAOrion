using AVAOrion.Rules;

namespace AVAOrion.World;

/// <summary>A colony's accounts for a turn, in billions of credits (BC).</summary>
public sealed class ColonyReport
{
    public double Gross, Cleanup, Ships, Defense, Industry, Ecology, Research, RP;
    public double MaxPop, MaxFactories, Working;
}

public sealed partial class Sim
{
    public const int BaseCost = 60, ColonistsPerShip = 4;

    // ───────────────────────────── room and riches ─────────────────────────────

    /// <summary>How many people a planet could hold for a people, with what they know.</summary>
    public double MaxPop(Star s, Empire e)
    {
        if (!s.HasPlanet) return 0;
        var kind = s.Kind;
        double room = Planets.SizeBase(s.Size) * kind.Habitable;
        if (s.Fertile) room *= 1.5;
        if (e.Kind.Name == "Nereid" && s.Planet is PlanetType.Ocean or PlanetType.Terran) room *= 1.25;
        if (!Planets.Hostile(s.Planet)) room *= 1 + Best(e, Effect.Soil);
        room += s.Terraformed - s.Waste;
        return Math.Max(3, Math.Round(room, 1));
    }

    /// <summary>What a people's ships cost to keep flying, as a share of what its colonies make.</summary>
    public double UpkeepFactor(Empire e)
    {
        double gross = 0, upkeep = 0;
        foreach (var s in W.Stars)
            if (s.Owner == e.Id) gross += Raw(s, e);
        foreach (var f in W.Fleets)
        {
            if (f.Owner != e.Id) continue;
            foreach (var st in f.Ships)
                if (e.Design(st.Design) is { } d) upkeep += d.Cost * 0.02 * st.Count;
        }
        foreach (var s in W.Stars)
            if (s.Owner == e.Id) upkeep += s.Bases * 0.5;
        return gross <= 0 ? 1 : Math.Clamp(1 - upkeep / gross, 0.4, 1);
    }

    private double Raw(Star s, Empire e)
    {
        double working = Math.Min(s.Factories, s.Pop * Controls(e));
        return (s.Pop * 0.5 + working) * Planets.MineralFactor(s.Minerals) * e.Kind.Industry;
    }

    private readonly Dictionary<int, (int Turn, double F)> _upkeep = [];

    private double Upkeep(Empire e)
    {
        if (_upkeep.TryGetValue(e.Id, out var u) && u.Turn == W.Turn) return u.F;
        double f = UpkeepFactor(e);
        _upkeep[e.Id] = (W.Turn, f);
        return f;
    }

    /// <summary>A colony's accounts: what it makes, and where it goes by its sliders.</summary>
    public ColonyReport Report(Star s)
    {
        var e = W.Empires[s.Owner];
        var r = new ColonyReport
        {
            MaxPop = MaxPop(s, e),
            MaxFactories = s.Pop * Controls(e),
            Working = Math.Min(s.Factories, s.Pop * Controls(e)),
        };
        r.Gross = Raw(s, e) * Upkeep(e);
        r.Cleanup = r.Working * 0.1 * Least(e, Effect.Waste, 1) * Least(e, Effect.EcoCost, 1);
        r.Ships = r.Gross * s.Slider(Spend.Ships) / 100;
        r.Defense = r.Gross * s.Slider(Spend.Defense) / 100;
        r.Industry = r.Gross * s.Slider(Spend.Industry) / 100;
        r.Ecology = r.Gross * s.Slider(Spend.Ecology) / 100;
        r.Research = r.Gross * s.Slider(Spend.Research) / 100;
        r.RP = r.Research * e.Kind.Research * (s.Artifacts ? 2 : 1) * (1 + Best(e, Effect.ResearchBonus));
        return r;
    }

    /// <summary>The ecology share that just covers a colony's waste (and a little growth while it's young).</summary>
    public int EcoNeed(Star s)
    {
        var r = Report(s);
        if (r.Gross <= 0) return 0;
        int need = (int)Math.Ceiling(r.Cleanup / r.Gross * 100 / 5) * 5;
        if (s.Pop < r.MaxPop * 0.6) need += 5;
        return Math.Clamp(need, 0, 100);
    }

    /// <summary>Sets one slider and evens out the others so they still add to a hundred (research gives first).</summary>
    public static void SetSlider(Star s, Spend which, int value)
    {
        int i = (int)which;
        s.Sliders[i] = Math.Clamp(value, 0, 100);
        Spend[] order = [Spend.Research, Spend.Industry, Spend.Ships, Spend.Defense, Spend.Ecology];
        int diff = 100 - s.Sliders.Sum();
        foreach (var o in order)
        {
            if (diff == 0) break;
            if (o == which) continue;
            int j = (int)o;
            int change = diff > 0 ? diff : Math.Max(diff, -s.Sliders[j]);
            s.Sliders[j] += change;
            diff -= change;
        }
        if (diff != 0) s.Sliders[i] += diff;
    }

    // ───────────────────────────── a colony's year ─────────────────────────────

    /// <summary>One turn in a colony: ships and bases built, factories raised, waste cleaned, research done, people grown.</summary>
    public double RunColony(Star s)
    {
        var e = W.Empires[s.Owner];
        var r = Report(s);
        // The shipyard.
        var design = e.Design(s.Building);
        if (design is not null && !design.Retired)
        {
            s.ShipBC += r.Ships;
            int built = 0;
            while (s.ShipBC >= design.Cost && built < 50)
            {
                s.ShipBC -= design.Cost;
                built++;
            }
            if (built > 0)
            {
                AddShips(FleetAt(e.Id, s), design.Id, built);
                Tell(e.Id, $"{s.Name} builds {(built > 1 ? built + " " : "a ")}{design.Name}{(built > 1 ? "s" : "")}.", 0xFFC8D8F0);
            }
        }
        else e.Reserve += r.Ships * 0.5;
        // Missile bases.
        s.BaseBC += r.Defense;
        while (s.BaseBC >= BaseCost)
        {
            s.BaseBC -= BaseCost;
            s.Bases++;
        }
        // Factories.
        double cost = FactoryCost(e);
        if (s.Factories < r.MaxFactories)
        {
            double add = Math.Min(r.Industry / cost, r.MaxFactories - s.Factories);
            s.Factories += add;
            e.Reserve += (r.Industry - add * cost) * 0.5;
        }
        else e.Reserve += r.Industry * 0.5;
        // Ecology: clean the waste first, then undo old damage, then terraform, then grow.
        double eco = r.Ecology;
        if (eco >= r.Cleanup)
        {
            eco -= r.Cleanup;
            double repair = Math.Min(s.Waste, eco * 0.25);
            s.Waste -= repair;
            eco -= repair * 4;
            double terra = Best(e, Effect.Terraform);
            if (terra > s.Terraformed && eco > 0)
            {
                double add = Math.Min(eco / 6, terra - s.Terraformed);
                s.Terraformed += add;
                eco -= add * 6;
            }
            double max = MaxPop(s, e);
            if (eco > 0 && s.Pop < max)
            {
                double price = e.Known[Techs.Id("Cloning")] ? 5 : 10;
                s.Pop = Math.Min(max, s.Pop + eco / price);
            }
        }
        else s.Waste += (r.Cleanup - eco) * 0.25;
        // People.
        double room = MaxPop(s, e);
        if (s.Pop < room) s.Pop = Math.Min(room, s.Pop + Math.Max(0.15, s.Pop * (1 - s.Pop / room) * 0.1 * e.Kind.Growth));
        else if (s.Pop > room) s.Pop = Math.Max(room, s.Pop - (s.Pop - room) * 0.2 - 0.1);
        s.Factories = Math.Min(s.Factories, s.Pop * Controls(e) * 1.5);
        return r.RP;
    }

    /// <summary>Puts reserve credits into a colony's shipyard, up to what its current design still needs.</summary>
    public double Hurry(Star s, double most = double.MaxValue)
    {
        var e = W.Empires[s.Owner];
        var d = e.Design(s.Building);
        if (d is null || d.Retired) return 0;
        double put = Math.Max(0, Math.Min(Math.Min(e.Reserve, most), d.Cost - s.ShipBC));
        e.Reserve -= put;
        s.ShipBC += put;
        if (put > 0) Play(Sfx.Coin, e.Id);
        return put;
    }

    // ───────────────────────────── settling ─────────────────────────────

    /// <summary>Whether a fleet could settle the star it's at: a colony ship, an empty world it can live on, no enemy in orbit.</summary>
    public bool CanColonise(Fleet f, out string why)
    {
        why = "";
        var e = W.Empires[f.Owner];
        if (f.At < 0) { why = "The fleet is in flight."; return false; }
        var s = W.Stars[f.At];
        if (!f.Ships.Any(x => e.Design(x.Design)?.Colony == true)) { why = "The fleet has no colony ship."; return false; }
        if (!s.HasPlanet) { why = $"{s.Name} has no planet."; return false; }
        if (s.Owner >= 0) { why = $"{s.Name} is already settled."; return false; }
        if (!CanSettle(e, s.Planet)) { why = $"Your people cannot live on a {s.Kind.Name.ToLowerInvariant()} world without {s.Kind.Needs}."; return false; }
        if (W.Fleets.Any(o => o.At == s.Id && o.Owner != f.Owner && AtWar(o.Owner, f.Owner) && Armed(o))) { why = "Enemy warships hold the system."; return false; }
        return true;
    }

    public bool Colonise(Fleet f)
    {
        if (!CanColonise(f, out _)) return false;
        var e = W.Empires[f.Owner];
        var s = W.Stars[f.At];
        var stack = f.Ships.First(x => e.Design(x.Design)?.Colony == true);
        stack.Count--;
        if (stack.Count == 0) f.Ships.Remove(stack);
        if (f.Count == 0) W.Fleets.Remove(f);
        s.Owner = e.Id;
        s.Pop = ColonistsPerShip;
        s.Factories = 0;
        s.Bases = 0;
        s.Waste = 0;
        s.FoundedTurn = W.Turn;
        s.Sliders = [0, 0, 70, 20, 10];
        s.Building = e.Active.FirstOrDefault(d => d.Armed)?.Id ?? -1;
        e.Explored[s.Id] = true;
        Tell(e.Id, $"A colony is founded on {s.Name}.", 0xFFFFD860);
        if (IsPlayer(e.Id))
        {
            W.Note($"Colony founded on {s.Name}.");
            Post(new Event(Happen.Colonised, e.Id, $"Your colonists have landed on {s.Name}, a {Planets.SizeNames[(int)s.Size].ToLowerInvariant()} {s.Kind.Name.ToLowerInvariant()} world. {s.Kind.About}", Star: s.Id));
        }
        Play(Sfx.Colonise, e.Id);
        if (s.Vault) VaultOpened(e);
        return true;
    }

    public bool Armed(Fleet f)
    {
        var e = W.Empires[f.Owner];
        return !f.IsTransport && f.Ships.Any(x => e.Design(x.Design)?.Armed == true);
    }
}
