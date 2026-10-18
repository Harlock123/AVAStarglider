using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAStarglider.Engine;
using AVAStarglider.Views;
using Controls = AVAStarglider.Engine.Controls;

namespace AVAStarglider.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set SG_SHOTS to a folder to save
/// them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 1)
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avastarglider-ui-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("SG_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAStarglider-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 200)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("JEZ", 74200, 5);
        g.HighScores.Insert("AVA", 22800, 2);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 300 && g.State != GameState.HighScores; i++) g.Idle(1);
        Shot(w, c, "high-scores");
    }

    private static void Play(string name, Func<GameEngine, bool> moment, int seconds = 200)
    {
        var (w, c, g) = Open();
        g.Go();
        g.SetLives(20);
        g.Pilot.Reset();
        Assert.True(PlayUntil(g, c, moment, seconds), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    private static bool Ahead(GameEngine g, double x, double y, double lo, double hi, double off)
    {
        var a = g.World.Agav;
        double d = NoveniaDef.Dist(a.X, a.Y, x, y);
        return d > lo && d < hi && Math.Abs(Sim.Wrap(NoveniaDef.Bearing(a.X, a.Y, x, y) - a.Heading)) < off;
    }

    [AvaloniaFact]
    public void Over_novenia() => Play("surface", g => g.World.Clock > 3);

    [AvaloniaFact]
    public void A_walker() => Play("walker", g => g.World.Egrons.Any(e => e.Kind == Kind.Walker && Ahead(g, e.X, e.Y, 250, 1000, 0.15)) && g.World.Bolts.Any(b => b.Mine));

    [AvaloniaFact]
    public void A_fighter() => Play("fighter", g => g.World.Egrons.Any(e => e.Kind == Kind.Fighter && Ahead(g, e.X, e.Y, 150, 450, 0.25)));

    [AvaloniaFact]
    public void The_starglider() => Play("starglider", g => g.World.Starglider is { } s && Ahead(g, s.X, s.Y, 300, 800, 0.3), 300);

    [AvaloniaFact]
    public void A_missile() => Play("missile", g => g.World.Missiles.Count > 0 && g.World.Missiles[0].Life < Sim.MissileLife - 0.5, 300);

    [AvaloniaFact]
    public void Power_lines()
    {
        var (w, c, g) = Open();
        g.Go();
        var world = g.World;
        world.Egrons.Clear();
        var l = world.Novenia.Lines[0];
        var a = world.Agav;
        double lx = NoveniaDef.Delta(l.X0, l.X1), ly = NoveniaDef.Delta(l.Y0, l.Y1);
        (a.X, a.Y, a.Alt, a.Heading, a.Speed) = (NoveniaDef.Wrap(l.X0 + lx * 0.12), NoveniaDef.Wrap(l.Y0 + ly * 0.12), 60, Math.Atan2(lx, ly), 30);
        a.Energy = 50;
        for (int i = 0; i < 90; i++) { g.Tick(default); if (i % 3 == 0) c.Advance(0.05); }
        Assert.True(world.Charging);
        Shot(w, c, "power");
    }

    [AvaloniaFact]
    public void Starglider_down() => Play("down", g => g.Phase == Phase.Cleared && g.PhaseTime > 0.3, 300);

    [AvaloniaFact]
    public void Game_over()
    {
        var (w, c, g) = Open();
        g.Go();
        g.SetLives(1);
        g.World.Score = 12400;
        g.World.Agav.Energy = 0;
        for (int i = 0; i < 600 && !(g.State == GameState.GameOver && g.StateTime > 1); i++) { g.Tick(default); if (i % 3 == 0) c.Advance(0.05); }
        Shot(w, c, "game-over");
    }
}
