namespace NitroCustom.Core.Hardware;

public sealed record SensorReading(Sensor Sensor, int? Value);

public sealed record KeyboardBacklightState(byte[] Raw)
{
    // Layout hypothesis (Linux "facer" driver): [effect, speed, brightness, 0, direction, R, G, B, ...]
    public byte Effect => Raw.Length > 0 ? Raw[0] : (byte)0;
    public byte Speed => Raw.Length > 1 ? Raw[1] : (byte)0;
    public byte Brightness => Raw.Length > 2 ? Raw[2] : (byte)0;
    public byte Direction => Raw.Length > 4 ? Raw[4] : (byte)0;
    public Rgb Color => Raw.Length > 7 ? new Rgb(Raw[5], Raw[6], Raw[7]) : default;
}

/// <summary>
/// High-level, verified operations on top of <see cref="IAcerWmi"/>. Thread-safe.
/// When <paramref name="ec"/> is given and works, the thermal mode goes through the EC HID channel only
/// (as Acer's own agent does); WMI misc 0x0B then reads back stale, so the mode is also read from the EC.
/// </summary>
public sealed class NitroController(IAcerWmi wmi, EcMode? ec = null)
{
    private readonly Lock _lock = new();
    private IReadOnlyList<ThermalProfile>? _profiles;
    private IReadOnlyList<Sensor>? _sensors;
    private bool? _ecUsable;

    /// <summary>True when the thermal mode goes through the EC HID channel.</summary>
    public bool UsesEc => Ec is not null;

    private EcMode? Ec
    {
        get
        {
            if (ec is null) return null;
            lock (_lock)
            {
                if (_ecUsable is null)
                {
                    try { _ecUsable = ec.IsSupported(); }
                    catch (Exception ex) when (ex is LightingHidException or AcerWmiException) { _ecUsable = false; }
                }
                return _ecUsable.Value ? ec : null;
            }
        }
    }

    // ---- thermal profiles ----

    public IReadOnlyList<ThermalProfile> GetSupportedProfiles()
    {
        lock (_lock)
        {
            if (_profiles is not null) return _profiles;
            var raw = wmi.Get(AcerProtocol.GetMisc, AcerProtocol.MiscSupportedProfiles);
            if (!AcerProtocol.TryDecodeMisc(raw, out var mask))
                throw new AcerWmiException($"Supported profiles query failed (0x{raw:X}).");
            return _profiles = AcerProtocol.DecodeSupportedProfiles(mask);
        }
    }

    public ThermalProfile GetProfile()
    {
        if (Ec is { } channel)
        {
            lock (_lock) return channel.Get();
        }
        var raw = wmi.Get(AcerProtocol.GetMisc, AcerProtocol.MiscPlatformProfile);
        if (!AcerProtocol.TryDecodeMisc(raw, out var id))
            throw new AcerWmiException($"Profile query failed (0x{raw:X}).");
        if (!Enum.IsDefined((ThermalProfile)id))
            throw new AcerWmiException($"Unknown profile id 0x{id:X2}.");
        return (ThermalProfile)id;
    }

    /// <summary>Sets the profile and verifies it by reading it back.</summary>
    public void SetProfile(ThermalProfile profile)
    {
        if (!GetSupportedProfiles().Contains(profile))
            throw new AcerWmiException($"Profile {profile} is not supported by this laptop.");
        var channel = Ec;
        lock (_lock)
        {
            if (channel is not null)
            {
                channel.Set(profile);
            }
            else
            {
                var status = wmi.Set(AcerProtocol.SetMisc, AcerProtocol.EncodeMisc(AcerProtocol.MiscPlatformProfile, (byte)profile));
                if (status != 0)
                    throw new AcerWmiException($"Firmware rejected profile {profile} (status 0x{status:X}). Performance/Turbo may need the charger.");
            }
            var now = GetProfile();
            if (now != profile)
                throw new AcerWmiException($"Profile {profile} was not applied (firmware reports {now}).");
        }
    }

