namespace AVALightCycle.Engine;

public readonly record struct Vec(double X, double Y)
{
    public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec operator *(Vec a, double k) => new(a.X * k, a.Y * k);
    public double Length => Math.Sqrt(X * X + Y * Y);
    public double Dot(Vec o) => X * o.X + Y * o.Y;
    public Vec Unit => Length < 1e-9 ? new Vec(0, -1) : this * (1 / Length);
    public static Vec Angle(double a) => new(Math.Cos(a), Math.Sin(a));
}

/// <summary>
/// The stick moves; the aim turns with the spin keys, or follows the right stick or the mouse; the
/// trigger fires (and, on a light cycle, slows it down).
/// </summary>
public readonly record struct Controls(int Dx, int Dy, bool Fire = false, int Spin = 0, Vec? AimDir = null, Vec? AimAt = null);

/// <summary>The screen: the score bar, and the arena below it where every game is played.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double Top = 64;
    public const double ArenaLeft = 32, ArenaRight = 992, ArenaTop = 96, ArenaBottom = 752;
}

public enum EffectKind { Score, Word, Burst, Derez }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}

/// <summary>Which of the four games.</summary>
public enum StageKind { Cycles, Tanks, Bugs, Core }

public enum StageResult { Running, Won, Lost }

/// <summary>
/// One of the four games. It runs itself from the controls; when it's won or lost it says so, and
/// the engine takes it from there.
/// </summary>
public abstract class Stage(GameEngine g)
{
    protected GameEngine G { get; } = g;
    public StageResult Result { get; protected set; }
    /// <summary>What did for the player, for the message.</summary>
    public string? Cause { get; protected set; }
    public double Time { get; protected set; }
    public abstract StageKind Kind { get; }
    public abstract void Update(Controls c, double dt);

    protected void Lose(string cause, double x, double y)
    {
        if (Result != StageResult.Running) return;
        Result = StageResult.Lost;
        Cause = cause;
        G.Burst(x, y, 0xFF4FD7FF, big: true);
        G.Play(Sfx.Derez);
    }

    protected void Win()
    {
        if (Result != StageResult.Running) return;
        Result = StageResult.Won;
    }

    public static string NameOf(StageKind k) => k switch
    {
        StageKind.Cycles => "THE CYCLE RACE",
        StageKind.Tanks => "THE TANK MAZE",
        StageKind.Bugs => "THE BUG GATE",
        _ => "THE CORE",
    };

    public static string HowTo(StageKind k) => k switch
    {
        StageKind.Cycles => "BOX IN THE ENEMY RIDERS - HOLD FIRE TO SLOW",
        StageKind.Tanks => "DESTROY THE ENEMY TANKS - SHOTS BOUNCE",
        StageKind.Bugs => "HOLD OFF THE BUGS TILL THE GATE OPENS, THEN GET IN",
        _ => "SHOOT A WAY THROUGH THE SHIELD AND CLIMB IN UNDER THE CORE",
    };
}

/// <summary>A runner on foot, for the bug gate and the core: moves freely and aims a disc gun.</summary>
public sealed class Runner
{
    public const double Speed = 230, Radius = 12;
    public double X, Y, Aim = -Math.PI / 2, Cool, Walk;
    public int Facing = 1;

    /// <summary>Moves with the stick and turns the aim, kept inside the given bounds.</summary>
    public void Move(Controls c, double dt, double left, double top, double right, double bottom)
    {
        var d = new Vec(c.Dx, c.Dy);
        if (d.Length > 0)
        {
            d = d.Unit;
            X = Math.Clamp(X + d.X * Speed * dt, left, right);
            Y = Math.Clamp(Y + d.Y * Speed * dt, top, bottom);
            Walk += dt * 10;
            if (c.Dx != 0) Facing = c.Dx;
        }
        Aim = Aiming.Turn(Aim, c, X, Y, dt);
        if (Cool > 0) Cool -= dt;
    }
}

public static class Aiming
{
    public const double SpinRate = 4.5;

    /// <summary>The aim after this step: the right stick or the mouse set it directly, the spin keys turn it.</summary>
    public static double Turn(double aim, Controls c, double x, double y, double dt)
    {
        if (c.AimDir is { } d && d.Length > 0.3) return Math.Atan2(d.Y, d.X);
        if (c.AimAt is { } at && (at - new Vec(x, y)).Length > 8) return Math.Atan2(at.Y - y, at.X - x);
        return aim + c.Spin * SpinRate * dt;
    }
}

public sealed class Shot
{
    public double X, Y, Vx, Vy, Life;
    public int Bounces;
    public bool Enemy, Gone;
}
