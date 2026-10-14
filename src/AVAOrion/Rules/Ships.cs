using System.Text.Json.Serialization;

namespace AVAOrion.Rules;

public enum Hull { Small, Medium, Large, Huge }

/// <summary>A hull: how much it can hold, how much punishment it takes, how hard it is to hit, and its price.</summary>
public sealed record HullInfo(string Name, int Space, int Hp, int Defense, int Cost);

/// <summary>A ship's weapon: damage per hit, shots a round, and for missiles how many rounds of them it carries.</summary>
public sealed record Weapon(string Name, int Min, int Max, int Shots, bool Missile, int Ammo, int Space, int Cost)
{
    public int Id { get; internal set; }
    public double Average => (Min + Max) / 2.0 * Shots;
}

public static class Ships
{
    public static readonly HullInfo[] Hulls =
    [
        new("Small", 40, 6, 2, 8),
        new("Medium", 100, 18, 1, 22),
        new("Large", 250, 55, 0, 60),
        new("Huge", 600, 160, -1, 150),
    ];

    public static HullInfo Info(Hull h) => Hulls[(int)h];

    public static readonly Weapon[] Weapons =
    [
        new("Laser", 1, 4, 1, false, 0, 10, 3),
        new("Nuclear Missile", 4, 4, 1, true, 2, 12, 4),
        new("Mass Driver", 2, 5, 1, false, 0, 12, 4),
        new("Gatling Laser", 1, 4, 4, false, 0, 22, 8),
        new("Hyper-V Missile", 6, 6, 1, true, 3, 14, 6),
        new("Ion Cannon", 3, 8, 1, false, 0, 15, 6),
        new("Fusion Beam", 4, 16, 1, false, 0, 18, 9),
        new("Stinger Missile", 9, 9, 1, true, 3, 16, 9),
        new("Graviton Beam", 6, 18, 1, false, 0, 20, 11),
        new("Phase Lance", 5, 30, 1, false, 0, 22, 13),
        new("Scourge Missile", 16, 16, 1, true, 3, 20, 14),
        new("Plasma Cannon", 8, 30, 1, false, 0, 24, 15),
        new("Annihilator", 10, 50, 1, false, 0, 30, 22),
    ];

    public static readonly string[] ArmorNames = ["Titanium", "Duralloy", "Ferrocrystal", "Neutronium", "Adamant"];
    public static readonly double[] ArmorFactor = [1, 1.5, 2, 3, 4];

    public const int ColonySpace = 80, ColonyCost = 100, TankSpace = 15, TankCost = 6;

    static Ships()
    {
        for (int i = 0; i < Weapons.Length; i++) Weapons[i].Id = i;
    }

    public static Weapon Weapon(string name) => Weapons.First(w => w.Name == name);
}

/// <summary>One weapon fitted to a design, and how many.</summary>
public sealed record Mount(int Weapon, int Count);

/// <summary>
/// A ship design: a hull fitted with an engine, armour, shields, a battle computer, jammers, weapons
/// and perhaps a colony pod or extra fuel. Designs are fixed once made; better ones replace them.
/// </summary>
public sealed class ShipDesign
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public Hull Hull { get; set; }
    public int Warp { get; set; } = 1;
    public int Armor { get; set; }
    public int Shield { get; set; }
    public int Computer { get; set; }
    public int Ecm { get; set; }
    public List<Mount> Mounts { get; set; } = [];
    public bool Colony { get; set; }
    public bool Tanks { get; set; }
    /// <summary>No longer offered for building (the ships already made fly on).</summary>
    public bool Retired { get; set; }

    [JsonIgnore] public HullInfo Info => Ships.Info(Hull);

    /// <summary>Space the fittings take: engines a fifth of the hull, then the rest.</summary>
    [JsonIgnore]
    public int SpaceUsed =>
        Info.Space / 5
        + Shield * Info.Space / 25 + Computer * Info.Space / 40 + Ecm * Info.Space / 40
        + Mounts.Sum(m => Ships.Weapons[m.Weapon].Space * m.Count)
        + (Colony ? Ships.ColonySpace : 0) + (Tanks ? Ships.TankSpace : 0);

    [JsonIgnore] public bool Fits => SpaceUsed <= Info.Space;

    [JsonIgnore]
    public int Cost =>
        (int)Math.Ceiling(Info.Cost * (1 + Armor * 0.15 + Warp * 0.05)
                          + (Shield + Computer + Ecm) * Info.Cost * 0.12
                          + Mounts.Sum(m => Ships.Weapons[m.Weapon].Cost * m.Count)
                          + (Colony ? Ships.ColonyCost : 0) + (Tanks ? Ships.TankCost : 0));

    [JsonIgnore] public int Hp => (int)Math.Round(Info.Hp * Ships.ArmorFactor[Armor]);
    [JsonIgnore] public bool Armed => Mounts.Count > 0;

    /// <summary>A rough measure of a design's worth in a fight.</summary>
    [JsonIgnore]
    public double Power => Armed ? (Mounts.Sum(m => Ships.Weapons[m.Weapon].Average * m.Count) * (1 + Computer * 0.15)) * Math.Sqrt(Hp * (1 + Shield * 0.3)) : 0;

    public string Describe()
    {
        var parts = new List<string> { $"{Info.Name} hull", $"warp {Warp}", $"{Ships.ArmorNames[Armor]} armour ({Hp} hp)" };
        if (Shield > 0) parts.Add($"shield {Shield}");
        if (Computer > 0) parts.Add($"computer {Computer}");
        if (Ecm > 0) parts.Add($"ECM {Ecm}");
        parts.AddRange(Mounts.Select(m => $"{m.Count} {Ships.Weapons[m.Weapon].Name}{(m.Count > 1 ? "s" : "")}"));
        if (Colony) parts.Add("colony pod");
        if (Tanks) parts.Add("reserve tanks");
        return string.Join(", ", parts) + ".";
    }

    public ShipDesign Copy() => new()
    {
        Id = Id, Name = Name, Hull = Hull, Warp = Warp, Armor = Armor, Shield = Shield, Computer = Computer, Ecm = Ecm,
        Mounts = [.. Mounts], Colony = Colony, Tanks = Tanks,
    };
}
