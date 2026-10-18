namespace AVAStarglider.Engine;

/// <summary>
/// The surface of Novenia, step by step: the AGAV flying, its lasers (tipping up or down onto whatever is near the line
/// of fire) and homing missiles; the Egrons; recharging under the power lines; docking in a silo for missiles; and
/// the great Starglider, which only missiles can bring down.
/// </summary>
public static class Sim
{
    private const double Dt = Field.Step;
    public const double BoltSpeed = 900, EnemyBolt = 300, LaserCost = 0.4, ChargeRate = 18, DockEnergy = 30, HitDamage = 8, BumpDamage = 15;
    public const double MissileSpeed = 380, MissileTurn = 1.9, MissileLife = 6;
    public const int StargliderLife = 4;

    public static int Points(Kind k) => k switch { Kind.Walker => 200, Kind.Tank => 300, Kind.Fighter => 400, _ => 5000 };

    public static void Start(World w, NoveniaDef n, int seed)
    {
        w.Novenia = n;
        w.Dice = new Dice(seed);
        w.Agav = new Agav { X = Field.Size / 2, Y = Field.Size / 2 };
        w.Egrons.Clear();
        w.Bolts.Clear();
        w.Missiles.Clear();
        w.Flashes.Clear();
        w.Clock = 0;
        w.NextEgron = 3;
        w.StargliderDown = w.StargliderCame = false;
        w.Kills = 0;
        w.Cause = "";
        for (int k = 0; k < n.Egrons; k++) Spawn(w, null);
    }

    /// <summary>A fresh AGAV in the middle of the surface (the Egrons keep on).</summary>
    public static void Respawn(World w)
    {
        int missiles = w.Agav.Missiles;
        w.Agav = new Agav { X = Field.Size / 2, Y = Field.Size / 2, Missiles = Math.Max(missiles, 2) };
        w.Bolts.RemoveAll(b => !b.Mine);
        w.Cause = "";
    }

    private static void Spawn(World w, Kind? kind)
    {
        var a = w.Agav;
        var k = kind ?? (w.Dice.Next(10) switch { < 4 => Kind.Walker, < 7 => Kind.Tank, _ => Kind.Fighter });
        double ang = w.Dice.Next() * Math.PI * 2, r = k == Kind.Starglider ? 2200 : 1100 + w.Dice.Next() * 1400;
        var e = new Egron
        {
            Kind = k, X = NoveniaDef.Wrap(a.X + Math.Sin(ang) * r), Y = NoveniaDef.Wrap(a.Y + Math.Cos(ang) * r),
            Heading = w.Dice.Next() * Math.PI * 2, Cool = 2 + w.Dice.Next() * 2, Phase = w.Dice.Next() * 10,
        };
        (e.Speed, e.Life, e.Z) = k switch
        {
            Kind.Walker => (40.0, 3.0, 0.0),
            Kind.Tank => (70.0, 4.0, 0.0),
            Kind.Fighter => (170.0, 2.0, 140.0),
            _ => (175.0, (double)StargliderLife, 190.0),
        };
        w.Egrons.Add(e);
    }

    public static double Wrap(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }

    public static void Step(World w, Controls c)
    {
        w.Clock += Dt;
        foreach (var f in w.Flashes) f.T += Dt;
        w.Flashes.RemoveAll(f => f.T > 0.8);
        var a = w.Agav;
        if (!a.Dead && !w.StargliderDown) Fly(w, a, c);
        Egrons(w);
        Bolts(w);
        Missiles(w);
        if (!a.Dead && a.Energy <= 0)
        {
            a.Energy = 0;
            a.Dead = true;
            if (w.Cause == "") w.Cause = "OUT OF ENERGY";
            w.Flashes.Add(new Flash(a.X, a.Y, a.Alt, true));
            w.Heard.Add(Sfx.Explode);
        }
    }

