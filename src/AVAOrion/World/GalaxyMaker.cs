using AVAOrion.Rules;

namespace AVAOrion.World;

/// <summary>
/// Makes a new galaxy from a seed: stars scattered with room between them, each with the kind of
/// planet its colour favours (yellow stars the green worlds, red and blue the hard ones), homes for
/// each people far apart with a few good worlds near each, and at the heart the Vault of Aeon.
/// </summary>
public static class GalaxyMaker
{
    public static int StarCount(GalaxySize s) => s switch { GalaxySize.Small => 26, GalaxySize.Large => 56, _ => 40 };

    private static readonly Dictionary<StarColour, (PlanetType T, int W)[]> Odds = new()
    {
        [StarColour.Yellow] = [(PlanetType.Terran, 20), (PlanetType.Ocean, 12), (PlanetType.Jungle, 12), (PlanetType.Arid, 12), (PlanetType.Steppe, 12), (PlanetType.Desert, 8), (PlanetType.Tundra, 6), (PlanetType.Barren, 6), (PlanetType.None, 12)],
        [StarColour.Orange] = [(PlanetType.Terran, 8), (PlanetType.Ocean, 8), (PlanetType.Jungle, 10), (PlanetType.Arid, 14), (PlanetType.Steppe, 12), (PlanetType.Desert, 14), (PlanetType.Tundra, 8), (PlanetType.Barren, 10), (PlanetType.Dead, 4), (PlanetType.None, 10)],
        [StarColour.Red] = [(PlanetType.Tundra, 18), (PlanetType.Desert, 10), (PlanetType.Barren, 20), (PlanetType.Dead, 18), (PlanetType.Radiated, 6), (PlanetType.Toxic, 8), (PlanetType.Steppe, 4), (PlanetType.None, 16)],
        [StarColour.White] = [(PlanetType.Radiated, 18), (PlanetType.Toxic, 18), (PlanetType.Inferno, 14), (PlanetType.Barren, 14), (PlanetType.Dead, 10), (PlanetType.Arid, 8), (PlanetType.Desert, 8), (PlanetType.None, 10)],
        [StarColour.Blue] = [(PlanetType.Inferno, 26), (PlanetType.Radiated, 24), (PlanetType.Toxic, 14), (PlanetType.Barren, 14), (PlanetType.Dead, 8), (PlanetType.None, 14)],
        [StarColour.Brown] = [(PlanetType.Dead, 30), (PlanetType.Barren, 20), (PlanetType.Tundra, 10), (PlanetType.None, 40)],
    };

    private static T Pick<T>(Rng rng, (T Item, int W)[] options)
    {
        int roll = rng.Next(options.Sum(o => o.W));
        foreach (var (item, w) in options)
        {
            if (roll < w) return item;
            roll -= w;
        }
        return options[^1].Item;
    }

    public static List<Star> Make(Rng rng, GalaxySize size, out double width, out double height)
    {
        int n = StarCount(size);
        width = Math.Round(Math.Sqrt(n * 34 * 1.35));
        height = Math.Round(width / 1.35);
        var stars = new List<Star>();
        var names = Planets.StarNames.OrderBy(_ => rng.Next(100000)).ToList();
        int guard = 0;
        while (stars.Count < n && guard++ < 20000)
        {
            double x = 2 + rng.Next((int)((width - 4) * 100)) / 100.0, y = 2 + rng.Next((int)((height - 4) * 100)) / 100.0;
            double spacing = guard > 10000 ? 2.5 : 3.6;
            if (stars.Any(s => WorldState.Distance(s.X, s.Y, x, y) < spacing)) continue;
            var colour = Pick(rng, [(StarColour.Yellow, 20), (StarColour.Orange, 20), (StarColour.Red, 28), (StarColour.White, 12), (StarColour.Blue, 8), (StarColour.Brown, 12)]);
            var star = new Star { Id = stars.Count, Name = names[stars.Count % names.Count], X = x, Y = y, Colour = colour };
            star.Planet = Pick(rng, Odds[colour]);
            star.Size = Pick(rng, [(PlanetSize.Tiny, 14), (PlanetSize.Small, 24), (PlanetSize.Medium, 30), (PlanetSize.Large, 20), (PlanetSize.Huge, 12)]);
            star.Minerals = colour switch
            {
                StarColour.Blue or StarColour.White => Pick(rng, [(Minerals.Normal, 50), (Minerals.Rich, 35), (Minerals.UltraRich, 15)]),
                StarColour.Red or StarColour.Brown => Pick(rng, [(Minerals.UltraPoor, 15), (Minerals.Poor, 30), (Minerals.Normal, 50), (Minerals.Rich, 5)]),
                _ => Pick(rng, [(Minerals.Poor, 15), (Minerals.Normal, 75), (Minerals.Rich, 10)]),
            };
            star.Fertile = !Planets.Hostile(star.Planet) && star.HasPlanet && rng.Chance(10);
            star.Artifacts = star.HasPlanet && rng.Chance(6);
            stars.Add(star);
        }
        return stars;
    }

    /// <summary>Picks a home star for each people, as far from the others as can be.</summary>
    public static List<Star> PickHomes(List<Star> stars, Rng rng, int count, double width, double height)
    {
        var homes = new List<Star>();
        // A home needs neighbours: at least two other stars within easy reach.
        var eligible = stars.Where(s => stars.Count(o => o != s && WorldState.Distance(o.X, o.Y, s.X, s.Y) <= 6.5) >= 2).ToList();
        if (eligible.Count < count * 2) eligible = stars;
        var first = eligible.OrderBy(_ => rng.Next(1000)).FirstOrDefault(s => s.X > 4 && s.Y > 4 && s.X < width - 4 && s.Y < height - 4) ?? eligible[0];
        homes.Add(first);
        while (homes.Count < count)
        {
            var next = eligible.Where(s => !homes.Contains(s)).OrderByDescending(s => homes.Min(h => WorldState.Distance(h.X, h.Y, s.X, s.Y)) + rng.Next(100) / 100.0).First();
            homes.Add(next);
        }
        return homes;
    }

    /// <summary>Makes a star a people's home: a large world of their kind, and two good worlds within easy reach.</summary>
    public static void Settle(List<Star> stars, Star home, Race race, Rng rng, IReadOnlyList<Star> homes)
    {
        home.Planet = race.Home;
        home.Size = PlanetSize.Large;
        home.Minerals = Minerals.Normal;
        home.Colour = race.Home is PlanetType.Barren or PlanetType.Tundra ? StarColour.Red : StarColour.Yellow;
        home.Fertile = false;
        home.Artifacts = false;
        var near = stars.Where(s => !homes.Contains(s) && WorldState.Distance(s.X, s.Y, home.X, home.Y) <= 6.5)
            .OrderBy(s => WorldState.Distance(s.X, s.Y, home.X, home.Y)).ToList();
        int good = near.Count(s => s.HasPlanet && !Planets.Hostile(s.Planet));
        foreach (var s in near)
        {
            if (good >= 2) break;
            if (s.HasPlanet && !Planets.Hostile(s.Planet)) continue;
            s.Planet = rng.Pick([PlanetType.Arid, PlanetType.Steppe, PlanetType.Ocean, PlanetType.Jungle, PlanetType.Terran]);
            s.Colour = StarColour.Yellow;
            if (s.Size == PlanetSize.Tiny) s.Size = PlanetSize.Small;
            good++;
        }
    }
}
