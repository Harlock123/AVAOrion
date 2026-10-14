using System.Text.Json.Serialization;
using AVAOrion.Rules;

namespace AVAOrion.World;

/// <summary>Where a colony's production goes, in percent.</summary>
public enum Spend { Ships, Defense, Industry, Ecology, Research }

/// <summary>
/// A star and the one planet worth the name that circles it, and the colony on it if there is one:
/// its people, its factories and missile bases, and how it spends what it makes.
/// </summary>
public sealed class Star
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public StarColour Colour { get; set; }
    public PlanetType Planet { get; set; }
    public PlanetSize Size { get; set; }
    public Minerals Minerals { get; set; } = Minerals.Normal;
    public bool Fertile { get; set; }
    public bool Artifacts { get; set; }
    /// <summary>The Vault of Aeon: the richest world in the galaxy, and its guardian.</summary>
    public bool Vault { get; set; }

    // The colony.
    public int Owner { get; set; } = -1;
    public double Pop { get; set; }
    public double Factories { get; set; }
    public int Bases { get; set; }
    /// <summary>Damage done to the bases in the current fight.</summary>
    public double BaseDamage { get; set; }
    public double ShipBC { get; set; }
    public double BaseBC { get; set; }
    /// <summary>People the world can't hold because of uncleaned waste.</summary>
    public double Waste { get; set; }
    /// <summary>People's room added by terraforming.</summary>
    public double Terraformed { get; set; }
    public int[] Sliders { get; set; } = [0, 0, 60, 10, 30];
    /// <summary>The design this colony's shipyard builds, or -1.</summary>
    public int Building { get; set; } = -1;
    public bool Capital { get; set; }
    public int FoundedTurn { get; set; }

    [JsonIgnore] public bool HasPlanet => Planet != PlanetType.None;
    [JsonIgnore] public bool Colonised => Owner >= 0 && Pop > 0;
    [JsonIgnore] public PlanetKind Kind => Planets.Kind(Planet);

    public int Slider(Spend s) => Sliders[(int)s];
}

/// <summary>A number of ships of one design flying together.</summary>
public sealed class Stack
{
    public int Design { get; set; }
    public int Count { get; set; }
}

/// <summary>
/// A fleet: ships (or a load of colonists headed for a landing) at a star, or in flight between
/// two. Fleets of one people at the same star merge.
/// </summary>
public sealed class Fleet
{
    public int Id { get; set; }
    public int Owner { get; set; }
    public List<Stack> Ships { get; set; } = [];
    /// <summary>Colonists aboard transports, for settling or invading; zero for warships.</summary>
    public int Troops { get; set; }
    /// <summary>The star it's at, or -1 in flight.</summary>
    public int At { get; set; } = -1;
    public int From { get; set; } = -1;
    public int Dest { get; set; } = -1;
    public double X { get; set; }
    public double Y { get; set; }
    /// <summary>Turns until it arrives.</summary>
    public int Eta { get; set; }
    /// <summary>For the computer rulers: what it's for, and where.</summary>
    public int Task { get; set; }
    public int TaskStar { get; set; } = -1;

    [JsonIgnore] public bool Moving => At < 0;
    [JsonIgnore] public bool IsTransport => Troops > 0;
    [JsonIgnore] public int Count => Ships.Sum(s => s.Count);
}

public enum Relation { None, Peace, War }

public enum Treaty { None, NonAggression, Alliance }

/// <summary>A star-faring people: its knowledge and what it's studying, its designs, its treasury and its dealings with the others.</summary>
public sealed class Empire
{
    public int Id { get; set; }
    public int Race { get; set; }
    public string Ruler { get; set; } = "";
    public bool Human { get; set; }
    public bool Alive { get; set; } = true;
    /// <summary>The galaxy's monsters, who answer to no one.</summary>
    public bool Monster { get; set; }
    public double Reserve { get; set; }
    /// <summary>Percent of research to each of the six fields.</summary>
    public int[] Alloc { get; set; } = [16, 17, 17, 17, 17, 16];
    /// <summary>What each field is studying (a tech id), or -1.</summary>
    public int[] Target { get; set; } = [-1, -1, -1, -1, -1, -1];
    public double[] Progress { get; set; } = new double[6];
    public bool[] Known { get; set; } = new bool[Techs.Count];
    public List<ShipDesign> Designs { get; set; } = [];
    public int NextDesign { get; set; } = 1;
    public Relation[] Relations { get; set; } = [];
    public Treaty[] Treaties { get; set; } = [];
    public bool[] Trade { get; set; } = [];
    public int[] Attitude { get; set; } = [];
    public int[] Cooldown { get; set; } = [];
    public int[] Betrayed { get; set; } = [];
    /// <summary>Stars whose planets this people has surveyed.</summary>
    public bool[] Explored { get; set; } = [];
    public int Kills { get; set; }
    public int Losses { get; set; }

    [JsonIgnore] public Race Kind => Races.All[Race];
    [JsonIgnore] public string Name => Monster ? "Monsters" : Kind.Name;
    [JsonIgnore] public string Adjective => Monster ? "monstrous" : Kind.Adjective;
    [JsonIgnore] public uint Colour => Monster ? Races.MonsterColour : Kind.Colour;
    [JsonIgnore] public int TechCount => Known.Count(k => k);

    public ShipDesign? Design(int id) => Designs.FirstOrDefault(d => d.Id == id);
    public IEnumerable<ShipDesign> Active => Designs.Where(d => !d.Retired);
}