    private static void Fly(World w, Agav a, Controls c)
    {
        var n = w.Novenia;
        a.Heading = Wrap(a.Heading + c.Dx * Agav.Turn * Dt);
        a.Bank += (c.Dx * 0.35 - a.Bank) * Math.Min(1, Dt * 5);
        if (c.Dy < 0) a.Speed = Math.Min(Agav.MaxSpeed, a.Speed + Agav.Accel * Dt);
        else if (c.Dy > 0) a.Speed = Math.Max(Agav.MinSpeed, a.Speed - Agav.Accel * Dt);
        a.Vz += (c.Climb * Agav.Climb - a.Vz) * Math.Min(1, Dt * 4);
        double nx = NoveniaDef.Wrap(a.X + Math.Sin(a.Heading) * a.Speed * Dt), ny = NoveniaDef.Wrap(a.Y + Math.Cos(a.Heading) * a.Speed * Dt);
        double nz = Math.Clamp(a.Alt + a.Vz * Dt, Field.Floor, Field.MaxAltitude);
        // Into a pylon, or the side of a silo: knocked back.
        bool bump = false;
        foreach (var l in n.Lines)
            foreach (var (px, py) in new[] { (l.X0, l.Y0), (l.X1, l.Y1) })
                if (nz < NoveniaBook.PylonHeight && NoveniaDef.Dist(nx, ny, px, py) < 20) bump = true;
        a.DockCool -= Dt;
        foreach (var s in n.Silos)
        {
            double d = NoveniaDef.Dist(nx, ny, s.X, s.Y);
            if (d < 30 && nz < NoveniaBook.SiloHeight + 22 && a.Speed < 90 && a.DockCool <= 0)
            {
                // Docked: missiles loaded, a little energy.
                a.Missiles = Agav.Missiles0;
                a.Energy = Math.Min(Agav.Energy0, a.Energy + DockEnergy);
                a.DockCool = 12;
                w.Heard.Add(Sfx.Dock);
            }
            else if (d < NoveniaBook.SiloSize / 2 + 8 && nz < NoveniaBook.SiloHeight) bump = true;
        }
        if (bump)
        {
            a.Energy -= BumpDamage;
            a.Speed = 0;
            w.Cause = "FLEW INTO A STRUCTURE";
            w.Heard.Add(Sfx.Bump);
            nx = a.X;
            ny = a.Y;
        }
        a.X = nx;
        a.Y = ny;
        a.Alt = nz;
        // Under a power line, between its pylons: energy.
        w.Charging = false;
        foreach (var l in n.Lines)
        {
            double lx = NoveniaDef.Delta(l.X0, l.X1), ly = NoveniaDef.Delta(l.Y0, l.Y1), len2 = lx * lx + ly * ly;
            double px = NoveniaDef.Delta(l.X0, a.X), py = NoveniaDef.Delta(l.Y0, a.Y);
            double t = (px * lx + py * ly) / len2;
            if (t < 0.08 || t > 0.92) continue;
            double off = Math.Abs(px * ly - py * lx) / Math.Sqrt(len2);
            if (off < 40 && a.Alt > 12 && a.Alt < NoveniaBook.LineHeight - 8) { w.Charging = true; break; }
        }
        if (w.Charging) a.Energy = Math.Min(Agav.Energy0, a.Energy + ChargeRate * Dt);
        a.FireTimer -= Dt;
        if (c.Fire && a.FireTimer <= 0 && a.Energy > LaserCost)
        {
            a.FireTimer = 0.16;
            a.Energy -= LaserCost;
            Laser(w, a);
        }
        if (c.Missile && !a.MissilePrev && a.Missiles > 0)
        {
            a.Missiles--;
            w.Missiles.Add(new Missile { X = a.X, Y = a.Y, Z = a.Alt - 3, Heading = a.Heading, Life = MissileLife, Target = MissileTarget(w, a) });
            w.Heard.Add(Sfx.Launch);
        }
        a.MissilePrev = c.Missile;
    }

    /// <summary>A laser bolt straight ahead, tipped up or down onto whatever is near the line of fire.</summary>
    private static void Laser(World w, Agav a)
    {
        double v = BoltSpeed + a.Speed, vz = 0, best = 0.12;
        foreach (var e in w.Egrons)
        {
            double d = NoveniaDef.Dist(a.X, a.Y, e.X, e.Y);
            if (d > 1300 || d < 15) continue;
            double off = Math.Abs(Wrap(NoveniaDef.Bearing(a.X, a.Y, e.X, e.Y) - a.Heading));
            if (off < best) { best = off; vz = (e.Z + e.Size * 0.4 - a.Alt) / (d / v); }
        }
        double sx = Math.Sin(a.Heading), sy = Math.Cos(a.Heading);
        w.Bolts.Add(new Bolt { X = a.X + sx * 8, Y = a.Y + sy * 8, Z = a.Alt - 2, Vx = sx * v, Vy = sy * v, Vz = vz, Life = 1.5, Mine = true });
        w.Heard.Add(Sfx.Laser);
    }

