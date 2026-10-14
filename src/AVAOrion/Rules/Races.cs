namespace AVAOrion.Rules;

/// <summary>
/// One of the star-faring peoples: its ruler, its colour, its gifts, and its temper. All invented.
/// Industry, Research and Growth multiply what each colony makes; Attack and Defense add to every
/// ship's aim and evasion; Ground adds to each soldier's strength; Diplomacy sweetens every deal.
/// </summary>
public sealed record Race(
    string Name, string Adjective, string Leader, string Title, uint Colour, string Gift,
    double Industry, double Research, double Growth, int Attack, int Defense, double Ground, int Diplomacy,
    PlanetType Home, bool Hardy, double Aggression, double Expansion, Field[] Leanings, string Nature)
{
    public int Id { get; internal set; }
    public string RulerName => Title.Length == 0 ? Leader : $"{Title} {Leader}";
}

public static class Races
{
    public static readonly Race[] All =
    [
        new("Halcyons", "Halcyon", "Seren Vale", "First Speaker", 0xFFE8C850, "Born diplomats: every other people likes them a little more, and their trade pays better.",
            1.0, 1.0, 1.0, 0, 0, 0, 25, PlanetType.Terran, false, 0.3, 0.6, [Field.Planetology, Field.Construction], "Smooth talkers who would rather trade than fight."),
        new("Vorrak", "Vorrak", "Gharesh", "Warmaster", 0xFFD84838, "Natural gunners: their ships aim truer.",
            1.0, 0.9, 1.0, 2, 0, 0.1, -20, PlanetType.Arid, false, 0.85, 0.6, [Field.Weapons, Field.Weapons], "A warrior people who count worth in victories."),
        new("Siltari", "Siltari", "Oss", "Hive-Mother", 0xFF70B040, "Tireless workers: half again as much from every colony's industry.",
            1.5, 0.85, 1.0, 0, 0, 0, 0, PlanetType.Desert, false, 0.55, 0.75, [Field.Construction, Field.Planetology], "A hive that builds without rest, and expands without end."),
        new("Ylem", "Ylem", "Aurum", "Choir of", 0xFFB070E8, "Minds of living crystal: half again as much research.",
            0.9, 1.5, 0.85, 0, 0, -0.1, 0, PlanetType.Tundra, false, 0.25, 0.45, [Field.Computers, Field.ForceFields], "Crystal thinkers, patient and aloof."),
        new("Marrow", "Marrow", "Ix", "Brood-Queen", 0xFFE08840, "Prolific breeders: their colonies grow twice as fast.",
            1.0, 0.9, 2.0, 0, 0, 0.1, -10, PlanetType.Jungle, false, 0.6, 0.9, [Field.Planetology, Field.Weapons], "A swarm that fills every world it touches."),
        new("Quorn", "Quorn", "Basaltan", "Grand Lith", 0xFF9AA0A8, "Beings of stone: they can settle barren and dead worlds from the start, and are fearsome on the ground.",
            1.1, 0.9, 0.8, 0, 0, 0.5, -5, PlanetType.Barren, true, 0.5, 0.6, [Field.Construction, Field.Propulsion], "Slow, heavy and very hard to move."),
        new("Aethon", "Aethon", "Skylord Peregrin", "", 0xFF60C8E8, "Sky-born pilots: their ships are harder to hit.",
            1.0, 1.0, 1.0, 0, 2, 0, 5, PlanetType.Steppe, false, 0.5, 0.55, [Field.Propulsion, Field.Computers], "Proud flyers, quick to take offence."),
        new("Nereid", "Nereid", "Tidecaller Shoa", "", 0xFF4090E0, "Children of the deep: their worlds' seas hold a quarter more people.",
            1.0, 1.1, 1.1, 0, 0, -0.1, 10, PlanetType.Ocean, false, 0.35, 0.65, [Field.Planetology, Field.ForceFields], "A calm people of the deep waters."),
    ];

    static Races()
    {
        for (int i = 0; i < All.Length; i++) All[i].Id = i;
    }

    public const uint MonsterColour = 0xFF8A3A9A;
}
