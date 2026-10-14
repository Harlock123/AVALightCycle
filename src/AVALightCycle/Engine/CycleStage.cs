namespace AVALightCycle.Engine;

/// <summary>
/// The cycle race: light cycles on a grid, each laying a wall behind it. Turn across an enemy's
/// path so it runs into a wall; don't run into one yourself. A cycle that crashes takes its wall
/// with it.
/// </summary>
public sealed class CycleStage : Stage
{
    public const double Cell = 8;
    public const int Cols = 120, Rows = 82;
    public static readonly (int X, int Y)[] Dirs = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    public sealed class Cycle
    {
        public int Id, X, Y, Dir;
        public bool Alive = true, Player;
        public double Acc, Speed;
        /// <summary>Where it turned, for drawing its wall: the start, every corner, and the head.</summary>
        public List<(int X, int Y)> Corners = [];
        public uint Colour;
        internal int Wanted = -1;
    }

    /// <summary>Who owns each cell: 0 empty, otherwise the id of the cycle whose wall it is.</summary>
    public int[,] Grid { get; } = new int[Cols, Rows];
    public List<Cycle> Cycles { get; } = [];
    public Cycle Player => Cycles[0];
    public override StageKind Kind => StageKind.Cycles;

    private static readonly uint[] EnemyColours = [0xFFFF9A2A, 0xFFFFE14A, 0xFFFF4A8A, 0xFFB07AFF];

    public CycleStage(GameEngine g) : base(g)
    {
        int enemies = Math.Min(4, 1 + g.Level / 2 + (g.Level >= 2 ? 1 : 0));
        double enemySpeed = Math.Min(23, 14 + g.Level * 1.2);
        Add(new Cycle { Id = 1, X = Cols / 2, Y = Rows - 6, Dir = 0, Player = true, Speed = 16, Colour = 0xFF4FD7FF });
        (int X, int Y, int Dir)[] starts = [(Cols / 2, 5, 2), (8, Rows / 2, 1), (Cols - 9, Rows / 2, 3), (Cols / 4, 5, 2)];
        for (int i = 0; i < enemies; i++)
            Add(new Cycle { Id = 2 + i, X = starts[i].X, Y = starts[i].Y, Dir = starts[i].Dir, Speed = enemySpeed, Colour = EnemyColours[i] });
    }

    private void Add(Cycle c)
    {
        Cycles.Add(c);
        Grid[c.X, c.Y] = c.Id;
        c.Corners.Add((c.X, c.Y));
        c.Corners.Add((c.X, c.Y));
    }

    public static (double X, double Y) Screen(double x, double y) => (Field.ArenaLeft + (x + 0.5) * Cell, Field.ArenaTop + (y + 0.5) * Cell);

    public override void Update(Controls c, double dt)
    {
        Time += dt;
        // The player turns with the stick (never straight back on itself); the trigger slows it.
        int want = c.Dx > 0 ? 1 : c.Dx < 0 ? 3 : c.Dy < 0 ? 0 : c.Dy > 0 ? 2 : -1;
        if (want >= 0 && want != (Player.Dir + 2) % 4) Player.Wanted = want;
        Player.Speed = c.Fire ? 10 : 16;
        foreach (var cy in Cycles.Where(x => x.Alive))
        {
            cy.Acc += cy.Speed * dt;
            while (cy.Acc >= 1 && cy.Alive && Result == StageResult.Running)
            {
                cy.Acc -= 1;
                if (cy.Player)
                {
                    if (cy.Wanted >= 0 && cy.Wanted != cy.Dir) Turn(cy, cy.Wanted);
                    cy.Wanted = -1;
                }
                else
                {
                    int d = Choose(cy, G.Rng, aggression: Math.Min(1.0, 0.35 + G.Level * 0.12), sight: Math.Min(260, 20 + G.Level * 35), wobble: Math.Max(4, 30 - G.Level * 5));
                    if (d != cy.Dir) Turn(cy, d);
                }
                Advance(cy);
            }
        }
        if (Result == StageResult.Running && Cycles.Where(x => !x.Player).All(x => !x.Alive))
        {
            G.AddScore(2000 * G.Level, Field.Width / 2, 400);
            Win();
        }
    }

