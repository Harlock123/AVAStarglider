using AVAStarglider.Engine;

namespace AVAStarglider.Tests;

/// <summary>How the computer pilot gets on, wave after wave (set SG_PROBE to run it, for tuning).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_pilot_does()
    {
        if (Environment.GetEnvironmentVariable("SG_PROBE") is not { } mode) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int seed = 1; seed <= 3; seed++)
        {
            var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "sg-probe-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };
            g.Go();
            g.SetLives(20);
            var deaths = new Dictionary<string, int>();
            var doing = new Dictionary<string, int>();
            var last = g.Phase;
            int t = 0, lastCleared = 0;
            var waves = new List<int>();
            for (; t < 60 * 900 && g.State == GameState.Playing && g.Cleared < 3; t++)
            {
                var c = mode == "lazy" ? new Controls(t / 300 % 2, -1, true, t % 120 == 0, 0) : g.Pilot.Decide(g);
                g.Tick(c);
                if (g.Phase == Phase.Dying && last != Phase.Dying) deaths[g.World.Cause] = deaths.GetValueOrDefault(g.World.Cause) + 1;
                if (g.Cleared != lastCleared) { lastCleared = g.Cleared; waves.Add(t / 60); }
                last = g.Phase;
                doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
            }
            output.WriteLine($"seed {seed}: {g.Cleared} waves (at {string.Join(", ", waves)}s) in {t / 60}s, score {g.Score}, {deaths.Values.Sum()} lost ({string.Join(", ", deaths.Select(d => $"{d.Key} x{d.Value}"))}); {string.Join(", ", doing.OrderByDescending(d => d.Value).Take(6).Select(d => $"{d.Key} {d.Value / 60}s"))}");
        }
        output.WriteLine($"total {sw.ElapsedMilliseconds} ms");
    }
}
