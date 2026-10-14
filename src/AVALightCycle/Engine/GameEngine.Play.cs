namespace AVALightCycle.Engine;

public sealed partial class GameEngine
{
    public const int FirstExtraLife = 25000, ExtraLifeEvery = 25000;

    public int Score { get; internal set; }
    public int Lives { get; internal set; }
    public int Level { get; private set; } = 1;
    /// <summary>Which of the four games are won this level.</summary>
    public bool[] Done { get; } = new bool[4];
    /// <summary>The highlighted game on the select screen.</summary>
    public int Cursor { get; private set; }
    public Stage? Stage { get; private set; }
    /// <summary>The computer player, for the demonstration (and the tests).</summary>
    public GridPilot Pilot { get; private set; } = new(1);

    private int _nextExtra;
    private Controls _prev;

    private void NewGame()
    {
        _rng = new Random(_seed + (int)(StateTime * 997));
        Score = 0;
        Lives = StartingLives;
        Level = 1;
        _nextExtra = FirstExtraLife;
        Array.Clear(Done);
        Cursor = 0;
        Stage = null;
        Pilot = new GridPilot(_rng.Next());
        Effects.Clear();
        SetPhase(Phase.Select);
    }

    public void AddScore(int points, double x, double y)
    {
        Score += points;
        Effects.Add(new Effect { Kind = EffectKind.Score, X = x, Y = y, Text = points.ToString(), Life = 0.9, Colour = 0xFFFFE14A });
        if (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
            Say("EXTRA LIFE!", 2);
        }
    }

    public void Burst(double x, double y, uint colour, bool big) =>
        Effects.Add(new Effect { Kind = big ? EffectKind.Derez : EffectKind.Burst, X = x, Y = y, Colour = colour, Life = big ? 1.4 : 0.4 });

    // ───────────────────────────── the step ─────────────────────────────

    private void Simulate(double dt, Controls c, bool startPressed)
    {
        PhaseTime += dt;
        switch (Phase)
        {
            case Phase.Select:
                Choose(c, startPressed);
                break;
            case Phase.Intro:
                if (PhaseTime > 2 || startPressed && PhaseTime > 0.3)
                {
                    SetPhase(Phase.Playing);
                    Music(true);
                }
                break;
            case Phase.Playing:
                Stage!.Update(c, dt);
                if (Stage.Result == StageResult.Lost)
                {
                    Music(false);
                    Say(Stage.Cause ?? "DEREZZED!", 2.2);
                    SetPhase(Phase.Derez);
                }
                else if (Stage.Result == StageResult.Won)
                {
                    Music(false);
                    Done[(int)Stage.Kind] = true;
                    Play(Sfx.Cleared);
                    Say("CLEARED!", 2);
                    SetPhase(Phase.Cleared);
                }
                break;
            case Phase.Derez:
                if (PhaseTime > 2.2)
                {
                    Lives--;
                    if (Lives <= 0) { GameOver(); break; }
                    // The same game again, from the start.
                    Enter(Stage!.Kind);
                }
                break;
            case Phase.Cleared:
                if (PhaseTime > 2.2)
                {
                    if (State == GameState.Attract) { ShowHighScores(-1); break; }
                    if (Done.All(d => d)) LevelUp();
                    else
                    {
                        Cursor = Array.FindIndex(Done, d => !d);
                        SetPhase(Phase.Select);
                    }
                }
                break;
            case Phase.LevelUp:
                if (PhaseTime > 3)
                {
                    Array.Clear(Done);
                    Cursor = 0;
                    SetPhase(Phase.Select);
                }
                break;
        }
        _prev = c;
    }

    /// <summary>The select screen: the four games in a square; the stick moves between them, fire enters.</summary>
    private void Choose(Controls c, bool startPressed)
    {
        int col = Cursor % 2, row = Cursor / 2;
        if (c.Dx != 0 && _prev.Dx == 0) { col = Math.Clamp(col + c.Dx, 0, 1); Play(Sfx.Move); }
        if (c.Dy != 0 && _prev.Dy == 0) { row = Math.Clamp(row + c.Dy, 0, 1); Play(Sfx.Move); }
        Cursor = row * 2 + col;
        if ((startPressed || c.Fire && !_prev.Fire) && PhaseTime > 0.3 && !Done[Cursor]) Enter((StageKind)Cursor);
    }

    private void Enter(StageKind kind)
    {
        Stage = kind switch
        {
            StageKind.Cycles => new CycleStage(this),
            StageKind.Tanks => new TankStage(this),
            StageKind.Bugs => new BugStage(this),
            _ => new CoreStage(this),
        };
        Play(Sfx.Choose);
        SetPhase(Phase.Intro);
    }

    private void LevelUp()
    {
        int bonus = 5000 * Level;
        Level++;
        Score += bonus;
        Play(Sfx.LevelUp);
        Say($"LEVEL {Level}!", 3);
        SetPhase(Phase.LevelUp);
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Test hook: straight into a game, already playing.</summary>
    internal T Into<T>(StageKind kind) where T : Stage
    {
        if (State != GameState.Playing) StartGame();
        Cursor = (int)kind;
        Enter(kind);
        SetPhase(Phase.Playing);
        return (T)Stage!;
    }

    internal void SetLevel(int level) => Level = level;
}
