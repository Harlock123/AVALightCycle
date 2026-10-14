using Avalonia.Input;

namespace AVALightCycle.Engine;

/// <summary>
/// Keyboard and gamepad boiled down to the controls: the stick (move), the aim (spin keys, the mouse or the right stick) and the trigger.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly ControllerManager _pad = new(0);

    public void OnKeyDown(Key key)
    {
        _held.Remove(key);
        _held.Add(key);
    }

    public void OnKeyUp(Key key) => _held.Remove(key);
    public bool Has(Key key) => _held.Contains(key);

    public void Update() => _pad.Update();

    private int Axis(Key[] minus, Key[] plus)
    {
        for (int i = _held.Count - 1; i >= 0; i--)
        {
            if (minus.Contains(_held[i])) return -1;
            if (plus.Contains(_held[i])) return 1;
        }
        return 0;
    }

    private static readonly Key[] LeftKeys = [Key.Left, Key.A, Key.NumPad4];
    private static readonly Key[] RightKeys = [Key.Right, Key.D, Key.NumPad6];
    private static readonly Key[] UpKeys = [Key.Up, Key.W, Key.NumPad8];
    private static readonly Key[] DownKeys = [Key.Down, Key.S, Key.NumPad2];

    public Controls Controls
    {
        get
        {
            int dx = Axis(LeftKeys, RightKeys), dy = Axis(UpKeys, DownKeys);
            if (dx == 0) dx = _pad.DPadLeft || _pad.LeftStickX < -0.4f ? -1 : _pad.DPadRight || _pad.LeftStickX > 0.4f ? 1 : 0;
            if (dy == 0) dy = _pad.DPadUp || _pad.LeftStickY < -0.4f ? -1 : _pad.DPadDown || _pad.LeftStickY > 0.4f ? 1 : 0;
            bool fire = Has(Key.Space) || Has(Key.LeftCtrl) || Has(Key.RightCtrl) || _pad.ButtonA || _pad.RightTrigger > 0.4f || MouseFire;
            int spin = Has(Key.Z) || Has(Key.Q) || _pad.LeftBumper ? -1 : Has(Key.X) || Has(Key.E) || _pad.RightBumper ? 1 : 0;
            Vec? aimDir = Math.Abs(_pad.RightStickX) + Math.Abs(_pad.RightStickY) > 0.5f ? new Vec(_pad.RightStickX, _pad.RightStickY) : null;
            return new Controls(dx, dy, fire, spin, aimDir, aimDir is null ? MouseAt : null);
        }
    }

    /// <summary>Where the mouse is pointing, on the game's screen, while it's in use.</summary>
    public Vec? MouseAt { get; set; }
    public bool MouseFire { get; set; }

    public bool Start => Has(Key.Enter) || Has(Key.Space) || _pad.ButtonStart || _pad.ButtonA;

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonX || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart;
    public bool PadBack => _pad.ButtonBack;

    public void Dispose() => _pad.Dispose();
}
