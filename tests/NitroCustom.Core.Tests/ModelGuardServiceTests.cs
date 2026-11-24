using Microsoft.Extensions.Logging.Abstractions;
using NitroCustom.Core.Hardware;
using NitroCustom.Core.Ipc;
using NitroCustom.Service;
using Xunit;

namespace NitroCustom.Core.Tests;

/// <summary>The service must not write anything to the firmware on a laptop model that was not verified.</summary>
public sealed class ModelGuardServiceTests : IDisposable
{
    private static readonly ModelInfo Verified = new("Acer", "Nitro ANV16S-41", "V1.12");
    private static readonly ModelInfo Unverified = new("Acer", "Nitro ANV16-71", "V1.00");

    private sealed class CountingWmi : IAcerWmi
    {
        public int Writes;

        public ulong Get(string method, uint input) => (method, input) switch
        {
            (AcerProtocol.GetMisc, AcerProtocol.MiscSupportedProfiles) => 0x7300,
            (AcerProtocol.GetMisc, AcerProtocol.MiscPlatformProfile) => (ulong)ThermalProfile.Balanced << 8,
            _ => 0x1,
        };

        public uint Set(string method, ulong input) { Writes++; return 0; }
        public (byte[] Output, byte Return) GetBytes(string method, uint input) => (new byte[15], 0);
        public uint SetBytes(string method, byte[] input) { Writes++; return 0; }
    }

    private sealed class CountingDevice : ILightingDevice
    {
        public int Writes;
        public byte[] GetFeature(byte reportId) => new byte[16];
        public void SetFeature(byte[] report) => Writes++;
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nitrocustom-test-" + Guid.NewGuid().ToString("N"));
    private readonly CountingWmi _wmi = new();
    private readonly CountingDevice _device = new();

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private SettingsStore NewStore() => new(NullLogger<SettingsStore>.Instance, _dir);

    private NitroService NewService(ModelInfo model, SettingsStore store) =>
        new(new NitroController(_wmi), new LightingController(_device), store, new EventHub(), model,
            NullLogger<NitroService>.Instance);

    [Fact]
    public void UnverifiedModelRefusesEveryWriteAndWritesNothing()
    {
        var store = NewStore();
        // A saved profile and lighting that would be re-applied at startup on a verified model.
        store.Update(s => s with { Profile = ThermalProfile.Turbo, Lighting = new KeyboardLighting() });
        var service = NewService(Unverified, store);

        Assert.False(service.WritesAllowed);
        Assert.Throws<IpcException>(() => service.SetProfile(ThermalProfile.Turbo));
        Assert.Throws<IpcException>(() => service.CycleProfile());
        Assert.Throws<IpcException>(() => service.SetLighting(new KeyboardLighting()));
        Assert.Throws<IpcException>(() => service.SetModeKeyColor(ThermalProfile.Turbo, new Rgb(1, 2, 3)));
        service.ApplySaved("startup");
        service.ApplySaved("resume");
        service.PollProfile();

        Assert.Equal(0, _wmi.Writes);
        Assert.Equal(0, _device.Writes);
        var model = service.GetModel();
        Assert.False(model.Verified);
        Assert.False(model.WritesAllowed);
    }

    [Fact]
    public void ConsentAllowsWritesIsRememberedAndCanBeWithdrawn()
    {
        var store = NewStore();
        var service = NewService(Unverified, store);

        Assert.True(service.SetUnverifiedConsent(true).WritesAllowed);
        service.SetLighting(new KeyboardLighting());
        Assert.True(_device.Writes > 0);
        Assert.True(NewService(Unverified, NewStore()).WritesAllowed); // survives a service restart

        Assert.False(service.SetUnverifiedConsent(false).WritesAllowed);
        var before = _device.Writes;
        Assert.Throws<IpcException>(() => service.SetLighting(new KeyboardLighting()));
        Assert.Equal(before, _device.Writes);
    }

    [Fact]
    public void VerifiedModelAlwaysWritesAndIgnoresConsent()
    {
        var service = NewService(Verified, NewStore());

        Assert.True(service.WritesAllowed);
        Assert.True(service.GetModel().Verified);
        Assert.True(service.SetUnverifiedConsent(false).WritesAllowed);
        service.SetLighting(new KeyboardLighting());
        Assert.True(_device.Writes > 0);
    }
}
