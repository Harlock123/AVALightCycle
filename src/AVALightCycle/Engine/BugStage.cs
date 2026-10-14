namespace AVALightCycle.Engine;

/// <summary>
/// The bug gate: get the runner into the glowing gate in the middle of the grid before the time
/// runs out. Grid bugs pour in from the edges, crawling along the grid lines towards you; a touch
/// is the end of you, a disc is the end of them.
/// </summary>
public sealed class BugStage : Stage
{
    public const double GateX = 512, GateY = 424, GateRadius = 34, Grid = 40, BugRadius = 11;

    public sealed class Bug
    {
        public double X, Y, Age;
        public int Dir;
        public bool Gone;
    }

    public Runner Runner { get; } = new() { X = 512, Y = 712 };
    public List<Bug> Bugs { get; } = [];
    public List<Shot> Shots { get; } = [];
    public double TimeLeft { get; private set; }
    public override StageKind Kind => StageKind.Bugs;

    private double _spawn = 0.6;
    private static readonly (int X, int Y)[] Dirs = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    /// <summary>The gate charges for a while before it will let anyone in.</summary>
    public double OpensIn { get; private set; }
    public bool GateOpen => OpensIn <= 0;
    public double TimeAllowed { get; }

    public BugStage(GameEngine g) : base(g)
    {
        OpensIn = Math.Min(20, 10 + g.Level * 2);
        TimeAllowed = TimeLeft = OpensIn + Math.Max(8, 14 - g.Level);
    }

    public double BugSpeed => Math.Min(170, 85 + G.Level * 10);

    public override void Update(Controls c, double dt)
    {
        Time += dt;
        TimeLeft -= dt;
        if (OpensIn > 0)
        {
            OpensIn -= dt;
            if (OpensIn <= 0) G.Play(Sfx.Gate);
        }
        var r = Runner;
        r.Move(c, dt, Field.ArenaLeft + 14, Field.ArenaTop + 14, Field.ArenaRight - 14, Field.ArenaBottom - 14);
        if (c.Fire && r.Cool <= 0)
        {
            var d = Vec.Angle(r.Aim);
            Shots.Add(new Shot { X = r.X + d.X * 16, Y = r.Y + d.Y * 16, Vx = d.X * 620, Vy = d.Y * 620, Life = 1.2 });
            r.Cool = 0.16;
            G.Play(Sfx.Shot);
        }
        Spawn(dt);
        foreach (var b in Bugs) Crawl(b, dt);
        foreach (var s in Shots)
        {
            s.X += s.Vx * dt;
            s.Y += s.Vy * dt;
            s.Life -= dt;
            if (s.Life <= 0 || s.X < Field.ArenaLeft || s.X > Field.ArenaRight || s.Y < Field.ArenaTop || s.Y > Field.ArenaBottom) { s.Gone = true; continue; }
            foreach (var b in Bugs.Where(b => !b.Gone))
            {
                if ((b.X - s.X) * (b.X - s.X) + (b.Y - s.Y) * (b.Y - s.Y) > (BugRadius + 4) * (BugRadius + 4)) continue;
                b.Gone = s.Gone = true;
                G.Burst(b.X, b.Y, 0xFF8AFF4A, big: false);
                G.AddScore(100, b.X, b.Y);
                G.Play(Sfx.Squash);
                break;
            }
        }
        Shots.RemoveAll(s => s.Gone);
        Bugs.RemoveAll(b => b.Gone);
        if (Bugs.Any(b => (b.X - r.X) * (b.X - r.X) + (b.Y - r.Y) * (b.Y - r.Y) < (BugRadius + Runner.Radius - 4) * (BugRadius + Runner.Radius - 4)))
        {
            Lose("BUGGED!", r.X, r.Y);
            return;
        }
        if (GateOpen && (r.X - GateX) * (r.X - GateX) + (r.Y - GateY) * (r.Y - GateY) < GateRadius * GateRadius)
        {
            G.AddScore(3000 + (int)TimeLeft * 100, GateX, GateY - 50);
            G.Play(Sfx.Gate);
            Win();
            return;
        }
        if (TimeLeft <= 0)
        {
            TimeLeft = 0;
            Lose("OUT OF TIME!", r.X, r.Y);
        }
    }

