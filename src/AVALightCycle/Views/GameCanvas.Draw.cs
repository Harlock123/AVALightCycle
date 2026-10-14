using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVALightCycle.Engine;

namespace AVALightCycle.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVALightCycle/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        return p;
    }

    private const uint White = 0xFFF2F2F2, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF7A8AB0, Cyan = 0xFF4FD7FF, Orange = 0xFFFF9A2A, Green = 0xFF8AFF4A;

    private static uint Alpha(uint argb, uint a) => (a << 24) | (argb & 0x00FFFFFF);

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool glow = false)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (glow)
            foreach (var (ox, oy) in new[] { (-2.0, 0.0), (2.0, 0.0), (0.0, -2.0), (0.0, 2.0) })
                dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(Alpha(colour, 0x40))), new Point(x + ox, at.Y - ft.Height / 2 + oy));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    /// <summary>A neon line: a wide faint glow under a bright core.</summary>
    private static void Neon(DrawingContext dc, uint colour, double width, Point a, Point b)
    {
        dc.DrawLine(Pen(Alpha(colour, 0x30), width * 3.5), a, b);
        dc.DrawLine(Pen(colour, width), a, b);
    }

    private static void NeonPath(DrawingContext dc, uint colour, double width, IReadOnlyList<Point> pts, bool closed = false)
    {
        if (pts.Count < 2) return;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], false);
            for (int i = 1; i < pts.Count; i++) c.LineTo(pts[i]);
            c.EndFigure(closed);
        }
        dc.DrawGeometry(null, Pen(Alpha(colour, 0x30), width * 3.5), g);
        dc.DrawGeometry(null, Pen(colour, width), g);
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF02030A), new Rect(0, 0, Field.Width, Field.Height));
        bool playing = g.State is GameState.Playing or GameState.Paused or GameState.Attract or GameState.GameOver;
        DrawGrid(dc, g.Stage is TankStage && playing && g.Phase != Phase.Select ? 0x18u : 0x30u);
        if (playing && g.Phase != Phase.Select && g.Phase != Phase.LevelUp)
        {
            using var clip = dc.PushClip(new Rect(Field.ArenaLeft + 1, Field.ArenaTop + 1, Field.ArenaRight - Field.ArenaLeft - 2, Field.ArenaBottom - Field.ArenaTop + 14));
            switch (g.Stage)
            {
                case CycleStage s: DrawCycles(dc, s); break;
                case TankStage s: DrawTanks(dc, s); break;
                case BugStage s: DrawBugs(dc, s); break;
                case CoreStage s: DrawCore(dc, s); break;
            }
        }
        else if (playing) DrawSelect(dc);
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0xA0000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Cyan, center: true, glow: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver) Text(dc, "END OF LINE", new Point(Field.Width / 2, 330), 100, Red, center: true, glow: true);
        if (g.State == GameState.Attract && g.Phase == Phase.Playing)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, 82), 24, Yellow, center: true);
    }

    // ───────────────────────────── the grid ─────────────────────────────

    private static void DrawGrid(DrawingContext dc, uint alpha)
    {
        uint line = Alpha(0xFF1E5AFF, alpha);
        for (double x = Field.ArenaLeft; x <= Field.ArenaRight + 0.5; x += 40)
            dc.DrawLine(Pen(line, 1), new Point(x, Field.ArenaTop), new Point(x, Field.ArenaBottom));
        for (double y = Field.ArenaTop; y <= Field.ArenaBottom + 0.5; y += 40)
            dc.DrawLine(Pen(line, 1), new Point(Field.ArenaLeft, y), new Point(Field.ArenaRight, y));
        var r = new Rect(Field.ArenaLeft, Field.ArenaTop, Field.ArenaRight - Field.ArenaLeft, Field.ArenaBottom - Field.ArenaTop);
        dc.DrawRectangle(Pen(Alpha(Cyan, 0x40), 8), r);
        dc.DrawRectangle(Pen(Cyan, 2), r);
    }

    // ───────────────────────────── the cycle race ─────────────────────────────

    private void DrawCycles(DrawingContext dc, CycleStage s)
    {
        foreach (var c in s.Cycles)
        {
            if (c.Corners.Count == 0) continue;
            var pts = c.Corners.Select(p => { var (x, y) = CycleStage.Screen(p.X, p.Y); return new Point(x, y); }).ToList();
            NeonPath(dc, c.Colour, 3, pts);
            if (!c.Alive) continue;
            // The bike: a long body pointing the way it rides, with a bright rider.
            var (hx, hy) = CycleStage.Screen(c.X, c.Y);
            var (dx, dy) = CycleStage.Dirs[c.Dir];
            var fwd = new Vec(dx, dy);
            var side = new Vec(-dy, dx);
            var head = new Vec(hx, hy) + fwd * 6;
            Point Q(Vec v) => new(v.X, v.Y);
            var nose = head + fwd * 4;
            var tail = head - fwd * 22;
            Poly(dc, 0xFF0A0A14, c.Colour, 2, Q(nose), Q(head + side * 6 - fwd * 4), Q(tail + side * 6), Q(tail - side * 6), Q(head - side * 6 - fwd * 4));
            dc.DrawEllipse(null, Pen(c.Colour, 2), Q(head - fwd * 4), 4, 4);
            dc.DrawEllipse(null, Pen(c.Colour, 2), Q(tail + fwd * 3), 4, 4);
            dc.DrawEllipse(Brush(White), null, Q(head - fwd * 10), 2.5, 2.5);
        }
        Text(dc, $"RIDERS LEFT {s.Cycles.Count(c => !c.Player && c.Alive)}", new Point(Field.ArenaRight - 12, Field.ArenaBottom - 16), 22, Dim, right: true);
    }

    // ───────────────────────────── the tank maze ─────────────────────────────

    private void DrawTanks(DrawingContext dc, TankStage s)
    {
        for (int x = 0; x < TankStage.Cols; x++)
        for (int y = 0; y < TankStage.Rows; y++)
        {
            if (!s.Walls[x, y]) continue;
            var (cx, cy) = TankStage.Centre(x, y);
            var r = new Rect(cx - 20, cy - 20, 40, 40);
            dc.FillRectangle(Brush(0xFF0A1430), r);
            // Glowing edges only where the wall meets open floor.
            uint edge = 0xFF3A7AFF;
            if (s.Open(x, y - 1)) Neon(dc, edge, 1.5, r.TopLeft, r.TopRight);
            if (s.Open(x, y + 1)) Neon(dc, edge, 1.5, r.BottomLeft, r.BottomRight);
            if (s.Open(x - 1, y)) Neon(dc, edge, 1.5, r.TopLeft, r.BottomLeft);
            if (s.Open(x + 1, y)) Neon(dc, edge, 1.5, r.TopRight, r.BottomRight);
        }
        // The teleport square.
        var (tx, ty) = TankStage.Centre(TankStage.CentreX, TankStage.CentreY);
        double pulse = 12 + Math.Sin(_clock * 6) * 4;
        Poly(dc, 0, White, 2, new Point(tx, ty - pulse), new Point(tx + pulse, ty), new Point(tx, ty + pulse), new Point(tx - pulse, ty));
        Poly(dc, Alpha(Cyan, 0x50), null, 0, new Point(tx, ty - pulse * 0.6), new Point(tx + pulse * 0.6, ty), new Point(tx, ty + pulse * 0.6), new Point(tx - pulse * 0.6, ty));
        foreach (var t in s.Tanks.Where(t => t.Alive)) DrawTank(dc, t);
        foreach (var sh in s.Shots)
        {
            uint c = sh.Enemy ? Orange : Cyan;
            var p = new Point(sh.X, sh.Y);
            dc.DrawEllipse(Brush(Alpha(c, 0x50)), null, p, 8, 8);
            dc.DrawEllipse(Brush(White), Pen(c, 2), p, 3.5, 3.5);
        }
    }

    private static void DrawTank(DrawingContext dc, TankStage.Tank t)
    {
        uint c = t.Flash > 0 ? White : t.Enemy ? (t.Hits == 0 ? Red : t.Hits == 1 ? Orange : Yellow) : Cyan;
        var (dx, dy) = TankStage.Dirs[t.Dir];
        var fwd = new Vec(dx, dy);
        var side = new Vec(-dy, dx);
        var at = new Vec(t.X, t.Y);
        Point Q(Vec v) => new(v.X, v.Y);
        // Treads and hull, turned the way it drives; the turret aims on its own.
        Poly(dc, 0xFF060A14, c, 2, Q(at + fwd * 15 + side * 14), Q(at + fwd * 15 - side * 14), Q(at - fwd * 15 - side * 14), Q(at - fwd * 15 + side * 14));
        Neon(dc, c, 1.5, Q(at + fwd * 15 + side * 9), Q(at - fwd * 15 + side * 9));
        Neon(dc, c, 1.5, Q(at + fwd * 15 - side * 9), Q(at - fwd * 15 - side * 9));
        var aim = Vec.Angle(t.Aim);
        dc.DrawEllipse(Brush(0xFF0A1020), Pen(c, 2), Q(at), 7, 7);
        Neon(dc, c, 3, Q(at), Q(at + aim * 20));
    }

    // ───────────────────────────── the bug gate ─────────────────────────────

    private void DrawBugs(DrawingContext dc, BugStage s)
    {
        // The gate: dim and charging until it opens, then rings that pulse inwards, faster as time runs short.
        var gate = new Point(BugStage.GateX, BugStage.GateY);
        if (!s.GateOpen)
        {
            double charge = 1 - s.OpensIn / Math.Max(1, s.TimeAllowed - Math.Max(8, 14 - Engine.Level));
            dc.DrawEllipse(null, Pen(Alpha(Red, 0x90), 3), gate, BugStage.GateRadius, BugStage.GateRadius);
            var arc = new StreamGeometry();
            using (var c = arc.Open())
            {
                c.BeginFigure(new Point(gate.X, gate.Y - BugStage.GateRadius), false);
                double end = -Math.PI / 2 + Math.PI * 2 * Math.Clamp(charge, 0.001, 0.999);
                c.ArcTo(new Point(gate.X + Math.Cos(end) * BugStage.GateRadius, gate.Y + Math.Sin(end) * BugStage.GateRadius), new Size(BugStage.GateRadius, BugStage.GateRadius), 0, charge > 0.5, SweepDirection.Clockwise);
            }
            dc.DrawGeometry(null, Pen(Yellow, 4), arc);
            Text(dc, $"OPENS IN {(int)Math.Ceiling(s.OpensIn)}", new Point(gate.X, gate.Y + BugStage.GateRadius + 18), 22, Yellow, center: true);
        }
        else
        {
            double speed = s.TimeLeft < 6 ? 3 : 1.4;
            for (int i = 0; i < 4; i++)
            {
                double t = (_clock * speed + i * 0.25) % 1;
                double r = BugStage.GateRadius * (1.6 - t);
                dc.DrawEllipse(null, Pen(Alpha(White, (uint)(255 * t)), 2), gate, r, r);
            }
            dc.DrawEllipse(Brush(Alpha(Cyan, 0x60)), Pen(Cyan, 3), gate, BugStage.GateRadius * 0.6, BugStage.GateRadius * 0.6);
            Text(dc, "GATE OPEN", new Point(gate.X, gate.Y + BugStage.GateRadius + 18), 22, Cyan, center: true);
        }
        foreach (var b in s.Bugs) DrawBug(dc, b);
        foreach (var sh in s.Shots) Neon(dc, Cyan, 2.5, new Point(sh.X, sh.Y), new Point(sh.X - sh.Vx * 0.02, sh.Y - sh.Vy * 0.02));
        DrawRunner(dc, s.Runner);
        // The clock along the bottom.
        double frac = Math.Clamp(s.TimeLeft / s.TimeAllowed, 0, 1);
        uint col = s.TimeLeft < 8 ? Red : Yellow;
        dc.FillRectangle(Brush(Alpha(col, 0x40)), new Rect(Field.ArenaLeft, Field.ArenaBottom + 4, (Field.ArenaRight - Field.ArenaLeft) * frac, 8));
        dc.FillRectangle(Brush(col), new Rect(Field.ArenaLeft, Field.ArenaBottom + 6, (Field.ArenaRight - Field.ArenaLeft) * frac, 4));
    }

    private void DrawBug(DrawingContext dc, BugStage.Bug b)
    {
        var at = new Point(b.X, b.Y);
        double wig = Math.Sin(b.Age * 20) * 3;
        for (int k = -1; k <= 1; k++)
        {
            dc.DrawLine(Pen(Green, 1.5), new Point(at.X - 6, at.Y + k * 5), new Point(at.X - 13, at.Y + k * 6 + wig));
            dc.DrawLine(Pen(Green, 1.5), new Point(at.X + 6, at.Y + k * 5), new Point(at.X + 13, at.Y + k * 6 - wig));
        }
        dc.DrawEllipse(Brush(Alpha(Green, 0x40)), null, at, 13, 13);
        dc.DrawEllipse(Brush(0xFF0A2008), Pen(Green, 2), at, 7, 9);
        dc.DrawEllipse(Brush(Red), null, new Point(at.X - 3, at.Y - 4), 1.8, 1.8);
        dc.DrawEllipse(Brush(Red), null, new Point(at.X + 3, at.Y - 4), 1.8, 1.8);
    }

    /// <summary>The runner on foot: a glowing figure with a disc gun pointing the way it aims.</summary>
    private void DrawRunner(DrawingContext dc, Runner r)
    {
        var at = new Point(r.X, r.Y);
        double s = Math.Sin(r.Walk) * 5;
        dc.DrawEllipse(Brush(Alpha(Cyan, 0x30)), null, at, 18, 18);
        Neon(dc, Cyan, 2.5, new Point(at.X, at.Y - 2), new Point(at.X - 4 + s, at.Y + 12));
        Neon(dc, Cyan, 2.5, new Point(at.X, at.Y - 2), new Point(at.X + 4 - s, at.Y + 12));
        Neon(dc, Cyan, 3.5, new Point(at.X, at.Y - 2), new Point(at.X, at.Y - 12));
        dc.DrawEllipse(Brush(0xFF0A1830), Pen(Cyan, 2), new Point(at.X, at.Y - 16), 5, 5);
        var aim = Vec.Angle(r.Aim);
        var hand = new Point(at.X + aim.X * 16, at.Y - 8 + aim.Y * 16);
        Neon(dc, White, 2, new Point(at.X, at.Y - 9), hand);
        dc.DrawEllipse(null, Pen(White, 2), hand, 4, 4);
    }

    // ───────────────────────────── the core ─────────────────────────────

    private void DrawCore(DrawingContext dc, CoreStage s)
    {
        var core = new Point(CoreStage.CoreX, CoreStage.CoreY);
        // The core: rings of turning segments around a bright heart, an opening at the bottom.
        for (int ring = 0; ring < 3; ring++)
        {
            double r = CoreStage.CoreRadius - ring * 18;
            int segs = 8 + ring * 4;
            double turn = s.Spin * (ring % 2 == 0 ? 1.2 : -1.6);
            for (int k = 0; k < segs; k++)
            {
                double a0 = turn + k * Math.PI * 2 / segs, a1 = a0 + Math.PI * 2 / segs * 0.6;
                var p0 = new Point(core.X + Math.Cos(a0) * r, core.Y + Math.Sin(a0) * r * 0.75);
                var p1 = new Point(core.X + Math.Cos(a1) * r, core.Y + Math.Sin(a1) * r * 0.75);
                Neon(dc, ring == 0 ? Red : ring == 1 ? Orange : Yellow, 3, p0, p1);
            }
        }
        double heart = 12 + Math.Sin(_clock * 8) * 3;
        dc.DrawEllipse(Brush(Alpha(White, 0x60)), null, core, heart * 1.8, heart * 1.8);
        dc.DrawEllipse(Brush(White), null, core, heart, heart);
        Neon(dc, Cyan, 2, new Point(core.X - 30, CoreStage.CoreY + 60), new Point(core.X - 30, s.BandTop - 4));
        Neon(dc, Cyan, 2, new Point(core.X + 30, CoreStage.CoreY + 60), new Point(core.X + 30, s.BandTop - 4));
        // The band of shield blocks.
        for (int row = 0; row < s.Rows; row++)
        {
            uint c = CoreStage.RowColour(row);
            double y = s.BandTop + row * CoreStage.BlockH;
            for (int col = 0; col < CoreStage.Columns; col++)
            {
                if (!s.Blocks[row, col]) continue;
                double x = s.BlockX(col);
                var r = new Rect(x + 1, y + 1, CoreStage.BlockW - 2, CoreStage.BlockH - 2);
                dc.FillRectangle(Brush(Alpha(c, 0x50)), r);
                dc.DrawRectangle(Pen(c, 1.5), r);
                // The block wrapping off the right edge also shows at the left.
                if (x + CoreStage.BlockW > Field.ArenaRight)
                {
                    var w = new Rect(x - CoreStage.Columns * CoreStage.BlockW + 1, y + 1, CoreStage.BlockW - 2, CoreStage.BlockH - 2);
                    dc.FillRectangle(Brush(Alpha(c, 0x50)), w);
                    dc.DrawRectangle(Pen(c, 1.5), w);
                }
            }
        }
        // The crush line.
        Neon(dc, Alpha(Red, 0xA0), 1.5, new Point(Field.ArenaLeft, CoreStage.CrushLine), new Point(Field.ArenaRight, CoreStage.CrushLine));
        foreach (var sp in s.Sparks)
        {
            var p = new Point(sp.X, sp.Y);
            for (int k = 0; k < 4; k++)
            {
                double a = _clock * 20 + k * Math.PI / 2;
                Neon(dc, Yellow, 1.5, p, new Point(p.X + Math.Cos(a) * 8, p.Y + Math.Sin(a) * 8));
            }
        }
        foreach (var sh in s.Shots) Neon(dc, Cyan, 2.5, new Point(sh.X, sh.Y), new Point(sh.X - sh.Vx * 0.02, sh.Y - sh.Vy * 0.02));
        DrawRunner(dc, s.Runner);
    }

    // ───────────────────────────── the select screen ─────────────────────────────

    private static Rect Box(int i) => new(Field.ArenaLeft + 40 + i % 2 * 470, Field.ArenaTop + 70 + i / 2 * 290, 410, 250);

    private void DrawSelect(DrawingContext dc)
    {
        var g = Engine;
        Text(dc, $"LEVEL {g.Level} - CHOOSE A GAME", new Point(Field.Width / 2, Field.ArenaTop + 34), 40, Cyan, center: true, glow: true);
        for (int i = 0; i < 4; i++)
        {
            var r = Box(i);
            bool on = g.Cursor == i && g.Phase == Phase.Select;
            uint c = g.Done[i] ? 0xFF3A4A6A : on ? Yellow : Cyan;
            dc.FillRectangle(Brush(0xE0040814), r);
            dc.DrawRectangle(Pen(Alpha(c, 0x40), on ? 10 : 6), r);
            dc.DrawRectangle(Pen(c, on ? 3 : 2), r);
            var kind = (StageKind)i;
            Text(dc, Stage.NameOf(kind), new Point(r.Center.X, r.Y + 30), 38, c, center: true);
            DrawIcon(dc, kind, new Point(r.Center.X, r.Center.Y + 10), g.Done[i] ? 0xFF3A4A6A : c);
            Text(dc, g.Done[i] ? "CLEARED" : Stage.HowTo(kind), new Point(r.Center.X, r.Bottom - 26), g.Done[i] ? 34 : 18, g.Done[i] ? Green : White, center: true);
        }
        if (g.Phase == Phase.Select) Text(dc, "STICK TO CHOOSE - FIRE TO ENTER", new Point(Field.Width / 2, Field.ArenaBottom - 22), 26, Dim, center: true);
    }

    private void DrawIcon(DrawingContext dc, StageKind k, Point c, uint col)
    {
        switch (k)
        {
            case StageKind.Cycles:
                NeonPath(dc, col, 3, [new Point(c.X - 110, c.Y + 30), new Point(c.X - 30, c.Y + 30), new Point(c.X - 30, c.Y - 30), new Point(c.X + 20, c.Y - 30)]);
                NeonPath(dc, Orange, 3, [new Point(c.X + 110, c.Y - 40), new Point(c.X + 60, c.Y - 40), new Point(c.X + 60, c.Y + 20), new Point(c.X + 10, c.Y + 20)]);
                break;
            case StageKind.Tanks:
                for (int i = -2; i <= 2; i++) dc.DrawRectangle(Pen(col, 2), new Rect(c.X + i * 44 - 18, c.Y - 40 + (i % 2) * 50, 36, 30));
                dc.DrawEllipse(null, Pen(Red, 2), new Point(c.X + 60, c.Y + 30), 10, 10);
                dc.DrawEllipse(null, Pen(col, 2), new Point(c.X - 60, c.Y + 30), 10, 10);
                break;
            case StageKind.Bugs:
                for (int i = 0; i < 3; i++) dc.DrawEllipse(null, Pen(Alpha(col, (uint)(255 - i * 70)), 2), c, 14 + i * 12, 14 + i * 12);
                for (int i = 0; i < 6; i++)
                {
                    double a = i * 1.05 + _clock;
                    dc.DrawEllipse(null, Pen(Green, 2), new Point(c.X + Math.Cos(a) * 85, c.Y + Math.Sin(a) * 40), 5, 6);
                }
                break;
            default:
                for (int i = 0; i < 12; i++)
                {
                    double a = _clock + i * Math.PI / 6;
                    Neon(dc, col, 2, new Point(c.X + Math.Cos(a) * 34, c.Y - 30 + Math.Sin(a) * 22), new Point(c.X + Math.Cos(a + 0.3) * 34, c.Y - 30 + Math.Sin(a + 0.3) * 22));
                }
                for (int row = 0; row < 3; row++)
                for (int i = -4; i <= 4; i++)
                    dc.DrawRectangle(Pen(CoreStage.RowColour(row), 1.5), new Rect(c.X + i * 22 - 10, c.Y + 10 + row * 12, 20, 10));
                break;
        }
    }

    // ───────────────────────────── effects and words ─────────────────────────────

    private void DrawEffect(DrawingContext dc, Engine.Effect e)
    {
        double t = e.Age / e.Life;
        switch (e.Kind)
        {
            case EffectKind.Score:
            case EffectKind.Word:
                Text(dc, e.Text, new Point(e.X, e.Y - t * 30), 24, e.Colour, center: true);
                break;
            case EffectKind.Burst:
                dc.DrawEllipse(null, Pen(Alpha(e.Colour, (uint)(255 * (1 - t))), 2), new Point(e.X, e.Y), 4 + t * 18, 4 + t * 18);
                break;
            case EffectKind.Derez:
                // Breaking up into glowing squares that scatter and fade.
                for (int i = 0; i < 24; i++)
                {
                    double a = i * 0.26 + (Hash(i, 1, 9) % 100) / 60.0, sp = 30 + Hash(i, 2, 9) % 90;
                    double x = e.X + Math.Cos(a) * sp * t, y = e.Y + Math.Sin(a) * sp * t;
                    double size = 6 * (1 - t) + 1;
                    dc.FillRectangle(Brush(Alpha(e.Colour, (uint)(255 * (1 - t)))), new Rect(x - size / 2, y - size / 2, size, size));
                }
                dc.DrawEllipse(null, Pen(Alpha(White, (uint)(200 * (1 - t))), 3), new Point(e.X, e.Y), 10 + t * 50, 10 + t * 50);
                break;
        }
    }

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF04060E), new Rect(0, 0, Field.Width, Field.Top));
        Neon(dc, Cyan, 1.5, new Point(0, Field.Top - 2), new Point(Field.Width, Field.Top - 2));
        Text(dc, "SCORE", new Point(14, 16), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(14, 42), 36, White);
        Text(dc, "HI", new Point(170, 16), 22, Dim);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(170, 42), 28, Dim);
        Text(dc, $"LEVEL {g.Level}", new Point(Field.Width / 2, 16), 22, Yellow, center: true);
        if (g.Stage is { } s && g.Phase is not (Phase.Select or Phase.LevelUp)) Text(dc, Stage.NameOf(s.Kind), new Point(Field.Width / 2, 42), 30, Cyan, center: true);
        else Text(dc, "THE GRID", new Point(Field.Width / 2, 42), 30, Cyan, center: true);
        Text(dc, "LIVES", new Point(Field.Width - 220, 16), 22, Dim);
        for (int i = 0; i < Math.Min(7, g.Lives); i++)
        {
            double x = Field.Width - 214 + i * 28;
            Neon(dc, Cyan, 2, new Point(x, 46), new Point(x + 18, 46));
            dc.DrawEllipse(null, Pen(Cyan, 1.5), new Point(x + 3, 46), 3, 3);
            dc.DrawEllipse(null, Pen(Cyan, 1.5), new Point(x + 15, 46), 3, 3);
        }
        if (g.Stage is BugStage b && g.Phase == Phase.Playing)
            Text(dc, $"TIME {(int)Math.Ceiling(b.TimeLeft)}", new Point(Field.Width - 14, 42), 26, b.TimeLeft < 8 ? Red : Yellow, right: true);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        if (g.Phase == Phase.Intro && g.Stage is { } s)
        {
            var r = new Rect(190, 290, 644, 170);
            dc.FillRectangle(Brush(0xE0040814), r);
            dc.DrawRectangle(Pen(Cyan, 2), r);
            Text(dc, Stage.NameOf(s.Kind), new Point(cx, 340), 60, Yellow, center: true, glow: true);
            Text(dc, Stage.HowTo(s.Kind), new Point(cx, 402), 26, White, center: true);
            return;
        }
        if (g.Phase == Phase.LevelUp)
        {
            Text(dc, "ALL FOUR GAMES CLEARED", new Point(cx, 330), 52, Cyan, center: true, glow: true);
            Text(dc, $"LEVEL {g.Level}", new Point(cx, 400), 70, Yellow, center: true, glow: true);
            return;
        }
        if (g.Message is { } msg && g.Phase is Phase.Derez or Phase.Cleared or Phase.Playing)
            Text(dc, msg, new Point(cx, 140), 64, g.Phase == Phase.Derez ? Red : Yellow, center: true, glow: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xC002030A), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "AVA LIGHT CYCLE", new Point(cx, 170), 112, Cyan, center: true, glow: true);
        Text(dc, "AFTER THE 1982 ARCADE CLASSIC", new Point(cx, 240), 28, Dim, center: true);
        // Two cycles racing across, one either way.
        double t = _clock * 260 % 1400 - 200;
        NeonPath(dc, Cyan, 3, [new Point(-50, 290), new Point(t, 290)]);
        NeonPath(dc, Orange, 3, [new Point(Field.Width + 50, 300), new Point(Field.Width - t, 300)]);
        string[] lines =
        [
            "FOUR GAMES ON THE GRID - WIN ALL FOUR TO REACH THE NEXT LEVEL",
            "THE CYCLE RACE   THE TANK MAZE   THE BUG GATE   THE CORE",
            "",
            "STICK OR ARROWS: MOVE      SPACE / CTRL / CLICK: FIRE",
            "AIM: MOUSE, RIGHT STICK, OR Z AND X TO TURN",
        ];
        for (int i = 0; i < lines.Length; i++) Text(dc, lines[i], new Point(cx, 370 + i * 38), 28, i == 1 ? Yellow : White, center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(cx, 620), 48, Yellow, center: true, glow: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xE002030A), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "BEST USERS", new Point(cx, 130), 72, Cyan, center: true, glow: true);
        Text(dc, "RANK NAME    SCORE   LEVEL", new Point(cx, 196), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,3}", new Point(cx, 240 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 700), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xE002030A), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true, glow: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Cyan, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
