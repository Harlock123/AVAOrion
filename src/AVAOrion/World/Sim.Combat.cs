using AVAOrion.Rules;

namespace AVAOrion.World;

/// <summary>One side's ships of one design in a fight, before and after.</summary>
public sealed class BattleGroup
{
    public string Name = "";
    public Hull Hull;
    public int Start, End;
}

/// <summary>Shots in one round from one group (or a planet's bases, Group -1) at another (or the bases).</summary>
public readonly record struct Volley(int Round, int Side, int Group, int Target, int Hits, int Kills, bool Missile);

/// <summary>A fight at a star, kept so the player can watch it play out.</summary>
public sealed class BattleReport
{
    public string Star = "";
    public int[] Empires = new int[2];
    public List<BattleGroup>[] Groups = [[], []];
    /// <summary>Which side the planet fought for, or -1; and its bases before and after.</summary>
    public int PlanetSide = -1;
    public int BasesStart, BasesEnd;
    public double PopLost;
    public List<Volley> Volleys = [];
    public int Rounds;
    /// <summary>The side left holding the system, or -1 if neither (a draw or both fled).</summary>
    public int Winner = -1;
    public bool[] Retreated = new bool[2];
}

public sealed partial class Sim
{
    public const int BaseHp = 40, Rounds = 10;

    private sealed class Unit(Fleet fleet, Stack stack, ShipDesign design)
    {
        public Fleet Fleet = fleet;
        public Stack Stack = stack;
        public ShipDesign Design = design;
        public int Count = stack.Count;
        public int Start = stack.Count;
        public double Hp = design.Hp;
        public int[] Ammo = design.Mounts.Select(m => Ships.Weapons[m.Weapon].Ammo).ToArray();
    }

    // ───────────────────────────── finding fights ─────────────────────────────

    /// <summary>Wherever peoples at war meet, they fight, each pair in turn, the system's owner first.</summary>
    private void Fights()
    {
        foreach (var star in W.Stars)
        {
            for (int guard = 0; guard < 6; guard++)
            {
                var here = W.Fleets.Where(f => f.At == star.Id && !f.IsTransport && f.Count > 0).Select(f => f.Owner).Distinct().ToList();
                if (star.Owner >= 0 && !here.Contains(star.Owner)) here.Insert(0, star.Owner);
                (int A, int B)? pair = null;
                foreach (int a in here)
                {
                    foreach (int b in here)
                        if (a != b && AtWar(a, b) && (Armed(a, star) || Armed(b, star))) { pair = (a, b); break; }
                    if (pair is not null) break;
                }
                if (pair is null) break;
                var result = Fight(star, pair.Value.A, pair.Value.B);
                if (result.Winner < 0 && !result.Retreated[0] && !result.Retreated[1]) break;
            }
        }
    }

    private bool Armed(int empire, Star star) =>
        (star.Owner == empire && star.Bases > 0) || W.Fleets.Any(f => f.At == star.Id && f.Owner == empire && Armed(f));

    /// <summary>A people's armed might at a star: its warships and (if its colony) its bases.</summary>
    public double Power(int empire, Star star)
    {
        var e = W.Empires[empire];
        double p = 0;
        foreach (var f in W.Fleets)
            if (f.At == star.Id && f.Owner == empire)
                foreach (var s in f.Ships)
                    if (e.Design(s.Design) is { } d) p += d.Power * s.Count;
        if (star.Owner == empire) p += star.Bases * BasePower(e);
        return p;
    }

    public static double BasePower(Empire e)
    {
        var w = BaseWeapon(e);
        return w.Average * 2 * (1 + ComputerTier(e) * 0.15) * Math.Sqrt(BaseHp);
    }

    private static Weapon BaseWeapon(Empire e) => Arsenal(e).Where(w => !w.Missile).OrderByDescending(w => w.Average).FirstOrDefault() ?? Ships.Weapons[0];

    // ───────────────────────────── the fight ─────────────────────────────

