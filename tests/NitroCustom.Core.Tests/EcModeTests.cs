using NitroCustom.Core.Hardware;

namespace NitroCustom.Core.Tests;

public class EcModeTests
{
    /// <summary>Emulates the EC collection: 5 modes, replies as captured on ANV16S-41.</summary>
    private sealed class FakeEc : ILightingDevice
    {
        public byte Mode = 3;
        public int ModeCount = 5;
        public bool IgnoreSets;
        public int Sets;
        private byte[] _reply = new byte[EcProtocol.ReportLength];

        public void SetFeature(byte[] r)
        {
            Assert.Equal((byte)0xA0, r[0]);
            Assert.Equal((byte)0xA0, r[2]);
            var command = (ushort)(r[3] | r[4] << 8);
            _reply = new byte[EcProtocol.ReportLength];
            _reply[0] = 0xA0;
            _reply[2] = 0xE0;
            _reply[3] = r[3];
            _reply[4] = r[4];
            switch (command, r[5])
            {
                case (EcProtocol.CommandStatus, EcProtocol.FunctionModeCapability):
                    _reply[5] = 5; _reply[6] = 0xFF; _reply[7] = (byte)ModeCount;
                    break;
                case (EcProtocol.CommandMode, EcProtocol.FunctionGet):
                    _reply[5] = Mode;
                    break;
                case (EcProtocol.CommandMode, EcProtocol.FunctionSet):
                    Sets++;
                    if (!IgnoreSets) Mode = r[6];
                    _reply[5] = 0xFF;
                    break;
            }
        }

        public byte[] GetFeature(byte reportId) => _reply;
    }

    /// <summary>WMI that only answers the supported-profiles query and fails on any profile access.</summary>
    private sealed class ProfilesOnlyWmi : IAcerWmi
    {
        public int ProfileCalls;
        public ulong Get(string method, uint input)
        {
            if (input == AcerProtocol.MiscSupportedProfiles) return 0x7300;
            ProfileCalls++;
            return 1;
        }
        public uint Set(string method, ulong input) { ProfileCalls++; return 1; }
        public (byte[] Output, byte Return) GetBytes(string method, uint input) => (new byte[15], 0);
        public uint SetBytes(string method, byte[] input) => 0;
    }

    [Fact]
    public void Encode_matches_captured_mode_set()
    {
        var r = EcProtocol.Encode(EcProtocol.CommandMode, EcProtocol.FunctionSet, 2);
        Assert.Equal(EcProtocol.ReportLength, r.Length);
        Assert.Equal(new byte[] { 0xA0, 0x00, 0xA0, 0x01, 0x00, 0x01, 0x02, 0x00 }, r[..8]);
    }

    [Theory]
    [InlineData(ThermalProfile.Turbo, 0)]
    [InlineData(ThermalProfile.Performance, 1)]
    [InlineData(ThermalProfile.Balanced, 2)]
    [InlineData(ThermalProfile.Quiet, 3)]
    [InlineData(ThermalProfile.Eco, 4)]
    public void Mode_values_round_trip(ThermalProfile profile, byte value)
    {
        Assert.Equal(value, EcProtocol.ModeValue(profile));
        Assert.Equal(profile, EcProtocol.ProfileFromValue(value));
    }

    [Fact]
    public void Controller_switches_and_reads_through_ec_only()
    {
        var ec = new FakeEc();
        var wmi = new ProfilesOnlyWmi();
        var c = new NitroController(wmi, new EcMode(ec));
        Assert.Equal(ThermalProfile.Quiet, c.GetProfile());
        c.SetProfile(ThermalProfile.Turbo);
        Assert.Equal(0, ec.Mode);
        Assert.Equal(ThermalProfile.Turbo, c.GetProfile());
        Assert.Equal(1, ec.Sets);
        Assert.Equal(0, wmi.ProfileCalls);
        Assert.True(c.UsesEc);
    }

    [Fact]
    public void Controller_reports_when_ec_ignores_the_switch()
    {
        var c = new NitroController(new ProfilesOnlyWmi(), new EcMode(new FakeEc { IgnoreSets = true }));
        var ex = Assert.Throws<AcerWmiException>(() => c.SetProfile(ThermalProfile.Balanced));
        Assert.Contains("not applied", ex.Message);
    }

    [Fact]
    public void Controller_falls_back_to_wmi_for_unknown_mode_count()
    {
        var wmi = new ProfilesOnlyWmi();
        var c = new NitroController(wmi, new EcMode(new FakeEc { ModeCount = 3 }));
        Assert.False(c.UsesEc);
        Assert.Throws<AcerWmiException>(() => c.GetProfile());
        Assert.True(wmi.ProfileCalls > 0);
    }
}
