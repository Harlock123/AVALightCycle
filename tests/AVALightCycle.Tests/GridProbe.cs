using AVALightCycle.Engine;

namespace AVALightCycle.Tests;

/// <summary>How the computer player does at each game and level (set GRID_PROBE to run it, for tuning).</summary>
public sealed class GridProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_plays()
    {
        if (Environment.GetEnvironmentVariable("GRID_PROBE") is null) return;
        foreach (var kind in Enum.GetValues<StageKind>())
        foreach (int level in new[] { 1, 3, 5 })
        {
            int won = 0;
            var times = new List<double>();
            var causes = new Dictionary<string, int>();
            for (int seed = 1; seed <= 8; seed++)
            {
                var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "grid-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
                g.StartGame();
                g.SetLevel(level);
                var s = g.Into<Stage>(kind);
                for (int t = 0; t < 60 * 120 && s.Result == StageResult.Running; t++) g.Tick(g.Pilot.Decide(g));
                if (s.Result == StageResult.Won) { won++; times.Add(s.Time); }
                else causes[s.Cause ?? "timeout"] = causes.GetValueOrDefault(s.Cause ?? "timeout") + 1;
            }
            output.WriteLine($"{kind} level {level}: won {won}/8 avg {(times.Count > 0 ? times.Average() : 0):0.0}s lost: {string.Join(", ", causes.Select(kv => kv.Key + " " + kv.Value))}");
        }
    }
}
