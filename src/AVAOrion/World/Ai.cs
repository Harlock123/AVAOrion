using AVAOrion.Rules;

namespace AVAOrion.World;

/// <summary>
/// The computer rulers. Each turn a ruler deals with its neighbours, keeps its ship designs up to
/// date, sets every colony's spending, chooses what each shipyard builds, and gives its fleets their
/// orders: scouts explore, colony ships settle the best worlds in reach, warships guard the colonies
/// and, in a war, gather and strike at the weakest enemy colony in range, and transports follow to
/// take it. Each people's nature colours every choice.
/// </summary>
public sealed class Ai(Sim sim)
{
    private WorldState W => sim.W;
    private Rng Rng => sim.Rng;

    private const int Colonise = 1, Explore = 2, Attack = 3, Guard = 4, Gather = 5;

    public void Play(Empire e)
    {
        if (!e.Alive || e.Monster || W.Over) return;
        Diplomacy(e);
        Studies(e);
        Redesign(e);
        foreach (var s in W.Stars.Where(s => s.Owner == e.Id).ToList()) Colony(e, s);
        Fleets(e);
        Transports(e);
    }

    // ───────────────────────────── neighbours ─────────────────────────────

    /// <summary>A people's armed might, all told.</summary>
    public double Might(int empire)
    {
        var e = W.Empires[empire];
        double m = 1;
        foreach (var f in W.Fleets)
            if (f.Owner == empire)
                foreach (var s in f.Ships)
                    if (e.Design(s.Design) is { } d) m += d.Power * s.Count;
        foreach (var s in W.Stars)
            if (s.Owner == empire) m += s.Bases * Sim.BasePower(e) * 0.5;
        return m;
    }

    private bool Neighbours(int a, int b) =>
        W.Stars.Any(x => x.Owner == a && W.Stars.Any(y => y.Owner == b && W.Distance(x, y) <= 9));

    private void Diplomacy(Empire e)
    {
        double mine = Might(e.Id);
        foreach (var o in W.Empires)
        {
            if (o.Id == e.Id || !o.Alive || o.Monster || !sim.Met(e.Id, o.Id)) continue;
            double theirs = Might(o.Id);
            int rest = (int)(20 - 40 * e.Kind.Aggression) + o.Kind.Diplomacy / 3 + (e.Trade[o.Id] ? 10 : 0) + (e.Treaties[o.Id] != Treaty.None ? 10 : 0);
            if (e.Attitude[o.Id] < rest) e.Attitude[o.Id]++;
            else if (e.Attitude[o.Id] > rest) e.Attitude[o.Id]--;
            bool close = Neighbours(e.Id, o.Id);
            if (close && Rng.Chance(25)) e.Attitude[o.Id] = Math.Max(-100, e.Attitude[o.Id] - 1);
            if (!sim.AtWar(e.Id, o.Id))
            {
                double ratio = Math.Clamp(mine / theirs, 0.3, 3);
                double urge = (0.2 + e.Kind.Aggression * 2) * Math.Max(0, ratio - 0.8) * (close ? 1.5 : 0.3) * Math.Clamp((40 - e.Attitude[o.Id]) / 50.0, 0, 2);
                if (e.Treaties[o.Id] == Treaty.NonAggression) urge *= 0.3;
                if (e.Treaties[o.Id] == Treaty.Alliance) urge *= 0.05;
                if (o.Human) urge *= 0.6 + W.Level * 0.25;
                if (W.Turn < 30) urge *= 0.2;
                if (W.Empires.Any(x => x.Alive && !x.Monster && x.Id != e.Id && sim.AtWar(e.Id, x.Id))) urge *= 0.3;
                if (Rng.Next(1000) < urge * 30 && e.Cooldown[o.Id] == 0)
                {
                    sim.DeclareWar(e.Id, o.Id);
                    continue;
                }
                // Treaties with those it likes.
                if (!o.Human)
                {
                    if (!e.Trade[o.Id] && e.Attitude[o.Id] > 5 && o.Attitude[e.Id] > 5 && Rng.Chance(5)) sim.SignTrade(e.Id, o.Id);
                    if (e.Treaties[o.Id] == Treaty.None && e.Attitude[o.Id] > 30 && o.Attitude[e.Id] > 30 && Rng.Chance(4)) sim.Sign(e.Id, o.Id, Treaty.NonAggression);
                }
                else if (mine > theirs * 1.6 && Rng.Next(1000) < e.Kind.Aggression * 20 && W.Turn > 40) Demand(e, o);
            }
            else if (e.Cooldown[o.Id] == 0)
            {
                bool losing = mine < theirs * 0.8;
                bool tired = Rng.Next(100) < 4 + (1 - e.Kind.Aggression) * 8;
                if (!(losing || tired)) continue;
                if (o.Human)
                {
                    if (Rng.Chance(30))
                    {
                        e.Cooldown[o.Id] = 8;
                        sim.Post(new Event(Happen.Offer, o.Id, $"{e.Ruler} of the {e.Name} sends word: \"Enough. Let there be peace between us.\"", e.Id));
                    }
                }
                else if (WouldMakePeace(o, e.Id)) sim.MakePeace(e.Id, o.Id);
                else e.Cooldown[o.Id] = 5;
            }
        }
    }

