namespace AVALightCycle.Engine;

/// <summary>
/// The tank maze: your tank against enemy tanks in a maze of walls. Shots bounce off the walls.
/// Each enemy takes three hits; one hit stops yours. The square in the middle teleports whoever
/// drives onto it.
/// </summary>
public sealed class TankStage : Stage
{
    public const double Tile = 40;
    public const int Cols = 24, Rows = 16;
    public const int CentreX = 11, CentreY = 7;
    public static readonly (int X, int Y)[] Dirs = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    public sealed class Tank
    {
        public double X, Y, Aim, Cool;
        public int Dir, TX, TY;
        public bool Moving, Enemy, Alive = true;
        public int Hits;
        public double Speed, Flash;
    }

    public bool[,] Walls { get; } = new bool[Cols, Rows];
    public List<Tank> Tanks { get; } = [];
    public List<Shot> Shots { get; } = [];
    public Tank Player => Tanks[0];
    /// <summary>Hits it takes to stop an enemy tank: two at first, three from level three.</summary>
    public int EnemyHits => G.Level < 3 ? 2 : 3;
    public override StageKind Kind => StageKind.Tanks;

    public TankStage(GameEngine g) : base(g)
    {
        Build(g.Rng);
        var p = Place(1, Rows - 2, enemy: false);
        p.Speed = 120;
        p.Aim = -Math.PI / 2;
        int enemies = Math.Min(5, 2 + (g.Level - 1) / 2);
        (int X, int Y)[] spots = [(Cols - 3, 1), (1, 1), (Cols - 3, Rows - 2), (CentreX, 1), (Cols - 3, CentreY)];
        for (int i = 0; i < enemies; i++)
        {
            var (x, y) = spots[i];
            var e = Place(x, y, enemy: true);
            e.Speed = Math.Min(115, 62 + g.Level * 8);
            // A moment's grace before they open fire.
            e.Cool = 3 + i * 0.8;
        }
    }

    public static (double X, double Y) Centre(int tx, int ty) => (Field.ArenaLeft + (tx + 0.5) * Tile, Field.ArenaTop + 16 + (ty + 0.5) * Tile);
    public static (int X, int Y) TileOf(double x, double y) => ((int)Math.Floor((x - Field.ArenaLeft) / Tile), (int)Math.Floor((y - Field.ArenaTop - 16) / Tile));
    public bool Open(int x, int y) => x >= 0 && y >= 0 && x < Cols && y < Rows && !Walls[x, y];

    /// <summary>A maze with plenty of loops: carved from a grid of rooms, then with walls knocked through.</summary>
    private void Build(Random rng)
    {
        for (int x = 0; x < Cols; x++)
        for (int y = 0; y < Rows; y++)
            Walls[x, y] = true;
        // Rooms at odd tiles, 11 by 7 of them.
        int rw = 11, rh = 7;
        var seen = new bool[rw, rh];
        var stack = new Stack<(int, int)>();
        stack.Push((0, 0));
        seen[0, 0] = true;
        Walls[1, 1] = false;
        while (stack.Count > 0)
        {
            var (cx, cy) = stack.Peek();
            var next = Dirs.Select(d => (cx + d.X, cy + d.Y)).Where(n => n.Item1 >= 0 && n.Item2 >= 0 && n.Item1 < rw && n.Item2 < rh && !seen[n.Item1, n.Item2]).OrderBy(_ => rng.Next()).ToList();
            if (next.Count == 0) { stack.Pop(); continue; }
            var (nx, ny) = next[0];
            seen[nx, ny] = true;
            Walls[1 + 2 * nx, 1 + 2 * ny] = false;
            Walls[1 + cx + nx, 1 + cy + ny] = false;
            stack.Push((nx, ny));
        }
        // Knock through about half the remaining inner walls between rooms, for loops.
        for (int x = 1; x < 2 * rw; x++)
        for (int y = 1; y < 2 * rh; y++)
            if (Walls[x, y] && (x % 2 == 1) != (y % 2 == 1) && rng.Next(100) < 45) Walls[x, y] = false;
        // A corridor down the right and along the bottom.
        for (int y = 1; y < Rows - 1; y++) Walls[Cols - 2, y] = false;
        for (int x = 1; x < Cols - 1; x++) Walls[x, Rows - 2] = false;
        Walls[CentreX, CentreY] = false;
    }

