using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AVAOrion.Rules;

namespace AVAOrion.Views;

/// <summary>
/// The galaxy's pictures that are painted pixel by pixel (made once, then kept as bitmaps and drawn
/// scaled up with no smoothing): every planet, lit from one side, and the nebulae behind the stars.
/// All original.
/// </summary>
public static class Sprites
{
    public static uint Shade(uint argb, double f)
    {
        uint r = (argb >> 16) & 0xFF, g = (argb >> 8) & 0xFF, b = argb & 0xFF;
        uint S(uint v) => (uint)Math.Clamp(f >= 1 ? v + (255 - v) * (f - 1) : v * f, 0, 255);
        return (argb & 0xFF000000) | (S(r) << 16) | (S(g) << 8) | S(b);
    }

    public static uint Mix(uint a, uint b, double t)
    {
        uint L(int shift) => (uint)Math.Clamp(((a >> shift) & 0xFF) * (1 - t) + ((b >> shift) & 0xFF) * t, 0, 255) << shift;
        return 0xFF000000 | L(16) | L(8) | L(0);
    }

    public static uint Alpha(uint argb, double a) => ((uint)Math.Clamp(a * 255, 0, 255) << 24) | (argb & 0xFFFFFF);

    private static double Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093 ^ y * 19349663 ^ salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return (h & 0xFFFF) / 65535.0;
    }

    /// <summary>Smooth noise from a hashed lattice.</summary>
    private static double Noise(double x, double y, int salt)
    {
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        double tx = x - x0, ty = y - y0;
        tx = tx * tx * (3 - 2 * tx);
        ty = ty * ty * (3 - 2 * ty);
        double a = Hash(x0, y0, salt) + (Hash(x0 + 1, y0, salt) - Hash(x0, y0, salt)) * tx;
        double b = Hash(x0, y0 + 1, salt) + (Hash(x0 + 1, y0 + 1, salt) - Hash(x0, y0 + 1, salt)) * tx;
        return a + (b - a) * ty;
    }

    private static double Fractal(double x, double y, int salt) =>
        (Noise(x, y, salt) * 4 + Noise(x * 2, y * 2, salt + 1) * 2 + Noise(x * 4, y * 4, salt + 2)) / 7;

    // ───────────────────────────── planets ─────────────────────────────

    public const int PlanetPixels = 40;

    /// <summary>A planet as a lit sphere: land and sea (or rock and fire) by its kind, a dark side, and a thin rim of air.</summary>
    public static uint[] PlanetPixelsOf(PlanetType type, int seed)
    {
        int n = PlanetPixels;
        var px = new uint[n * n];
        var kind = Planets.Kind(type);
        uint land = kind.Land, sea = kind.Sea;
        double r = n / 2.0 - 1;
        // How much of each world is "sea" (the second colour).
        double seaShare = type switch
        {
            PlanetType.Terran => 0.55, PlanetType.Ocean => 0.8, PlanetType.Jungle => 0.3, PlanetType.Arid => 0.3, PlanetType.Steppe => 0.35,
            PlanetType.Desert => 0.2, PlanetType.Tundra => 0.4, PlanetType.Inferno => 0.45, PlanetType.Toxic => 0.5, _ => 0.4,
        };
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            double dx = (x + 0.5 - n / 2.0) / r, dy = (y + 0.5 - n / 2.0) / r;
            double d2 = dx * dx + dy * dy;
            if (d2 > 1) continue;
            double z = Math.Sqrt(1 - d2);
            // Map the sphere's surface onto noise, so the land curves away at the edges.
            double u = dx / (z + 0.6) * 2.2, v = dy / (z + 0.6) * 2.2;
            double h = Fractal(u + 7, v + 3, seed);
            uint c = h < seaShare ? sea : land;
            if (type is PlanetType.Terran or PlanetType.Ocean or PlanetType.Jungle && h > 0.78) c = 0xFFE8EEF0; // high cloud
            if (type is PlanetType.Tundra && Math.Abs(dy) > 0.6) c = 0xFFF0F4F8;
            if (type is PlanetType.Barren or PlanetType.Dead && Hash(x / 3, y / 3, seed) > 0.9) c = Shade(land, 0.7);
            // Light from the upper left.
            double light = Math.Clamp(0.25 + 0.95 * (-dx * 0.6 - dy * 0.55 + z * 0.6), 0.12, 1.15);
            c = Shade(c, light);
            if (d2 > 0.86) c = Mix(c, type is PlanetType.Inferno ? 0xFFFF8040 : type is PlanetType.Toxic ? 0xFFC0F060 : 0xFF9AD0FF, 0.35);
            px[y * n + x] = c;
        }
        return px;
    }

    private static readonly Dictionary<(PlanetType, int), Bitmap> PlanetCache = [];

    public static Bitmap PlanetBitmap(PlanetType type, int seed)
    {
        if (!PlanetCache.TryGetValue((type, seed), out var b)) PlanetCache[(type, seed)] = b = Bitmap(PlanetPixelsOf(type, seed), PlanetPixels);
        return b;
    }

    // ───────────────────────────── nebulae ─────────────────────────────

    public const int NebulaPixels = 128;

    /// <summary>Faint clouds of gas behind the stars, coloured by the galaxy's seed.</summary>
    public static uint[] NebulaPixelsOf(int seed)
    {
        int n = NebulaPixels;
        var px = new uint[n * n];
        uint[] tints = [0xFF6A3A9A, 0xFF2A5AA8, 0xFFA83A6A, 0xFF2A8A8A];
        uint a = tints[Math.Abs(seed) % 4], b = tints[(Math.Abs(seed) / 4 + 1) % 4];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            double h = Fractal(x / 22.0, y / 22.0, seed);
            double k = Fractal(x / 9.0 + 5, y / 9.0 + 2, seed + 7);
            double glow = Math.Clamp((h - 0.48) * 2.4, 0, 1) * (0.6 + 0.4 * k);
            uint c = Mix(a, b, Noise(x / 30.0, y / 30.0, seed + 3));
            px[y * n + x] = Alpha(c, glow * 0.28);
        }
        return px;
    }

    private static readonly Dictionary<int, Bitmap> NebulaCache = [];

    public static Bitmap NebulaBitmap(int seed)
    {
        if (!NebulaCache.TryGetValue(seed, out var b))
        {
            if (NebulaCache.Count > 4) NebulaCache.Clear();
            NebulaCache[seed] = b = Bitmap(NebulaPixelsOf(seed), NebulaPixels);
        }
        return b;
    }

    // ───────────────────────────── bitmaps ─────────────────────────────

    public static Bitmap Bitmap(uint[] px, int n)
    {
        var wb = new WriteableBitmap(new PixelSize(n, n), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (var fb = wb.Lock())
        {
            unsafe
            {
                for (int y = 0; y < n; y++)
                    px.AsSpan(y * n, n).CopyTo(new Span<uint>((void*)(fb.Address + y * fb.RowBytes), n));
            }
        }
        return wb;
    }
}
