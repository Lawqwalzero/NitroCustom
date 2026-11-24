namespace NitroCustom.Core.Hardware;

/// <summary>Lighting targets behind the ENE lighting HID device (ACPI\ENEK5130, usage page FF5A).</summary>
public enum LightingTarget : byte
{
    Keyboard = 0x21,
    ModeKey = 0x65,
}

/// <summary>
/// Effect codes accepted by the lighting firmware. Static (2) and Wave (7) were captured from Acer's log;
/// the rest match the table in the MIT-licensed "nitrous" project and the hardware sweep 0..10 (each code looks different).
/// </summary>
public enum LightingEffect : byte
{
    Direct = 0x01,
    Static = 0x02,
    Breathing = 0x04,
    Neon = 0x05,
    /// <summary>Mode-change blink; colour bytes are a palette index, not RGB.</summary>
    OperatingMode = 0x06,
    Wave = 0x07,
    Shifting = 0x08,
    Zoom = 0x09,
    Meteor = 0x0A,
    Twinkling = 0x0B,
}

/// <summary>Direction byte for moving effects.</summary>
public enum LightingDirection : byte
{
    None = 0,
    Right = 1,
    Left = 2,
}

/// <summary>One lighting update. Brightness is 0..100, speed and direction are passed through as is.</summary>
public readonly record struct LightingCommand(
    LightingTarget Target,
    byte Effect,
    byte Brightness,
    byte Speed,
    byte Direction,
    Rgb Color,
    KeyboardZone Zones);

/// <summary>Byte layout of the ENE lighting feature reports (taken from Acer's own controller log).</summary>
public static class LightingProtocol
{
    public const string DeviceMatch = "enek5130";
    public const ushort UsagePage = 0xFF5A;
    public const int ReportLength = 11;

    /// <summary>Report listing the lighting targets: [A1, count, target...].</summary>
    public const byte DeviceListReport = 0xA1;
    /// <summary>Report that applies a lighting update.</summary>
    public const byte UpdateReport = 0xA4;

    public const byte MaxBrightness = 100;
    /// <summary>Speed range for animated effects (static uses 0).</summary>
    public const byte MinSpeed = 1, MaxSpeed = 9;
    /// <summary>Direction byte Acer sends for whole-keyboard effects.</summary>
    public const byte DefaultDirection = 0x02;

    /// <summary>[A4, target, effect, brightness, speed, direction, R, G, B, zones, 0].</summary>
    public static byte[] EncodeUpdate(LightingCommand c)
    {
        var report = new byte[ReportLength];
        report[0] = UpdateReport;
        report[1] = (byte)c.Target;
        report[2] = c.Effect;
        report[3] = Math.Min(c.Brightness, MaxBrightness);
        report[4] = c.Speed;
        report[5] = c.Direction;
        report[6] = c.Color.R;
        report[7] = c.Color.G;
        report[8] = c.Color.B;
        report[9] = c.Target == LightingTarget.Keyboard ? (byte)c.Zones : (byte)0;
        return report;
    }

    /// <summary>Decodes the A1 report into the list of targets the firmware exposes.</summary>
    public static IReadOnlyList<LightingTarget> DecodeDeviceList(ReadOnlySpan<byte> report)
    {
        if (report.Length < 2 || report[0] != DeviceListReport) return [];
        var count = Math.Min(report[1], report.Length - 2);
        var list = new List<LightingTarget>(count);
        for (var i = 0; i < count; i++) list.Add((LightingTarget)report[2 + i]);
        return list;
    }

    public static LightingCommand Static(Rgb color, byte brightness, KeyboardZone zones = KeyboardZone.All) =>
        new(LightingTarget.Keyboard, (byte)LightingEffect.Static, brightness, 0, DefaultDirection, color, zones);

    /// <summary>Per-zone colours: one static update per zone, direction 0 (what Acer sends for "direct").</summary>
    public static IEnumerable<LightingCommand> Zones(IReadOnlyList<Rgb> colors, byte brightness)
    {
        if (colors.Count is < 1 or > 4) throw new ArgumentException("Expected 1..4 zone colours.", nameof(colors));
        for (var i = 0; i < colors.Count; i++)
            yield return new(LightingTarget.Keyboard, (byte)LightingEffect.Static, brightness, 0, 0, colors[i], (KeyboardZone)(1 << i));
    }

    /// <summary>Animated whole-keyboard effect. Speed is clamped to 1..9.</summary>
    public static LightingCommand Effect(LightingEffect effect, byte brightness, byte speed,
        LightingDirection direction = LightingDirection.Left, Rgb color = default) =>
        new(LightingTarget.Keyboard, (byte)effect, brightness, Math.Clamp(speed, MinSpeed, MaxSpeed),
            (byte)direction, color, KeyboardZone.All);

    public static LightingCommand ModeKey(Rgb color, byte brightness) =>
        new(LightingTarget.ModeKey, (byte)LightingEffect.Static, brightness, 0, 0, color, KeyboardZone.None);

    /// <summary>Turns a saved keyboard lighting into the reports to send.</summary>
    public static IReadOnlyList<LightingCommand> Build(KeyboardLighting l)
    {
        // Off is a black static frame: an animated effect at brightness 0 is not reliably dark on the firmware.
        if (!l.Enabled) return [Static(new Rgb(0, 0, 0), 0)];
        return l.Effect switch
        {
            LightingEffect.Static => [Static(Rgb.Parse(l.Color), l.Brightness)],
            LightingEffect.Direct => Zones(l.ZoneColors.Select(Rgb.Parse).ToList(), l.Brightness).ToList(),
            _ => [Effect(l.Effect, l.Brightness, l.Speed, l.Direction, Rgb.Parse(l.Color))],
        };
    }
}

/// <summary>Keyboard lighting as the user configures it (shared by the service, the app and the settings file).</summary>
public sealed record KeyboardLighting
{
    public bool Enabled { get; init; } = true;
    public LightingEffect Effect { get; init; } = LightingEffect.Static;
    /// <summary>#RRGGBB for single-colour effects.</summary>
    public string Color { get; init; } = "#FF2A2A";
    /// <summary>#RRGGBB per zone (left to right) for <see cref="LightingEffect.Direct"/>.</summary>
    public IReadOnlyList<string> ZoneColors { get; init; } = ["#FF2A2A", "#FF2A2A", "#FF2A2A", "#FF2A2A"];
    public byte Brightness { get; init; } = 60;
    public byte Speed { get; init; } = 4;
    public LightingDirection Direction { get; init; } = LightingDirection.Left;

    /// <summary>Throws <see cref="FormatException"/>/<see cref="ArgumentException"/> when a field is out of range.</summary>
    public KeyboardLighting Validated()
    {
        if (!Enum.IsDefined(Effect) || Effect == LightingEffect.OperatingMode)
            throw new ArgumentException($"Unsupported effect '{Effect}'.");
        _ = Rgb.Parse(Color);
        if (ZoneColors.Count != 4) throw new ArgumentException("Exactly 4 zone colours are required.");
        foreach (var c in ZoneColors) _ = Rgb.Parse(c);
        return this with
        {
            Brightness = Math.Min(Brightness, LightingProtocol.MaxBrightness),
            Speed = Math.Clamp(Speed, LightingProtocol.MinSpeed, LightingProtocol.MaxSpeed),
        };
    }
}
