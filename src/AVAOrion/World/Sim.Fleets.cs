using AVAOrion.Rules;

namespace AVAOrion.World;

public sealed partial class Sim
{
    // ───────────────────────────── orders ─────────────────────────────

    /// <summary>Whether every ship in a fleet carries reserve tanks (three parsecs more range).</summary>
    public bool Tanked(Fleet f)
    {
        var e = W.Empires[f.Owner];
        return f.Ships.Count > 0 && f.Ships.All(s => e.Design(s.Design)?.Tanks == true);
    }

    public bool CanSend(Fleet f, Star dest, out string why)
    {
        why = "";
        if (f.At == dest.Id && f.Dest < 0) { why = "The fleet is already there."; return false; }
        var e = W.Empires[f.Owner];
        if (e.Monster) return true;
        if (dest.Owner == f.Owner) return true;
        if (!InRange(f.Owner, dest, Tanked(f)))
        {
            why = $"{dest.Name} is out of range: {Reach(f.Owner, dest.X, dest.Y):0.0} parsecs from your nearest colony, and your ships carry fuel for {Range(e) + (Tanked(f) ? 3 : 0):0}.";
            return false;
        }
        return true;
    }

    /// <summary>
    /// Sends a fleet (or some of its ships, split off into a fleet of their own) to a star. Returns
    /// the fleet that's going, or null if it can't.
    /// </summary>
    public Fleet? Send(Fleet f, Star dest, IReadOnlyDictionary<int, int>? some = null, bool retreat = false)
    {
        if (!retreat && !CanSend(f, dest, out _)) return null;
        var going = f;
        if (some is not null && some.Any(kv => kv.Value > 0) && some.Sum(kv => kv.Value) < f.Count && f.At >= 0)
        {
            going = NewFleet(f.Owner, W.Stars[f.At]);
            foreach (var (design, n) in some)
            {
                var st = f.Ships.FirstOrDefault(s => s.Design == design);
                if (st is null || n <= 0) continue;
                int take = Math.Min(n, st.Count);
                st.Count -= take;
                AddShips(going, design, take);
            }
            f.Ships.RemoveAll(s => s.Count == 0);
        }
        if (going.At >= 0) going.From = going.At;
        going.At = -1;
        going.Dest = dest.Id;
        going.Eta = TravelTurns(going, dest);
        if (going.X == dest.X && going.Y == dest.Y)
        {
            going.At = dest.Id;
            going.Dest = -1;
            going.Eta = 0;
        }
        return going;
    }

    /// <summary>Loads colonists into transports bound for a star: to swell a colony of one's own, or to invade an enemy's.</summary>
    public Fleet? SendTransports(Star from, Star to, int troops, out string why)
    {
        why = "";
        if (from.Owner < 0) { why = "No colony here."; return null; }
        int owner = from.Owner;
        if (troops < 1 || troops > (int)from.Pop - 1) { why = "Not enough people to spare."; return null; }
        if (to == from) { why = "Choose another star."; return null; }
        if (!to.HasPlanet) { why = $"{to.Name} has no planet to land on."; return null; }
        if (to.Owner >= 0 && to.Owner != owner && !AtWar(owner, to.Owner)) { why = $"You are not at war with the {W.Empires[to.Owner].Name}."; return null; }
        if (to.Owner < 0) { why = "Transports land on colonies (your own, or an enemy's). To settle a new world, send a colony ship."; return null; }
        if (!InRange(owner, to)) { why = $"{to.Name} is out of range."; return null; }
        from.Pop -= troops;
        var f = new Fleet { Id = W.NextId++, Owner = owner, Troops = troops, From = from.Id, X = from.X, Y = from.Y, Dest = to.Id };
        f.Eta = TravelTurns(f, to);
        W.Fleets.Add(f);
        Play(Sfx.Launch, owner);
        return f;
    }

    // ───────────────────────────── flight ─────────────────────────────

    /// <summary>Every fleet in flight moves a turn closer; those that arrive join whatever's there.</summary>
    private void Fly()
    {
        foreach (var f in W.Fleets.ToList())
        {
            if (f.Dest < 0) continue;
            var dest = W.Stars[f.Dest];
            f.Eta--;
            if (f.Eta <= 0)
            {
                f.X = dest.X;
                f.Y = dest.Y;
                f.At = dest.Id;
                f.Dest = -1;
                if (!W.Empires[f.Owner].Monster) W.Empires[f.Owner].Explored[dest.Id] = true;
                if (dest.Vault && IsPlayer(f.Owner) && W.Fleets.Any(m => m.At == dest.Id && W.Empires[m.Owner].Monster))
                    Tell(f.Owner, "At Aeon your ships find the Warden waiting.", 0xFFE05040);
                if (IsPlayer(f.Owner) && dest.Owner < 0 && dest.HasPlanet && CanSettle(W.Empires[f.Owner], dest.Planet)
                    && f.Ships.Any(x => W.Empires[f.Owner].Design(x.Design)?.Colony == true))
                    Tell(f.Owner, $"A colony ship has reached {dest.Name}: press F there to found a colony.", 0xFFFFD860);
                continue;
            }
            double d = WorldState.Distance(f.X, f.Y, dest.X, dest.Y);
            double step = Math.Min(d, d / (f.Eta + 1));
            f.X += (dest.X - f.X) / d * step;
            f.Y += (dest.Y - f.Y) / d * step;
        }
        foreach (var s in W.Stars) Merge(s);
    }

    /// <summary>The nearest colony of a people to a point, for retreating fleets.</summary>
    public Star? NearestColony(int empire, double x, double y) =>
        W.Stars.Where(s => s.Owner == empire).OrderBy(s => WorldState.Distance(s.X, s.Y, x, y)).FirstOrDefault();

