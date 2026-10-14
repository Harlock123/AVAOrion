using System.Text.Json;
using System.Text.Json.Serialization;

namespace AVAOrion.World;

public enum GalaxySize { Small, Medium, Large }

/// <summary>
/// Everything about a game in progress that a save must keep: the stars and their colonies, the
/// fleets, the peoples, the year, and how it ended if it has.
/// </summary>
public sealed class WorldState
{
    public const int StartYear = 2300, LastYear = 2700;

    public List<Star> Stars { get; set; } = [];
    public List<Fleet> Fleets { get; set; } = [];
    public List<Empire> Empires { get; set; } = [];
    public double Width { get; set; }
    public double Height { get; set; }
    public GalaxySize Size { get; set; } = GalaxySize.Medium;
    public int Turn { get; set; }
    public int Level { get; set; } = 1;
    public int NextId { get; set; } = 1;
    public ulong Seed { get; set; }
    public int Player { get; set; }
    /// <summary>When the Galactic Council next sits (a turn number), or -1 if it never will again.</summary>
    public int CouncilTurn { get; set; } = 150;
    public List<string> Journal { get; set; } = [];
    public HashSet<string> Flags { get; set; } = [];
    public int Winner { get; set; } = -2;
    public string Outcome { get; set; } = "";
    public DateTime Saved { get; set; }

    [JsonIgnore] public bool Over => Winner != -2;
    [JsonIgnore] public int Year => StartYear + Turn;
    [JsonIgnore] public Empire Human => Empires[Player];
    [JsonIgnore] public Empire? Monsters => Empires.FirstOrDefault(e => e.Monster);

    public Star StarAt(int id) => Stars[id];

    public IEnumerable<Star> ColoniesOf(int empire) => Stars.Where(s => s.Owner == empire);
    public IEnumerable<Fleet> FleetsOf(int empire) => Fleets.Where(f => f.Owner == empire);
    public IEnumerable<Fleet> FleetsAt(int star) => Fleets.Where(f => f.At == star);

    public static double Distance(double x1, double y1, double x2, double y2) => Math.Sqrt((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2));
    public double Distance(Star a, Star b) => Distance(a.X, a.Y, b.X, b.Y);

    public void Note(string text) => Journal.Add($"{Year}: {text}");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static WorldState FromJson(string json)
    {
        var w = JsonSerializer.Deserialize<WorldState>(json, Json) ?? throw new InvalidDataException("Empty save");
        if (w.Stars.Count == 0 || w.Empires.Count == 0) throw new InvalidDataException("Damaged save");
        return w;
    }
}