    private Tank Place(int tx, int ty, bool enemy)
    {
        Walls[tx, ty] = false;
        var (x, y) = Centre(tx, ty);
        var t = new Tank { X = x, Y = y, TX = tx, TY = ty, Enemy = enemy, Aim = enemy ? Math.PI / 2 : -Math.PI / 2 };
        Tanks.Add(t);
        return t;
    }

    public override void Update(Controls c, double dt)
    {
        Time += dt;
        var p = Player;
        // The player's tank: drive with the stick, turn the turret, fire.
        int want = c.Dx > 0 ? 1 : c.Dx < 0 ? 3 : c.Dy < 0 ? 0 : c.Dy > 0 ? 2 : -1;
        p.Aim = Aiming.Turn(p.Aim, c, p.X, p.Y, dt);
        Drive(p, want, dt);
        if (p.Cool > 0) p.Cool -= dt;
        if (c.Fire && p.Cool <= 0 && Shots.Count(s => !s.Enemy) < 3)
        {
            Fire(p);
            p.Cool = 0.25;
        }
        foreach (var e in Tanks.Where(t => t.Enemy && t.Alive)) Think(e, dt);
        MoveShots(dt);
        foreach (var t in Tanks) if (t.Flash > 0) t.Flash -= dt;
        if (Result == StageResult.Running && Tanks.All(t => !t.Enemy || !t.Alive))
        {
            G.AddScore(2000 * G.Level, Field.Width / 2, 400);
            Win();
        }
    }