    // ───────────────────────────── landings ─────────────────────────────

    /// <summary>Transports that have arrived come down: on a colony of their own they join it; on an enemy's they fight for it.</summary>
    private void Landings()
    {
        foreach (var f in W.Fleets.Where(f => f.IsTransport && f.At >= 0).ToList())
        {
            W.Fleets.Remove(f);
            var s = W.Stars[f.At];
            var e = W.Empires[f.Owner];
            if (s.Owner == f.Owner)
            {
                s.Pop = Math.Min(MaxPop(s, e), s.Pop + f.Troops);
                Tell(f.Owner, $"{f.Troops} colonists land on {s.Name}.", 0xFFC8D8F0);
                continue;
            }
            if (s.Owner < 0)
            {
                Tell(f.Owner, $"The transports find no one to land with at {s.Name}, and are lost.", 0xFFE0A040);
                continue;
            }
            int defender = s.Owner;
            if (!AtWar(f.Owner, defender)) continue;
            // Enemy warships in orbit, or the bases, shoot the landing craft down.
            int troops = f.Troops;
            bool guarded = W.Fleets.Any(o => o.At == s.Id && o.Owner == defender && Armed(o));
            if (guarded) troops = (int)(troops * 0.3);
            for (int i = 0; i < s.Bases && troops > 0; i++) troops -= Rng.Range(1, 3);
            Play(Sfx.Landing, f.Owner);
            if (troops <= 0)
            {
                Tell(f.Owner, $"Our transports are shot down over {s.Name}.", 0xFFE05040);
                Tell(defender, $"Enemy transports are shot down over {s.Name}.", 0xFF60D080);
                continue;
            }
            Invade(s, f.Owner, troops);
        }
    }

    /// <summary>Soldiers against colonists, one against one, until one side is gone. The winner holds the world.</summary>
    public bool Invade(Star s, int attacker, int troops)
    {
        int defender = s.Owner;
        var ea = W.Empires[attacker];
        var ed = W.Empires[defender];
        double a = 1 + Ground(ea), d = 1 + Ground(ed) + 0.2;
        int att = troops, def = (int)Math.Ceiling(s.Pop);
        double p = a / (a + d);
        while (att > 0 && def > 0)
        {
            if (Rng.Next(10000) < p * 10000) def--;
            else att--;
        }
        if (att <= 0)
        {
            s.Pop = Math.Max(1, Math.Min(s.Pop, def));
            Tell(attacker, $"Our invasion of {s.Name} is thrown back.", 0xFFE05040);
            if (IsPlayer(defender)) Post(new Event(Happen.Battle, defender, $"An invasion of {s.Name} by the {ea.Name} has been thrown back!", Star: s.Id));
            return false;
        }
        Capture(s, attacker, att);
        return true;
    }

    /// <summary>A colony changes hands: the new owners keep most of the factories, and learn something from the old.</summary>
    public void Capture(Star s, int victor, int people)
    {
        int loser = s.Owner;
        var ev = W.Empires[victor];
        var el = W.Empires[loser];
        bool capital = s.Capital;
        s.Owner = victor;
        s.Pop = people;
        s.Factories *= 0.7;
        s.Bases = 0;
        s.BaseDamage = 0;
        s.ShipBC = 0;
        s.Capital = false;
        s.Sliders = [0, 0, 60, 20, 20];
        s.Building = ev.Active.FirstOrDefault(d => d.Armed)?.Id ?? -1;
        ev.Explored[s.Id] = true;
        var stolen = Enumerable.Range(0, Techs.Count).Where(t => el.Known[t] && !ev.Known[t]).OrderBy(_ => Rng.Next(1000)).FirstOrDefault(-1);
        string theft = "";
        if (stolen >= 0)
        {
            Learn(ev, stolen, $"the archives of {s.Name}");
            theft = $" Its archives teach us {Techs.All[stolen].Name}.";
        }
        if (IsPlayer(victor))
        {
            Post(new Event(Happen.ColonyTaken, victor, $"Our soldiers have taken {s.Name} from the {el.Name}.{theft}", Star: s.Id));
            W.Note($"Took {s.Name} from the {el.Name}.");
        }
        else if (IsPlayer(loser))
        {
            Post(new Event(Happen.ColonyLost, loser, $"The {ev.Name} have invaded and taken {s.Name}!", Star: s.Id));
            W.Note($"Lost {s.Name} to the {ev.Name}.");
            Play(Sfx.Alarm);
        }
        else Tell(-1, $"The {ev.Name} take {s.Name} from the {el.Name}.", 0xFFE0A040);
        if (capital) NewCapital(loser);
        CheckFallen(loser);
    }

    private void NewCapital(int empire)
    {
        var next = W.Stars.Where(x => x.Owner == empire).OrderByDescending(x => x.Pop).FirstOrDefault();
        if (next is not null) next.Capital = true;
    }

    /// <summary>A colony bombed to nothing: the world is empty again.</summary>
    private void Wipe(Star s)
    {
        int loser = s.Owner;
        bool capital = s.Capital;
        s.Owner = -1;
        s.Pop = 0;
        s.Factories = 0;
        s.Bases = 0;
        s.Capital = false;
        s.Waste = 0;
        if (capital) NewCapital(loser);
        if (IsPlayer(loser))
        {
            Post(new Event(Happen.ColonyLost, loser, $"{s.Name} has been bombed to ruin. No one is left alive.", Star: s.Id));
            W.Note($"{s.Name} destroyed.");
        }
        CheckFallen(loser);
    }
}
