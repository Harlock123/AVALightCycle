namespace AVALightCycle.Engine;

/// <summary>
/// The core: a spinning core behind a band of shield blocks that turns and slowly comes down.
/// Shoot a gap through the band and slip up through it into the core before the band crushes
/// you. The core spits sparks; touching a block or a spark is the end.
/// </summary>
public sealed class CoreStage : Stage
{
    public const int Columns = 30;
    public const double BlockW = 32, BlockH = 18, CoreX = 512, CoreY = 160, CoreRadius = 64;
    public const double Floor = Field.ArenaBottom - 14;

    public sealed class Spark
    {
        public double X, Y, Vx, Vy;
        public bool Gone;
    }

    public Runner Runner { get; } = new() { X = 512, Y = 700 };
    /// <summary>Which blocks of the band remain, row by row (row 0 at the top).</summary>
    public bool[,] Blocks { get; }
    public int Rows { get; }
    /// <summary>How far round the band has turned, in pixels, and how far down it has come.</summary>
    public double Offset { get; private set; }
    public double BandTop { get; private set; } = 236;
    public List<Shot> Shots { get; } = [];
    public List<Spark> Sparks { get; } = [];
    public double Spin { get; private set; }
    public override StageKind Kind => StageKind.Core;

    private double _sparkTimer = 2.5;

    public CoreStage(GameEngine g) : base(g)
    {
        Rows = Math.Min(8, 4 + g.Level / 2);
        Blocks = new bool[Rows, Columns];
        for (int r = 0; r < Rows; r++)
        for (int col = 0; col < Columns; col++)
            Blocks[r, col] = true;
    }

    public double BandBottom => BandTop + Rows * BlockH;
    /// <summary>When the band comes down this far, there's no room left to stand.</summary>
    public const double CrushLine = Floor - 40;
    public double TurnSpeed => 55 + G.Level * 10;
    /// <summary>The way up into the core: only straight under it can the runner climb into the band.</summary>
    public const double DoorHalf = 42;
    public double FallSpeed => 4 + G.Level * 1.2;

    /// <summary>Where a block is across the screen now, as the band turns (it wraps round).</summary>
    public double BlockX(int col)
    {
        double width = Columns * BlockW;
        double x = (col * BlockW + Offset) % width;
        if (x < 0) x += width;
        return Field.ArenaLeft + x;
    }

    /// <summary>The block of a row at a point across the screen.</summary>
    public int ColumnAt(double x)
    {
        double width = Columns * BlockW;
        double rel = ((x - Field.ArenaLeft - Offset) % width + width) % width;
        return (int)(rel / BlockW) % Columns;
    }