    /// <summary>
    /// Ten rounds at most. Every gun fires each round at an enemy group (or the planet's bases):
    /// a beam hits on its computer against the target's evasion, a missile against its jammers;
    /// shields take their points off every hit. A side hopelessly outgunned withdraws to its
    /// nearest colony. Whoever holds the system afterwards bombs an enemy colony there.
    /// </summary>
    public BattleReport Fight(Star star, int a, int b)
    {
        int[] who = [a, b];
        var units = new List<Unit>[2];
        var report = new BattleReport { Star = star.Name, Empires = [a, b], PlanetSide = star.Owner == a ? 0 : star.Owner == b ? 1 : -1, BasesStart = star.Bases };
        for (int side = 0; side < 2; side++)
        {
            var e = W.Empires[who[side]];
            units[side] = [];
            foreach (var f in W.Fleets.Where(f => f.At == star.Id && f.Owner == who[side] && !f.IsTransport))
                foreach (var st in f.Ships)
                    if (e.Design(st.Design) is { } d && st.Count > 0)
                        units[side].Add(new Unit(f, st, d));
            report.Groups[side] = units[side].Select(u => new BattleGroup { Name = u.Design.Name, Hull = u.Design.Hull, Start = u.Count }).ToList();
        }
        star.BaseDamage = 0;
        int round;
        for (round = 1; round <= Rounds; round++)
        {
            for (int side = 0; side < 2; side++)
            {
                if (report.Retreated[side]) continue;
                Volleys(star, report, units, who, side, round);
                if (report.PlanetSide == side && star.Bases > 0) BaseVolleys(star, report, units, who, side, round);
            }
            if (Done(star, report, units)) break;
            // The hopelessly outgunned withdraw (but not from their own colony).
            if (round >= 2)
                for (int side = 0; side < 2; side++)
                {
                    if (report.Retreated[side] || report.PlanetSide == side) continue;
                    double mine = SidePower(units[side], star, report, side), theirs = SidePower(units[1 - side], star, report, 1 - side);
                    if (mine < theirs * 0.3 || (mine == 0 && units[side].Any(u => u.Count > 0)))
                        report.Retreated[side] = true;
                }
            if (report.Retreated[0] || report.Retreated[1]) break;
        }
        report.Rounds = Math.Min(round, Rounds);
        // Write the losses back to the fleets.
        for (int side = 0; side < 2; side++)
        {
            var e = W.Empires[who[side]];
            int lost = 0;
            for (int i = 0; i < units[side].Count; i++)
            {
                var u = units[side][i];
                lost += u.Start - u.Count;
                u.Stack.Count = u.Count;
                report.Groups[side][i].End = u.Count;
            }
            e.Losses += lost;
            W.Empires[who[1 - side]].Kills += lost;
            foreach (var f in units[side].Select(u => u.Fleet).Distinct()) f.Ships.RemoveAll(s => s.Count <= 0);
        }
        W.Fleets.RemoveAll(f => !f.IsTransport && f.Count == 0);
        report.BasesEnd = star.Bases;
        // Who holds the field: the side with armed ships or bases left, if the other has none.
        bool[] stands = [Standing(star, units[0], report, 0), Standing(star, units[1], report, 1)];
        report.Winner = stands[0] && !stands[1] ? 0 : stands[1] && !stands[0] ? 1 : -1;
        // The losers' unarmed ships can't hold the system: they flee, or are taken apart.
        for (int side = 0; side < 2; side++)
        {
            bool beaten = report.Retreated[side] || report.Winner == 1 - side;
            if (!beaten) continue;
            var home = NearestColony(who[side], star.X, star.Y);
            foreach (var f in W.Fleets.Where(f => f.At == star.Id && f.Owner == who[side] && !f.IsTransport).ToList())
            {
                if (home is null || home == star || report.Winner == 1 - side && !report.Retreated[side]) W.Fleets.Remove(f);
                else Send(f, home, retreat: true);
            }
        }
        // Bombing an enemy colony left without defenders.
        if (report.Winner >= 0 && report.PlanetSide == 1 - report.Winner && star.Bases == 0 && star.Owner >= 0)
        {
            double damage = units[report.Winner].Where(u => u.Count > 0).Sum(u => u.Design.Mounts.Sum(m => Ships.Weapons[m.Weapon].Average * m.Count) * u.Count);
            double shield = Best(W.Empires[star.Owner], Effect.PlanetShield);
            double lostPop = Math.Min(star.Pop * 0.4, Math.Max(0, damage - shield * 4) / 12);
            star.Pop -= lostPop;
            star.Factories = Math.Max(0, star.Factories - damage / 6);
            report.PopLost = lostPop;
            if (star.Pop < 0.5) Wipe(star);
        }
        if (IsPlayer(a) || IsPlayer(b) || Sees(W.Player, star.X, star.Y) && !W.Empires[a].Monster) Battles.Add(report);
        if (IsPlayer(a) || IsPlayer(b)) Play(Sfx.Explosion);
        CheckFallen(a);
        CheckFallen(b);
        return report;
    }

