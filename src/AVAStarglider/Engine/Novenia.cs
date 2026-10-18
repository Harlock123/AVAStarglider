namespace AVAStarglider.Engine;

/// <summary>
/// The controls: left and right turn the AGAV, up speeds it up and down slows it; climb and dive; fire the lasers;
/// and launch a missile.
/// </summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false, bool Missile = false, int Climb = 0);

/// <summary>The screen, and the planet's surface: a square that wraps round at its edges.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double Size = 6000, MaxAltitude = 320, Floor = 6;
}

/// <summary>A pair of pylons with a power line slung between them.</summary>
public readonly record struct LineDef(double X0, double Y0, double X1, double Y1);

/// <summary>A missile silo: dock in it (low, slow, over its middle) for missiles and a little energy.</summary>
public readonly record struct SiloDef(double X, double Y);

/// <summary>The surface: its power lines and silos, and how hard the Egrons come this time.</summary>
public sealed class NoveniaDef
{
    public int Level;
    public List<LineDef> Lines = [];
    public List<SiloDef> Silos = [];
    /// <summary>How many Egrons at once, and how fast and often they shoot.</summary>
    public int Egrons;
    /// <summary>How many to bring down before the Starglider comes.</summary>
    public int Quota;
    public double Aggression;
    public uint Grid;

    /// <summary>The shortest way across the wrapping surface from one coordinate to another.</summary>
    public static double Delta(double from, double to)
    {
        double d = (to - from) % Field.Size;
        if (d > Field.Size / 2) d -= Field.Size;
        if (d < -Field.Size / 2) d += Field.Size;
        return d;
    }

    public static double Wrap(double v) => ((v % Field.Size) + Field.Size) % Field.Size;

    public static double Dist(double ax, double ay, double bx, double by)
    {
        double dx = Delta(ax, bx), dy = Delta(ay, by);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public static double Bearing(double fx, double fy, double tx, double ty) => Math.Atan2(Delta(fx, tx), Delta(fy, ty));
}

public static class NoveniaBook
{
    public const double PylonHeight = 120, LineHeight = 105, PylonGap = 520, SiloSize = 70, SiloHeight = 44;
    private static readonly uint[] Grids = [0xFF30C060, 0xFF3080E0, 0xFFE0A030, 0xFFC040C0, 0xFF40C0C0, 0xFFE05050];

    public static NoveniaDef Build(int level)
    {
        var d = new Dice(4242 + level * 37);
        var n = new NoveniaDef { Level = level, Egrons = 5 + Math.Min(7, level * 2), Quota = 8 + level * 3, Aggression = 1 + level * 0.25, Grid = Grids[level % Grids.Length] };
        // Power lines across the surface, each a straight run between two pylons.
        for (int k = 0; k < 6; k++)
        {
            double x = d.Next() * Field.Size, y = d.Next() * Field.Size, a = d.Next() * Math.PI;
            n.Lines.Add(new LineDef(x, y, NoveniaDef.Wrap(x + Math.Sin(a) * PylonGap), NoveniaDef.Wrap(y + Math.Cos(a) * PylonGap)));
        }
        // Silos, well spread.
        while (n.Silos.Count < 4)
        {
            double x = d.Next() * Field.Size, y = d.Next() * Field.Size;
            if (n.Silos.Any(s => NoveniaDef.Dist(s.X, s.Y, x, y) < 1800)) continue;
            if (n.Lines.Any(l => NoveniaDef.Dist(l.X0, l.Y0, x, y) < 400 || NoveniaDef.Dist(l.X1, l.Y1, x, y) < 400)) continue;
            n.Silos.Add(new SiloDef(x, y));
        }
        return n;
    }
}
