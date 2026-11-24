using NitroCustom.Core.Hardware;
using NitroCustom.Core.Ipc;

namespace NitroCustom.Service;

/// <summary>Pushes events to all connected pipe clients.</summary>
public sealed class EventHub
{
    public event Action<IpcMessage>? Published;

    public void Publish(string name, object? data) =>
        Published?.Invoke(new IpcMessage
        {
            Type = IpcMessageType.Event,
            Name = name,
            Data = data is null ? null : IpcProtocol.ToElement(data),
        });
}

/// <summary>Use cases shared by the pipe API, hotkeys and startup restore.</summary>
public sealed class NitroService(
    NitroController hw, LightingController light, SettingsStore store, EventHub hub, ModelInfo model, ILogger<NitroService> log)
{
    private const byte ModeKeyBrightness = 80;
    private ThermalProfile? _lastKnownProfile;

    public StateDto GetState()
    {
        var profile = hw.GetProfile();
        if (_lastKnownProfile != profile) OnProfileChanged(profile, "state");
        return new StateDto(profile, hw.GetSupportedProfiles(), store.Current.CycleOrder, GetSensors(), GetLighting(), GetModel());
    }

    /// <summary>Verified models always write; any other model only after the user agreed.</summary>
    public bool WritesAllowed => model.Status == ModelStatus.Verified || store.Current.AllowUnverifiedModel;

    public ModelDto GetModel() => new(model.Display, model.Bios, model.Status == ModelStatus.Verified, WritesAllowed);

    /// <summary>Records the user's choice for a model that was not verified. Verified models ignore it.</summary>
    public ModelDto SetUnverifiedConsent(bool allow)
    {
        if (model.Status == ModelStatus.Unverified)
        {
            store.Update(s => s with { AllowUnverifiedModel = allow });
            log.LogWarning("Writes to the firmware of unverified model {Model} {State} by the user", model.Display, allow ? "enabled" : "disabled");
        }
        return GetModel();
    }

    private void RequireWrites()
    {
        if (!WritesAllowed)
            throw new IpcException("Read-only: this laptop model has not been verified, so changes to modes and lighting are disabled until you allow them in the window.");
    }

    public IReadOnlyList<SensorDto> GetSensors() =>
        hw.ReadSensors().Select(r => new SensorDto(r.Sensor, r.Value)).ToList();

    public LightingDto GetLighting()
    {
        var s = store.Current;
        return new LightingDto(s.Lighting ?? new KeyboardLighting(), s.ModeKeyColors);
    }

    public ThermalProfile SetProfile(ThermalProfile profile)
    {
        RequireWrites();
        SwitchProfile(profile);
        store.Update(s => s with { Profile = profile });
        OnProfileChanged(profile, "user");
        return profile;
    }

    public ThermalProfile CycleProfile()
    {
        RequireWrites();
        var next = hw.NextProfile(store.Current.CycleOrder);
        SwitchProfile(next);
        store.Update(s => s with { Profile = next });
        OnProfileChanged(next, "cycle");
        return next;
    }

    public LightingDto SetLighting(KeyboardLighting lighting)
    {
        RequireWrites();
        var valid = lighting.Validated();
        light.Apply(valid);
        store.Update(s => s with { Lighting = valid });
        return PublishLighting();
    }

    public LightingDto SetModeKeyColor(ThermalProfile profile, Rgb color)
    {
        RequireWrites();
        store.Update(s => s with { ModeKeyColors = new Dictionary<ThermalProfile, string>(s.ModeKeyColors) { [profile] = color.ToString() } });
        if (_lastKnownProfile == profile) WriteModeKey(profile);
        return PublishLighting();
    }

    /// <summary>Re-applies the saved profile, lighting and mode-key colour (startup, resume). Never throws.</summary>
    public void ApplySaved(string reason)
    {
        var s = store.Current;
        if (reason == "startup")
            log.LogInformation("Model {Model}, BIOS {Bios}: {Status}, writes {Writes}", model.Display, model.Bios, model.Status, WritesAllowed ? "allowed" : "blocked (read-only)");
        if (!WritesAllowed)
        {
            // Read-only: report the profile the firmware is in, change nothing.
            try { OnProfileChanged(hw.GetProfile(), reason); }
            catch (Exception ex) { log.LogWarning(ex, "Could not read the profile ({Reason})", reason); }
            return;
        }
        try
        {
            var current = hw.GetProfile();
            if (s.Profile is { } profile && current != profile)
            {
                SwitchProfile(profile);
                current = profile;
            }
            OnProfileChanged(current, reason);
            log.LogInformation("Profile {Profile} applied via {Channel} ({Reason})", current, hw.UsesEc ? "EC HID" : "WMI", reason);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Could not apply saved profile {Profile} ({Reason})", s.Profile, reason);
        }

        if (s.Lighting is { } lighting)
        {
            try
            {
                light.Apply(lighting);
                log.LogInformation("Lighting {Effect} applied ({Reason})", lighting.Effect, reason);
            }
            catch (Exception ex) { log.LogWarning(ex, "Could not apply saved lighting ({Reason})", reason); }
        }
    }

    /// <summary>Detects profile changes made outside NitroCustom and tells clients about them.</summary>
    public void PollProfile()
    {
        var now = hw.GetProfile();
        if (_lastKnownProfile == now)
        {
            // The firmware sometimes puts its own mode-key colour back after our retries; keep ours on the key.
            if (!SwitchInProgress) WriteModeKey(now);
            return;
        }
        if (_lastKnownProfile is { } last) log.LogInformation("Profile changed outside NitroCustom: {Old} -> {New}", last, now);
        OnProfileChanged(now, "external");
    }

    public void PublishTouchpad(bool enabled) => hub.Publish(IpcEvents.Touchpad, new TouchpadDto(enabled));

    private LightingDto PublishLighting()
    {
        var dto = GetLighting();
        hub.Publish(IpcEvents.LightingChanged, dto);
        return dto;
    }

    private void OnProfileChanged(ThermalProfile profile, string reason)
    {
        _lastKnownProfile = profile;
        log.LogInformation("Profile {Profile} ({Reason})", profile, reason);
        if (WritesAllowed)
        {
            ApplyPowerMode(profile);
            ApplyModeKey(profile);
        }
        hub.Publish(IpcEvents.ProfileChanged, new ProfileDto(profile, reason));
    }

    private void ApplyPowerMode(ThermalProfile profile)
    {
        var target = WindowsPowerMode.For(profile);
        try
        {
            if (WindowsPowerMode.Get() == target) return;
            var error = WindowsPowerMode.Set(target);
            if (error != 0) log.LogWarning("Windows power mode for {Profile} not set (error {Error})", profile, error);
            else log.LogInformation("Windows power mode {Mode} for {Profile}", target, profile);
        }
        catch (Exception ex) { log.LogWarning(ex, "Windows power mode for {Profile} not set", profile); }
    }

    /// <summary>
    /// Switches through the EC channel (when available), so the firmware plays the new mode's own
    /// acknowledgement colour. Nothing is written to the LEDs here: that would recolour the blink.
    /// </summary>
    private void SwitchProfile(ThermalProfile profile)
    {
        _switchedAt = Environment.TickCount64;
        hw.SetProfile(profile);
    }

    /// <summary>
    /// The firmware plays its own "mode changed" blink after a switch and leaves the mode key in its own colour,
    /// so our colour is written once the blink is over (right away when no switch is in progress).
    /// </summary>
    private void ApplyModeKey(ThermalProfile profile)
    {
        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref _modeKeyRetry, cts)?.Cancel();
        if (!SwitchInProgress) WriteModeKey(profile);
        _ = Task.Run(async () =>
        {
            foreach (var delay in ModeKeyRetryDelays)
            {
                await Task.Delay(delay, cts.Token);
                WriteModeKey(profile);
            }
        }, cts.Token).ContinueWith(_ => { }, TaskScheduler.Default);
    }

    /// <summary>
    /// Writes after a switch, each delay counted from the previous write (0.6, 1.2, 2, 3, 4.5 s after the switch):
    /// the firmware blink is not always the same length, so one late write is not enough.
    /// </summary>
    private static readonly TimeSpan[] ModeKeyRetryDelays =
        [TimeSpan.FromMilliseconds(600), TimeSpan.FromMilliseconds(600), TimeSpan.FromMilliseconds(800),
         TimeSpan.FromMilliseconds(1000), TimeSpan.FromMilliseconds(1500)];

    /// <summary>True during the firmware blink after our own switch (our colour must not cut it short).</summary>
    private bool SwitchInProgress => Environment.TickCount64 - _switchedAt < 500;
    private CancellationTokenSource? _modeKeyRetry;
    private long _switchedAt = long.MinValue / 2;

    /// <summary>Returns false when there is no colour for the profile or the write failed.</summary>
    private bool WriteModeKey(ThermalProfile profile)
    {
        if (!WritesAllowed) return false;
        if (!store.Current.ModeKeyColors.TryGetValue(profile, out var color)) return false;
        try
        {
            light.SetModeKey(Rgb.Parse(color), ModeKeyBrightness);
            // The firmware's mode-switch blink restores its own keyboard lighting; keep the keyboard dark if the user turned it off.
            if (store.Current.Lighting is { Enabled: false } off) light.Apply(off);
            return true;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Could not set mode key colour for {Profile}", profile);
            return false;
        }
    }
}
