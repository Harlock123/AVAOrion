namespace AVAOrion.Rules;

/// <summary>The six fields of learning, each studied side by side.</summary>
public enum Field { Computers, Construction, ForceFields, Planetology, Propulsion, Weapons }

/// <summary>What a discovery does.</summary>
public enum Effect
{
    None, Computer, Scanner, Ecm, Controls, ResearchBonus,
    Waste, FactoryCost, Armor, Ground,
    Shield, PlanetShield,
    EcoCost, Environment, Terraform, Soil, Cloning,
    Range, Warp, Inertial,
    Weapon,
}

/// <summary>A discovery: its field, how advanced it is, and what it gives.</summary>
public sealed record Tech(Field Field, int Level, string Name, Effect Effect, double Value, string About)
{
    public int Id { get; internal set; }
}

/// <summary>
/// Every discovery in the six fields. A field may study anything up to six levels past the best it
/// already knows; the cost of a discovery grows with the square of its level.
/// </summary>
public static class Techs
{
    public static readonly Tech[] All =
    [
        // Computers.
        new(Field.Computers, 1, "Battle Computer Mk I", Effect.Computer, 1, "Ships' guns aim better: attack +1."),
        new(Field.Computers, 4, "Deep Space Scanner", Effect.Scanner, 4, "Colonies see ships and planets four parsecs away."),
        new(Field.Computers, 8, "ECM Jammer Mk I", Effect.Ecm, 1, "Ships confuse missiles: missile defence +1."),
        new(Field.Computers, 11, "Battle Computer Mk II", Effect.Computer, 2, "Attack +2."),
        new(Field.Computers, 14, "Robotic Controls III", Effect.Controls, 3, "Each colonist runs three factories."),
        new(Field.Computers, 18, "Improved Space Scanner", Effect.Scanner, 7, "Colonies see seven parsecs away."),
        new(Field.Computers, 21, "Battle Computer Mk III", Effect.Computer, 3, "Attack +3."),
        new(Field.Computers, 24, "ECM Jammer Mk II", Effect.Ecm, 2, "Missile defence +2."),
        new(Field.Computers, 28, "Robotic Controls IV", Effect.Controls, 4, "Each colonist runs four factories."),
        new(Field.Computers, 32, "Battle Computer Mk IV", Effect.Computer, 4, "Attack +4."),
        new(Field.Computers, 36, "ECM Jammer Mk III", Effect.Ecm, 3, "Missile defence +3."),
        new(Field.Computers, 40, "Robotic Controls V", Effect.Controls, 5, "Each colonist runs five factories."),
        new(Field.Computers, 45, "Battle Computer Mk V", Effect.Computer, 5, "Attack +5."),
        new(Field.Computers, 50, "Cybernetic Mind", Effect.ResearchBonus, 0.25, "A quarter more research everywhere."),
        // Construction.
        new(Field.Construction, 3, "Reduced Industrial Waste", Effect.Waste, 0.8, "Factories foul their worlds a fifth less."),
        new(Field.Construction, 6, "Industrial Tech 9", Effect.FactoryCost, 9, "Factories cost 9 to build."),
        new(Field.Construction, 9, "Duralloy Plating", Effect.Armor, 1, "Ships' hulls half as strong again."),
        new(Field.Construction, 12, "Industrial Tech 8", Effect.FactoryCost, 8, "Factories cost 8."),
        new(Field.Construction, 15, "Battle Suits", Effect.Ground, 10, "Soldiers fight a tenth better."),
        new(Field.Construction, 18, "Reduced Industrial Waste II", Effect.Waste, 0.6, "Waste down to three fifths."),
        new(Field.Construction, 20, "Ferrocrystal Plating", Effect.Armor, 2, "Hulls twice as strong."),
        new(Field.Construction, 23, "Industrial Tech 7", Effect.FactoryCost, 7, "Factories cost 7."),
        new(Field.Construction, 27, "Reduced Industrial Waste III", Effect.Waste, 0.4, "Waste down to two fifths."),
        new(Field.Construction, 30, "Neutronium Plating", Effect.Armor, 3, "Hulls three times as strong."),
        new(Field.Construction, 33, "Industrial Tech 6", Effect.FactoryCost, 6, "Factories cost 6."),
        new(Field.Construction, 37, "Powered Armour", Effect.Ground, 20, "Soldiers fight a fifth better."),
        new(Field.Construction, 41, "Waste Recycling", Effect.Waste, 0.2, "Hardly any waste at all."),
        new(Field.Construction, 44, "Industrial Tech 5", Effect.FactoryCost, 5, "Factories cost 5."),
        new(Field.Construction, 48, "Adamant Plating", Effect.Armor, 4, "Hulls four times as strong."),
        // Force fields.
        new(Field.ForceFields, 2, "Deflector Shield I", Effect.Shield, 1, "Ship shields turn aside one point of every hit."),
        new(Field.ForceFields, 8, "Planetary Shield V", Effect.PlanetShield, 5, "Colonies shrug off five points of every hit."),
        new(Field.ForceFields, 12, "Deflector Shield II", Effect.Shield, 2, "Shields take two points off every hit."),
        new(Field.ForceFields, 16, "Personal Deflector", Effect.Ground, 10, "Soldiers fight a tenth better."),
        new(Field.ForceFields, 20, "Planetary Shield X", Effect.PlanetShield, 10, "Colonies shrug off ten points."),
        new(Field.ForceFields, 24, "Deflector Shield III", Effect.Shield, 3, "Shields take three points off."),
        new(Field.ForceFields, 30, "Deflector Shield IV", Effect.Shield, 4, "Shields take four points off."),
        new(Field.ForceFields, 34, "Planetary Shield XV", Effect.PlanetShield, 15, "Colonies shrug off fifteen points."),
        new(Field.ForceFields, 38, "Deflector Shield V", Effect.Shield, 5, "Shields take five points off."),
        new(Field.ForceFields, 44, "Deflector Shield VII", Effect.Shield, 7, "Shields take seven points off."),
        new(Field.ForceFields, 48, "Planetary Barrier XX", Effect.PlanetShield, 20, "Colonies shrug off twenty points."),
        // Planetology.
        new(Field.Planetology, 1, "Ecological Restoration", Effect.EcoCost, 0.8, "Cleaning up waste costs a fifth less."),
        new(Field.Planetology, 4, "Barren Environment", Effect.Environment, 0, "Domed colonies on barren and dead worlds."),
        new(Field.Planetology, 7, "Terraforming +10", Effect.Terraform, 10, "Ecology spending can make room for ten more people on each world."),
        new(Field.Planetology, 10, "Improved Eco Restoration", Effect.EcoCost, 0.6, "Cleaning up costs two fifths less."),
        new(Field.Planetology, 13, "Toxic Environment", Effect.Environment, 0, "Sealed colonies on toxic worlds."),
        new(Field.Planetology, 16, "Soil Enrichment", Effect.Soil, 0.25, "Worlds hold a quarter more people."),
        new(Field.Planetology, 19, "Inferno Environment", Effect.Environment, 0, "Shielded colonies on inferno worlds."),
        new(Field.Planetology, 22, "Terraforming +20", Effect.Terraform, 20, "Room for twenty more people on each world."),
        new(Field.Planetology, 25, "Cloning", Effect.Cloning, 0, "New colonists cost half as much to raise."),
        new(Field.Planetology, 28, "Radiated Environment", Effect.Environment, 0, "Hardened colonies on radiated worlds."),
        new(Field.Planetology, 32, "Advanced Soil Enrichment", Effect.Soil, 0.5, "Worlds hold half as many people again."),
        new(Field.Planetology, 36, "Terraforming +30", Effect.Terraform, 30, "Room for thirty more people on each world."),
        new(Field.Planetology, 40, "Complete Eco Restoration", Effect.EcoCost, 0.3, "Cleaning up costs very little."),
        new(Field.Planetology, 45, "Gaia Transformation", Effect.Terraform, 50, "Room for fifty more people on each world."),
        // Propulsion.
        new(Field.Propulsion, 2, "Hydrogen Fuel Cells", Effect.Range, 6, "Ships may travel six parsecs from a colony."),
        new(Field.Propulsion, 5, "Nuclear Engines", Effect.Warp, 2, "Ships travel at warp 2."),
        new(Field.Propulsion, 8, "Deuterium Fuel Cells", Effect.Range, 7, "Range seven parsecs."),
        new(Field.Propulsion, 11, "Sub-Light Drives", Effect.Warp, 3, "Warp 3."),
        new(Field.Propulsion, 15, "Irridium Fuel Cells", Effect.Range, 8, "Range eight parsecs."),
        new(Field.Propulsion, 19, "Fusion Drive", Effect.Warp, 4, "Warp 4."),
        new(Field.Propulsion, 23, "Inertial Stabiliser", Effect.Inertial, 1, "Ships dodge better: defence +1."),
        new(Field.Propulsion, 27, "Uridium Fuel Cells", Effect.Range, 10, "Range ten parsecs."),
        new(Field.Propulsion, 31, "Ion Drive", Effect.Warp, 5, "Warp 5."),
        new(Field.Propulsion, 35, "Inertial Nullifier", Effect.Inertial, 2, "Defence +2."),
        new(Field.Propulsion, 39, "Hyper Drive", Effect.Warp, 6, "Warp 6."),
        new(Field.Propulsion, 43, "Thorium Cells", Effect.Range, 99, "Ships may go anywhere."),
        new(Field.Propulsion, 48, "Interphased Drive", Effect.Warp, 8, "Warp 8."),
        // Weapons (the weapons themselves are in Ships.Weapons).
        new(Field.Weapons, 1, "Laser", Effect.Weapon, 0, "A beam of light: 1 to 4 damage."),
        new(Field.Weapons, 1, "Nuclear Missile", Effect.Weapon, 1, "A missile: 4 damage, two salvoes."),
        new(Field.Weapons, 4, "Hand Lasers", Effect.Ground, 5, "Soldiers fight a twentieth better."),
        new(Field.Weapons, 7, "Mass Driver", Effect.Weapon, 2, "A magnetic gun: 2 to 5 damage."),
        new(Field.Weapons, 10, "Gatling Laser", Effect.Weapon, 3, "Four laser shots a round."),
        new(Field.Weapons, 13, "Hyper-V Missile", Effect.Weapon, 4, "A missile: 6 damage."),
        new(Field.Weapons, 16, "Ion Cannon", Effect.Weapon, 5, "3 to 8 damage."),
        new(Field.Weapons, 20, "Fusion Beam", Effect.Weapon, 6, "4 to 16 damage."),
        new(Field.Weapons, 23, "Fusion Rifles", Effect.Ground, 10, "Soldiers fight a tenth better."),
        new(Field.Weapons, 26, "Stinger Missile", Effect.Weapon, 7, "A missile: 9 damage."),
        new(Field.Weapons, 29, "Graviton Beam", Effect.Weapon, 8, "6 to 18 damage."),
        new(Field.Weapons, 33, "Phase Lance", Effect.Weapon, 9, "5 to 30 damage."),
        new(Field.Weapons, 37, "Scourge Missile", Effect.Weapon, 10, "A missile: 16 damage."),
        new(Field.Weapons, 41, "Plasma Cannon", Effect.Weapon, 11, "8 to 30 damage."),
        new(Field.Weapons, 44, "Plasma Rifles", Effect.Ground, 15, "Soldiers fight three twentieths better."),
        new(Field.Weapons, 48, "Annihilator", Effect.Weapon, 12, "10 to 50 damage."),
    ];

    private static readonly Dictionary<string, int> ByName = [];

    static Techs()
    {
        for (int i = 0; i < All.Length; i++)
        {
            All[i].Id = i;
            ByName[All[i].Name] = i;
        }
    }

    public static int Count => All.Length;
    public static int Id(string name) => ByName.TryGetValue(name, out int i) ? i : throw new KeyNotFoundException("No such tech: " + name);
    public static Tech Get(string name) => All[Id(name)];

    public static readonly string[] FieldNames = ["Computers", "Construction", "Force Fields", "Planetology", "Propulsion", "Weapons"];

    /// <summary>The research needed for a discovery of a given level.</summary>
    public static int Cost(int level) => 30 + level * level * 5;

    /// <summary>The discoveries known at the start: the laser and the nuclear missile.</summary>
    public static readonly string[] Start = ["Laser", "Nuclear Missile"];
}