    private void Demand(Empire e, Empire human)
    {
        var tech = Enumerable.Range(0, Techs.Count).Where(t => human.Known[t] && !e.Known[t]).OrderBy(_ => Rng.Next(100)).FirstOrDefault(-1);
        int bc = human.Reserve >= 100 ? (int)Math.Min(human.Reserve / 2, 50 + W.Turn) : 0;
        if (tech < 0 && bc == 0) return;
        if (tech >= 0) bc = 0;
        string what = tech >= 0 ? $"the secret of {Techs.All[tech].Name}" : $"{bc} BC";
        sim.Post(new Event(Happen.Demand, human.Id, $"{e.Ruler} of the {e.Name} demands {what}, \"or our fleets will come for it.\"", e.Id, bc, tech));
    }

    /// <summary>Whether a computer ruler would make peace.</summary>
    public bool WouldMakePeace(Empire e, int other)
    {
        if (!sim.AtWar(e.Id, other) || e.Cooldown[other] > 0) return false;
        double mine = Might(e.Id), theirs = Might(other);
        double will = 40 - e.Kind.Aggression * 40 + e.Attitude[other] / 2.0 + (theirs > mine ? 25 : -10) + W.Empires[other].Kind.Diplomacy / 2.0;
        if (e.Betrayed[other] > W.Turn - 30) will -= 30;
        return will > 25;
    }

    /// <summary>Whether a computer ruler would sign a treaty (or a trade agreement) with the player.</summary>
    public bool WouldSign(Empire e, int other, Treaty t, bool trade = false)
    {
        if (sim.AtWar(e.Id, other)) return false;
        int bonus = W.Empires[other].Kind.Diplomacy;
        int need = trade ? 0 : t == Treaty.NonAggression ? 20 : 50;
        return e.Attitude[other] + bonus / 2 >= need;
    }

    /// <summary>Whether a computer ruler would swap one discovery for another with the player.</summary>
    public bool WouldTrade(Empire e, int give, int get)
    {
        if (sim.AtWar(e.Id, W.Player) || e.Attitude[W.Player] < -20) return false;
        return Techs.All[get].Level >= Techs.All[give].Level - 2;
    }

    // ───────────────────────────── studies and designs ─────────────────────────────

    private void Studies(Empire e)
    {
        var alloc = new int[6];
        for (int f = 0; f < 6; f++) alloc[f] = 12;
        foreach (var l in e.Kind.Leanings) alloc[(int)l] += 10;
        bool war = W.Empires.Any(o => o.Alive && !o.Monster && sim.AtWar(e.Id, o.Id));
        if (war)
        {
            alloc[(int)Field.Weapons] += 8;
            alloc[(int)Field.ForceFields] += 4;
        }
        // Early on, reach and new worlds matter most.
        if (W.Turn < 60)
        {
            alloc[(int)Field.Propulsion] += 6;
            alloc[(int)Field.Planetology] += 6;
        }
        int sum = alloc.Sum();
        for (int f = 0; f < 6; f++) alloc[f] = alloc[f] * 100 / sum;
        alloc[(int)Field.Construction] += 100 - alloc.Sum();
        e.Alloc = alloc;
    }