    private void Spawn(double dt)
    {
        _spawn -= dt;
        if (_spawn > 0 || Bugs.Count >= 26 + G.Level * 2) return;
        _spawn = Math.Max(0.22, 0.7 - G.Level * 0.06);
        // From a grid point on one of the edges, heading in.
        int side = G.Rng.Next(4);
        double x, y;
        int dir;
        switch (side)
        {
            case 0: x = Snap(G.Rng.Next(32, 992), Field.ArenaLeft); y = Field.ArenaTop; dir = 2; break;
            case 1: x = Field.ArenaRight; y = Snap(G.Rng.Next(96, 640), Field.ArenaTop); dir = 3; break;
            case 2: x = Field.ArenaLeft; y = Snap(G.Rng.Next(96, 640), Field.ArenaTop); dir = 1; break;
            default: x = Snap(G.Rng.Next(32, 992), Field.ArenaLeft); y = Field.ArenaTop; dir = 2; break;
        }
        Bugs.Add(new Bug { X = x, Y = y, Dir = dir });
    }

    private static double Snap(double v, double origin) => origin + Math.Round((v - origin) / Grid) * Grid;

    /// <summary>Bugs crawl along the grid lines, choosing at each crossing the way that brings them nearer the runner.</summary>
    private void Crawl(Bug b, double dt)
    {
        b.Age += dt;
        double step = BugSpeed * dt;
        var (dx, dy) = Dirs[b.Dir];
        double nx = b.X + dx * step, ny = b.Y + dy * step;
        // Crossing a grid point: choose again.
        double gx = Snap(b.X, Field.ArenaLeft), gy = Snap(b.Y, Field.ArenaTop);
        bool crossedX = dx != 0 && (b.X - gx) * (nx - gx) <= 0 && Math.Abs(b.X - gx) > 1e-9;
        bool crossedY = dy != 0 && (b.Y - gy) * (ny - gy) <= 0 && Math.Abs(b.Y - gy) > 1e-9;
        if (crossedX || crossedY)
        {
            b.X = crossedX ? gx : b.X;
            b.Y = crossedY ? gy : b.Y;
            b.Dir = Toward(b);
            return;
        }
        b.X = Math.Clamp(nx, Field.ArenaLeft, Field.ArenaRight);
        b.Y = Math.Clamp(ny, Field.ArenaTop, Field.ArenaBottom);
        if (b.X <= Field.ArenaLeft || b.X >= Field.ArenaRight || b.Y <= Field.ArenaTop || b.Y >= Field.ArenaBottom) b.Dir = Toward(b);
    }

    private int Toward(Bug b)
    {
        var r = Runner;
        double dx = r.X - b.X, dy = r.Y - b.Y;
        // Mostly the bigger way, sometimes the other, now and then at random.
        int major = Math.Abs(dx) > Math.Abs(dy) ? (dx > 0 ? 1 : 3) : (dy > 0 ? 2 : 0);
        int minor = Math.Abs(dx) > Math.Abs(dy) ? (dy > 0 ? 2 : 0) : (dx > 0 ? 1 : 3);
        int roll = G.Rng.Next(10);
        return roll < 6 ? major : roll < 9 ? minor : G.Rng.Next(4);
    }

    /// <summary>Test hook: a bug at a place, heading one way.</summary>
    internal Bug PutBug(double x, double y, int dir)
    {
        var b = new Bug { X = x, Y = y, Dir = dir };
        Bugs.Add(b);
        return b;
    }

    internal void StopSpawning() => _spawn = double.MaxValue;
}
