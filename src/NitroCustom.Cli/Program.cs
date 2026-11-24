using System.Globalization;
using NitroCustom.Core.Hardware;
using NitroCustom.Core.Ipc;

// nitroctl: test tool. "direct" commands talk to the firmware (admin), "svc" commands talk to the service.
const string Usage = """
nitroctl - NitroCustom test tool

Direct firmware access (run as administrator, service NOT required):
  status                         profile, sensors, keyboard zones, raw states
  sensors [seconds]              live sensors (default 30 s)
  profile                        show current and supported profiles
  profile <Quiet|Balanced|Performance|Turbo|Eco>
  cycle                          next profile (Quiet > Balanced > Performance > Turbo)
  kb                             keyboard zone colours + raw backlight block
  kb zone <1|2|3|4|all|1,3> <#RRGGBB>
  kb raw-set <32 hex chars>      EXPERIMENTAL: write raw 16-byte backlight block
  kb sweep [max]                 EXPERIMENTAL: step through effect ids 0..max (default 9)
  events [seconds]               print firmware hotkey events (default 30 s)

Keyboard lighting over HID (no admin, no service):
  light info                     lighting targets reported by the device
  light static <#RRGGBB> [bright 0-100]
  light zones <#c1> [#c2] [#c3] [#c4] [bright]
  light modekey <#RRGGBB> [bright]
  light off
  light effect <name|code> [speed 1-9] [direction 1=right 2=left] [#RRGGBB] [bright]
                                 names: static breathing neon wave shifting zoom meteor twinkling
  light sweep [from] [to] [seconds]   show effect codes one by one (default 0..10, 5 s each)
  light raw <10 hex bytes>       send [A4 + bytes] as is

Thermal mode over the EC HID channel (no admin, no service; the way Acer's agent switches):
  ec                             mode count and current mode
  ec <Quiet|Balanced|Performance|Turbo|Eco>

Through the NitroCustom service (no admin needed):
  svc state | svc profile <name> | svc cycle | svc listen [seconds]
  svc light <effect> [#RRGGBB] [bright]   saved by the service, restored after reboot/sleep
  svc modekey <profile> <#RRGGBB>         mode-key colour for a profile
""";

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Console.WriteLine(Usage);
    return 0;
}

try
{
    if (args[0] == "svc") return await Svc(args[1..]);
    if (args[0] == "light") return Light(args[1..]);
    if (args[0] == "ec") return Ec(args[1..]);
    return Direct(args);
}
catch (Exception ex) when (ex is AcerWmiException or IpcException or LightingHidException or ArgumentException or FormatException)
{
    Console.Error.WriteLine("ERROR: " + ex.Message);
    return 1;
}

static int Ec(string[] args)
{
    using var hid = EcHid.Open();
    var ec = new EcMode(hid);
    Console.WriteLine($"Supported (4-5 modes): {ec.IsSupported()}");
    if (args.Length > 0)
    {
        var target = Enum.Parse<ThermalProfile>(args[0], ignoreCase: true);
        ec.Set(target);
        Thread.Sleep(100);
    }
    Console.WriteLine($"EC mode: {ec.Get()}");
    return 0;
}

static int Light(string[] args)
{
    using var hid = LightingHid.Open();
    var light = new LightingController(hid);
    static byte Bright(string[] a, int i) => a.Length > i ? byte.Parse(a[i], CultureInfo.InvariantCulture) : (byte)50;
    static byte Num(string[] a, int i, byte fallback) => a.Length > i ? byte.Parse(a[i], CultureInfo.InvariantCulture) : fallback;
    switch (args)
    {
        case ["info"]:
            Console.WriteLine($"Device: {hid.Path}");
            Console.WriteLine($"Targets: {string.Join(", ", light.GetTargets())}");
            return 0;
        case ["static", var color, ..]:
            light.SetStatic(Rgb.Parse(color), Bright(args, 2));
            break;
        case ["zones", ..] when args.Length >= 2:
            var colors = args[1..].TakeWhile(a => a.StartsWith('#')).Select(Rgb.Parse).ToList();
            light.SetZones(colors, Bright(args, 1 + colors.Count));
            break;
        case ["modekey", var color, ..]:
            light.SetModeKey(Rgb.Parse(color), Bright(args, 2));
            break;
        case ["off"]:
            light.Off();
            break;
        case ["effect", var code, ..]:
            var tint = args.Length > 4 ? Rgb.Parse(args[4]) : new Rgb(0, 0, 0);
            var effectCode = byte.TryParse(code, CultureInfo.InvariantCulture, out var n) ? n : (byte)Enum.Parse<LightingEffect>(code, true);
            light.Apply(new LightingCommand(LightingTarget.Keyboard, effectCode,
                Num(args, 5, 50), Num(args, 2, 5), Num(args, 3, LightingProtocol.DefaultDirection), tint, KeyboardZone.All));
            break;
        case ["sweep", ..]:
            var from = Num(args, 1, 0);
            var to = Num(args, 2, 10);
            var hold = TimeSpan.FromSeconds(Num(args, 3, 5));
            for (int effect = from; effect <= to; effect++)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} effect {effect}");
                light.Apply(new LightingCommand(LightingTarget.Keyboard, (byte)effect, 50, 5, LightingProtocol.DefaultDirection,
                    new Rgb(0xFF, 0x00, 0x00), KeyboardZone.All));
                Thread.Sleep(hold);
            }
            light.SetStatic(new Rgb(0xFF, 0xFF, 0xFF), 50);
            Console.WriteLine("Done, back to static white.");
            return 0;
        case ["raw", ..] when args.Length >= 2:
            var bytes = Convert.FromHexString(string.Concat(args[1..]).Replace(",", "").Replace(" ", ""));
            var report = new byte[LightingProtocol.ReportLength];
            report[0] = LightingProtocol.UpdateReport;
            bytes.AsSpan(0, Math.Min(bytes.Length, report.Length - 1)).CopyTo(report.AsSpan(1));
            hid.SetFeature(report);
            Console.WriteLine($"Sent {Convert.ToHexString(report)}");
            return 0;
        default:
            Console.Error.WriteLine("Unknown light command.\n");
            Console.WriteLine(Usage);
            return 2;
    }
    Console.WriteLine("OK");
    return 0;
}

