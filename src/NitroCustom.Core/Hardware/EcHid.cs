using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NitroCustom.Core.Hardware;

/// <summary>
/// Acer EC "feature" channel on HID 1025:174B, collection with usage page FF05 / usage 1, report A0 (65 bytes).
/// This is how Acer's own agent switches the thermal mode: one HID Mode report, after which the firmware plays
/// the mode's own acknowledgement colour. Writing WMI misc 0x0B as well makes it acknowledge a second time.
/// Layout from nitrous (MIT), checked on ANV16S-41: request A0 00 A0 cmdLo cmdHi function payload...,
/// reply A0 statusLo statusHi cmdLo cmdHi data...
/// </summary>
public static class EcProtocol
{
    public const byte ReportId = 0xA0;
    public const int ReportLength = 65;
    public const ushort UsagePage = 0xFF05;
    public const string DeviceMatch = "1025174b";

    public const ushort CommandStatus = 0x0000, CommandMode = 0x0001;
    public const byte FunctionSet = 1, FunctionGet = 2, FunctionModeCapability = 5;
    public const ushort StatusDone = 0xE000;

    public static byte[] Encode(ushort command, byte function, params byte[] payload)
    {
        if (payload.Length > ReportLength - 6) throw new ArgumentException("Payload too long.", nameof(payload));
        var report = new byte[ReportLength];
        report[0] = ReportId;
        report[1] = 0x00;
        report[2] = 0xA0;
        report[3] = (byte)command;
        report[4] = (byte)(command >> 8);
        report[5] = function;
        payload.CopyTo(report, 6);
        return report;
    }

    public static ushort Status(byte[] reply) => (ushort)(reply[1] | reply[2] << 8);

    public static ushort Command(byte[] reply) => (ushort)(reply[3] | reply[4] << 8);

    /// <summary>Mode capability: echoed function byte at 5, count (little-endian) at 7..8.</summary>
    public static int DecodeModeCount(byte[] reply) => reply[7] | reply[8] << 8;

    /// <summary>EC mode value, counted from the hottest mode (checked on hardware for 5 modes).</summary>
    public static byte ModeValue(ThermalProfile profile) => profile switch
    {
        ThermalProfile.Turbo => 0,
        ThermalProfile.Performance => 1,
        ThermalProfile.Balanced => 2,
        ThermalProfile.Quiet => 3,
        ThermalProfile.Eco => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null),
    };

    public static ThermalProfile? ProfileFromValue(byte value) => value switch
    {
        0 => ThermalProfile.Turbo,
        1 => ThermalProfile.Performance,
        2 => ThermalProfile.Balanced,
        3 => ThermalProfile.Quiet,
        4 => ThermalProfile.Eco,
        _ => null,
    };
}

/// <summary>Thermal mode over the EC HID channel. Only used when the firmware reports 4 or 5 modes.</summary>
public sealed class EcMode(ILightingDevice device)
{
    private static readonly TimeSpan ReplyDelay = TimeSpan.FromMilliseconds(20);
    private const int Attempts = 3;

    public bool IsSupported()
    {
        var reply = Exchange(EcProtocol.CommandStatus, EcProtocol.FunctionModeCapability);
        return EcProtocol.DecodeModeCount(reply) is 4 or 5;
    }

    public ThermalProfile Get()
    {
        var reply = Exchange(EcProtocol.CommandMode, EcProtocol.FunctionGet);
        return EcProtocol.ProfileFromValue(reply[5])
               ?? throw new AcerWmiException($"Unknown EC mode value {reply[5]}.");
    }

    public void Set(ThermalProfile profile) =>
        Exchange(EcProtocol.CommandMode, EcProtocol.FunctionSet, EcProtocol.ModeValue(profile));

    private byte[] Exchange(ushort command, byte function, params byte[] payload)
    {
        var request = EcProtocol.Encode(command, function, payload);
        byte[] reply = [];
        for (var i = 0; i < Attempts; i++)
        {
            device.SetFeature(request);
            Thread.Sleep(ReplyDelay);
            reply = device.GetFeature(EcProtocol.ReportId);
            if (EcProtocol.Status(reply) == EcProtocol.StatusDone && EcProtocol.Command(reply) == command) return reply;
        }
        throw new AcerWmiException(
            $"EC command 0x{command:X4}/{function} failed (status 0x{EcProtocol.Status(reply):X4}).");
    }
}

/// <summary>Opens the EC collection of HID 1025:174B. No admin rights needed.</summary>
public sealed class EcHid : ILightingDevice, IDisposable
{
    private readonly SafeFileHandle _handle;
    private readonly int _featureLength;
    private readonly Lock _lock = new();

    private EcHid(SafeFileHandle handle, int featureLength)
    {
        _handle = handle;
        _featureLength = featureLength;
    }

    public static EcHid Open()
    {
        foreach (var path in HidPaths.Find(EcProtocol.DeviceMatch))
        {
            var handle = Native.CreateFile(path, Native.GenericRead | Native.GenericWrite, Native.FileShareReadWrite,
                IntPtr.Zero, Native.OpenExisting, 0, IntPtr.Zero);
            if (handle.IsInvalid) continue;
            if (Native.TryGetCaps(handle, out var caps) && caps.UsagePage == EcProtocol.UsagePage && caps.Usage == 1)
                return new EcHid(handle, Math.Max((int)caps.FeatureReportByteLength, EcProtocol.ReportLength));
            handle.Dispose();
        }
        throw new LightingHidException("EC HID collection (1025:174B, FF05) not found.");
    }

    public byte[] GetFeature(byte reportId)
    {
        var buffer = new byte[_featureLength];
        buffer[0] = reportId;
        lock (_lock)
        {
            if (!Native.HidD_GetFeature(_handle, buffer, buffer.Length))
                throw new LightingHidException($"EC GetFeature failed (Win32 error {Marshal.GetLastPInvokeError()}).");
        }
        return buffer;
    }

    public void SetFeature(byte[] report)
    {
        var buffer = new byte[_featureLength];
        report.AsSpan(0, Math.Min(report.Length, buffer.Length)).CopyTo(buffer);
        lock (_lock)
        {
            if (!Native.HidD_SetFeature(_handle, buffer, buffer.Length))
                throw new LightingHidException($"EC SetFeature failed (Win32 error {Marshal.GetLastPInvokeError()}).");
        }
    }

    public void Dispose() => _handle.Dispose();

    private static class Native
    {
        public const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, FileShareReadWrite = 3, OpenExisting = 3;

        [StructLayout(LayoutKind.Sequential)]
        public struct HidpCaps
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
                NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
                NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        public static bool TryGetCaps(SafeFileHandle handle, out HidpCaps caps)
        {
            caps = default;
            if (!HidD_GetPreparsedData(handle, out var data)) return false;
            try { return HidP_GetCaps(data, ref caps) == HidpStatusSuccess; }
            finally { HidD_FreePreparsedData(data); }
        }

        private const int HidpStatusSuccess = 0x00110000;

        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetFeature(SafeFileHandle h, byte[] buffer, int length);
        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_SetFeature(SafeFileHandle h, byte[] buffer, int length);
        [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
        [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, ref HidpCaps caps);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);
    }
}