    /// <summary>What a missile goes for: the Starglider if it's anywhere ahead, else the nearest Egron ahead.</summary>
    public static Egron? MissileTarget(World w, Agav a)
    {
        Egron? pick = null;
        double best = double.MaxValue;
        foreach (var e in w.Egrons)
        {
            double off = Math.Abs(Wrap(NoveniaDef.Bearing(a.X, a.Y, e.X, e.Y) - a.Heading));
            if (off > 0.7) continue;
            double score = NoveniaDef.Dist(a.X, a.Y, e.X, e.Y) - (e.Kind == Kind.Starglider ? 5000 : 0);
            if (score < best) { best = score; pick = e; }
        }
        return pick;
    }

    private static void Egrons(World w)
    {
        var a = w.Agav;
        var n = w.Novenia;
        foreach (var e in w.Egrons)
        {
            e.Cool -= Dt;
            double d = NoveniaDef.Dist(e.X, e.Y, a.X, a.Y);
            double toward = NoveniaDef.Bearing(e.X, e.Y, a.X, a.Y);
            double want = e.Kind switch
            {
                // Fighters circle the AGAV; the Starglider sweeps round in great loops, closing now and then.
                Kind.Fighter => toward + (d < 300 ? Math.PI / 2 : 0),
                Kind.Starglider => toward + (d < 700 ? Math.PI / 2.2 : Math.Sin(w.Clock * 0.3 + e.Phase) * 0.8),
                _ => d < 1600 ? toward : e.Heading + Math.Sin(w.Clock * 0.5 + e.Phase) * 0.5,
            };
            double rate = e.Kind is Kind.Fighter or Kind.Starglider ? 1.4 : 0.8;
            e.Heading = Wrap(e.Heading + Math.Clamp(Wrap(want - e.Heading), -rate * Dt, rate * Dt));
            double sp = e.Kind is Kind.Walker or Kind.Tank && d < 200 ? 0 : e.Speed;
            e.X = NoveniaDef.Wrap(e.X + Math.Sin(e.Heading) * sp * Dt);
            e.Y = NoveniaDef.Wrap(e.Y + Math.Cos(e.Heading) * sp * Dt);
            if (e.Kind == Kind.Fighter) e.Z += (Math.Clamp(a.Alt + 20, 60, 260) - e.Z) * Math.Min(1, Dt * 0.8);
            if (e.Kind == Kind.Starglider)
            {
                e.Z = 170 + Math.Sin(w.Clock * 0.7 + e.Phase) * 50;
                // Its swoop: contact with the AGAV costs dear.
                if (!a.Dead && d < 70 && Math.Abs(e.Z - a.Alt) < 50 && e.Cool <= 0)
                {
                    e.Cool = 3;
                    a.Energy -= 25;
                    w.Cause = "STRUCK BY THE STARGLIDER";
                    w.Heard.Add(Sfx.Hurt);
                }
                continue;
            }
            // Fire at an AGAV in range.
            if (!a.Dead && !w.StargliderDown && e.Cool <= 0 && d < 650)
            {
                e.Cool = (2.6 + w.Dice.Next() * 1.5) / n.Aggression;
                double t = Math.Sqrt(d * d + (a.Alt - e.Z) * (a.Alt - e.Z)) / EnemyBolt;
                double tx = NoveniaDef.Delta(e.X, a.X) + Math.Sin(a.Heading) * a.Speed * t * 0.6, ty = NoveniaDef.Delta(e.Y, a.Y) + Math.Cos(a.Heading) * a.Speed * t * 0.6;
                double ez = e.Z + e.Size * 0.6;
                w.Bolts.Add(new Bolt { X = e.X, Y = e.Y, Z = ez, Vx = tx / t, Vy = ty / t, Vz = (a.Alt - ez) / t, Life = t + 0.4 });
                w.Heard.Add(Sfx.EgronFire);
            }
        }
        // Enough of them down: the Starglider comes.
        if (!w.StargliderCame && w.Kills >= n.Quota)
        {
            w.StargliderCame = true;
            Spawn(w, Kind.Starglider);
            w.Heard.Add(Sfx.SayStarglider);
        }
        // More Egrons, to keep up the numbers.
        w.NextEgron -= Dt;
        if (w.NextEgron <= 0)
        {
            w.NextEgron = 4;
            if (w.Egrons.Count(e => e.Kind != Kind.Starglider) < n.Egrons) Spawn(w, null);
        }
    }

