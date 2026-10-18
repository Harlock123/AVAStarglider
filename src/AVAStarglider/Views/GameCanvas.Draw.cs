using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAStarglider.Engine;

namespace AVAStarglider.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAStarglider/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Cyan = 0xFF6AE8FF, Orange = 0xFFFF9A30;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);


    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused;

    private const double ViewH = 600, Focal = 620, Near = 4, Far = 3400, HorizonY = 280;
    private static readonly (double X, double Y, double Z) Light = Norm(0.4, -0.5, 0.75);

    /// <summary>The camera: where (on the wrapping surface), and its heading and bank. Things are placed relative to it.</summary>
    private readonly record struct Cam(double X, double Y, double Z, double Sin, double Cos, double RSin, double RCos);
    private readonly record struct V3(double S, double U, double F);

    private sealed class Item
    {
        public double Depth;
        public Point[] Pts = [];
        public uint Fill, Edge;
        public double Width = 1, R;
        public int Kind;
    }

    private readonly List<Item> _items = [];
    private World? _titleWorld;

    private static (double X, double Y, double Z) Norm(double x, double y, double z)
    {
        double l = Math.Sqrt(x * x + y * y + z * z);
        return (x / l, y / l, z / l);
    }

    private static uint Mix(uint a, uint b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        uint Ch(int sh) => (uint)Math.Round(((a >> sh) & 0xFF) * (1 - t) + ((b >> sh) & 0xFF) * t);
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    private static uint Shade(uint c, double k) => Mix(0xFF000000, c, k);
    private static double Fade(double d) => Math.Clamp((d - 2200) / (Far - 2200), 0, 0.95);

    /// <summary>A point on the surface into the camera's frame: across, up, and ahead (the surface wraps round).</summary>
    private static V3 ToCam(in Cam c, double x, double y, double z)
    {
        double dx = NoveniaDef.Delta(c.X, x), dy = NoveniaDef.Delta(c.Y, y);
        return new V3(dx * c.Cos - dy * c.Sin, z - c.Z, dx * c.Sin + dy * c.Cos);
    }

    private static Point Screen(in Cam c, V3 v)
    {
        double px = v.S / v.F * Focal, py = -v.U / v.F * Focal;
        return new Point(Field.Width / 2 + px * c.RCos - py * c.RSin, HorizonY + px * c.RSin + py * c.RCos);
    }

    private static Point[]? Project(in Cam c, List<V3> poly)
    {
        var outp = new List<V3>(poly.Count + 2);
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            bool ina = a.F >= Near, inb = b.F >= Near;
            if (ina) outp.Add(a);
            if (ina != inb)
            {
                double t = (Near - a.F) / (b.F - a.F);
                outp.Add(new V3(a.S + (b.S - a.S) * t, a.U + (b.U - a.U) * t, Near));
            }
        }
        if (outp.Count < 3) return null;
        var pts = new Point[outp.Count];
        for (int i = 0; i < pts.Length; i++) pts[i] = Screen(c, outp[i]);
        return pts;
    }

    private void Face(in Cam c, double depth, uint fill, uint edge, params (double X, double Y, double Z)[] v)
    {
        var poly = new List<V3>(v.Length);
        foreach (var p in v) poly.Add(ToCam(c, p.X, p.Y, p.Z));
        if (Project(c, poly) is { } pts) _items.Add(new Item { Depth = depth, Pts = pts, Fill = fill, Edge = edge });
    }

    private void Line(in Cam c, double depth, uint colour, double width, (double X, double Y, double Z) a, (double X, double Y, double Z) b)
    {
        var va = ToCam(c, a.X, a.Y, a.Z);
        var vb = ToCam(c, b.X, b.Y, b.Z);
        if (va.F < Near && vb.F < Near) return;
        if (va.F < Near) va = Cut(vb, va);
        else if (vb.F < Near) vb = Cut(va, vb);
        _items.Add(new Item { Depth = depth, Pts = [Screen(c, va), Screen(c, vb)], Edge = colour, Width = width, Kind = 1 });

        static V3 Cut(V3 p, V3 q)
        {
            double t = (Near - p.F) / (q.F - p.F);
            return new V3(p.S + (q.S - p.S) * t, p.U + (q.U - p.U) * t, Near);
        }
    }

    /// <summary>One face of a convex solid, if it faces the eye: shaded, with a bright edge.</summary>
    private void Solid(in Cam cam, (double X, double Y, double Z) centre, double depth, uint colour, params (double X, double Y, double Z)[] v)
    {
        var c = cam;
        // (Worked relative to the camera, so the wrap doesn't matter.)
        (double X, double Y, double Z) R((double X, double Y, double Z) p) => (NoveniaDef.Delta(c.X, p.X), NoveniaDef.Delta(c.Y, p.Y), p.Z);
        var (a, b, d) = (R(v[0]), R(v[1]), R(v[2]));
        var m = R(centre);
        double ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z, wx = d.X - a.X, wy = d.Y - a.Y, wz = d.Z - a.Z;
        var n = Norm(uy * wz - uz * wy, uz * wx - ux * wz, ux * wy - uy * wx);
        double mx = v.Average(p => NoveniaDef.Delta(c.X, p.X)), my = v.Average(p => NoveniaDef.Delta(c.Y, p.Y)), mz = v.Average(p => p.Z);
        if (n.X * (mx - m.X) + n.Y * (my - m.Y) + n.Z * (mz - m.Z) < 0) n = (-n.X, -n.Y, -n.Z);
        if (n.X * (0 - a.X) + n.Y * (0 - a.Y) + n.Z * (c.Z - a.Z) <= 0) return;
        double lit = 0.4 + 0.6 * Math.Max(0, n.X * Light.X + n.Y * Light.Y + n.Z * Light.Z);
        Face(c, depth, Mix(Shade(colour, lit * 0.55), 0xFF000000, Fade(depth)), Mix(colour, 0xFF000000, Fade(depth)), v);
    }

    private void Box(in Cam c, double depth, uint colour, double x, double y, double z0, double w, double d, double h, double turn = 0)
    {
        double s = Math.Sin(turn), co = Math.Cos(turn);
        (double, double, double) P(double px, double py, double pz) => (x + px * co + py * s, y - px * s + py * co, pz);
        double hw = w / 2, hd = d / 2, z1 = z0 + h;
        var m = (x, y, z0 + h / 2);
        Solid(c, m, depth, colour, P(hw, -hd, z0), P(hw, hd, z0), P(hw, hd, z1), P(hw, -hd, z1));
        Solid(c, m, depth, colour, P(-hw, -hd, z0), P(-hw, -hd, z1), P(-hw, hd, z1), P(-hw, hd, z0));
        Solid(c, m, depth, colour, P(-hw, hd, z0), P(-hw, hd, z1), P(hw, hd, z1), P(hw, hd, z0));
        Solid(c, m, depth, colour, P(-hw, -hd, z0), P(hw, -hd, z0), P(hw, -hd, z1), P(-hw, -hd, z1));
        Solid(c, m, depth, colour, P(-hw, -hd, z1), P(hw, -hd, z1), P(hw, hd, z1), P(-hw, hd, z1));
    }

    private static Cam MakeCam(double x, double y, double z, double heading, double bank) =>
        new(x, y, z, Math.Sin(heading), Math.Cos(heading), Math.Sin(-bank), Math.Cos(-bank));

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF000000), new Rect(0, 0, Field.Width, Field.Height));
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            case GameState.GameOver: DrawGameOver(dc); break;
            default:
            {
                var w = g.World;
                var a = w.Agav;
                using (dc.PushClip(new Rect(0, 0, Field.Width, ViewH)))
                {
                    DrawNovenia(dc, w, MakeCam(a.X, a.Y, a.Alt + 4, a.Heading, a.Bank));
                    DrawSight(dc, w);
                    if (g.Phase == Phase.Dying)
                        dc.FillRectangle(Brush(Alpha(0xFFFF4020, Math.Max(0, 0.6 - g.PhaseTime * 0.3))), new Rect(0, 0, Field.Width, ViewH));
                    else if (a.Energy < 25 && (int)(_clock * 4) % 2 == 0)
                        dc.DrawRectangle(null, Pen(0xA0FF2020, 8), new Rect(4, 4, Field.Width - 8, ViewH - 8));
                    if (w.Charging) dc.DrawRectangle(null, Pen(Alpha(0xFF60C0FF, 0.4 + 0.3 * Math.Sin(_clock * 12)), 6), new Rect(8, 8, Field.Width - 16, ViewH - 16));
                }
                DrawPanel(dc, w);
                if (g.Message is { } msg)
                {
                    double wdt = msg.Length * 18 + 40;
                    dc.FillRectangle(Brush(0xB0000000), new Rect(512 - wdt / 2, 110, wdt, 56), 6);
                    Text(dc, msg, new Point(512, 138), 40, g.Phase == Phase.Dying ? Red : Yellow, center: true);
                }
                break;
            }
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 300), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 360), 26, White, center: true);
        }
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, 30), 22, Yellow, center: true);
    }

    /// <summary>Novenia from the AGAV: sky and ground, the grid, then everything on it, farthest first.</summary>
    private void DrawNovenia(DrawingContext dc, World w, Cam c)
    {
        var n = w.Novenia;
        dc.FillRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromUInt32(0xFF000008), 0), new GradientStop(Color.FromUInt32(0xFF101838), 1) },
        }, new Rect(0, 0, Field.Width, ViewH));
        Point Tilt(double x, double y) => new(Field.Width / 2 + x * c.RCos - y * c.RSin, HorizonY + x * c.RSin + y * c.RCos);
        Poly(dc, 0xFF020806, null, 0, Tilt(-3000, 0), Tilt(3000, 0), Tilt(3000, 3000), Tilt(-3000, 3000));
        _items.Clear();
        // The grid on the ground, round about.
        const double step = 200, reach = 2600;
        double gx0 = Math.Floor((c.X - reach) / step) * step, gy0 = Math.Floor((c.Y - reach) / step) * step;
        for (double g = 0; g <= reach * 2; g += step)
        {
            uint col = Mix(Shade(n.Grid, 0.55), 0xFF000000, 0.15);
            Line(c, 1e9, col, 1, (gx0 + g, c.Y - reach, 0), (gx0 + g, c.Y + reach, 0));
            Line(c, 1e9, col, 1, (c.X - reach, gy0 + g, 0), (c.X + reach, gy0 + g, 0));
        }
        foreach (var it in _items) dc.DrawLine(Pen(it.Edge, it.Width), it.Pts[0], it.Pts[1]);
        _items.Clear();
        double Dist(double x, double y) => NoveniaDef.Dist(c.X, c.Y, x, y);
        // Pylons and their power lines.
        foreach (var l in n.Lines)
        {
            foreach (var (px, py) in new[] { (l.X0, l.Y0), (l.X1, l.Y1) })
            {
                double d = Dist(px, py);
                if (d > Far) continue;
                uint col = Mix(0xFFC0C0D0, 0xFF000000, Fade(d));
                for (int k = 0; k < 4; k++)
                {
                    double ox = k % 2 == 0 ? -10 : 10, oy = k < 2 ? -10 : 10;
                    Line(c, d, col, 2, (px + ox, py + oy, 0), (px + ox * 0.3, py + oy * 0.3, NoveniaBook.PylonHeight));
                }
                Line(c, d, col, 3, (px - 30, py, NoveniaBook.LineHeight + 6), (px + 30, py, NoveniaBook.LineHeight + 6));
            }
            double dm = Dist(l.X0 + NoveniaDef.Delta(l.X0, l.X1) / 2, l.Y0 + NoveniaDef.Delta(l.Y0, l.Y1) / 2);
            if (dm < Far + 300)
            {
                uint cable = Mix(0xFF60C0FF, 0xFF000000, Fade(dm));
                double lx = NoveniaDef.Delta(l.X0, l.X1), ly = NoveniaDef.Delta(l.Y0, l.Y1);
                for (int k = 0; k < 8; k++)
                {
                    double t0 = k / 8.0, t1 = (k + 1) / 8.0;
                    double s0 = NoveniaBook.LineHeight - Math.Sin(t0 * Math.PI) * 14, s1 = NoveniaBook.LineHeight - Math.Sin(t1 * Math.PI) * 14;
                    Line(c, dm, cable, 2, (l.X0 + lx * t0, l.Y0 + ly * t0, s0), (l.X0 + lx * t1, l.Y0 + ly * t1, s1));
                }
            }
        }
        // Silos.
        foreach (var s in n.Silos)
        {
            double d = Dist(s.X, s.Y);
            if (d > Far) continue;
            Box(c, d, 0xFF8090A0, s.X, s.Y, 0, NoveniaBook.SiloSize, NoveniaBook.SiloSize, NoveniaBook.SiloHeight);
            Face(c, d - 0.5, Mix(0xFF101010, 0xFF000000, Fade(d)), Mix((int)(_clock * 2) % 2 == 0 ? 0xFFFFE040 : 0xFF806010, 0xFF000000, Fade(d)),
                (s.X - 18, s.Y - 18, NoveniaBook.SiloHeight + 0.5), (s.X + 18, s.Y - 18, NoveniaBook.SiloHeight + 0.5), (s.X + 18, s.Y + 18, NoveniaBook.SiloHeight + 0.5), (s.X - 18, s.Y + 18, NoveniaBook.SiloHeight + 0.5));
        }
        // The Egrons.
        foreach (var e in w.Egrons)
        {
            double d = Dist(e.X, e.Y);
            if (d > Far) continue;
            DrawEgron(c, e, d);
        }
        // Bolts, missiles, flashes.
        foreach (var b in w.Bolts)
        {
            double d = Dist(b.X, b.Y);
            Line(c, d, b.Mine ? 0xFFFF6060 : 0xFF60FF60, 3, (b.X, b.Y, b.Z), (b.X - b.Vx * 0.02, b.Y - b.Vy * 0.02, b.Z - b.Vz * 0.02));
        }
        foreach (var m in w.Missiles)
        {
            double d = Dist(m.X, m.Y);
            Box(c, d, 0xFFE0E0F0, m.X, m.Y, m.Z - 2, 4, 14, 4, m.Heading);
            Line(c, d + 1, Alpha(0xFFFFA040, 0.8), 3, (m.X, m.Y, m.Z), (m.X - Math.Sin(m.Heading) * 40, m.Y - Math.Cos(m.Heading) * 40, m.Z - Math.Sin(m.Pitch) * 40));
        }
        foreach (var f in w.Flashes)
        {
            var v = ToCam(c, f.X, f.Y, f.Z);
            if (v.F < Near) continue;
            double r = (f.Big ? 80 : 20) * (0.3 + f.T * 1.4) / v.F * Focal;
            _items.Add(new Item { Depth = v.F - 1, Pts = [Screen(c, v)], R = r, Kind = 2, Fill = Alpha(f.Big ? 0xFFFFA040 : 0xFFFFF0A0, 1 - f.T / 0.8) });
        }
        foreach (var it in _items.OrderByDescending(i => i.Depth))
            switch (it.Kind)
            {
                case 0: Poly(dc, it.Fill, it.Edge, 1.5, it.Pts); break;
                case 1: dc.DrawLine(Pen(it.Edge, it.Width), it.Pts[0], it.Pts[1]); break;
                default:
                    dc.DrawEllipse(Brush(Alpha(it.Fill, ((it.Fill >> 24) & 0xFF) / 255.0 * 0.5)), Pen(it.Fill, 3), it.Pts[0], it.R, it.R);
                    break;
            }
    }

    private void DrawEgron(in Cam c, Egron e, double d)
    {
        double s = Math.Sin(e.Heading), co = Math.Cos(e.Heading);
        switch (e.Kind)
        {
            case Kind.Walker:
            {
                // Two long legs, striding; a body and a head on top.
                double stride = Math.Sin(_clock * 4 + e.Phase) * 6;
                Box(c, d + 0.2, 0xFFC0A040, e.X + co * 6 + s * stride, e.Y - s * 6 + co * stride, 0, 5, 5, 26, e.Heading);
                Box(c, d + 0.2, 0xFFC0A040, e.X - co * 6 - s * stride, e.Y + s * 6 - co * stride, 0, 5, 5, 26, e.Heading);
                Box(c, d, 0xFFE0C060, e.X, e.Y, 26, 20, 14, 10, e.Heading);
                Box(c, d - 0.2, 0xFFFF6040, e.X + s * 6, e.Y + co * 6, 36, 8, 8, 6, e.Heading);
                break;
            }
            case Kind.Tank:
                Box(c, d + 0.2, 0xFF60A060, e.X, e.Y, 0, 26, 34, 10, e.Heading);
                Box(c, d, 0xFF80C080, e.X, e.Y, 10, 14, 14, 8, e.Heading);
                Line(c, d - 0.2, 0xFFC0FFC0, 3, (e.X, e.Y, 15), (e.X + s * 26, e.Y + co * 26, 16));
                break;
            case Kind.Fighter:
            {
                // A delta wing.
                var nose = (e.X + s * 18, e.Y + co * 18, e.Z);
                var l = (e.X - s * 12 - co * 16, e.Y - co * 12 + s * 16, e.Z);
                var r = (e.X - s * 12 + co * 16, e.Y - co * 12 - s * 16, e.Z);
                var fin = (e.X - s * 10, e.Y - co * 10, e.Z + 8);
                var m = (e.X, e.Y, e.Z + 2);
                Solid(c, m, d, 0xFFFF60A0, nose, l, fin);
                Solid(c, m, d, 0xFFFF60A0, nose, fin, r);
                Solid(c, m, d, 0xFFC04080, nose, r, l);
                Solid(c, m, d, 0xFFC04080, l, r, fin);
                break;
            }
            default:
            {
                // The Starglider: a great bird, its wings beating.
                double flap = Math.Sin(_clock * 2.2 + e.Phase) * 22;
                var body = (e.X, e.Y, e.Z);
                Box(c, d + 0.3, 0xFF4060FF, e.X, e.Y, e.Z - 8, 18, 70, 16, e.Heading);
                var head = (e.X + s * 46, e.Y + co * 46, e.Z + 6);
                Solid(c, body, d - 0.3, 0xFF6080FF, (e.X + s * 30, e.Y + co * 30, e.Z + 8), (e.X + s * 30 - co * 8, e.Y + co * 30 + s * 8, e.Z - 2), head);
                Solid(c, body, d - 0.3, 0xFF6080FF, (e.X + s * 30, e.Y + co * 30, e.Z + 8), head, (e.X + s * 30 + co * 8, e.Y + co * 30 - s * 8, e.Z - 2));
                foreach (int side in new[] { -1, 1 })
                {
                    var root0 = (e.X + s * 18 + side * co * 8, e.Y + co * 18 - side * s * 8, e.Z + 4);
                    var root1 = (e.X - s * 18 + side * co * 8, e.Y - co * 18 - side * s * 8, e.Z + 4);
                    var tip = (e.X - s * 10 + side * co * 90, e.Y - co * 10 - side * s * 90, e.Z + flap);
                    Face(c, d, Mix(0xFF2040C0, 0xFF000000, Fade(d)), Mix(0xFF80A0FF, 0xFF000000, Fade(d)), root0, tip, root1);
                }
                Line(c, d - 0.5, 0xFFFFFF60, 2, (e.X + s * 46, e.Y + co * 46, e.Z + 6), (e.X + s * 62, e.Y + co * 62, e.Z + 2));
                break;
            }
        }
    }

    private void DrawSight(DrawingContext dc, World w)
    {
        if (w.Agav.Dead) return;
        uint s = 0xC0FFFF60;
        double cy = HorizonY;
        dc.DrawEllipse(null, Pen(s, 1.5), new Point(512, cy), 22, 22);
        dc.DrawLine(Pen(s, 2), new Point(512 - 36, cy), new Point(512 - 14, cy));
        dc.DrawLine(Pen(s, 2), new Point(512 + 14, cy), new Point(512 + 36, cy));
        dc.DrawLine(Pen(s, 2), new Point(512, cy - 36), new Point(512, cy - 14));
        dc.DrawLine(Pen(s, 2), new Point(512, cy + 14), new Point(512, cy + 36));
    }

    /// <summary>The cockpit panel: energy, missiles, speed and height, the radar, the score.</summary>
    private void DrawPanel(DrawingContext dc, World w)
    {
        var g = Engine;
        var a = w.Agav;
        double y0 = ViewH;
        dc.FillRectangle(Brush(0xFF0C0C14), new Rect(0, y0, Field.Width, Field.Height - y0));
        dc.FillRectangle(Brush(w.Novenia.Grid), new Rect(0, y0, Field.Width, 3));
        Text(dc, "ENERGY", new Point(24, y0 + 24), 24, Cyan);
        double e = Math.Clamp(a.Energy / Agav.Energy0, 0, 1);
        dc.FillRectangle(Brush(0xFF1A1A28), new Rect(24, y0 + 42, 240, 18));
        dc.FillRectangle(Brush(e < 0.25 ? Red : e < 0.5 ? Orange : 0xFF50E070), new Rect(24, y0 + 42, 240 * e, 18));
        if (w.Charging) Text(dc, "CHARGING", new Point(270, y0 + 51), 20, 0xFF60C0FF);
        Text(dc, "MISSILES", new Point(24, y0 + 84), 22, Cyan);
        for (int i = 0; i < Agav.Missiles0; i++)
            dc.FillRectangle(Brush(i < a.Missiles ? 0xFFE0E0F0 : 0xFF2A2A38), new Rect(130 + i * 26, y0 + 74, 16, 22), 3);
        Text(dc, $"SPEED {a.Speed,3:0}   HEIGHT {a.Alt,3:0}", new Point(24, y0 + 120), 24, 0xFFB0B0D0);
        Text(dc, $"LIVES {Math.Max(0, g.Lives)}", new Point(24, y0 + 150), 24, 0xFFFF8080);
        // The radar: what's about, the way ahead up.
        double rx = 640, ry = y0 + 84, rr = 72, range = 1800;
        dc.DrawEllipse(Brush(0xFF061008), Pen(0xFF2A6A3A, 2), new Point(rx, ry), rr, rr);
        dc.DrawEllipse(null, Pen(0xFF1A3A22, 1), new Point(rx, ry), rr / 2, rr / 2);
        double fs = Math.Sin(a.Heading), fc = Math.Cos(a.Heading);
        void Blip(double x, double y, double r, uint col)
        {
            double dx = NoveniaDef.Delta(a.X, x), dy = NoveniaDef.Delta(a.Y, y);
            double side = dx * fc - dy * fs, ahead = dx * fs + dy * fc;
            double px = side / range * rr, py = -ahead / range * rr, len = Math.Sqrt(px * px + py * py);
            if (len > rr - 3) { px *= (rr - 3) / len; py *= (rr - 3) / len; }
            dc.FillEllipse(rx + px, ry + py, r, col);
        }
        foreach (var l in w.Novenia.Lines) { Blip(l.X0, l.Y0, 2, 0xFF60C0FF); Blip(l.X1, l.Y1, 2, 0xFF60C0FF); }
        foreach (var s in w.Novenia.Silos) Blip(s.X, s.Y, 3.5, 0xFFFFE040);
        foreach (var x in w.Egrons) Blip(x.X, x.Y, x.Kind == Kind.Starglider ? 5 : 2.5, x.Kind == Kind.Starglider ? 0xFF6080FF : 0xFFFF5050);
        Poly(dc, 0xFF80FF90, null, 0, new Point(rx, ry - 6), new Point(rx + 4, ry + 4), new Point(rx - 4, ry + 4));
        Text(dc, $"SCORE {g.Score:000000}", new Point(Field.Width - 24, y0 + 30), 28, Yellow, right: true);
        Text(dc, $"WAVE {g.Level + 1}", new Point(Field.Width - 24, y0 + 64), 24, 0xFFB0B0D0, right: true);
        Text(dc, w.Starglider is { } sg ? $"STARGLIDER {sg.Life:0}" : "STARGLIDER DOWN", new Point(Field.Width - 24, y0 + 96), 22, 0xFF8090FF, right: true);
        Text(dc, $"EGRONS DOWN {w.Kills}", new Point(Field.Width - 24, y0 + 126), 22, 0xFFB0B0D0, right: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        if (_titleWorld == null) { _titleWorld = new World(); Sim.Start(_titleWorld, NoveniaBook.Build(0), 3); }
        var w = _titleWorld;
        if (w.Starglider == null) w.Egrons.Add(new Egron { Kind = Kind.Starglider, Life = Sim.StargliderLife });
        var sg = w.Starglider;
        double a = _clock * 0.12;
        if (sg != null) { sg.X = Field.Size / 2 + Math.Sin(a) * 160; sg.Y = Field.Size / 2 + 420 + Math.Cos(a) * 90; sg.Z = 150; sg.Heading = a + Math.PI / 2; }
        using (dc.PushClip(new Rect(0, 0, Field.Width, Field.Height)))
            DrawNovenia(dc, w, MakeCam(Field.Size / 2, Field.Size / 2, 120, 0, 0.05));
        dc.FillRectangle(Brush(0xB0000000), new Rect(0, 30, Field.Width, 150));
        Text(dc, "AVA STARGLIDER", new Point(cx, 92), 104, 0xFF80A0FF, center: true);
        Text(dc, "AFTER JEZ SAN'S 1986 GAME FOR RAINBIRD", new Point(cx, 160), 22, Dim, center: true);
        dc.FillRectangle(Brush(0xC0000000), new Rect(0, 470, Field.Width, 298));
        Text(dc, "THE EGRONS HAVE OVERRUN NOVENIA. FLY THE LAST AGAV OVER ITS SURFACE: LASERS FOR THE WALKERS, TANKS AND FIGHTERS,", new Point(cx, 492), 18, White, center: true);
        Text(dc, "MISSILES FOR THE GREAT STARGLIDER, WHICH NO LASER CAN HURT. RECHARGE BY FLYING LOW ALONG THE POWER LINES,", new Point(cx, 518), 18, White, center: true);
        Text(dc, "AND DOCK IN THE SILOS FOR MORE MISSILES. BRING THE STARGLIDER DOWN TO CLEAR EACH WAVE.", new Point(cx, 544), 18, White, center: true);
        Text(dc, "PRESS ENTER TO START", new Point(cx, 604), 40, (int)(_clock * 2) % 2 == 0 ? Yellow : Orange, center: true);
        Text(dc, "ARROWS: TURN, FASTER/SLOWER   Q/A: CLIMB/DIVE   SPACE: LASERS   X: MISSILE   P: PAUSE", new Point(cx, 660), 20, Dim, center: true);
        Text(dc, $"HIGH SCORE {Engine.HighScores.TopScore:000000}", new Point(cx, 710), 24, Cyan, center: true);
    }

    private void DrawGameOver(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        Text(dc, "GAME OVER", new Point(cx, 240), 110, Red, center: true);
        Text(dc, $"STARGLIDERS DOWN {Engine.Cleared}", new Point(cx, 330), 34, White, center: true);
        Text(dc, $"SCORE {Engine.Score}", new Point(cx, 420), 44, Yellow, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "THE BEST PILOTS", new Point(cx, 120), 64, 0xFFFFE040, center: true);
        Text(dc, "RANK NAME    SCORE   WAVES", new Point(cx, 190), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,6}", new Point(cx, 236 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 680), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
    public static void FillRectangle(this DrawingContext dc, IBrush brush, Rect rect, double radius) => dc.FillRectangle(brush, rect, (float)radius);
}
