namespace AVAStarglider.Engine;

/// <summary>A small random number generator whose state can be copied.</summary>
public struct Dice
{
    private ulong _s;
    public Dice(int seed) => _s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

    public double Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 7;
        _s ^= _s << 17;
        return (_s >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int n) => (int)(Next() * n);
}

/// <summary>The AGAV: where, how high, which way it heads (0 is north, turning clockwise), how fast, and what it has left.</summary>
public sealed class Agav
{
    public const double MaxSpeed = 260, MinSpeed = 0, Accel = 120, Turn = 1.7, Climb = 90, Energy0 = 100;
    public const int Missiles0 = 3;
    public double X, Y, Alt = 80, Heading, Speed = 80, Vz, Bank;
    public double Energy = Energy0, FireTimer, DockCool;
    public int Missiles = Missiles0;
    public bool Dead, MissilePrev;
    public Agav Copy() => (Agav)MemberwiseClone();
}

public enum Kind { Walker, Tank, Fighter, Starglider }

/// <summary>An Egron: a walker or a tank on the ground, a fighter in the air, or the great Starglider bird.</summary>
public sealed class Egron
{
    public Kind Kind;
    public double X, Y, Z, Heading, Speed, Life, Cool, Phase;
    public Egron Copy() => (Egron)MemberwiseClone();
    public double Size => Kind switch { Kind.Walker => 22, Kind.Tank => 20, Kind.Fighter => 18, _ => 60 };
}

public sealed class Bolt
{
    public double X, Y, Z, Vx, Vy, Vz, Life;
    public bool Mine;
    public Bolt Copy() => (Bolt)MemberwiseClone();
}

/// <summary>A homing missile and what it's after (an index into the Egrons, or -1).</summary>
public sealed class Missile
{
    public double X, Y, Z, Heading, Pitch, Life;
    public Egron? Target;
    public Missile Copy() => (Missile)MemberwiseClone();
}

public sealed record Flash(double X, double Y, double Z, bool Big)
{
    public double T;
}

/// <summary>Everything on the surface that moves or changes, kept together so it can be copied whole.</summary>
public sealed class World
{
    public NoveniaDef Novenia = null!;
    public Agav Agav = new();
    public List<Egron> Egrons = [];
    public List<Bolt> Bolts = [];
    public List<Missile> Missiles = [];
    public List<Flash> Flashes = [];
    public List<Sfx> Heard = [];
    public Dice Dice = new(1);
    public double Clock, NextEgron;
    public int Score, Kills;
    public bool StargliderDown, StargliderCame, Charging;
    public string Cause = "";

    public Egron? Starglider => Egrons.FirstOrDefault(e => e.Kind == Kind.Starglider);

    public World Copy()
    {
        var map = Egrons.ToDictionary(e => e, e => e.Copy());
        return new World
        {
            Novenia = Novenia, Agav = Agav.Copy(), Egrons = Egrons.Select(e => map[e]).ToList(), Bolts = Bolts.Select(b => b.Copy()).ToList(),
            Missiles = Missiles.Select(m => { var c = m.Copy(); c.Target = m.Target != null && map.TryGetValue(m.Target, out var t) ? t : null; return c; }).ToList(),
            Dice = Dice, Clock = Clock, NextEgron = NextEgron, Score = Score, Kills = Kills, StargliderDown = StargliderDown, StargliderCame = StargliderCame, Charging = Charging, Cause = Cause,
        };
    }
}
