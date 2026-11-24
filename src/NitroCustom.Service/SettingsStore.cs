using System.Text.Json;
using NitroCustom.Core.Hardware;
using NitroCustom.Core.Ipc;

namespace NitroCustom.Service;

public sealed record ServiceSettings
{
    /// <summary>Last profile chosen by the user; re-applied at startup and after resume.</summary>
    public ThermalProfile? Profile { get; init; }

    /// <summary>Order used by the mode button.</summary>
    public IReadOnlyList<ThermalProfile> CycleOrder { get; init; } =
        [ThermalProfile.Quiet, ThermalProfile.Balanced, ThermalProfile.Performance, ThermalProfile.Turbo];

    /// <summary>Keyboard lighting; null = leave the firmware state as is until the user changes it.</summary>
    public KeyboardLighting? Lighting { get; init; }

    /// <summary>Mode-key colour per profile (#RRGGBB).</summary>
    public IReadOnlyDictionary<ThermalProfile, string> ModeKeyColors { get; init; } = DefaultModeKeyColors;

    public static readonly IReadOnlyDictionary<ThermalProfile, string> DefaultModeKeyColors = new Dictionary<ThermalProfile, string>
    {
        [ThermalProfile.Eco] = "#00E060",
        [ThermalProfile.Quiet] = "#FFFFFF",
        [ThermalProfile.Balanced] = "#00A0FF",
        [ThermalProfile.Performance] = "#FF7A00",
        [ThermalProfile.Turbo] = "#FF0000",
    };
}

/// <summary>Settings in %ProgramData%\NitroCustom\settings.json (written by the service only).</summary>
public sealed class SettingsStore
{
    private readonly ILogger<SettingsStore> _log;
    private readonly string _path;
    private readonly Lock _lock = new();
    private ServiceSettings _current;
    private static readonly JsonSerializerOptions FileJson = new(IpcProtocol.Json) { WriteIndented = true };

    public SettingsStore(ILogger<SettingsStore> log)
    {
        _log = log;
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NitroCustom");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
        _current = Load();
    }

    public ServiceSettings Current { get { lock (_lock) return _current; } }

    public ServiceSettings Update(Func<ServiceSettings, ServiceSettings> change)
    {
        lock (_lock)
        {
            _current = change(_current);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_current, FileJson));
            File.Move(tmp, _path, overwrite: true);
            return _current;
        }
    }

    private ServiceSettings Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<ServiceSettings>(File.ReadAllText(_path), IpcProtocol.Json) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _log.LogWarning(ex, "Settings file {Path} is unreadable, using defaults", _path);
        }
        return new();
    }
}