static int Direct(string[] args)
{
    using var wmi = new AcerWmi();
    var hw = new NitroController(wmi);
    switch (args)
    {
        case ["status"]:
            PrintProfile(hw);
            PrintSensors(hw.ReadSensors());
            PrintZones(hw);
            Console.WriteLine($"Fan behavior raw: 0x{hw.GetFanBehaviorRaw():X}");
            return 0;
        case ["sensors", ..]:
            var until = DateTime.Now.AddSeconds(args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 30);
            while (DateTime.Now < until)
            {
                var line = string.Join(" | ", hw.ReadSensors().Select(r => $"{Short(r.Sensor)} {r.Value?.ToString() ?? "n/a"}"));
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} | {line} | {hw.GetProfile()}");
                Thread.Sleep(2000);
            }
            return 0;
        case ["profile"]:
            PrintProfile(hw);
            return 0;
        case ["profile", var name]:
            var p = ParseProfile(name);
            hw.SetProfile(p);
            Console.WriteLine($"Profile set and confirmed: {hw.GetProfile()}");
            return 0;
        case ["cycle"]:
            Console.WriteLine($"Profile now: {hw.CycleProfile([ThermalProfile.Quiet, ThermalProfile.Balanced, ThermalProfile.Performance, ThermalProfile.Turbo])}");
            return 0;
        case ["kb"]:
            PrintZones(hw);
            return 0;
        case ["kb", "zone", var zones, var color]:
            hw.SetZoneColor(ParseZones(zones), Rgb.Parse(color));
            Console.WriteLine("Zone colour set and confirmed.");
            PrintZones(hw);
            return 0;
        case ["kb", "raw-set", var hex]:
            var payload = Convert.FromHexString(hex.Replace(" ", ""));
            Console.WriteLine($"Before: {Convert.ToHexString(hw.GetKeyboardBacklight().Raw)}");
            Console.WriteLine($"Status: 0x{hw.SetKeyboardBacklightRaw(payload):X}");
            Console.WriteLine($"After:  {Convert.ToHexString(hw.GetKeyboardBacklight().Raw)}");
            return 0;
        case ["kb", "sweep", ..]:
            // Tries effect ids 0..max with a red base colour; the user watches the keyboard and notes each look.
            var max = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 9;
            Console.WriteLine($"Start: {Convert.ToHexString(hw.GetKeyboardBacklight().Raw)}");
            Console.WriteLine("Watch the keyboard. Press Enter for the next effect.\n");
            for (var mode = 0; mode <= max; mode++)
            {
                byte[] effect = [(byte)mode, 3, 100, 0, 1, 0xFF, 0x00, 0x00, 0, 1, 0, 0, 0, 0, 0, 0];
                var status = hw.SetKeyboardBacklightRaw(effect);
                Console.Write($"Effect {mode}: status 0x{status:X}, read back {Convert.ToHexString(hw.GetKeyboardBacklight().Raw)}  -> what do you see? [Enter]");
                Console.ReadLine();
            }
            return 0;
        case ["events", ..]:
            var seconds = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 30;
            using (var watcher = new HotkeyEventWatcher())
            {
                watcher.EventReceived += e => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {e}");
                watcher.Start();
                Console.WriteLine($"Listening {seconds} s (try Fn+F10)...");
                Thread.Sleep(TimeSpan.FromSeconds(seconds));
            }
            return 0;
        default:
            Console.Error.WriteLine("Unknown command.\n");
            Console.WriteLine(Usage);
            return 2;
    }
}