    private void Redesign(Empire e)
    {
        if (W.Turn % 10 != e.Id % 10 && e.Active.Any(d => d.Armed)) return;
        double best = W.Stars.Where(s => s.Owner == e.Id).Select(s => sim.Report(s).Gross).DefaultIfEmpty(0).Max();
        var hull = Designer.HullFor(best);
        var fresh = Designer.Warship(e, hull);
        var current = e.Active.Where(d => d.Armed && d.Hull == hull).OrderByDescending(d => d.Id).FirstOrDefault();
        if (current is null || fresh.Power > current.Power * 1.15) Designer.Add(e, fresh);
        var colony = e.Active.FirstOrDefault(d => d.Colony);
        if (colony is null || colony.Warp < Sim.Warp(e)) Designer.Add(e, Designer.ColonyShip(e));
        var scout = e.Active.FirstOrDefault(d => d.Tanks && !d.Armed && !d.Colony);
        if (scout is null) Designer.Add(e, Designer.Scout(e));
    }

    private ShipDesign? BestWarship(Empire e, double bc)
    {
        var armed = e.Active.Where(d => d.Armed).ToList();
        // A ship it can finish within a few turns, the best for its cost.
        var affordable = armed.Where(d => d.Cost <= Math.Max(bc * 6, 30)).ToList();
        return (affordable.Count > 0 ? affordable : armed).OrderByDescending(d => d.Power).FirstOrDefault();
    }

    // ───────────────────────────── colonies ─────────────────────────────

    private bool Threatened(Empire e, Star s) =>
        W.Fleets.Any(f => f.Owner != e.Id && sim.AtWar(e.Id, f.Owner) && sim.Armed(f) && WorldState.Distance(f.X, f.Y, s.X, s.Y) <= 6)
        || W.Stars.Any(o => o.Owner >= 0 && o.Owner != e.Id && sim.AtWar(e.Id, o.Owner) && W.Distance(o, s) <= 7);

    private void Colony(Empire e, Star s)
    {
        var r = sim.Report(s);
        bool war = W.Empires.Any(o => o.Alive && !o.Monster && sim.AtWar(e.Id, o.Id));
        bool young = s.Pop < 12 || W.Turn - s.FoundedTurn < 10 && !s.Capital;
        int eco = sim.EcoNeed(s);
        int industry = s.Factories < r.MaxFactories - 0.5 ? (young ? 70 : 45) : 0;
        int ships = young ? 0 : war ? 40 : (int)(12 + e.Kind.Aggression * 18);
        if (ColonyTargets(e).Count > 0 && s.Capital) ships = Math.Max(ships, 25);
        int defense = 0;
        if (!young && Threatened(e, s) && s.Bases < 2 + W.Turn / 40) defense = 15;
        int[] sliders = [ships, defense, industry, eco, 0];
        int total = sliders.Sum();
        if (total > 100)
        {
            double k = (100.0 - eco) / Math.Max(1, total - eco);
            sliders[0] = (int)(sliders[0] * k);
            sliders[1] = (int)(sliders[1] * k);
            sliders[2] = (int)(sliders[2] * k);
        }
        sliders[4] = 100 - sliders.Take(4).Sum();
        s.Sliders = sliders;
        // What the shipyard builds.
        var colonyDesign = e.Active.Where(d => d.Colony).OrderByDescending(d => d.Id).FirstOrDefault();
        var scoutDesign = e.Active.Where(d => d.Tanks && !d.Armed && !d.Colony).OrderByDescending(d => d.Id).FirstOrDefault();
        int colonyShips = Count(e, d => d.Colony) + W.Stars.Count(x => x.Owner == e.Id && e.Design(x.Building)?.Colony == true && x != s);
        int scouts = Count(e, d => d.Tanks && !d.Armed && !d.Colony);
        int targets = ColonyTargets(e).Count;
        if (colonyDesign is not null && targets > colonyShips && colonyShips < 3 && (s.Capital || r.Gross > 30) && !(war && Threatened(e, s)))
            s.Building = colonyDesign.Id;
        else if (scoutDesign is not null && scouts < 2 && W.Turn < 80 && Unexplored(e).Count > 0 && s.Capital)
            s.Building = scoutDesign.Id;
        else s.Building = BestWarship(e, r.Ships)?.Id ?? -1;
        // A full treasury hurries the shipyards, the threatened ones first.
        if (e.Reserve > 120 + W.Turn && (Threatened(e, s) || war || Rng.Chance(30))) sim.Hurry(s, e.Reserve / 3);
    }

    private int Count(Empire e, Func<ShipDesign, bool> which)
    {
        int n = 0;
        foreach (var f in W.Fleets)
            if (f.Owner == e.Id)
                foreach (var s in f.Ships)
                    if (e.Design(s.Design) is { } d && which(d)) n += s.Count;
        return n;
    }

