using NitroCustom.Core.Hardware;

namespace NitroCustom.Core.Tests;

public class LightingProtocolTests
{
    private static string Hex(byte[] b) => Convert.ToHexString(b);

    [Fact]
    public void Static_red_matches_acer_log()
    {
        // Acer log: SetLightingDeviceUpdateReport | 21,02,32,00,02,FF,00,00,0F,00
        var report = LightingProtocol.EncodeUpdate(LightingProtocol.Static(new Rgb(0xFF, 0, 0), 50));
        Assert.Equal("A421023200 02FF00000F00".Replace(" ", ""), Hex(report));
    }

    [Fact]
    public void Zones_emit_one_report_per_zone_with_direction_zero()
    {
        // Acer log: zone 04 = 21,02,32,00,00,00,00,FF,04,00 and zone 08 = 21,02,32,00,00,FF,FF,FF,08,00
        var reports = LightingProtocol
            .Zones([new Rgb(0xFF, 0, 0), new Rgb(0, 0xFF, 0), new Rgb(0, 0, 0xFF), new Rgb(0xFF, 0xFF, 0xFF)], 50)
            .Select(c => Hex(LightingProtocol.EncodeUpdate(c)))
            .ToList();
        Assert.Equal(
            ["A42102320000FF00000100", "A4210232000000FF000200", "A421023200000000FF0400", "A42102320000FFFFFF0800"],
            reports);
    }

    [Fact]
    public void Mode_key_matches_acer_log()
    {
        // Acer log: 65,02,32,00,00,00,FF,00,00,00
        var report = LightingProtocol.EncodeUpdate(LightingProtocol.ModeKey(new Rgb(0, 0xFF, 0), 50));
        Assert.Equal("A4650232000000FF000000", Hex(report));
    }

    [Fact]
    public void Wave_matches_acer_log()
    {
        // Acer log: 21,07,32,05,02,00,00,00,0F,00
        var c = LightingProtocol.Effect(LightingEffect.Wave, 50, 5);
        Assert.Equal("A421073205020000000F00", Hex(LightingProtocol.EncodeUpdate(c)));
    }

    [Fact]
    public void Effect_speed_is_clamped_to_1_9()
    {
        Assert.Equal(1, LightingProtocol.EncodeUpdate(LightingProtocol.Effect(LightingEffect.Breathing, 50, 0))[4]);
        Assert.Equal(9, LightingProtocol.EncodeUpdate(LightingProtocol.Effect(LightingEffect.Breathing, 50, 20))[4]);
    }

    [Fact]
    public void Brightness_is_clamped_to_100()
    {
        var report = LightingProtocol.EncodeUpdate(LightingProtocol.Static(new Rgb(1, 2, 3), 250));
        Assert.Equal(100, report[3]);
    }

    [Fact]
    public void Device_list_decodes_targets()
    {
        byte[] a1 = [0xA1, 0x02, 0x65, 0x21, 0, 0, 0, 0, 0, 0, 0];
        Assert.Equal([LightingTarget.ModeKey, LightingTarget.Keyboard], LightingProtocol.DecodeDeviceList(a1));
    }

    [Fact]
    public void Controller_sends_through_device()
    {
        var fake = new FakeLighting();
        new LightingController(fake).Off();
        Assert.Equal("A42102000002000000 0F00".Replace(" ", ""), Hex(Assert.Single(fake.Sent)));
    }

    [Fact]
    public void Build_static_direct_and_effect()
    {
        var baseline = new KeyboardLighting { Color = "#00FF00", Brightness = 40 };
        Assert.Equal("A42102280002" + "00FF00" + "0F00", Hex(LightingProtocol.EncodeUpdate(Assert.Single(LightingProtocol.Build(baseline)))));

        var direct = baseline with { Effect = LightingEffect.Direct, ZoneColors = ["#FF0000", "#00FF00", "#0000FF", "#FFFFFF"] };
        Assert.Equal(4, LightingProtocol.Build(direct).Count);

        var neon = baseline with { Effect = LightingEffect.Neon, Speed = 7, Direction = LightingDirection.Right };
        Assert.Equal("A4210528070100FF000F00", Hex(LightingProtocol.EncodeUpdate(Assert.Single(LightingProtocol.Build(neon)))));
    }

    [Fact]
    public void Build_disabled_sends_zero_brightness()
    {
        var off = new KeyboardLighting { Enabled = false, Effect = LightingEffect.Wave, Brightness = 80 };
        var command = Assert.Single(LightingProtocol.Build(off));
        Assert.Equal((byte)LightingEffect.Static, command.Effect);
        Assert.Equal(0, LightingProtocol.EncodeUpdate(command)[3]);
    }

    [Fact]
    public void Validated_rejects_bad_input_and_clamps()
    {
        Assert.Throws<FormatException>(() => new KeyboardLighting { Color = "red" }.Validated());
        Assert.Throws<ArgumentException>(() => new KeyboardLighting { ZoneColors = ["#000000"] }.Validated());
        Assert.Throws<ArgumentException>(() => new KeyboardLighting { Effect = LightingEffect.OperatingMode }.Validated());
        var v = new KeyboardLighting { Brightness = 200, Speed = 0 }.Validated();
        Assert.Equal((100, 1), (v.Brightness, v.Speed));
    }

    [Fact]
    public void Reconnecting_device_reopens_once_after_failure()
    {
        var opened = 0;
        var first = new FakeLighting { FailNext = true };
        var second = new FakeLighting();
        using var device = new ReconnectingLightingDevice(() => ++opened == 1 ? first : second);
        device.SetFeature(new byte[LightingProtocol.ReportLength]);
        Assert.Equal(2, opened);
        Assert.Empty(first.Sent);
        Assert.Single(second.Sent);
    }

    private sealed class FakeLighting : ILightingDevice
    {
        public List<byte[]> Sent { get; } = [];
        public byte[] GetFeature(byte reportId) => [reportId, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        public bool FailNext { get; set; }
        public void SetFeature(byte[] report)
        {
            if (FailNext) { FailNext = false; throw new LightingHidException("stale handle"); }
            Sent.Add(report);
        }
    }
}
