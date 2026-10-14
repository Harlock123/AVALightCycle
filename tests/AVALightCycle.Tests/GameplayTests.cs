using AVALightCycle.Engine;
using Engine = AVALightCycle.Engine;

namespace AVALightCycle.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avalightcycle-tests-" + Guid.NewGuid().ToString("N"));

    private static GameEngine Game(int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.StartGame();
        return g;
    }

    private static Engine.Controls Stick(int dx = 0, int dy = 0, bool fire = false, int spin = 0, Vec? at = null) => new(dx, dy, fire, spin, null, at);

    // ───────────────────────────── the select screen ─────────────────────────────

    [Fact]
    public void The_select_screen_moves_between_the_four_games_and_enters_one()
    {
        var g = Game();
        Assert.Equal(Phase.Select, g.Phase);
        g.Idle(30);
        g.Run(1, Stick(dx: 1));
        g.Idle(1);
        g.Run(1, Stick(dy: 1));
        g.Idle(1);
        Assert.Equal(3, g.Cursor);
        g.Run(1, Stick(fire: true));
        Assert.Equal(Phase.Intro, g.Phase);
        Assert.IsType<CoreStage>(g.Stage);
        g.Idle(60 * 3);
        Assert.Equal(Phase.Playing, g.Phase);
    }

    [Fact]
    public void A_game_already_won_this_level_cant_be_entered_again()
    {
        var g = Game();
        g.Done[0] = true;
        g.Idle(30);
        g.Run(1, Stick(fire: true));
        Assert.Equal(Phase.Select, g.Phase);
    }

    [Fact]
    public void Winning_all_four_games_moves_on_a_level_with_a_bonus()
    {
        var g = Game();
        g.Done[0] = g.Done[1] = g.Done[2] = true;
        var core = g.Into<CoreStage>(StageKind.Core);
        core.StopSparks();
        core.ClearAll();
        core.Runner.X = CoreStage.CoreX;
        int score = g.Score;
        for (int i = 0; i < 300 && g.Phase == Phase.Playing; i++) g.Run(1, Stick(dy: -1));
        Assert.Equal(Phase.Cleared, g.Phase);
        g.Idle(60 * 3);
        Assert.Equal(Phase.LevelUp, g.Phase);
        Assert.Equal(2, g.Level);
        Assert.True(g.Score >= score + 5000);
        g.Idle(60 * 4);
        Assert.Equal(Phase.Select, g.Phase);
        Assert.All(g.Done, d => Assert.False(d));
    }

    [Fact]
    public void Losing_costs_a_life_and_replays_the_same_game_and_the_last_life_ends_it()
    {
        var g = Game();
        var bugs = g.Into<BugStage>(StageKind.Bugs);
        bugs.StopSpawning();
        bugs.PutBug(bugs.Runner.X, bugs.Runner.Y - 4, 2);
        g.Idle(2);
        Assert.Equal(Phase.Derez, g.Phase);
        g.Idle(60 * 3);
        Assert.Equal(GameEngine.StartingLives - 1, g.Lives);
        Assert.Equal(Phase.Intro, g.Phase);
        Assert.IsType<BugStage>(g.Stage);
        Assert.NotSame(bugs, g.Stage);
        g.Lives = 1;
        g.Idle(60 * 3);
        var again = (BugStage)g.Stage!;
        again.PutBug(again.Runner.X, again.Runner.Y - 4, 2);
        g.Idle(60 * 3);
        Assert.Equal(GameState.GameOver, g.State);
    }

    // ───────────────────────────── the cycle race ─────────────────────────────

    [Fact]
    public void A_cycle_lays_a_wall_and_turns_with_the_stick_but_never_straight_back()
    {
        var g = Game();
        var s = g.Into<CycleStage>(StageKind.Cycles);
        var me = s.Player;
        int y0 = me.Y;
        g.Idle(30);
        Assert.True(me.Y < y0 - 4);
        Assert.Equal(me.Id, s.Grid[me.X, y0 - 1]);
        g.Run(1, Stick(dy: 1));
        g.Idle(5);
        Assert.Equal(0, me.Dir);
        g.Run(6, Stick(dx: 1));
        Assert.Equal(1, me.Dir);
    }

    [Fact]
    public void Running_into_a_wall_crashes_the_cycle()
    {
        var g = Game();
        var s = g.Into<CycleStage>(StageKind.Cycles);
        s.Wall(s.Player.X - 3, s.Player.Y - 6, s.Player.X + 3, s.Player.Y - 6);
        g.Idle(60);
        Assert.Equal(StageResult.Lost, s.Result);
    }

    [Fact]
    public void An_enemy_that_crashes_takes_its_wall_with_it_and_the_last_one_wins_the_race()
    {
        var g = Game();
        var s = g.Into<CycleStage>(StageKind.Cycles);
        foreach (var e in s.Cycles.Skip(1))
        {
            // Box each enemy in so it must crash.
            s.Wall(e.X - 2, e.Y - 2, e.X + 2, e.Y - 2);
            s.Wall(e.X - 2, e.Y + 2, e.X + 2, e.Y + 2);
            s.Wall(e.X - 2, e.Y - 2, e.X - 2, e.Y + 2);
            s.Wall(e.X + 2, e.Y - 2, e.X + 2, e.Y + 2);
        }
        g.Idle(60);
        Assert.All(s.Cycles.Skip(1), e => Assert.False(e.Alive));
        Assert.All(s.Cycles.Skip(1), e => Assert.DoesNotContain(Enumerable.Range(0, CycleStage.Cols).SelectMany(x => Enumerable.Range(0, CycleStage.Rows).Select(y => s.Grid[x, y])), v => v == e.Id));
        Assert.Equal(StageResult.Won, s.Result);
    }

    [Fact]
    public void The_trigger_slows_the_cycle()
    {
        var g = Game();
        var s = g.Into<CycleStage>(StageKind.Cycles);
        int y0 = s.Player.Y;
        g.Run(60, Stick(fire: true));
        int slow = y0 - s.Player.Y;
        var g2 = Game();
        var s2 = g2.Into<CycleStage>(StageKind.Cycles);
        g2.Idle(60);
        Assert.True(y0 - s2.Player.Y > slow);
    }

    // ───────────────────────────── the tank maze ─────────────────────────────

    [Fact]
    public void The_maze_is_connected_and_tanks_start_on_open_floor()
    {
        foreach (int seed in new[] { 1, 2, 3, 4 })
        {
            var g = Game(seed);
            var s = g.Into<TankStage>(StageKind.Tanks);
            foreach (var t in s.Tanks)
            {
                Assert.True(s.Open(t.TX, t.TY));
                if (t.Enemy) Assert.NotNull(s.Path(s.Player.TX, s.Player.TY, t.TX, t.TY));
            }
            Assert.True(s.Open(TankStage.CentreX, TankStage.CentreY));
        }
    }

    [Fact]
    public void The_tank_drives_tile_to_tile_and_aims_on_its_own()
    {
        var g = Game();
        var s = g.Into<TankStage>(StageKind.Tanks);
        var p = s.Player;
        int ty = p.TY;
        g.Run(30, Stick(dy: -1));
        Assert.True(p.TY < ty || !s.Open(p.TX, ty - 1));
        double aim = p.Aim;
        g.Run(20, Stick(spin: 1));
        Assert.True(p.Aim > aim + 1);
        g.Run(1, Stick(at: new Vec(p.X + 100, p.Y)));
        Assert.Equal(0, p.Aim, 3);
    }

    [Fact]
    public void Shots_bounce_off_walls()
    {
        var g = Game();
        var s = g.Into<TankStage>(StageKind.Tanks);
        var p = s.Player;
        // Fire straight down at the wall below.
        g.Run(1, Stick(fire: true, at: new Vec(p.X, p.Y + 200)));
        // The tank starts beside the bottom wall: the shell fired down comes straight back up.
        var shot = Assert.Single(s.Shots, x => !x.Enemy);
        for (int i = 0; i < 60 && shot.Bounces == 0 && !shot.Gone; i++) g.Idle(1);
        Assert.Equal(1, shot.Bounces);
        Assert.True(shot.Vy < 0);
    }

    [Fact]
    public void Enemy_tanks_take_hits_to_stop_and_one_hit_stops_yours()
    {
        var g = Game();
        var s = g.Into<TankStage>(StageKind.Tanks);
        var e = s.Tanks.First(t => t.Enemy);
        for (int i = 0; i < s.EnemyHits; i++)
        {
            s.Shots.Add(new Shot { X = e.X - 30, Y = e.Y, Vx = 400, Life = 1 });
            g.Idle(8);
        }
        Assert.False(e.Alive);
        // From along the open corridor to its right.
        s.Shots.Add(new Shot { X = s.Player.X + 30, Y = s.Player.Y, Vx = -400, Life = 1, Enemy = true });
        g.Idle(8);
        Assert.Equal(StageResult.Lost, s.Result);
    }

    // ───────────────────────────── the bug gate ─────────────────────────────

    [Fact]
    public void The_gate_lets_nobody_in_until_it_opens()
    {
        var g = Game();
        var s = g.Into<BugStage>(StageKind.Bugs);
        s.StopSpawning();
        s.Runner.X = BugStage.GateX;
        s.Runner.Y = BugStage.GateY;
        g.Idle(30);
        Assert.Equal(StageResult.Running, s.Result);
        for (int i = 0; i < 60 * 30 && !s.GateOpen; i++) g.Idle(1);
        g.Idle(2);
        Assert.Equal(StageResult.Won, s.Result);
    }

    [Fact]
    public void Discs_squash_bugs_and_time_running_out_is_the_end()
    {
        var g = Game();
        var s = g.Into<BugStage>(StageKind.Bugs);
        s.StopSpawning();
        var bug = s.PutBug(s.Runner.X, s.Runner.Y - 200, 1);
        for (int i = 0; i < 40 && s.Bugs.Contains(bug); i++) g.Run(1, Stick(fire: i % 2 == 0, at: new Vec(bug.X, bug.Y)));
        Assert.DoesNotContain(bug, s.Bugs);
        Assert.Equal(100, g.Score);
        g.Idle((int)(60 * (s.TimeLeft + 1)));
        Assert.Equal(StageResult.Lost, s.Result);
        Assert.Equal("OUT OF TIME!", s.Cause);
    }

    [Fact]
    public void Bugs_crawl_towards_the_runner()
    {
        var g = Game();
        var s = g.Into<BugStage>(StageKind.Bugs);
        s.StopSpawning();
        var bug = s.PutBug(Field.ArenaLeft + 40, Field.ArenaTop + 40, 1);
        double d0 = Math.Abs(bug.X - s.Runner.X) + Math.Abs(bug.Y - s.Runner.Y);
        g.Idle(120);
        Assert.True(Math.Abs(bug.X - s.Runner.X) + Math.Abs(bug.Y - s.Runner.Y) < d0 - 100);
    }

    // ───────────────────────────── the core ─────────────────────────────

    [Fact]
    public void The_band_turns_and_comes_down_and_discs_chip_blocks_out()
    {
        var g = Game();
        var s = g.Into<CoreStage>(StageKind.Core);
        s.StopSparks();
        double top = s.BandTop, offset = s.Offset;
        for (int i = 0; i < 60; i++) g.Run(1, Stick(fire: i % 2 == 0, at: new Vec(s.Runner.X, 100)));
        Assert.True(s.BandTop > top);
        Assert.True(s.Offset > offset);
        Assert.True(g.Score >= 50);
        int left = 0;
        foreach (var b in s.Blocks) if (b) left++;
        Assert.True(left < s.Rows * CoreStage.Columns);
    }

    [Fact]
    public void Only_beneath_the_door_can_the_runner_climb_into_the_band()
    {
        var g = Game();
        var s = g.Into<CoreStage>(StageKind.Core);
        s.StopSparks();
        s.Runner.X = 200;
        g.Run(120, Stick(dy: -1));
        Assert.True(s.Runner.Y >= s.BandBottom + Runner.Radius);
        Assert.Equal(StageResult.Running, s.Result);
    }

    [Fact]
    public void Climbing_into_the_core_wins_and_touching_the_band_or_being_crushed_loses()
    {
        var g = Game();
        var s = g.Into<CoreStage>(StageKind.Core);
        s.StopSparks();
        s.Runner.X = CoreStage.CoreX;
        g.Run(120, Stick(dy: -1));
        Assert.Equal(StageResult.Lost, s.Result);
        Assert.Equal("HIT THE SHIELD!", s.Cause);
        var g3 = Game();
        var s3 = g3.Into<CoreStage>(StageKind.Core);
        s3.StopSparks();
        s3.ClearAll();
        s3.Runner.X = CoreStage.CoreX;
        g3.Run(180, Stick(dy: -1));
        Assert.Equal(StageResult.Won, s3.Result);
        var g2 = Game();
        var s2 = g2.Into<CoreStage>(StageKind.Core);
        s2.StopSparks();
        s2.Runner.Y = CoreStage.Floor;
        g2.Idle(60 * 120);
        Assert.Equal(StageResult.Lost, s2.Result);
        Assert.Equal("CRUSHED!", s2.Cause);
    }

    // ───────────────────────────── the machine ─────────────────────────────

    [Fact]
    public void The_computer_player_can_win_each_game_at_level_one()
    {
        foreach (var kind in Enum.GetValues<StageKind>())
        {
            bool won = false;
            for (int seed = 1; seed <= 6 && !won; seed++)
            {
                var g = Game(seed);
                var s = g.Into<Stage>(kind);
                for (int t = 0; t < 60 * 120 && s.Result == StageResult.Running; t++) g.Tick(g.Pilot.Decide(g));
                won = s.Result == StageResult.Won;
            }
            Assert.True(won, $"{kind}: the computer never won");
        }
    }

    [Fact]
    public void The_attract_mode_plays_itself_then_shows_the_high_scores()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 2);
        Assert.Equal(GameState.Attract, g.State);
        Assert.NotNull(g.Stage);
        for (int i = 0; i < 60 * (GameEngine.AttractSeconds + 60) && g.State == GameState.Attract; i++) g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
    }

    [Fact]
    public void Extra_lives_come_with_the_score()
    {
        var g = Game();
        g.AddScore(GameEngine.FirstExtraLife, 0, 0);
        Assert.Equal(GameEngine.StartingLives + 1, g.Lives);
        g.AddScore(GameEngine.ExtraLifeEvery, 0, 0);
        Assert.Equal(GameEngine.StartingLives + 2, g.Lives);
    }

    [Fact]
    public void A_good_score_asks_for_initials()
    {
        var g = Game();
        g.Score = 99999;
        g.Lives = 1;
        var bugs = g.Into<BugStage>(StageKind.Bugs);
        bugs.PutBug(bugs.Runner.X, bugs.Runner.Y - 4, 2);
        g.Idle(60 * 8);
        Assert.Equal(GameState.EnterInitials, g.State);
        g.Initials!.Type('L');
        g.Initials.Type('U');
        g.Initials.Type('X');
        g.Initials.Finish();
        g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(new HighScoreEntry("LUX", 99999, 1), g.HighScores.Entries[0]);
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play()
        {
            var g = new GameEngine(9, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.StartGame();
            for (int t = 0; t < 60 * 60; t++) g.Tick(g.Pilot.Decide(g));
            return $"{g.Score}:{g.Lives}:{g.Phase}:{g.Stage?.Kind}";
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void High_scores_persist_in_their_folder()
    {
        string folder = TempFolder();
        var t = HighScoreTable.Load(folder);
        Assert.Equal(HighScoreTable.Size, t.Entries.Count);
        t.Insert("BIG", 123456, 9);
        t.Save();
        Assert.Equal(new HighScoreEntry("BIG", 123456, 9), HighScoreTable.Load(folder).Entries[0]);
    }
}