    public override void Update(Controls c, double dt)
    {
        Time += dt;
        Spin += dt;
        Offset += TurnSpeed * dt;
        BandTop += FallSpeed * dt;
        var r = Runner;
        // Below the band the runner goes where it likes; into it and up only beneath the door.
        bool atDoor = Math.Abs(r.X - CoreX) < DoorHalf - Runner.Radius;
        double ceiling = atDoor ? Field.ArenaTop + 14 : BandBottom + Runner.Radius + 2;
        if (r.Y < BandBottom + Runner.Radius) r.Move(c, dt, CoreX - DoorHalf + Runner.Radius + 1, ceiling, CoreX + DoorHalf - Runner.Radius - 1, Floor);
        else r.Move(c, dt, Field.ArenaLeft + 14, ceiling, Field.ArenaRight - 14, Floor);
        if (c.Fire && r.Cool <= 0)
        {
            var d = Vec.Angle(r.Aim);
            Shots.Add(new Shot { X = r.X + d.X * 16, Y = r.Y + d.Y * 16, Vx = d.X * 640, Vy = d.Y * 640, Life = 1.2 });
            r.Cool = 0.14;
            G.Play(Sfx.Shot);
        }
        foreach (var s in Shots)
        {
            s.X += s.Vx * dt;
            s.Y += s.Vy * dt;
            s.Life -= dt;
            if (s.Life <= 0 || s.Y < Field.ArenaTop || s.Y > Field.ArenaBottom || s.X < Field.ArenaLeft || s.X > Field.ArenaRight) { s.Gone = true; continue; }
            if (BlockAt(s.X, s.Y) is { } hit)
            {
                Blocks[hit.Row, hit.Col] = false;
                s.Gone = true;
                G.AddScore(50, s.X, s.Y);
                G.Play(Sfx.Chip);
                G.Burst(s.X, s.Y, RowColour(hit.Row), big: false);
            }
        }
        Shots.RemoveAll(s => s.Gone);
        Sparkle(dt);
        // Blocks and sparks are deadly; so is the band reaching the floor.
        foreach (var (dx, dy) in new[] { (0.0, -Runner.Radius), (-Runner.Radius + 3, 0.0), (Runner.Radius - 3, 0.0), (0.0, Runner.Radius - 3) })
            if (BlockAt(r.X + dx, r.Y + dy) is not null) { Lose("HIT THE SHIELD!", r.X, r.Y); return; }
        if (Sparks.Any(s => (s.X - r.X) * (s.X - r.X) + (s.Y - r.Y) * (s.Y - r.Y) < 14 * 14)) { Lose("SPARKED!", r.X, r.Y); return; }
        if (BandBottom >= CrushLine) { Lose("CRUSHED!", r.X, r.Y); return; }
        // Through the band and into the core.
        if (r.Y < BandTop - 6)
        {
            G.AddScore(3000 + Rows * 500, CoreX, CoreY + 90);
            G.Play(Sfx.Gate);
            Win();
        }
    }

    public (int Row, int Col)? BlockAt(double x, double y)
    {
        if (y < BandTop || y >= BandBottom || x < Field.ArenaLeft || x > Field.ArenaRight) return null;
        int row = (int)((y - BandTop) / BlockH);
        int col = ColumnAt(x);
        return Blocks[row, col] ? (row, col) : null;
    }

    public static uint RowColour(int row) => row switch { 0 => 0xFFFF4A3A, 1 => 0xFFFF9A2A, 2 => 0xFFFFE14A, 3 => 0xFF70E070, 4 => 0xFF4FD7FF, 5 => 0xFF7A8AFF, _ => 0xFFD07AFF };

    private void Sparkle(double dt)
    {
        _sparkTimer -= dt;
        if (_sparkTimer <= 0)
        {
            _sparkTimer = Math.Max(1.0, 2.6 - G.Level * 0.15);
            // A spark falls from a random place along the band's underside.
            double x = Field.ArenaLeft + 20 + G.Rng.NextDouble() * (Field.ArenaRight - Field.ArenaLeft - 40);
            var to = new Vec(Runner.X - x, Runner.Y - BandBottom).Unit;
            Sparks.Add(new Spark { X = x, Y = BandBottom + 4, Vx = to.X * 150, Vy = Math.Max(100, to.Y * 190) });
            G.Play(Sfx.Spark);
        }
        foreach (var s in Sparks)
        {
            s.X += s.Vx * dt;
            s.Y += s.Vy * dt;
            if (s.Y > Field.ArenaBottom || s.X < Field.ArenaLeft || s.X > Field.ArenaRight) s.Gone = true;
        }
        Sparks.RemoveAll(s => s.Gone);
    }

    /// <summary>Test hook: clears a whole column of the band where it is now above a point.</summary>
    internal void ClearAbove(double x)
    {
        for (int row = 0; row < Rows; row++)
        for (int dx = -1; dx <= 1; dx++)
            Blocks[row, ColumnAt(x + dx * BlockW)] = false;
    }

    internal void StopSparks() => _sparkTimer = double.MaxValue;

    /// <summary>Test hook: the whole band shot away.</summary>
    internal void ClearAll() => Array.Clear(Blocks);
}