static async Task<int> Svc(string[] args)
{
    await using var client = new IpcClient();
    await client.ConnectAsync(TimeSpan.FromSeconds(3));
    switch (args)
    {
        case ["state"]:
            PrintState(await client.RequestAsync<StateDto>(IpcCommands.GetState));
            return 0;
        case ["profile", var name]:
            var r = await client.RequestAsync<ProfileDto>(IpcCommands.SetProfile, new SetProfileArgs(ParseProfile(name)));
            Console.WriteLine($"Profile: {r?.Profile}");
            return 0;
        case ["cycle"]:
            Console.WriteLine($"Profile: {(await client.RequestAsync<ProfileDto>(IpcCommands.CycleProfile))?.Profile}");
            return 0;
        case ["light", var effectName, ..]:
            var effect = Enum.Parse<LightingEffect>(effectName, true);
            var current = (await client.RequestAsync<StateDto>(IpcCommands.GetState))?.Lighting.Keyboard ?? new KeyboardLighting();
            var next = current with
            {
                Enabled = true,
                Effect = effect,
                Color = args.Length > 2 ? args[2] : current.Color,
                Brightness = args.Length > 3 ? byte.Parse(args[3], CultureInfo.InvariantCulture) : current.Brightness,
            };
            PrintLighting(await client.RequestAsync<LightingDto>(IpcCommands.SetLighting, next));
            return 0;
        case ["modekey", var name, var color]:
            PrintLighting(await client.RequestAsync<LightingDto>(IpcCommands.SetModeKeyColor, new SetModeKeyColorArgs(ParseProfile(name), color)));
            return 0;
        case ["listen", ..]:
            var seconds = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 30;
            client.EventReceived += (name, data) => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {name} {data}");
            Console.WriteLine($"Listening to service events {seconds} s...");
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            return 0;
        default:
            Console.Error.WriteLine("Unknown svc command.\n");
            Console.WriteLine(Usage);
            return 2;
    }
}

static void PrintProfile(NitroController hw) =>
    Console.WriteLine($"Profile: {hw.GetProfile()}   supported: {string.Join(", ", hw.GetSupportedProfiles())}");

static void PrintSensors(IEnumerable<SensorReading> readings)
{
    foreach (var r in readings) Console.WriteLine($"  {r.Sensor,-20} {r.Value?.ToString() ?? "n/a"}");
}

static void PrintZones(NitroController hw)
{
    Console.WriteLine("Keyboard zones: " + string.Join("  ", hw.GetZoneColors().Select(kv => $"{kv.Key}={kv.Value?.ToString() ?? "n/a"}")));
    var kb = hw.GetKeyboardBacklight();
    Console.WriteLine($"Backlight raw: {Convert.ToHexString(kb.Raw)}  (effect {kb.Effect}, speed {kb.Speed}, brightness {kb.Brightness}, dir {kb.Direction}, color {kb.Color})");
}

static void PrintState(StateDto? s)
{
    if (s is null) { Console.WriteLine("(no data)"); return; }
    Console.WriteLine($"Profile: {s.Profile}   supported: {string.Join(", ", s.SupportedProfiles)}   cycle: {string.Join(" > ", s.CycleOrder)}");
    foreach (var x in s.Sensors) Console.WriteLine($"  {x.Sensor,-20} {x.Value?.ToString() ?? "n/a"}");
    PrintLighting(s.Lighting);
}

static void PrintLighting(LightingDto? l)
{
    if (l is null) return;
    var k = l.Keyboard;
    Console.WriteLine($"Keyboard: {(k.Enabled ? "on" : "off")} {k.Effect} {k.Color} bright {k.Brightness} speed {k.Speed} {k.Direction}  zones {string.Join(",", k.ZoneColors)}");
    Console.WriteLine("Mode key: " + string.Join("  ", l.ModeKeyColors.Select(kv => $"{kv.Key}={kv.Value}")));
}

static string Short(Sensor s) => s switch
{
    Sensor.CpuTemperature => "CPU °C",
    Sensor.CpuFanSpeed => "CPU fan",
    Sensor.SystemTemperature => "Sys °C",
    Sensor.GpuFanSpeed => "GPU fan",
    Sensor.GpuTemperature => "GPU °C",
    _ => s.ToString(),
};

static ThermalProfile ParseProfile(string name) =>
    Enum.TryParse<ThermalProfile>(name, ignoreCase: true, out var p) && Enum.IsDefined(p)
        ? p
        : throw new ArgumentException($"Unknown profile '{name}'. Use Quiet, Balanced, Performance, Turbo or Eco.");

static KeyboardZone ParseZones(string text)
{
    if (text.Equals("all", StringComparison.OrdinalIgnoreCase)) return KeyboardZone.All;
    var mask = KeyboardZone.None;
    foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        mask |= part switch
        {
            "1" => KeyboardZone.Zone1,
            "2" => KeyboardZone.Zone2,
            "3" => KeyboardZone.Zone3,
            "4" => KeyboardZone.Zone4,
            _ => throw new ArgumentException($"Unknown zone '{part}'. Use 1..4, all, or a list like 1,3."),
        };
    }
    return mask;
}
