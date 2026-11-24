using NitroCustom.Core.Hardware;
using NitroCustom.Core.Ipc;

namespace NitroCustom.Core;

public readonly record struct OsdBounds(int X, int Y, int Width, int Height);

/// <summary>Pure presentation state. All calls belong to the agent's window thread.</summary>
public sealed class OsdState
{
    public const int FadeInMs = 120, HoldMs = 1500, FadeOutMs = 250;
    /// <summary>Drawing canvas at 96 DPI; the card inside it sizes itself to the title.</summary>
    public const int CanvasWidth = 360, CanvasHeight = 96;
    private ThermalProfile? _profile;
    private IReadOnlyDictionary<ThermalProfile, string> _colors = new Dictionary<ThermalProfile, string>();
    private long? _shownAt;
    private long _revision;
    private long _profileRevision, _colorRevision;
    public ThermalProfile Profile => _profile ?? ThermalProfile.Balanced;
    public string Title => ProfileNames.Title(Profile);
    public Rgb Color => _colors.TryGetValue(Profile, out var color) ? Rgb.Parse(color) : new Rgb(160, 160, 160);
    public long Revision => _revision;

    public void BeginConnection()
    {
        _profile = null;
        _shownAt = null;
        _revision = 0;
        _profileRevision = _colorRevision = 0;
    }

    public void ApplySnapshot(StateDto state, long revisionBeforeRequest)
    {
        // An event received while getState was in flight is newer than its snapshot.
        if (_profileRevision <= revisionBeforeRequest) _profile = state.Profile;
        if (_colorRevision <= revisionBeforeRequest) _colors = state.Lighting.ModeKeyColors;
    }

    public void Lighting(LightingDto lighting)
    {
        _colorRevision = ++_revision;
        _colors = lighting.ModeKeyColors;
    }

    public bool Changed(ProfileDto change, long now, bool allowed)
    {
        _profileRevision = ++_revision;
        var previous = _profile;
        _profile = change.Profile;
        if (change.Reason is "startup" or "resume")
        {
            _shownAt = null;
            return false;
        }
        bool explicitChange = change.Reason is "cycle" or "user" or "external";
        if (!allowed || (!explicitChange && (previous is null || previous == change.Profile)))
            return false;
        _shownAt = now;
        return true;
    }

    public byte Alpha(long now)
    {
        if (_shownAt is not { } start) return 0;
        long elapsed = Math.Max(0, now - start);
        if (elapsed < FadeInMs) return (byte)(255 * elapsed / FadeInMs);
        if (elapsed < FadeInMs + HoldMs) return 255;
        return (byte)Math.Clamp(255 * (FadeInMs + HoldMs + FadeOutMs - elapsed) / FadeOutMs, 0, 255);
    }

    public bool Finished(long now) => _shownAt is null || now - _shownAt >= FadeInMs + HoldMs + FadeOutMs;
    public static bool NotificationsAllowed(int shellState) => shellState is not (1 or 3 or 4);

    public static OsdBounds Place(OsdBounds work, uint dpi)
    {
        double scale = Math.Max(1, dpi) / 96.0;
        int width = Math.Min(work.Width, (int)Math.Round(CanvasWidth * scale));
        int height = Math.Min(work.Height, (int)Math.Round(CanvasHeight * scale));
        int top = Math.Clamp((int)Math.Round(work.Height / 3.0 - height / 2.0), 0, work.Height - height);
        return new(work.X + (work.Width - width) / 2, work.Y + top, width, height);
    }
}