    private static void Kill(World w, Egron e)
    {
        w.Score += Points(e.Kind);
        w.Kills++;
        w.Flashes.Add(new Flash(e.X, e.Y, e.Z + e.Size * 0.5, true));
        w.Heard.Add(e.Kind == Kind.Starglider ? Sfx.StargliderDown : Sfx.Boom);
        if (e.Kind == Kind.Starglider) w.StargliderDown = true;
    }

    private static void Bolts(World w)
    {
        var a = w.Agav;
        foreach (var b in w.Bolts)
        {
            b.X = NoveniaDef.Wrap(b.X + b.Vx * Dt);
            b.Y = NoveniaDef.Wrap(b.Y + b.Vy * Dt);
            b.Z += b.Vz * Dt;
            b.Life -= Dt;
            if (b.Life <= 0) continue;
            if (b.Z < 0) { b.Life = 0; w.Flashes.Add(new Flash(b.X, b.Y, 0, false)); continue; }
            if (b.Mine)
            {
                foreach (var e in w.Egrons)
                {
                    if (e.Life <= 0) continue;
                    double d = NoveniaDef.Dist(b.X, b.Y, e.X, e.Y), dz = b.Z - (e.Z + e.Size * 0.5);
                    if (d * d + dz * dz > e.Size * e.Size) continue;
                    b.Life = 0;
                    if (e.Kind == Kind.Starglider) { w.Heard.Add(Sfx.Deflect); break; }
                    e.Life -= 1;
                    w.Flashes.Add(new Flash(b.X, b.Y, b.Z, false));
                    if (e.Life <= 0) Kill(w, e);
                    else w.Heard.Add(Sfx.Hit);
                    break;
                }
            }
            else if (!a.Dead)
            {
                double d = NoveniaDef.Dist(b.X, b.Y, a.X, a.Y), dz = b.Z - a.Alt;
                if (d * d + dz * dz < 16 * 16)
                {
                    b.Life = 0;
                    a.Energy -= HitDamage;
                    w.Cause = "SHOT DOWN";
                    w.Flashes.Add(new Flash(a.X, a.Y, a.Alt, false));
                    w.Heard.Add(Sfx.Hurt);
                }
            }
        }
        w.Bolts.RemoveAll(b => b.Life <= 0);
        w.Egrons.RemoveAll(e => e.Life <= 0);
    }

    private static void Missiles(World w)
    {
        foreach (var m in w.Missiles)
        {
            m.Life -= Dt;
            if (m.Target is { Life: > 0 } t && w.Egrons.Contains(t))
            {
                double want = NoveniaDef.Bearing(m.X, m.Y, t.X, t.Y);
                m.Heading = Wrap(m.Heading + Math.Clamp(Wrap(want - m.Heading), -MissileTurn * Dt, MissileTurn * Dt));
                double d = NoveniaDef.Dist(m.X, m.Y, t.X, t.Y);
                double wantPitch = Math.Atan2(t.Z + t.Size * 0.5 - m.Z, Math.Max(1, d));
                m.Pitch += Math.Clamp(wantPitch - m.Pitch, -MissileTurn * Dt, MissileTurn * Dt);
            }
            double cp = Math.Cos(m.Pitch);
            m.X = NoveniaDef.Wrap(m.X + Math.Sin(m.Heading) * cp * MissileSpeed * Dt);
            m.Y = NoveniaDef.Wrap(m.Y + Math.Cos(m.Heading) * cp * MissileSpeed * Dt);
            m.Z += Math.Sin(m.Pitch) * MissileSpeed * Dt;
            if (m.Z < 0) { m.Life = 0; w.Flashes.Add(new Flash(m.X, m.Y, 0, true)); continue; }
            foreach (var e in w.Egrons)
            {
                if (e.Life <= 0) continue;
                double d = NoveniaDef.Dist(m.X, m.Y, e.X, e.Y), dz = m.Z - (e.Z + e.Size * 0.5);
                if (d * d + dz * dz > (e.Size + 10) * (e.Size + 10)) continue;
                m.Life = 0;
                e.Life -= e.Kind == Kind.Starglider ? 1 : 99;
                w.Flashes.Add(new Flash(m.X, m.Y, m.Z, true));
                if (e.Life <= 0) Kill(w, e);
                else w.Heard.Add(Sfx.StargliderHit);
                break;
            }
        }
        w.Missiles.RemoveAll(m => m.Life <= 0);
        w.Egrons.RemoveAll(e => e.Life <= 0);
    }
}