    /// <summary>Worlds a people could settle and reach, best first.</summary>
    private List<Star> ColonyTargets(Empire e) =>
        W.Stars.Where(s => s.Owner < 0 && s.HasPlanet && e.Explored[s.Id] && Sim.CanSettle(e, s.Planet) && sim.InRange(e.Id, s)
                           && !W.Fleets.Any(f => f.At == s.Id && f.Owner != e.Id && sim.Armed(f) && (sim.AtWar(e.Id, f.Owner) || W.Empires[f.Owner].Monster)))
            .OrderByDescending(s => sim.MaxPop(s, e) * Planets.MineralFactor(s.Minerals) * (s.Artifacts ? 1.5 : 1) - sim.Reach(e.Id, s.X, s.Y) * 3)
            .ToList();

    private List<Star> Unexplored(Empire e) =>
        W.Stars.Where(s => !e.Explored[s.Id] && sim.InRange(e.Id, s, tanks: true)).ToList();

    // ───────────────────────────── fleets ─────────────────────────────

    private void Fleets(Empire e)
    {
        var claimed = new HashSet<int>(W.Fleets.Where(f => f.Owner == e.Id && f.Task is Colonise or Explore && f.Dest >= 0).Select(f => f.Dest));
        foreach (var f in W.Fleets.Where(f => f.Owner == e.Id && f.At >= 0 && !f.IsTransport).ToList())
        {
            if (!W.Fleets.Contains(f)) continue;
            var here = W.Stars[f.At];
            // Colony ships: settle here if this is a target, else go to the best one.
            foreach (var st in f.Ships.Where(x => e.Design(x.Design)?.Colony == true).ToList())
            {
                for (int i = 0; i < st.Count; i++)
                {
                    if (here.Owner < 0 && sim.CanColonise(f, out _) && ColonyTargets(e).Contains(here))
                    {
                        sim.Colonise(f);
                        break;
                    }
                    var target = ColonyTargets(e).FirstOrDefault(t => !claimed.Contains(t.Id));
                    if (target is null) break;
                    var go = sim.Send(f, target, new Dictionary<int, int> { [st.Design] = 1 });
                    if (go is null) break;
                    go.Task = Colonise;
                    claimed.Add(target.Id);
                    if (go == f) break;
                }
            }
            if (!W.Fleets.Contains(f) || f.At < 0) continue;
            // Scouts: off to the nearest unknown star.
            foreach (var st in f.Ships.Where(x => e.Design(x.Design) is { } d && !d.Armed && !d.Colony).ToList())
            {
                for (int i = st.Count - 1; i >= 0; i--)
                {
                    var target = Unexplored(e).Where(t => !claimed.Contains(t.Id)).OrderBy(t => W.Distance(t, here)).FirstOrDefault();
                    if (target is null) break;
                    var go = sim.Send(f, target, new Dictionary<int, int> { [st.Design] = 1 });
                    if (go is null) break;
                    go.Task = Explore;
                    claimed.Add(target.Id);
                    if (go == f) break;
                }
            }
            if (!W.Fleets.Contains(f) || f.At < 0) continue;
            Warships(e, f);
        }
    }

    private double FleetPower(Empire e, Fleet f) =>
        f.Ships.Sum(s => (e.Design(s.Design)?.Power ?? 0) * s.Count);

    /// <summary>A star's strength against an attacker: the owner's bases and every hostile armed fleet there.</summary>
    private double Defence(Empire attacker, Star s)
    {
        double d = 0;
        if (s.Owner >= 0 && s.Owner != attacker.Id) d += s.Bases * Sim.BasePower(W.Empires[s.Owner]);
        foreach (var f in W.Fleets)
            if (f.At == s.Id && f.Owner != attacker.Id && (sim.AtWar(attacker.Id, f.Owner) || W.Empires[f.Owner].Monster))
                d += FleetPower(W.Empires[f.Owner], f);
        return d;
    }

