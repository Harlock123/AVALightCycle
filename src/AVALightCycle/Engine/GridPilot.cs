namespace AVALightCycle.Engine;

/// <summary>
/// The computer player, with a way of playing each game: on the cycle grid it rides like the enemy
/// riders, keeping room to move and cutting across their paths; in the tank maze it hunts the
/// nearest tank and fires once it has a clear shot; at the bug gate it heads for the gate, edging
/// round bugs and shooting the nearest; at the core it digs a three-block-wide shaft through the
/// band, keeps under it as the band turns, and goes up once it's through. Used for the
/// demonstration and the tests.
/// </summary>
public sealed class GridPilot(int seed)
{
    private readonly Random _rng = new(seed);
    private bool _fireLast;
    private int _shaftCol = -1;
    private Stage? _stage;

    public Controls Decide(GameEngine g)
    {
        switch (g.Phase)
        {
            case Phase.Select:
            {
                int target = Array.FindIndex(g.Done, d => !d);
                if (target < 0) return default;
                if (g.Cursor != target)
                {
                    int dx = target % 2 - g.Cursor % 2, dy = target / 2 - g.Cursor / 2;
                    bool press = !_fireLast;
                    _fireLast = press;
                    return press ? new Controls(Math.Sign(dx), Math.Sign(dy)) : default;
                }
                return Tap(new Controls(0, 0));
            }
            case Phase.Playing:
                if (!ReferenceEquals(_stage, g.Stage)) { _stage = g.Stage; _shaftCol = -1; }
                return g.Stage switch
                {
                    CycleStage s => Ride(s),
                    TankStage s => Hunt(s),
                    BugStage s => Run(s),
                    CoreStage s => Dig(s),
                    _ => default,
                };
            default:
                return default;
        }
    }

    /// <summary>A fresh press of the trigger every other tick.</summary>
    private Controls Tap(Controls c)
    {
        bool press = !_fireLast;
        _fireLast = press;
        return c with { Fire = press };
    }

    private static Controls Way(int dir) => dir switch
    {
        0 => new Controls(0, -1),
        1 => new Controls(1, 0),
        2 => new Controls(0, 1),
        _ => new Controls(-1, 0),
    };

    private Controls Ride(CycleStage s)
    {
        var me = s.Player;
        if (!me.Alive) return default;
        int d = s.Choose(me, _rng, aggression: 0.45);
        // Slow down when the way ahead is tight.
        int room = s.Room(me.X + CycleStage.Dirs[d].X, me.Y + CycleStage.Dirs[d].Y, 60);
        return Way(d) with { Fire = room < 30 };
    }

    private Controls Hunt(TankStage s)
    {
        var me = s.Player;
        var target = s.Tanks.Where(t => t.Enemy && t.Alive).OrderBy(t => Math.Abs(t.TX - me.TX) + Math.Abs(t.TY - me.TY)).FirstOrDefault();
        if (target is null) return default;
        var aim = new Vec(target.X, target.Y);
        // A shell on its way: step out of its line.
        foreach (var sh in s.Shots.Where(x => x.Enemy))
        {
            var v = new Vec(sh.Vx, sh.Vy);
            var rel = new Vec(me.X - sh.X, me.Y - sh.Y);
            double along = rel.Dot(v.Unit);
            if (along < 0 || along > 260) continue;
            double off = Math.Abs(rel.X * v.Unit.Y - rel.Y * v.Unit.X);
            if (off > 24) continue;
            int across = Math.Abs(v.X) > Math.Abs(v.Y) ? 0 : 1;
            foreach (int d in across == 0 ? new[] { 0, 2 } : new[] { 1, 3 })
                if (s.Open(me.TX + TankStage.Dirs[d].X, me.TY + TankStage.Dirs[d].Y)) return Tap(Way(d) with { AimAt = aim });
        }
        bool clear = s.Clear(me.X, me.Y, target.X, target.Y);
        int? step = s.Path(me.TX, me.TY, target.TX, target.TY);
        var move = step is { } dir ? Way(dir) : default;
        // With a clear shot, fire, closing in only while it's still far.
        if (clear)
        {
            double dist = Math.Sqrt((target.X - me.X) * (target.X - me.X) + (target.Y - me.Y) * (target.Y - me.Y));
            return Tap((dist > 220 ? move : default) with { AimAt = aim });
        }
        return move with { AimAt = aim };
    }

