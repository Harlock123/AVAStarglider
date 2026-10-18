using Avalonia.Input;
using AVAStarglider.Engine;

namespace AVAStarglider.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avastarglider-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>The first wave, with no Egrons about.</summary>
    private static World Quiet()
    {
        var w = new World();
        Sim.Start(w, NoveniaBook.Build(0), 1);
        w.Egrons.Clear();
        w.NextEgron = 1e9;
        return w;
    }

    private static void Run(World w, int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Sim.Step(w, c);
    }

    private static Egron Put(World w, Kind kind, double ahead, double z = 0)
    {
        var a = w.Agav;
        var e = new Egron { Kind = kind, X = NoveniaDef.Wrap(a.X + Math.Sin(a.Heading) * ahead), Y = NoveniaDef.Wrap(a.Y + Math.Cos(a.Heading) * ahead), Z = z, Life = kind == Kind.Starglider ? Sim.StargliderLife : 3, Cool = 99 };
        w.Egrons.Add(e);
        return e;
    }

    [Fact]
    public void It_flies_turns_speeds_up_slows_climbs_and_dives()
    {
        var w = Quiet();
        var a = w.Agav;
        double y = a.Y;
        Run(w, 60);
        Assert.True(NoveniaDef.Delta(y, a.Y) > 60);
        Run(w, 60, new Controls(Dy: -1));
        Assert.True(a.Speed > 150);
        Run(w, 120, new Controls(Dy: 1));
        Assert.Equal(Agav.MinSpeed, a.Speed, 3);
        double alt = a.Alt;
        Run(w, 60, new Controls(Climb: 1));
        Assert.True(a.Alt > alt + 50);
        Run(w, 30, new Controls(Dx: 1));
        Assert.True(a.Heading > 0.4);
    }

    [Fact]
    public void Lasers_cost_energy_and_bring_down_a_walker()
    {
        var w = Quiet();
        var a = w.Agav;
        a.Speed = 0;
        var e = Put(w, Kind.Walker, 300);
        Run(w, 60, new Controls(Fire: true));
        Assert.DoesNotContain(e, w.Egrons);
        Assert.Equal(Sim.Points(Kind.Walker), w.Score);
        Assert.True(a.Energy < Agav.Energy0);
    }

    [Fact]
    public void Lasers_cannot_hurt_the_starglider_but_missiles_can()
    {
        var w = Quiet();
        w.Agav.Speed = 0;
        var sg = Put(w, Kind.Starglider, 400, w.Agav.Alt - 30);
        sg.Speed = 0;
        Run(w, 60, new Controls(Fire: true));
        Assert.Equal(Sim.StargliderLife, sg.Life);
        for (int k = 0; k < Sim.StargliderLife; k++)
        {
            w.Agav.Missiles = 3;
            Sim.Step(w, new Controls(Missile: true));
            Run(w, 90);
            sg.Speed = 0;
        }
        Assert.True(w.StargliderDown);
        Assert.Equal(Sim.Points(Kind.Starglider), w.Score);
    }

    [Fact]
    public void The_starglider_comes_once_enough_egrons_are_down()
    {
        var w = Quiet();
        Assert.Null(w.Starglider);
        w.Kills = w.Novenia.Quota;
        Run(w, 2);
        Assert.NotNull(w.Starglider);
    }

    [Fact]
    public void Under_a_power_line_the_energy_comes_back()
    {
        var w = Quiet();
        var a = w.Agav;
        var l = w.Novenia.Lines[0];
        a.X = NoveniaDef.Wrap(l.X0 + NoveniaDef.Delta(l.X0, l.X1) / 2);
        a.Y = NoveniaDef.Wrap(l.Y0 + NoveniaDef.Delta(l.Y0, l.Y1) / 2);
        a.Alt = 50;
        a.Speed = 0;
        a.Energy = 40;
        Run(w, 60);
        Assert.True(w.Charging);
        Assert.Equal(40 + Sim.ChargeRate, a.Energy, 0);
    }

    [Fact]
    public void Docking_in_a_silo_loads_missiles()
    {
        var w = Quiet();
        var a = w.Agav;
        var s = w.Novenia.Silos[0];
        (a.X, a.Y, a.Alt, a.Speed, a.Missiles) = (s.X, s.Y, NoveniaBook.SiloHeight + 10, 0, 0);
        Run(w, 2);
        Assert.Equal(Agav.Missiles0, a.Missiles);
    }

    [Fact]
    public void Flying_into_a_pylon_hurts()
    {
        var w = Quiet();
        var a = w.Agav;
        var l = w.Novenia.Lines[0];
        (a.X, a.Y, a.Alt) = (l.X0, NoveniaDef.Wrap(l.Y0 - 60), 50);
        a.Heading = 0;
        a.Speed = 100;
        Run(w, 60);
        Assert.True(a.Energy < Agav.Energy0 - 10);
        Assert.Equal("FLEW INTO A STRUCTURE", w.Cause);
    }

    [Fact]
    public void Egrons_shoot_and_without_energy_the_agav_is_lost()
    {
        var w = Quiet();
        var a = w.Agav;
        a.Speed = 0;
        var e = Put(w, Kind.Tank, 400);
        e.Cool = 0;
        e.Speed = 0;
        Run(w, 60 * 4);
        Assert.True(a.Energy < Agav.Energy0);
        a.Energy = 0.1;
        e.Cool = 0;
        for (int i = 0; i < 60 * 8 && !a.Dead; i++) { e.Cool = Math.Min(e.Cool, 0.5); Sim.Step(w, default); }
        Assert.True(a.Dead);
    }

    // ───────────────────────────── the game ─────────────────────────────

    private static GameEngine Engine() => new(HighScoreTable.Load(TempFolder())) { Silent = true };

    [Fact]
    public void The_starglider_down_scores_the_bonus_and_on_to_the_next_wave()
    {
        var g = Engine();
        g.Go();
        g.World.StargliderDown = true;
        g.Run(2);
        Assert.Equal(Phase.Cleared, g.Phase);
        Assert.Equal(1, g.Cleared);
        Assert.True(g.Score > 0);
        g.Run((int)(60 * GameEngine.ClearedSeconds) + 4);
        Assert.Equal(1, g.Level);
    }

    [Fact]
    public void An_agav_lost_and_the_next_and_the_last_is_game_over()
    {
        var g = Engine();
        g.Go();
        g.World.Agav.Energy = 0;
        g.Run(2);
        g.Run((int)(60 * GameEngine.DyingSeconds) + 4);
        Assert.Equal(GameEngine.Lives0 - 1, g.Lives);
        Assert.False(g.World.Agav.Dead);
        g.SetLives(1);
        g.World.Agav.Energy = 0;
        g.Run((int)(60 * GameEngine.DyingSeconds) + 6);
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void The_title_gives_way_to_the_demonstration_and_enter_starts_a_game()
    {
        var g = Engine();
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle(60 * 6);
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal(GameEngine.Lives0, g.Lives);
    }

    [Fact]
    public void Paused_nothing_moves()
    {
        var g = Engine();
        g.Go();
        g.Run(10);
        g.Pause();
        double y = g.World.Agav.Y;
        g.Run(60);
        Assert.Equal(y, g.World.Agav.Y);
        g.Resume();
        g.Run(10);
        Assert.NotEqual(y, g.World.Agav.Y);
    }

    [Fact]
    public void The_keys_fly_fire_and_launch()
    {
        using var input = new InputHandler();
        foreach (var (key, check) in new (Key, Func<Controls, bool>)[]
                 {
                     (Key.Left, c => c.Dx == -1), (Key.Right, c => c.Dx == 1), (Key.Up, c => c.Dy == -1), (Key.Down, c => c.Dy == 1),
                     (Key.Q, c => c.Climb == 1), (Key.A, c => c.Climb == -1), (Key.Space, c => c.Fire), (Key.X, c => c.Missile),
                 })
        {
            input.OnKeyDown(key);
            Assert.True(check(input.Player), key.ToString());
            input.OnKeyUp(key);
        }
    }
}
