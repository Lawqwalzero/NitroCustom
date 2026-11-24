using System.Globalization;

namespace NitroCustom.Core.Hardware;

/// <summary>Thermal profiles as encoded by the firmware (misc setting 0x0B).</summary>
public enum ThermalProfile : byte
{
    Quiet = 0x00,
    Balanced = 0x01,
    Performance = 0x04,
    Turbo = 0x05,
    Eco = 0x06,
}

/// <summary>Sensor ids for GetGamingSysInfo (cmd 0x01).</summary>
public enum Sensor : byte
{
    CpuTemperature = 0x01,
    CpuFanSpeed = 0x02,
    SystemTemperature = 0x03,
    ExternalTemperature = 0x04,
    GpuFanSpeed = 0x06,
    GpuTemperature = 0x0A,
}

/// <summary>24-bit colour.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb Parse(string text)
    {
        var s = text.Trim().TrimStart('#');
        if (s.Length != 6 || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            throw new FormatException($"Colour must be #RRGGBB, got '{text}'.");
        return new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>Keyboard lighting zones (4-zone RGB keyboard). Values are bit masks.</summary>
[Flags]
public enum KeyboardZone : byte
{
    None = 0,
    Zone1 = 0x01,
    Zone2 = 0x02,
    Zone3 = 0x04,
    Zone4 = 0x08,
    All = 0x0F,
}

/// <summary>Firmware hotkey event delivered through APGeEvent.</summary>
public abstract record HotkeyEvent
{
    public sealed record TouchpadToggled(bool Enabled) : HotkeyEvent;

    public sealed record Unknown(byte[] Detail) : HotkeyEvent
    {
        public override string ToString() => $"Unknown({Convert.ToHexString(Detail)})";
    }
}

/// <summary>
/// Encoding of the Acer gaming WMI interface (root\WMI:AcerGamingFunction).
/// Codes cross-checked against the Linux acer-wmi driver and verified on ANV16S-41.
/// For every 64-bit reply the low byte is a status (0 = success).
/// </summary>
public static class AcerProtocol
{
    public const string GetSysInfo = "GetGamingSysInfo";
    public const string GetMisc = "GetGamingMiscSetting";
    public const string SetMisc = "SetGamingMiscSetting";
    public const string GetFanBehavior = "GetGamingFanBehavior";
    public const string GetRgbKb = "GetGamingRgbKb";
    public const string SetRgbKb = "SetGamingRgbKb";
    public const string GetKbBacklight = "GetGamingKBBacklight";
    public const string SetKbBacklight = "SetGamingKBBacklight";

    public const uint SysInfoSupportedSensors = 0x00;
    public const uint SysInfoSensorReading = 0x01;
    public const uint MiscSupportedProfiles = 0x0A;
    public const uint MiscPlatformProfile = 0x0B;
    public const uint FanBehaviorAllFans = 0x09;
    public const uint KbBacklightCurrent = 0x01;

    public static bool IsSuccess(ulong raw) => (raw & 0xFF) == 0;

    // ---- sensors ----

    public static uint EncodeSensorQuery(Sensor sensor) => SysInfoSensorReading | ((uint)sensor << 8);

    public static bool TryDecodeSensor(ulong raw, out int value)
    {
        value = (int)((raw >> 8) & 0xFFFF);
        return IsSuccess(raw);
    }

    /// <summary>Supported sensors: bits 24..40 of the reply, bit N = sensor id N+1.</summary>
    public static IReadOnlyList<Sensor> DecodeSupportedSensors(ulong raw)
    {
        if (!IsSuccess(raw)) return [];
        var mask = (raw >> 24) & 0x1FFFF;
        var list = new List<Sensor>();
        for (var bit = 0; bit < 17; bit++)
        {
            if ((mask & (1UL << bit)) == 0) continue;
            var id = (Sensor)(bit + 1);
            if (Enum.IsDefined(id)) list.Add(id);
        }
        return list;
    }

    // ---- misc settings (thermal profile) ----

    public static ulong EncodeMisc(uint setting, byte value) => setting | ((ulong)value << 8);

    public static bool TryDecodeMisc(ulong raw, out byte value)
    {
        value = (byte)((raw >> 8) & 0xFF);
        return IsSuccess(raw);
    }

    /// <summary>Supported profiles: bit N of the mask = profile id N.</summary>
    public static IReadOnlyList<ThermalProfile> DecodeSupportedProfiles(byte mask)
    {
        var list = new List<ThermalProfile>();
        foreach (var p in Enum.GetValues<ThermalProfile>())
            if ((mask & (1 << (byte)p)) != 0) list.Add(p);
        return list;
    }

    // ---- keyboard zones ----

    public static uint EncodeZoneQuery(KeyboardZone zone) => (uint)zone;

    public static ulong EncodeZoneColor(KeyboardZone zones, Rgb c) =>
        (ulong)zones | ((ulong)c.R << 8) | ((ulong)c.G << 16) | ((ulong)c.B << 24);

    public static bool TryDecodeZoneColor(ulong raw, out Rgb color)
    {
        color = new Rgb((byte)(raw >> 8), (byte)(raw >> 16), (byte)(raw >> 24));
        return IsSuccess(raw);
    }

    public static readonly KeyboardZone[] SingleZones =
        [KeyboardZone.Zone1, KeyboardZone.Zone2, KeyboardZone.Zone3, KeyboardZone.Zone4];

    // ---- hotkey events ----

    /// <summary>APGeEvent.EventDetail: [type, code, state, ...]. Type 0x01 = hotkey.</summary>
    public static HotkeyEvent DecodeEvent(byte[] detail)
    {
        if (detail is [0x01, 0x82, var state, ..])
            return new HotkeyEvent.TouchpadToggled(state != 0);
        return new HotkeyEvent.Unknown(detail);
    }
}
