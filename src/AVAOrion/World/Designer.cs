using AVAOrion.Rules;

namespace AVAOrion.World;

/// <summary>
/// Draws up ship designs from what a people knows: scouts with extra fuel, colony ships, and
/// warships fitted with the best engine, armour, shields and computers that leave room for guns,
/// then filled with the best beams and missiles. The computer rulers design all their ships this
/// way, and the player can ask for a design to be drawn up for them.
/// </summary>
public static class Designer
{
    public static readonly string[] HullNames = ["Fighter", "Destroyer", "Cruiser", "Battleship"];

    public static void Starting(Empire e)
    {
        Add(e, Scout(e));
        Add(e, Warship(e, Hull.Small, "Fighter"));
        Add(e, Warship(e, Hull.Medium, "Destroyer"));
        Add(e, ColonyShip(e));
    }

    public static ShipDesign Add(Empire e, ShipDesign d)
    {
        d.Id = e.NextDesign++;
        e.Designs.Add(d);
        // No more than six designs on offer: the oldest warship goes first.
        while (e.Active.Count() > 6)
        {
            var old = e.Active.Where(x => x != d && x.Armed).OrderBy(x => x.Id).FirstOrDefault() ?? e.Active.First(x => x != d);
            old.Retired = true;
        }
        return d;
    }

    /// <summary>A name not yet used: Cruiser, Cruiser II, Cruiser III...</summary>
    public static string Fresh(Empire e, string name)
    {
        if (e.Designs.All(d => d.Name != name)) return name;
        string[] marks = ["II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII", "XIII", "XIV", "XV"];
        foreach (var m in marks)
            if (e.Designs.All(d => d.Name != $"{name} {m}")) return $"{name} {m}";
        return $"{name} {e.NextDesign}";
    }

    public static ShipDesign Scout(Empire e) => new()
    {
        Name = Fresh(e, "Scout"), Hull = Hull.Small, Warp = Sim.Warp(e), Armor = Sim.ArmorTier(e), Tanks = true,
    };

    public static ShipDesign ColonyShip(Empire e) => new()
    {
        Name = Fresh(e, "Colony Ship"), Hull = Hull.Medium, Warp = Sim.Warp(e), Armor = Sim.ArmorTier(e), Colony = true,
    };

    /// <summary>The best warship of a hull size: protection first, as long as it leaves room for guns.</summary>
    public static ShipDesign Warship(Empire e, Hull hull, string? name = null)
    {
        var d = new ShipDesign
        {
            Name = Fresh(e, name ?? HullNames[(int)hull]), Hull = hull, Warp = Sim.Warp(e), Armor = Sim.ArmorTier(e),
            Shield = Sim.ShieldTier(e), Computer = Sim.ComputerTier(e), Ecm = (Sim.EcmTier(e) + 1) / 2,
        };
        var beams = Sim.Arsenal(e).Where(w => !w.Missile).OrderByDescending(w => w.Average / w.Space).ThenByDescending(w => w.Max).ToList();
        var missiles = Sim.Arsenal(e).Where(w => w.Missile).OrderByDescending(w => w.Average).ToList();
        // Leave at least half the room for weapons.
        while (d.SpaceUsed > d.Info.Space / 2 && (d.Shield > 0 || d.Computer > 0 || d.Ecm > 0))
        {
            if (d.Ecm > 0) d.Ecm--;
            else if (d.Shield >= d.Computer && d.Shield > 0) d.Shield--;
            else d.Computer--;
        }
        int room = d.Info.Space - d.SpaceUsed;
        var beam = beams.FirstOrDefault();
        var missile = missiles.FirstOrDefault();
        int missileRoom = missile is null ? 0 : room / 3;
        var mounts = new List<Mount>();
        if (missile is not null && missileRoom >= missile.Space)
        {
            int n = missileRoom / missile.Space;
            mounts.Add(new Mount(missile.Id, n));
            room -= n * missile.Space;
        }
        if (beam is not null && room >= beam.Space)
        {
            int n = room / beam.Space;
            mounts.Add(new Mount(beam.Id, n));
            room -= n * beam.Space;
        }
        // Whatever room is left takes the smallest weapon that fits.
        var filler = Sim.Arsenal(e).Where(w => w.Space <= room).OrderByDescending(w => w.Average).FirstOrDefault();
        if (filler is not null)
        {
            int n = room / filler.Space;
            var same = mounts.FindIndex(m => m.Weapon == filler.Id);
            if (same >= 0) mounts[same] = mounts[same] with { Count = mounts[same].Count + n };
            else mounts.Add(new Mount(filler.Id, n));
        }
        d.Mounts = mounts;
        return d;
    }

    /// <summary>The biggest hull a people should build warships on, given what its colonies make.</summary>
    public static Hull HullFor(double bestColonyBC) => bestColonyBC switch { > 160 => Hull.Huge, > 70 => Hull.Large, > 20 => Hull.Medium, _ => Hull.Small };
}