    /// <summary>Tanks drive from tile centre to tile centre, turning only at a centre.</summary>
    private void Drive(Tank t, int want, double dt)
    {
        var (cx, cy) = Centre(t.TX, t.TY);
        if (!t.Moving)
        {
            int d = want >= 0 && Open(t.TX + Dirs[want].X, t.TY + Dirs[want].Y) ? want : -1;
            if (d < 0) return;
            t.Dir = d;
            t.TX += Dirs[d].X;
            t.TY += Dirs[d].Y;
            t.Moving = true;
            (cx, cy) = Centre(t.TX, t.TY);
        }
        // A reversal mid-way is allowed: head back to the tile it came from.
        if (want >= 0 && want == (t.Dir + 2) % 4)
        {
            t.Dir = want;
            t.TX += Dirs[want].X;
            t.TY += Dirs[want].Y;
            (cx, cy) = Centre(t.TX, t.TY);
        }
        double step = t.Speed * dt;
        double dx = cx - t.X, dy = cy - t.Y, dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist <= step)
        {
            t.X = cx;
            t.Y = cy;
            t.Moving = false;
            if (t.TX == CentreX && t.TY == CentreY) Teleport(t);
            // Keep going if the stick still says so.
            if (want >= 0 && Open(t.TX + Dirs[want].X, t.TY + Dirs[want].Y)) Drive(t, want, Math.Max(0, (step - dist) / t.Speed));
        }
        else
        {
            t.X += dx / dist * step;
            t.Y += dy / dist * step;
        }
    }

    private void Teleport(Tank t)
    {
        var spots = new List<(int, int)>();
        for (int x = 1; x < Cols - 1; x++)
        for (int y = 1; y < Rows - 1; y++)
            if (Open(x, y) && Math.Abs(x - CentreX) + Math.Abs(y - CentreY) > 8 && !Tanks.Any(o => o != t && o.Alive && o.TX == x && o.TY == y)) spots.Add((x, y));
        if (spots.Count == 0) return;
        var (tx, ty) = spots[G.Rng.Next(spots.Count)];
        G.Burst(t.X, t.Y, 0xFFFFFFFF, big: false);
        t.TX = tx;
        t.TY = ty;
        (t.X, t.Y) = Centre(tx, ty);
        G.Play(Sfx.Teleport);
    }

    private void Fire(Tank t)
    {
        // Enemy gunners are a little off, and their shells slower.
        double aim = t.Aim + (t.Enemy ? (G.Rng.NextDouble() - 0.5) * Math.Max(0.06, 0.32 - G.Level * 0.04) : 0);
        double speed = t.Enemy ? Math.Min(380, 250 + G.Level * 20) : 420;
        var d = Vec.Angle(aim);
        Shots.Add(new Shot { X = t.X + d.X * 15, Y = t.Y + d.Y * 15, Vx = d.X * speed, Vy = d.Y * speed, Life = 2.2, Enemy = t.Enemy });
        G.Play(t.Enemy ? Sfx.EnemyShot : Sfx.Shot);
    }

    /// <summary>Enemy tanks: head for the player by the shortest way, and fire when they can see it.</summary>
    private void Think(Tank e, double dt)
    {
        var p = Player;
        if (e.Cool > 0) e.Cool -= dt;
        int want = -1;
        if (!e.Moving && Path(e.TX, e.TY, p.TX, p.TY) is { } first) want = first;
        Drive(e, want, dt);
        double aim = Math.Atan2(p.Y - e.Y, p.X - e.X);
        // The turret swings round to the target.
        double diff = Math.IEEERemainder(aim - e.Aim, Math.PI * 2);
        double turn = Math.Min(3, 1.4 + G.Level * 0.25);
        e.Aim += Math.Clamp(diff, -turn * dt, turn * dt);
        if (e.Cool <= 0 && Math.Abs(diff) < 0.15 && p.Alive && Clear(e.X, e.Y, p.X, p.Y))
        {
            Fire(e);
            e.Cool = Math.Max(1.2, 3.4 - G.Level * 0.25);
        }
    }

    /// <summary>The first step of the shortest way through the maze, or null if there's none.</summary>
    public int? Path(int fx, int fy, int tx, int ty)
    {
        if (fx == tx && fy == ty) return null;
        var from = new int[Cols, Rows];
        for (int x = 0; x < Cols; x++) for (int y = 0; y < Rows; y++) from[x, y] = -1;
        var q = new Queue<(int, int)>();
        q.Enqueue((tx, ty));
        from[tx, ty] = 9;
        while (q.Count > 0)
        {
            var (cx, cy) = q.Dequeue();
            for (int d = 0; d < 4; d++)
            {
                int nx = cx + Dirs[d].X, ny = cy + Dirs[d].Y;
                if (!Open(nx, ny) || from[nx, ny] >= 0) continue;
                from[nx, ny] = (d + 2) % 4;
                if (nx == fx && ny == fy) return from[nx, ny];
                q.Enqueue((nx, ny));
            }
        }
        return null;
    }

    /// <summary>Whether a straight line between two points crosses no wall.</summary>
    public bool Clear(double x0, double y0, double x1, double y1)
    {
        double len = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        int n = (int)(len / 8) + 1;
        for (int i = 1; i < n; i++)
        {
            var (tx, ty) = TileOf(x0 + (x1 - x0) * i / n, y0 + (y1 - y0) * i / n);
            if (!Open(tx, ty)) return false;
        }
        return true;
    }

    private void MoveShots(double dt)
    {
        foreach (var s in Shots)
        {
            s.Life -= dt;
            if (s.Life <= 0) { s.Gone = true; continue; }
            // Across, then down, bouncing off whichever wall it meets.
            var (ax, ay) = TileOf(s.X + s.Vx * dt, s.Y);
            if (s.Vx != 0 && !Open(ax, ay)) { s.Vx = -s.Vx; s.Bounces++; G.Play(Sfx.Ricochet); }
            else s.X += s.Vx * dt;
            var (bx, by) = TileOf(s.X, s.Y + s.Vy * dt);
            if (s.Vy != 0 && !Open(bx, by)) { s.Vy = -s.Vy; s.Bounces++; G.Play(Sfx.Ricochet); }
            else s.Y += s.Vy * dt;
            if (s.Bounces > 2) { s.Gone = true; continue; }
            foreach (var t in Tanks)
            {
                if (!t.Alive || t.Enemy == s.Enemy) continue;
                if ((t.X - s.X) * (t.X - s.X) + (t.Y - s.Y) * (t.Y - s.Y) > 17 * 17) continue;
                s.Gone = true;
                Hit(t);
                break;
            }
        }
        Shots.RemoveAll(s => s.Gone);
    }

    private void Hit(Tank t)
    {
        if (!t.Enemy)
        {
            t.Alive = false;
            Lose("TANK DESTROYED!", t.X, t.Y);
            return;
        }
        t.Hits++;
        t.Flash = 0.2;
        G.AddScore(100, t.X, t.Y - 24);
        G.Play(Sfx.Hit);
        if (t.Hits < EnemyHits) return;
        t.Alive = false;
        G.Burst(t.X, t.Y, 0xFFFF6A2A, big: true);
        G.Play(Sfx.Boom);
        G.AddScore(1000, t.X, t.Y);
    }
}