    private static double SidePower(List<Unit> units, Star star, BattleReport report, int side)
    {
        double p = units.Where(u => u.Count > 0).Sum(u => u.Design.Power * u.Count);
        if (report.PlanetSide == side) p += star.Bases * 40;
        return p;
    }

    private static bool Standing(Star star, List<Unit> units, BattleReport report, int side) =>
        !report.Retreated[side] && (units.Any(u => u.Count > 0 && u.Design.Armed) || report.PlanetSide == side && star.Bases > 0);

    private static bool Done(Star star, BattleReport report, List<Unit>[] units) =>
        !Standing(star, units[0], report, 0) || !Standing(star, units[1], report, 1);

    private int Evasion(Empire e, ShipDesign d) => d.Info.Defense + (int)Best(e, Effect.Inertial) + e.Kind.Defense;

    private void Volleys(Star star, BattleReport report, List<Unit>[] units, int[] who, int side, int round)
    {
        var e = W.Empires[who[side]];
        var foe = W.Empires[who[1 - side]];
        var targets = units[1 - side];
        for (int gi = 0; gi < units[side].Count; gi++)
        {
            var u = units[side][gi];
            if (u.Count <= 0) continue;
            for (int mi = 0; mi < u.Design.Mounts.Count; mi++)
            {
                var m = u.Design.Mounts[mi];
                var w = Ships.Weapons[m.Weapon];
                if (w.Missile)
                {
                    if (u.Ammo[mi] <= 0) continue;
                    u.Ammo[mi]--;
                }
                int shots = Math.Min(400, u.Count * m.Count * w.Shots);
                Fire(star, report, targets, side, gi, round, shots, w, u.Design.Computer + e.Kind.Attack, foe);
            }
        }
    }

    private void BaseVolleys(Star star, BattleReport report, List<Unit>[] units, int[] who, int side, int round)
    {
        var e = W.Empires[who[side]];
        var foe = W.Empires[who[1 - side]];
        var w = BaseWeapon(e);
        Fire(star, report, units[1 - side], side, -1, round, Math.Min(400, star.Bases * 2 * w.Shots), w, ComputerTier(e) + 1, foe);
    }

    /// <summary>A volley of shots at one enemy group (or the planet's bases), chosen by how many targets each offers.</summary>
    private void Fire(Star star, BattleReport report, List<Unit> targets, int side, int group, int round, int shots, Weapon w, int attack, Empire foe)
    {
        var live = targets.Where(t => t.Count > 0).ToList();
        bool bases = report.PlanetSide == 1 - side && star.Bases > 0;
        if (live.Count == 0 && !bases) return;
        int pick = Rng.Next(live.Sum(t => t.Count) + (bases ? star.Bases * 3 : 0));
        Unit? target = null;
        foreach (var t in live)
        {
            if (pick < t.Count) { target = t; break; }
            pick -= t.Count;
        }
        int hits = 0, kills = 0;
        if (target is null)
        {
            // At the bases: they don't dodge, but the planet's shield takes its share.
            double shield = Best(foe, Effect.PlanetShield);
            for (int i = 0; i < shots && star.Bases > 0; i++)
            {
                if (Rng.Next(100) >= 60 + attack * 5) continue;
                double dmg = Rng.Range(w.Min, w.Max) - shield;
                if (dmg <= 0) continue;
                hits++;
                star.BaseDamage += dmg;
                while (star.BaseDamage >= BaseHp && star.Bases > 0)
                {
                    star.BaseDamage -= BaseHp;
                    star.Bases--;
                    kills++;
                }
            }
            report.Volleys.Add(new Volley(round, side, group, -1, hits, kills, w.Missile));
            return;
        }
        int defense = Evasion(foe, target.Design);
        double chance = w.Missile
            ? 0.7 - 0.1 * target.Design.Ecm + 0.05 * attack - 0.05 * defense
            : 0.5 + 0.1 * (attack - defense);
        chance = Math.Clamp(chance, 0.05, 0.95);
        for (int i = 0; i < shots && target.Count > 0; i++)
        {
            if (Rng.Next(1000) >= chance * 1000) continue;
            double dmg = Rng.Range(w.Min, w.Max) - target.Design.Shield;
            if (dmg <= 0) continue;
            hits++;
            target.Hp -= dmg;
            if (target.Hp <= 0)
            {
                target.Count--;
                kills++;
                target.Hp = target.Design.Hp;
            }
        }
        report.Volleys.Add(new Volley(round, side, group, targets.IndexOf(target), hits, kills, w.Missile));
    }
}