    /// <summary>Moves to the next profile of <paramref name="order"/> (supported ones only) and returns it.</summary>
    public ThermalProfile CycleProfile(IEnumerable<ThermalProfile> order)
    {
        var next = NextProfile(order);
        SetProfile(next);
        return next;
    }

    /// <summary>The profile after the current one in <paramref name="order"/> (unsupported entries skipped).</summary>
    public ThermalProfile NextProfile(IEnumerable<ThermalProfile> order)
    {
        var supported = GetSupportedProfiles();
        var cycle = order.Where(supported.Contains).Distinct().ToList();
        if (cycle.Count == 0) throw new AcerWmiException("No supported profiles in the cycle order.");
        var index = cycle.IndexOf(GetProfile());
        return cycle[(index + 1) % cycle.Count];
    }

    // ---- sensors ----

    public IReadOnlyList<Sensor> GetSupportedSensors()
    {
        lock (_lock)
        {
            return _sensors ??= AcerProtocol.DecodeSupportedSensors(
                wmi.Get(AcerProtocol.GetSysInfo, AcerProtocol.SysInfoSupportedSensors));
        }
    }

    public IReadOnlyList<SensorReading> ReadSensors()
    {
        var list = new List<SensorReading>();
        foreach (var s in GetSupportedSensors())
        {
            var raw = wmi.Get(AcerProtocol.GetSysInfo, AcerProtocol.EncodeSensorQuery(s));
            list.Add(new SensorReading(s, AcerProtocol.TryDecodeSensor(raw, out var v) ? v : null));
        }
        return list;
    }

    // ---- keyboard lighting ----

    public IReadOnlyDictionary<KeyboardZone, Rgb?> GetZoneColors()
    {
        var result = new Dictionary<KeyboardZone, Rgb?>();
        foreach (var zone in AcerProtocol.SingleZones)
        {
            var raw = wmi.Get(AcerProtocol.GetRgbKb, AcerProtocol.EncodeZoneQuery(zone));
            result[zone] = AcerProtocol.TryDecodeZoneColor(raw, out var c) ? c : null;
        }
        return result;
    }

    /// <summary>Sets a static colour for one or more zones and verifies each zone by reading it back.</summary>
    public void SetZoneColor(KeyboardZone zones, Rgb color)
    {
        if (zones == KeyboardZone.None || ((byte)zones & ~(byte)KeyboardZone.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(zones), zones, "Zone mask must be within 0x01..0x0F.");
        lock (_lock)
        {
            var status = wmi.Set(AcerProtocol.SetRgbKb, AcerProtocol.EncodeZoneColor(zones, color));
            if (status != 0)
                throw new AcerWmiException($"Firmware rejected zone colour (status 0x{status:X}).");
            foreach (var zone in AcerProtocol.SingleZones.Where(z => zones.HasFlag(z)))
            {
                var raw = wmi.Get(AcerProtocol.GetRgbKb, AcerProtocol.EncodeZoneQuery(zone));
                if (!AcerProtocol.TryDecodeZoneColor(raw, out var now) || now != color)
                    throw new AcerWmiException($"{zone} colour was not applied (firmware reports 0x{raw:X}).");
            }
        }
    }

    public KeyboardBacklightState GetKeyboardBacklight()
    {
        var (output, ret) = wmi.GetBytes(AcerProtocol.GetKbBacklight, AcerProtocol.KbBacklightCurrent);
        if (ret != 0) throw new AcerWmiException($"Keyboard backlight query failed (return {ret}).");
        return new KeyboardBacklightState(output);
    }

    /// <summary>Experimental: writes a raw 16-byte effect block. Used to verify the layout before exposing effects.</summary>
    public uint SetKeyboardBacklightRaw(byte[] payload)
    {
        if (payload.Length != 16) throw new ArgumentException("Payload must be exactly 16 bytes.", nameof(payload));
        lock (_lock) return wmi.SetBytes(AcerProtocol.SetKbBacklight, payload);
    }

    // ---- fans (read-only for now) ----

    public ulong GetFanBehaviorRaw() => wmi.Get(AcerProtocol.GetFanBehavior, AcerProtocol.FanBehaviorAllFans);
}
