using NitroCustom.Core;
using NitroCustom.Core.Hardware;
using NitroCustom.Core.Ipc;

namespace NitroCustom.Core.Tests;

public sealed class OsdStateTests
{
    private static LightingDto Colors(string color) =>
        new(new KeyboardLighting(), new Dictionary<ThermalProfile, string> { [ThermalProfile.Turbo] = color });
    private static StateDto Snapshot(ThermalProfile profile, string color) =>
        new(profile, [], [], [], Colors(color));

    [Fact]
    public void InitialAndRestoreEventsStaySilentButExplicitSwitchRestartsFade()
    {
        var state = new OsdState();
        state.ApplySnapshot(Snapshot(ThermalProfile.Balanced, "#FF0000"), 0);
        Assert.False(state.Changed(new(ThermalProfile.Balanced), 0, true));
        Assert.True(state.Changed(new(ThermalProfile.Turbo, "cycle"), 1000, true));
        Assert.Equal(0, state.Alpha(1000));
        Assert.Equal(255, state.Alpha(1120));
        Assert.Equal(255, state.Alpha(2619));
        Assert.InRange(state.Alpha(2700), (byte)1, (byte)254);
        Assert.True(state.Changed(new(ThermalProfile.Quiet, "user"), 2700, true));
        Assert.False(state.Finished(2800));
        Assert.Equal(ProfileNames.Title(ThermalProfile.Quiet), state.Title);
        Assert.False(state.Changed(new(ThermalProfile.Quiet, "resume"), 2800, true));
        Assert.True(state.Finished(2800));
    }

    [Fact]
    public void SnapshotCannotOverwriteEventsButStillSuppliesUnchangedFields()
    {
        var state = new OsdState();
        state.Changed(new(ThermalProfile.Turbo, "cycle"), 1000, true);
        state.ApplySnapshot(Snapshot(ThermalProfile.Balanced, "#123456"), 0);
        Assert.Equal(ThermalProfile.Turbo, state.Profile);
        Assert.Equal(new Rgb(0x12, 0x34, 0x56), state.Color);
        state.BeginConnection();
        state.Lighting(Colors("#654321"));
        state.ApplySnapshot(Snapshot(ThermalProfile.Turbo, "#123456"), 0);
        Assert.Equal(ProfileNames.Title(ThermalProfile.Turbo), state.Title);
        Assert.Equal(new Rgb(0x65, 0x43, 0x21), state.Color);
        Assert.True(state.Finished(1000));
    }

    [Theory]
    [InlineData(96, 360, 96)]
    [InlineData(144, 540, 144)]
    [InlineData(192, 720, 192)]
    public void PlacesOnNegativeMonitorWithinWorkArea(uint dpi, int width, int height)
    {
        var bounds = OsdState.Place(new(-1920, -200, 1920, 1040), dpi);
        Assert.Equal(width, bounds.Width);
        Assert.Equal(height, bounds.Height);
        Assert.Equal(-1920 + (1920 - width) / 2, bounds.X);
        Assert.InRange(bounds.Y, -200, 840 - height);
        Assert.InRange(Math.Abs(bounds.Y + height / 2.0 - (-200 + 1040 / 3.0)), 0, 1);
    }

    [Fact]
    public void TinyWorkAreaClampsWindowAndSuppressedEventDoesNotReplay()
    {
        Assert.Equal(new OsdBounds(-20, 10, 50, 30), OsdState.Place(new(-20, 10, 50, 30), 144));
        var state = new OsdState();
        Assert.False(state.Changed(new(ThermalProfile.Turbo, "external"), 10, false));
        Assert.True(state.Finished(20));
        Assert.False(state.Changed(new(ThermalProfile.Turbo, "state"), 30, true));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(2, true)]
    [InlineData(5, true)]
    public void FiltersShellState(int shellState, bool allowed) =>
        Assert.Equal(allowed, OsdState.NotificationsAllowed(shellState));

    [Fact]
    public void OldProfileMessageStillDeserializes()
    {
        var value = System.Text.Json.JsonSerializer.Deserialize<ProfileDto>(
            """{"profile":"Turbo"}""", IpcProtocol.Json);
        Assert.NotNull(value);
        Assert.Null(value.Reason);
        Assert.Equal(ThermalProfile.Turbo, value.Profile);
    }
}
