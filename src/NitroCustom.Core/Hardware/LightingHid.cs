using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NitroCustom.Core.Hardware;

public sealed class LightingHidException(string message) : Exception(message);

/// <summary>Feature-report transport to the lighting device (abstracted for tests).</summary>
public interface ILightingDevice
{
    byte[] GetFeature(byte reportId);
    void SetFeature(byte[] report);
}

/// <summary>Opens the ENE lighting HID collection and exchanges feature reports. No admin rights needed.</summary>
public sealed class LightingHid : ILightingDevice, IDisposable
{
    private readonly SafeFileHandle _handle;
    private readonly Lock _lock = new();

    public string Path { get; }

    private LightingHid(string path, SafeFileHandle handle)
    {
        Path = path;
        _handle = handle;
    }

    public static LightingHid Open()
    {
        var path = FindDevicePath(LightingProtocol.DeviceMatch)
                   ?? throw new LightingHidException("Lighting device (ENEK5130) not found.");
        var handle = Native.CreateFile(path, Native.GenericRead | Native.GenericWrite, Native.FileShareReadWrite,
            IntPtr.Zero, Native.OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
            throw new LightingHidException($"Cannot open lighting device (Win32 error {Marshal.GetLastPInvokeError()}).");
        return new LightingHid(path, handle);
    }

    public byte[] GetFeature(byte reportId)
    {
        var buffer = new byte[LightingProtocol.ReportLength];
        buffer[0] = reportId;
        lock (_lock)
        {
            if (!Native.HidD_GetFeature(_handle, buffer, buffer.Length))
                throw new LightingHidException($"GetFeature 0x{reportId:X2} failed (Win32 error {Marshal.GetLastPInvokeError()}).");
        }
        return buffer;
    }

    public void SetFeature(byte[] report)
    {
        if (report.Length != LightingProtocol.ReportLength)
            throw new ArgumentException($"Report must be {LightingProtocol.ReportLength} bytes.", nameof(report));
        lock (_lock)
        {
            if (!Native.HidD_SetFeature(_handle, report, report.Length))
                throw new LightingHidException($"SetFeature 0x{report[0]:X2} failed (Win32 error {Marshal.GetLastPInvokeError()}).");
        }
    }

    public void Dispose() => _handle.Dispose();

    private static string? FindDevicePath(string match) => HidPaths.Find(match).FirstOrDefault();

    private static class Native
    {
        public const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, FileShareReadWrite = 3, OpenExisting = 3;

        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetFeature(SafeFileHandle h, byte[] buffer, int length);
        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_SetFeature(SafeFileHandle h, byte[] buffer, int length);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);
    }
}

/// <summary>Enumerates present HID interface paths containing a substring.</summary>
internal static class HidPaths
{
    public static List<string> Find(string match)
    {
        var result = new List<string>();
        Native.HidD_GetHidGuid(out var guid);
        var set = Native.SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, Native.DigcfPresent | Native.DigcfDeviceInterface);
        if (set == new IntPtr(-1)) return result;
        try
        {
            for (var i = 0; ; i++)
            {
                var data = new Native.SpDeviceInterfaceData { cbSize = Marshal.SizeOf<Native.SpDeviceInterfaceData>() };
                if (!Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data)) return result;
                Native.SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out var required, IntPtr.Zero);
                var buffer = Marshal.AllocHGlobal(required);
                try
                {
                    Marshal.WriteInt32(buffer, 8); // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W on x64
                    if (!Native.SetupDiGetDeviceInterfaceDetail(set, ref data, buffer, required, out _, IntPtr.Zero)) continue;
                    var path = Marshal.PtrToStringUni(buffer + 4);
                    if (path != null && path.Contains(match, StringComparison.OrdinalIgnoreCase)) result.Add(path);
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }
        finally { Native.SetupDiDestroyDeviceInfoList(set); }
    }

    private static class Native
    {
        public const int DigcfPresent = 0x02, DigcfDeviceInterface = 0x10;

        [StructLayout(LayoutKind.Sequential)]
        public struct SpDeviceInterfaceData
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [DllImport("hid.dll")] public static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("setupapi.dll", SetLastError = true)] public static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr parent, int flags);
        [DllImport("setupapi.dll", SetLastError = true)] public static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, int index, ref SpDeviceInterfaceData data);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)] public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SpDeviceInterfaceData data, IntPtr detail, int size, out int required, IntPtr info);
        [DllImport("setupapi.dll")] public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    }
}

/// <summary>
/// Long-lived device for the service: opens the HID handle on first use and reopens it once when a call
/// fails (the handle goes stale after sleep or a driver restart).
/// </summary>
public sealed class ReconnectingLightingDevice(Func<ILightingDevice> open) : ILightingDevice, IDisposable
{
    private readonly Lock _lock = new();
    private ILightingDevice? _device;

    public ReconnectingLightingDevice() : this(LightingHid.Open) { }

    public byte[] GetFeature(byte reportId) => Run(d => d.GetFeature(reportId));

    public void SetFeature(byte[] report) => Run(d => { d.SetFeature(report); return 0; });

    private T Run<T>(Func<ILightingDevice, T> call)
    {
        lock (_lock)
        {
            try
            {
                return call(_device ??= open());
            }
            catch (LightingHidException)
            {
                Reset();
                return call(_device = open());
            }
        }
    }

    private void Reset()
    {
        (_device as IDisposable)?.Dispose();
        _device = null;
    }

    public void Dispose()
    {
        lock (_lock) Reset();
    }
}

/// <summary>High-level lighting operations on top of <see cref="ILightingDevice"/>.</summary>
public sealed class LightingController(ILightingDevice device)
{
    public IReadOnlyList<LightingTarget> GetTargets() =>
        LightingProtocol.DecodeDeviceList(device.GetFeature(LightingProtocol.DeviceListReport));

    public void Apply(LightingCommand command) => device.SetFeature(LightingProtocol.EncodeUpdate(command));

    public void SetStatic(Rgb color, byte brightness, KeyboardZone zones = KeyboardZone.All) =>
        Apply(LightingProtocol.Static(color, brightness, zones));

    public void SetZones(IReadOnlyList<Rgb> colors, byte brightness)
    {
        foreach (var command in LightingProtocol.Zones(colors, brightness)) Apply(command);
    }

    public void SetModeKey(Rgb color, byte brightness) => Apply(LightingProtocol.ModeKey(color, brightness));

    public void Apply(KeyboardLighting lighting)
    {
        foreach (var command in LightingProtocol.Build(lighting)) Apply(command);
    }

    public void Off() => SetStatic(new Rgb(0, 0, 0), 0);
}
