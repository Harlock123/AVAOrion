namespace AVAOrion.Rules;

public enum StarColour { Yellow, Orange, Red, White, Blue, Brown }

public enum PlanetType { None, Terran, Ocean, Jungle, Arid, Steppe, Desert, Tundra, Barren, Dead, Inferno, Toxic, Radiated }

public enum PlanetSize { Tiny, Small, Medium, Large, Huge }

public enum Minerals { UltraPoor, Poor, Normal, Rich, UltraRich }

/// <summary>A kind of world: how much of it people can live on, what it takes to settle it, and its colours.</summary>
public sealed record PlanetKind(string Name, double Habitable, string? Needs, uint Land, uint Sea, string About);

public static class Planets
{
    public static readonly PlanetKind[] Kinds =
    [
        new("No planet", 0, null, 0, 0, "Only dust and comets circle this star."),
        new("Terran", 1.0, null, 0xFF4A9A48, 0xFF2A64B8, "Seas, forests and good air: the kindest of worlds."),
        new("Ocean", 1.0, null, 0xFF2E6AC8, 0xFF1E4C9A, "A world of water, scattered with green islands."),
        new("Jungle", 0.85, null, 0xFF2E7A30, 0xFF5AA040, "Hot, wet and teeming."),
        new("Arid", 0.75, null, 0xFFB89048, 0xFF8A6A30, "Dry plains and thin rivers."),
        new("Steppe", 0.75, null, 0xFF9AA058, 0xFF7A8040, "Endless grass under a big sky."),
        new("Desert", 0.6, null, 0xFFD8B070, 0xFFB88A48, "Dunes and rock, water deep underground."),
        new("Tundra", 0.5, null, 0xFFB8C8C8, 0xFF8898A0, "Frozen ground and short summers."),
        new("Barren", 0.3, "Barren Environment", 0xFF8A8078, 0xFF6A605A, "Bare rock and no air: only domes can hold people."),
        new("Dead", 0.25, "Barren Environment", 0xFF6A6460, 0xFF4A4644, "A cold, dead world."),
        new("Inferno", 0.2, "Inferno Environment", 0xFFD8582A, 0xFF8A2A18, "Lakes of fire under a crushing sky."),
        new("Toxic", 0.2, "Toxic Environment", 0xFF9AB830, 0xFF5A7A18, "Its air would eat a ship's hull."),
        new("Radiated", 0.2, "Radiated Environment", 0xFFB0A0D0, 0xFF6A5A90, "Bathed in its star's fury."),
    ];

    public static PlanetKind Kind(PlanetType t) => Kinds[(int)t];

    public static readonly string[] SizeNames = ["Tiny", "Small", "Medium", "Large", "Huge"];

    /// <summary>The most people a fully habitable world of this size can hold.</summary>
    public static int SizeBase(PlanetSize s) => s switch { PlanetSize.Tiny => 20, PlanetSize.Small => 40, PlanetSize.Medium => 65, PlanetSize.Large => 90, _ => 120 };

    public static readonly string[] MineralNames = ["Ultra poor", "Poor", "", "Rich", "Ultra rich"];

    public static double MineralFactor(Minerals m) => m switch { Minerals.UltraPoor => 0.4, Minerals.Poor => 0.65, Minerals.Rich => 1.6, Minerals.UltraRich => 2.2, _ => 1 };

    public static bool Hostile(PlanetType t) => Kind(t).Needs is not null;

    public static uint StarTone(StarColour c) => c switch
    {
        StarColour.Yellow => 0xFFFFE070,
        StarColour.Orange => 0xFFFFA040,
        StarColour.Red => 0xFFFF5A40,
        StarColour.White => 0xFFF0F4FF,
        StarColour.Blue => 0xFF70A8FF,
        _ => 0xFF9A6A48,
    };

    /// <summary>Invented star names, shuffled into each new galaxy.</summary>
    public static readonly string[] StarNames =
    [
        "Aldren", "Brisa", "Cetho", "Dravis", "Elun", "Fennix", "Gorra", "Hesk", "Ilvar", "Jessa", "Kessa", "Lorn", "Mirren",
        "Noxa", "Orveth", "Pell", "Quillon", "Ravel", "Sarn", "Tolle", "Umbrin", "Vesh", "Wyre", "Xandu", "Yrsa", "Zell",
        "Ashka", "Belloc", "Corvin", "Delma", "Esker", "Firn", "Galen", "Hollis", "Iskra", "Jarra", "Kito", "Lyss",
        "Morrow", "Nyth", "Osk", "Prell", "Quade", "Rhune", "Selk", "Tarrow", "Ulla", "Varn", "Welk", "Xeth", "Yorr", "Zandar",
        "Amaru", "Brenn", "Calyx", "Dorn", "Eiris", "Fallow", "Gisk", "Halde", "Imra", "Juno-Vey", "Korra", "Lumen", "Mael",
        "Nerra", "Ossa", "Pryn", "Rook", "Sable", "Thane", "Ursk", "Vanta", "Wren", "Ysolde", "Zephra",
    ];
}