    private static void Turn(Cycle c, int dir)
    {
        c.Dir = dir;
        c.Corners[^1] = (c.X, c.Y);
        c.Corners.Add((c.X, c.Y));
    }

    private void Advance(Cycle c)
    {
        var (dx, dy) = Dirs[c.Dir];
        int nx = c.X + dx, ny = c.Y + dy;
        if (!Free(nx, ny))
        {
            Crash(c);
            return;
        }
        c.X = nx;
        c.Y = ny;
        Grid[nx, ny] = c.Id;
        c.Corners[^1] = (nx, ny);
    }

    public bool Free(int x, int y) => x >= 0 && y >= 0 && x < Cols && y < Rows && Grid[x, y] == 0;

    private void Crash(Cycle c)
    {
        c.Alive = false;
        var (sx, sy) = Screen(c.X, c.Y);
        if (c.Player)
        {
            Lose("CRASHED!", sx, sy);
            return;
        }
        G.Burst(sx, sy, c.Colour, big: true);
        G.Play(Sfx.Derez);
        G.AddScore(1000, sx, sy);
        // Its wall goes with it.
        for (int x = 0; x < Cols; x++)
        for (int y = 0; y < Rows; y++)
            if (Grid[x, y] == c.Id) Grid[x, y] = 0;
        c.Corners.Clear();
    }

    /// <summary>
    /// A rider's choice of way: never into a wall, preferring room to move (counted out to a limit),
    /// and, the more aggressive it is, the way that heads across the target's path. Used for the
    /// enemy riders and for the computer playing the player's cycle.
    /// </summary>
    public int Choose(Cycle me, Random rng, double aggression, Cycle? target = null, int sight = 260, double wobble = 3)
    {
        target ??= me.Player ? Cycles.Where(x => !x.Player && x.Alive).OrderBy(x => Math.Abs(x.X - me.X) + Math.Abs(x.Y - me.Y)).FirstOrDefault() : Player;
        int best = me.Dir;
        double bestScore = double.MinValue;
        foreach (int d in new[] { me.Dir, (me.Dir + 1) % 4, (me.Dir + 3) % 4 })
        {
            var (dx, dy) = Dirs[d];
            int nx = me.X + dx, ny = me.Y + dy;
            if (!Free(nx, ny)) continue;
            double room = Room(nx, ny, sight);
            double score = Math.Min(room, 140) * 2 + (d == me.Dir ? 4 : 0) + rng.NextDouble() * wobble;
            // A little more weight on the straight run ahead, to avoid dead ends.
            score += Math.Min(Ray(nx, ny, d), 20);
            if (target is { Alive: true } && room > Math.Min(60, sight / 2))
            {
                var (tdx, tdy) = Dirs[target.Dir];
                int ax = target.X + tdx * 8, ay = target.Y + tdy * 8;
                double dist = Math.Abs(ax - nx) + Math.Abs(ay - ny);
                score -= dist * aggression * 0.9;
            }
            if (score > bestScore) { bestScore = score; best = d; }
        }
        return best;
    }

    private int Ray(int x, int y, int dir)
    {
        var (dx, dy) = Dirs[dir];
        int n = 0;
        while (Free(x + dx, y + dy) && n < 40) { x += dx; y += dy; n++; }
        return n;
    }

    /// <summary>How much open grid can be reached from a cell, counted up to a limit.</summary>
    private readonly Queue<(int, int)> _queue = new();
    private readonly bool[,] _seen = new bool[Cols, Rows];

    public int Room(int x, int y, int limit)
    {
        Array.Clear(_seen);
        _queue.Clear();
        _queue.Enqueue((x, y));
        _seen[x, y] = true;
        int n = 0;
        while (_queue.Count > 0 && n < limit)
        {
            var (cx, cy) = _queue.Dequeue();
            n++;
            foreach (var (dx, dy) in Dirs)
            {
                int ax = cx + dx, ay = cy + dy;
                if (!Free(ax, ay) || _seen[ax, ay]) continue;
                _seen[ax, ay] = true;
                _queue.Enqueue((ax, ay));
            }
        }
        return n;
    }

    /// <summary>Test hook: puts down a stretch of wall.</summary>
    internal void Wall(int x0, int y0, int x1, int y1)
    {
        for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++)
        for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++)
            Grid[x, y] = 99;
    }
}
