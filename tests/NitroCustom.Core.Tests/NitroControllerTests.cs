using NitroCustom.Core.Hardware;

namespace NitroCustom.Core.Tests;

public class NitroControllerTests
{
    /// <summary>Minimal firmware emulation: profile + zone colours, supported masks as on ANV16S-41.</summary>
    private sealed class FakeWmi : IAcerWmi
    {
        public byte Profile = (byte)ThermalProfile.Quiet;
        public readonly Dictionary<byte, Rgb> Zones = new() { [1] = default, [2] = default, [4] = default, [8] = default };
        public bool RejectWrites;
        public bool IgnoreWrites;

        public ulong Get(string method, uint input) => (method, input) switch
        {
            (AcerProtocol.GetMisc, AcerProtocol.MiscSupportedProfiles) => 0x7300,
            (AcerProtocol.GetMisc, AcerProtocol.MiscPlatformProfile) => (ulong)Profile << 8,
            (AcerProtocol.GetSysInfo, AcerProtocol.SysInfoSupportedSensors) => 0x227000000,
            (AcerProtocol.GetSysInfo, _) => 0x2A00,
            (AcerProtocol.GetRgbKb, _) when Zones.TryGetValue((byte)input, out var c) => AcerProtocol.EncodeZoneColor(KeyboardZone.None, c),
            (AcerProtocol.GetRgbKb, _) => 0x1,
            _ => 0x1,
        };

        public uint Set(string method, ulong input)
        {
            if (RejectWrites) return 1;
            if (IgnoreWrites) return 0;
            switch (method)
            {
                case AcerProtocol.SetMisc when (input & 0xFF) == AcerProtocol.MiscPlatformProfile:
                    Profile = (byte)(input >> 8);
                    return 0;
                case AcerProtocol.SetRgbKb:
                    var color = new Rgb((byte)(input >> 8), (byte)(input >> 16), (byte)(input >> 24));
                    foreach (var z in Zones.Keys.ToList())
                        if ((input & z) != 0) Zones[z] = color;
                    return 0;
                default:
                    return 1;
            }
        }

        public (byte[] Output, byte Return) GetBytes(string method, uint input) => (new byte[15], 0);
        public uint SetBytes(string method, byte[] input) => 0;
    }

    [Fact]
    public void SetProfile_AppliesAndVerifies()
    {
        var wmi = new FakeWmi();
        new NitroController(wmi).SetProfile(ThermalProfile.Performance);
        Assert.Equal((byte)ThermalProfile.Performance, wmi.Profile);
    }

    [Fact]
    public void SetProfile_ThrowsWhenFirmwareRejects()
    {
        var c = new NitroController(new FakeWmi { RejectWrites = true });
        Assert.Throws<AcerWmiException>(() => c.SetProfile(ThermalProfile.Turbo));
    }

    [Fact]
    public void SetProfile_ThrowsWhenReadBackDiffers()
    {
        var c = new NitroController(new FakeWmi { IgnoreWrites = true });
        var ex = Assert.Throws<AcerWmiException>(() => c.SetProfile(ThermalProfile.Balanced));
        Assert.Contains("not applied", ex.Message);
    }

    [Fact]
    public void CycleProfile_FollowsOrderAndWraps()
    {
        var wmi = new FakeWmi();
        var c = new NitroController(wmi);
        ThermalProfile[] order = [ThermalProfile.Quiet, ThermalProfile.Balanced, ThermalProfile.Performance];
        Assert.Equal(ThermalProfile.Balanced, c.CycleProfile(order));
        Assert.Equal(ThermalProfile.Performance, c.CycleProfile(order));
        Assert.Equal(ThermalProfile.Quiet, c.CycleProfile(order));
    }

    [Fact]
    public void CycleProfile_StartsFromFirstWhenCurrentNotInOrder()
    {
        var wmi = new FakeWmi { Profile = (byte)ThermalProfile.Eco };
        Assert.Equal(ThermalProfile.Quiet, new NitroController(wmi).CycleProfile([ThermalProfile.Quiet, ThermalProfile.Turbo]));
    }

    [Fact]
    public void ReadSensors_ReturnsSupportedOnly()
    {
        var readings = new NitroController(new FakeWmi()).ReadSensors();
        Assert.Equal(5, readings.Count);
        Assert.All(readings, r => Assert.Equal(42, r.Value));
    }

    [Fact]
    public void SetZoneColor_SetsOnlyMaskedZones()
    {
        var wmi = new FakeWmi();
        var c = new NitroController(wmi);
        c.SetZoneColor(KeyboardZone.Zone1 | KeyboardZone.Zone4, new Rgb(1, 2, 3));
        var zones = c.GetZoneColors();
        Assert.Equal(new Rgb(1, 2, 3), zones[KeyboardZone.Zone1]);
        Assert.Equal(default(Rgb), zones[KeyboardZone.Zone2]);
        Assert.Equal(new Rgb(1, 2, 3), zones[KeyboardZone.Zone4]);
    }

    [Fact]
    public void SetZoneColor_RejectsBadMask()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NitroController(new FakeWmi()).SetZoneColor((KeyboardZone)0x10, default));
    }
}
