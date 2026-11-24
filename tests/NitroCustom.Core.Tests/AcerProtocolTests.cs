using NitroCustom.Core.Hardware;

namespace NitroCustom.Core.Tests;

/// <summary>Raw values below were captured on the real ANV16S-41 (BIOS V1.12) on 2026-11-24.</summary>
public class AcerProtocolTests
{
    [Fact]
    public void SupportedSensors_MatchCapturedMask()
    {
        var sensors = AcerProtocol.DecodeSupportedSensors(0x227000000);
        Assert.Equal(
            [Sensor.CpuTemperature, Sensor.CpuFanSpeed, Sensor.SystemTemperature, Sensor.GpuFanSpeed, Sensor.GpuTemperature],
            sensors);
    }

    [Theory]
    [InlineData(0x2800UL, 40)]
    [InlineData(0x72C00UL, 1836)]
    [InlineData(0x2C00UL, 44)]
    public void SensorReading_Decodes(ulong raw, int expected)
    {
        Assert.True(AcerProtocol.TryDecodeSensor(raw, out var value));
        Assert.Equal(expected, value);
    }

    [Fact]
    public void SensorReading_FailsOnStatus()
    {
        Assert.False(AcerProtocol.TryDecodeSensor(0x10000000001, out _));
    }

    [Fact]
    public void SensorQuery_UsesCommand01()
    {
        Assert.Equal(0x0201u, AcerProtocol.EncodeSensorQuery(Sensor.CpuFanSpeed));
        Assert.Equal(0x0A01u, AcerProtocol.EncodeSensorQuery(Sensor.GpuTemperature));
    }

    [Fact]
    public void SupportedProfiles_MatchCapturedMask()
    {
        Assert.True(AcerProtocol.TryDecodeMisc(0x7300, out var mask));
        Assert.Equal(
            [ThermalProfile.Quiet, ThermalProfile.Balanced, ThermalProfile.Performance, ThermalProfile.Turbo, ThermalProfile.Eco],
            AcerProtocol.DecodeSupportedProfiles(mask));
    }

    [Theory]
    [InlineData(0x0UL, ThermalProfile.Quiet)]
    [InlineData(0x100UL, ThermalProfile.Balanced)]
    public void CurrentProfile_Decodes(ulong raw, ThermalProfile expected)
    {
        Assert.True(AcerProtocol.TryDecodeMisc(raw, out var id));
        Assert.Equal(expected, (ThermalProfile)id);
    }

    [Fact]
    public void SetProfile_EncodingMatchesVerifiedScript()
    {
        // tools/acer_set_profile.ps1 sent 0x0B | (1 << 8) and the firmware confirmed Balanced.
        Assert.Equal(0x10BUL, AcerProtocol.EncodeMisc(AcerProtocol.MiscPlatformProfile, (byte)ThermalProfile.Balanced));
    }

    [Fact]
    public void ZoneColor_DecodesCapturedValue()
    {
        Assert.True(AcerProtocol.TryDecodeZoneColor(0x329000, out var c));
        Assert.Equal(new Rgb(0x90, 0x32, 0x00), c);
    }

    [Fact]
    public void ZoneColor_EncodingIsSymmetricToGet()
    {
        Assert.Equal(0x0032900FUL, AcerProtocol.EncodeZoneColor(KeyboardZone.All, new Rgb(0x90, 0x32, 0x00)));
    }

    [Theory]
    [InlineData(new byte[] { 0x01, 0x82, 0x00, 0, 0, 0, 0, 0 }, false)]
    [InlineData(new byte[] { 0x01, 0x82, 0x01, 0, 0, 0, 0, 0 }, true)]
    public void TouchpadEvent_Decodes(byte[] detail, bool enabled)
    {
        Assert.Equal(new HotkeyEvent.TouchpadToggled(enabled), AcerProtocol.DecodeEvent(detail));
    }

    [Fact]
    public void UnknownEvent_IsKept()
    {
        Assert.IsType<HotkeyEvent.Unknown>(AcerProtocol.DecodeEvent([0x02, 0x10]));
    }

    [Theory]
    [InlineData("#903200", 0x90, 0x32, 0x00)]
    [InlineData("ff0080", 0xFF, 0x00, 0x80)]
    public void Rgb_Parses(string text, byte r, byte g, byte b)
    {
        Assert.Equal(new Rgb(r, g, b), Rgb.Parse(text));
    }

    [Fact]
    public void Rgb_RejectsGarbage()
    {
        Assert.Throws<FormatException>(() => Rgb.Parse("#12345"));
    }
}