    private void Warships(Empire e, Fleet f)
    {
        var here = W.Stars[f.At];
        var armed = f.Ships.Where(s => e.Design(s.Design)?.Armed == true).ToDictionary(s => s.Design, s => s.Count);
        if (armed.Count == 0) return;
        double power = armed.Sum(kv => e.Design(kv.Key)!.Power * kv.Value);
        // Defend a colony under threat nearby.
        var threatened = W.Stars.Where(s => s.Owner == e.Id && s != here && W.Fleets.Any(o => o.At == s.Id && o.Owner != e.Id && sim.Armed(o) && (sim.AtWar(e.Id, o.Owner) || W.Empires[o.Owner].Monster)))
            .OrderBy(s => W.Distance(s, here)).FirstOrDefault();
        if (threatened is not null && Defence(e, threatened) < power * 1.2 && W.Distance(threatened, here) < 12)
        {
            Go(f, threatened, armed, Guard);
            return;
        }
        // In a war: strike the weakest enemy colony in range, or gather nearer to it.
        var enemies = W.Empires.Where(o => o.Alive && !o.Monster && sim.AtWar(e.Id, o.Id)).Select(o => o.Id).ToHashSet();
        if (enemies.Count > 0)
        {
            var target = W.Stars.Where(s => s.Owner >= 0 && enemies.Contains(s.Owner) && sim.InRange(e.Id, s))
                .OrderBy(s => Defence(e, s) + W.Distance(s, here) * 3).FirstOrDefault();
            if (target is not null)
            {
                double defence = Defence(e, target);
                if (power >= defence * 1.4 + 10)
                {
                    Go(f, target, armed, Attack);
                    return;
                }
                var stage = W.Stars.Where(s => s.Owner == e.Id).OrderBy(s => W.Distance(s, target)).First();
                if (stage != here && (here.Owner != e.Id || W.Distance(stage, target) < W.Distance(here, target) - 1))
                {
                    Go(f, stage, armed, Gather);
                    return;
                }
                return;
            }
        }
        // The Vault, once strong enough.
        var vault = W.Stars.FirstOrDefault(s => s.Vault && s.Owner < 0);
        if (vault is not null && sim.InRange(e.Id, vault) && W.Fleets.Any(m => m.At == vault.Id && W.Empires[m.Owner].Monster) && power > Defence(e, vault) * 1.5)
        {
            Go(f, vault, armed, Attack);
            return;
        }
        // Monsters at a colony ship's target, or near home: clear them if able.
        var lair = W.Stars.Where(s => !s.Vault && sim.InRange(e.Id, s) && W.Fleets.Any(m => m.At == s.Id && W.Empires[m.Owner].Monster))
            .OrderBy(s => W.Distance(s, here)).FirstOrDefault();
        if (lair is not null && power > Defence(e, lair) * 1.5)
        {
            Go(f, lair, armed, Attack);
            return;
        }
        // Otherwise home to the nearest colony.
        if (here.Owner != e.Id && sim.NearestColony(e.Id, here.X, here.Y) is { } home) Go(f, home, armed, Guard);
    }

    private void Go(Fleet f, Star to, Dictionary<int, int> ships, int task)
    {
        if (f.At == to.Id) return;
        var go = sim.Send(f, to, ships);
        if (go is not null)
        {
            go.Task = task;
            go.TaskStar = to.Id;
        }
    }

    // ───────────────────────────── invasions ─────────────────────────────

    /// <summary>Where its warships hold an enemy colony's orbit and its bases are gone, a people sends soldiers down.</summary>
    private void Transports(Empire e)
    {
        foreach (var s in W.Stars.Where(s => s.Owner >= 0 && s.Owner != e.Id && sim.AtWar(e.Id, s.Owner)).ToList())
        {
            bool held = W.Fleets.Any(f => f.At == s.Id && f.Owner == e.Id && sim.Armed(f)) && s.Bases == 0
                        && !W.Fleets.Any(f => f.At == s.Id && f.Owner == s.Owner && sim.Armed(f));
            if (!held) continue;
            if (W.Fleets.Any(f => f.IsTransport && f.Owner == e.Id && f.Dest == s.Id)) continue;
            var ed = W.Empires[s.Owner];
            double odds = (1 + Sim.Ground(e)) / (1 + Sim.Ground(ed) + 0.2);
            int need = (int)Math.Ceiling(s.Pop / odds * 1.4) + 4;
            var from = W.Stars.Where(x => x.Owner == e.Id && x.Pop - need > 8 && sim.InRange(e.Id, s)).OrderBy(x => W.Distance(x, s)).FirstOrDefault();
            if (from is null) continue;
            sim.SendTransports(from, s, need, out _);
        }
    }
}