    private Controls Run(BugStage s)
    {
        var r = s.Runner;
        var pos = new Vec(r.X, r.Y);
        var want = (new Vec(BugStage.GateX, BugStage.GateY) - pos).Unit;
        BugStage.Bug? nearest = null;
        double nd = double.MaxValue;
        foreach (var b in s.Bugs)
        {
            var away = pos - new Vec(b.X, b.Y);
            double d = away.Length;
            if (d < nd) { nd = d; nearest = b; }
            if (d < 90) want += away.Unit * ((90 - d) / 30);
        }
        int dx = Math.Abs(want.X) > 0.35 ? Math.Sign(want.X) : 0, dy = Math.Abs(want.Y) > 0.35 ? Math.Sign(want.Y) : 0;
        if (nearest is not null && nd < 330) return Tap(new Controls(dx, dy, AimAt: new Vec(nearest.X, nearest.Y)));
        return new Controls(dx, dy, AimAt: new Vec(BugStage.GateX, BugStage.GateY));
    }

    private Controls Dig(CoreStage s)
    {
        var r = s.Runner;
        // Already climbing beneath the door: keep going, staying in the middle of the shaft.
        if (r.Y < s.BandBottom + Runner.Radius)
        {
            double mid = _shaftCol >= 0 ? s.BlockX(_shaftCol) + CoreStage.BlockW : CoreStage.CoreX;
            return new Controls(Math.Abs(mid - r.X) > 4 ? Math.Sign(mid - r.X) : 0, -1, AimAt: new Vec(r.X, r.Y - 200));
        }
        // Sidestep a spark coming close.
        foreach (var sp in s.Sparks)
        {
            double dx = r.X - sp.X, dy = r.Y - sp.Y;
            if (Math.Abs(dx) < 40 && dy > -10 && dy < 110) return new Controls(dx >= 0 ? 1 : -1, 0, AimAt: new Vec(r.X, r.Y - 200));
        }
        // A shaft four blocks wide, dug from the left so it turns round to the door as it's finished.
        double Centre() => s.BlockX(_shaftCol) + CoreStage.BlockW;
        if (_shaftCol < 0 || Centre() > CoreStage.CoreX + 30 && !Clear(s) || Centre() > Field.ArenaRight - 80)
            _shaftCol = s.ColumnAt(Field.ArenaLeft + 120);
        double centre = Centre();
        bool done = Clear(s);
        double wantX = centre, wantY = s.BandBottom + 60;
        if (done)
        {
            // Wait for it to come round to the door, then up.
            if (centre >= CoreStage.CoreX - 34 && centre <= CoreStage.CoreX + 8) { wantX = CoreStage.CoreX + 4; wantY = 0; }
            else if (centre > CoreStage.CoreX + 8) { _shaftCol = s.ColumnAt(Field.ArenaLeft + 120); return default; }
        }
        int mx = Math.Abs(wantX - r.X) > 4 ? Math.Sign(wantX - r.X) : 0;
        int my = Math.Abs(wantY - r.Y) > 6 ? Math.Sign(wantY - r.Y) : 0;
        if (done) return new Controls(mx, my, AimAt: new Vec(r.X, r.Y - 200));
        // Shoot the lowest block left in the shaft, a little ahead of where it is as the band turns.
        int pick = -1, lowest = -1;
        for (int row = s.Rows - 1; row >= 0 && pick < 0; row--)
            foreach (int col in Shaft(s))
                if (s.Blocks[row, col]) { pick = col; lowest = row; break; }
        double tx = s.BlockX(pick) + CoreStage.BlockW / 2, ty = s.BandTop + (lowest + 0.5) * CoreStage.BlockH;
        double flight = Math.Sqrt((tx - r.X) * (tx - r.X) + (ty - r.Y) * (ty - r.Y)) / 640;
        return Tap(new Controls(mx, my, AimAt: new Vec(tx + s.TurnSpeed * flight, ty)));
    }

    private IEnumerable<int> Shaft(CoreStage s)
    {
        for (int k = -1; k <= 2; k++) yield return (_shaftCol + k + CoreStage.Columns) % CoreStage.Columns;
    }

    private bool Clear(CoreStage s)
    {
        for (int row = 0; row < s.Rows; row++)
            foreach (int col in Shaft(s))
                if (s.Blocks[row, col]) return false;
        return true;
    }
}
