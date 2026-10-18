namespace AVAStarglider.Engine;

/// <summary>
/// The computer pilot, for the demonstration and the tests. Low on energy, it flies to the nearest power line and
/// along under it; out of missiles with the Starglider still flying, it docks at the nearest silo. Otherwise it
/// hunts: with missiles and the Starglider in reach it turns on the Starglider and launches; else it turns on the
/// nearest Egron and fires the lasers once it's lined up. It keeps high enough to clear the pylons except when it
/// means to go low.
/// </summary>
public sealed class Pilot
{
    public const double Cruise = 150;
    private bool _missilePrev;
    private bool _charging;

    public string Doing { get; private set; } = "";

    public void Reset()
    {
        _missilePrev = false;
        _charging = false;
        Doing = "";
    }

    public Controls Decide(GameEngine g) => g.Phase != Phase.Play ? default : Decide(g.World);

    public Controls Decide(World w)
    {
        var a = w.Agav;
        var n = w.Novenia;
        if (a.Dead || w.StargliderDown) return default;
        // Recharging: until nearly full.
        if (a.Energy < 35) _charging = true;
        if (a.Energy > 92) _charging = false;
        if (_charging) return Charge(w, a);
        var sg = w.Starglider;
        // Missiles low with the Starglider about (or soon to be): dock for more.
        if (a.Missiles == 0 && w.Missiles.Count == 0 && (sg != null || w.Kills >= w.Novenia.Quota - 2)) return Dock(w, a);
        // The Starglider, with a missile.
        if (sg != null && a.Missiles > 0 && w.Missiles.Count == 0)
        {
            Doing = "after the Starglider";
            double d = NoveniaDef.Dist(a.X, a.Y, sg.X, sg.Y), err = Sim.Wrap(NoveniaDef.Bearing(a.X, a.Y, sg.X, sg.Y) - a.Heading);
            bool launch = d < 1300 && Math.Abs(err) < 0.25;
            bool press = launch && !_missilePrev;
            _missilePrev = launch && !_missilePrev ? true : false;
            return new Controls(Turn(err), Speed(a, d > 600 ? 220 : 140), false, press, Hold(a, Math.Max(Cruise, sg.Z - 20)));
        }
        _missilePrev = false;
        // The nearest Egron (not the Starglider: lasers can't hurt it).
        Egron? target = null;
        double best = double.MaxValue;
        foreach (var e in w.Egrons)
        {
            if (e.Kind == Kind.Starglider) continue;
            double d = NoveniaDef.Dist(a.X, a.Y, e.X, e.Y);
            if (d < best) { best = d; target = e; }
        }
        if (target == null) { Doing = "patrolling"; return new Controls(0, Speed(a, 150), false, false, Hold(a, Cruise)); }
        Doing = "hunting " + target.Kind.ToString().ToLowerInvariant() + "s";
        double terr = Sim.Wrap(NoveniaDef.Bearing(a.X, a.Y, target.X, target.Y) - a.Heading);
        bool fire = best < 1000 && Math.Abs(terr) < 0.09;
        // Too close to turn in on it: run on and come round.
        double want = best < 160 && Math.Abs(terr) > 0.5 ? 220 : best < 400 ? 110 : 200;
        double alt = target.Kind == Kind.Fighter ? target.Z : Cruise;
        return new Controls(Turn(terr), Speed(a, want), fire, false, Hold(a, alt));
    }

    private static int Turn(double err) => Math.Abs(err) < 0.03 ? 0 : Math.Sign(err);
    private static int Speed(Agav a, double want) => a.Speed < want - 8 ? -1 : a.Speed > want + 8 ? 1 : 0;
    private static int Hold(Agav a, double alt) => a.Alt < alt - 6 ? 1 : a.Alt > alt + 6 ? -1 : 0;

    /// <summary>To the nearest power line, lined up along it, and under it between the pylons.</summary>
    private Controls Charge(World w, Agav a)
    {
        Doing = "recharging";
        var n = w.Novenia;
        LineDef line = n.Lines[0];
        double best = double.MaxValue;
        foreach (var l in n.Lines)
        {
            double mx = l.X0 + NoveniaDef.Delta(l.X0, l.X1) / 2, my = l.Y0 + NoveniaDef.Delta(l.Y0, l.Y1) / 2;
            double d = NoveniaDef.Dist(a.X, a.Y, mx, my);
            if (d < best) { best = d; line = l; }
        }
        double lx = NoveniaDef.Delta(line.X0, line.X1), ly = NoveniaDef.Delta(line.Y0, line.Y1), len = Math.Sqrt(lx * lx + ly * ly);
        double ux = lx / len, uy = ly / len;
        double px = NoveniaDef.Delta(line.X0, a.X), py = NoveniaDef.Delta(line.Y0, a.Y);
        double along = px * ux + py * uy, side = px * uy - py * ux;
        // Which way along it: the way the AGAV already heads.
        double lineHeading = Math.Atan2(ux, uy);
        bool backwards = Math.Abs(Sim.Wrap(lineHeading - a.Heading)) > Math.PI / 2;
        double dir = backwards ? -1 : 1;
        double heading = backwards ? Sim.Wrap(lineHeading + Math.PI) : lineHeading;
        // An approach point off the near end, on the line; then along it, steering onto it.
        double start = backwards ? len + 160 : -160;
        bool onLine = Math.Abs(side) < 30 && (backwards ? along < len - 20 && along > 30 : along > 20 && along < len - 30);
        if (!onLine)
        {
            double tx = line.X0 + ux * start, ty = line.Y0 + uy * start;
            double d = NoveniaDef.Dist(a.X, a.Y, tx, ty);
            if (d > 60)
            {
                double err = Sim.Wrap(NoveniaDef.Bearing(a.X, a.Y, tx, ty) - a.Heading);
                return new Controls(Turn(err), Speed(a, d > 400 ? 200 : 80), false, false, Hold(a, d > 300 ? Cruise : 50));
            }
            double herr = Sim.Wrap(heading - a.Heading);
            return new Controls(Turn(herr), Speed(a, 60), false, false, Hold(a, 50));
        }
        // Under the line: steer onto it, slow, low.
        double correct = Math.Clamp(-side * dir * 0.01, -0.4, 0.4);
        double e2 = Sim.Wrap(heading + correct - a.Heading);
        // Near the far pylon: round again.
        bool end = backwards ? along < 60 : along > len - 60;
        return new Controls(Turn(e2), Speed(a, end ? 30 : 50), false, false, Hold(a, 50));
    }

    /// <summary>To the nearest silo: low and slow over its middle.</summary>
    private Controls Dock(World w, Agav a)
    {
        Doing = "docking for missiles";
        var s = w.Novenia.Silos.OrderBy(x => NoveniaDef.Dist(a.X, a.Y, x.X, x.Y)).First();
        double d = NoveniaDef.Dist(a.X, a.Y, s.X, s.Y), err = Sim.Wrap(NoveniaDef.Bearing(a.X, a.Y, s.X, s.Y) - a.Heading);
        double alt = d > 250 ? Cruise : NoveniaBook.SiloHeight + 10;
        double speed = d > 400 ? 220 : d > 120 ? 70 : 30;
        // (Close in and still high: wait for the height to come down before closing.)
        if (d < 120 && a.Alt > NoveniaBook.SiloHeight + 20) speed = 0;
        return new Controls(Turn(err), Speed(a, speed), false, false, Hold(a, alt));
    }
}
