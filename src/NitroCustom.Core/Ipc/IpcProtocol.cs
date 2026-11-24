using System.Text.Json;
using System.Text.Json.Serialization;
using NitroCustom.Core.Hardware;

namespace NitroCustom.Core.Ipc;

/// <summary>
/// Service &lt;-&gt; client protocol: one JSON <see cref="IpcMessage"/> per line over a named pipe.
/// Client sends requests; service answers with responses (same id) and pushes events at any time.
/// </summary>
public static class IpcProtocol
{
    public const string PipeName = "NitroCustom.v1";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(IpcMessage message) => JsonSerializer.Serialize(message, Json);

    public static IpcMessage? Deserialize(string line) => JsonSerializer.Deserialize<IpcMessage>(line, Json);

    public static JsonElement ToElement<T>(T value) => JsonSerializer.SerializeToElement(value, Json);

    public static T? FromElement<T>(JsonElement? element) =>
        element is { } e ? e.Deserialize<T>(Json) : default;
}

public enum IpcMessageType { Request, Response, Event }

public sealed record IpcMessage
{
    public IpcMessageType Type { get; init; }
    public int Id { get; init; }
    /// <summary>Command name for requests, event name for events.</summary>
    public string? Name { get; init; }
    public JsonElement? Data { get; init; }
    public string? Error { get; init; }
}

public static class IpcCommands
{
    public const string GetState = "getState";
    public const string GetSensors = "getSensors";
    public const string SetProfile = "setProfile";
    public const string CycleProfile = "cycleProfile";
    public const string SetLighting = "setLighting";
    public const string SetModeKeyColor = "setModeKeyColor";
    public const string SetUnverifiedConsent = "setUnverifiedConsent";
}

public static class IpcEvents
{
    public const string ProfileChanged = "profileChanged";
    public const string LightingChanged = "lightingChanged";
    public const string Touchpad = "touchpad";
}

// ---- DTOs ----

public sealed record SensorDto(Sensor Sensor, int? Value);

public sealed record StateDto(
    ThermalProfile Profile,
    IReadOnlyList<ThermalProfile> SupportedProfiles,
    IReadOnlyList<ThermalProfile> CycleOrder,
    IReadOnlyList<SensorDto> Sensors,
    LightingDto Lighting,
    ModelDto? Model = null);

/// <summary>
/// The laptop model and whether writes to the firmware are allowed on it. Verified models always allow them;
/// on other models they stay off until the user consents (null in <see cref="StateDto"/> = older service).
/// </summary>
public sealed record ModelDto(string Model, string Bios, bool Verified, bool WritesAllowed);

public sealed record ConsentArgs(bool Allow);

/// <summary>Keyboard lighting plus the mode-key colour per profile (#RRGGBB).</summary>
public sealed record LightingDto(KeyboardLighting Keyboard, IReadOnlyDictionary<ThermalProfile, string> ModeKeyColors);

public sealed record SetProfileArgs(ThermalProfile Profile);

public sealed record ProfileDto(ThermalProfile Profile, string? Reason = null);

public sealed record SetModeKeyColorArgs(ThermalProfile Profile, string Color);

public sealed record TouchpadDto(bool Enabled);
