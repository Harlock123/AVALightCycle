using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVALightCycle.Engine;
using AVALightCycle.Views;

namespace AVALightCycle.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set GRID_SHOTS to a
/// folder to save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avalightcycle-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
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
        var dir = Environment.GetEnvironmentVariable("GRID_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVALightCycle-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private static void PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 120)
    {
        for (int t = 0; t < 60 * maxSeconds && !done(g); t++)
        {
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        c.Advance(0.016);
    }

    [AvaloniaFact]
    public void The_title_select_and_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(0.6);
        Shot(w, c, "title");
        g.Tick(default, start: true);
        g.Done[0] = true;
        g.Idle(20);
        g.Tick(new Engine.Controls(1, 0));
        g.Idle(2);
        c.Advance(0.4);
        Shot(w, c, "select");
        g.ReturnToTitle();
        g.HighScores.Insert("LUX", 61250, 5);
        g.HighScores.Insert("AVA", 42000, 3);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 120 && g.State != GameState.HighScores; i++) g.Tick(default);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void The_four_games()
    {
        var (w, c, g) = Open(4);
        g.StartGame();
        g.SetLevel(3);
        var cycles = g.Into<CycleStage>(StageKind.Cycles);
        PlayUntil(g, c, g => cycles.Time > 9 || cycles.Result != StageResult.Running);
        Shot(w, c, "cycles");
        var tanks = g.Into<TankStage>(StageKind.Tanks);
        PlayUntil(g, c, g => tanks.Time > 2.5 && tanks.Shots.Count >= 2 || tanks.Result != StageResult.Running);
        Shot(w, c, "tanks");
        g.SetLevel(2);
        var bugs = g.Into<BugStage>(StageKind.Bugs);
        PlayUntil(g, c, g => bugs.Time > 6 && bugs.Shots.Count > 0 || bugs.Result != StageResult.Running);
        Shot(w, c, "bugs");
        PlayUntil(g, c, g => bugs.GateOpen || bugs.Result != StageResult.Running);
        g.Idle(1);
        c.Advance(0.3);
        Shot(w, c, "gate-open");
        g.SetLevel(3);
        var core = g.Into<CoreStage>(StageKind.Core);
        PlayUntil(g, c, g => core.Time > 2.5 && core.Shots.Count > 1 && core.Runner.Y > core.BandBottom + 20 || core.Result != StageResult.Running);
        Shot(w, c, "core");
        PlayUntil(g, c, g => core.Runner.Y < core.BandBottom - 30 || core.Result != StageResult.Running);
        Shot(w, c, "climb");
        // A crash, for the derez.
        var again = g.Into<CycleStage>(StageKind.Cycles);
        g.Run(60 * 5, new Engine.Controls(-1, 0));
        c.Advance(0.3);
        Shot(w, c, "derez");
    }
}
