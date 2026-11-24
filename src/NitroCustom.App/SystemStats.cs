using System.Runtime.InteropServices;

namespace NitroCustom.App;

/// <summary>CPU load (from GetSystemTimes deltas) and RAM usage, no performance counters needed.</summary>
public sealed class SystemStats
{
    private ulong _idle, _kernel, _user;

    /// <summary>CPU load in percent since the previous call (first call returns 0).</summary>
    public double CpuPercent()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        ulong di = idle - _idle, dk = kernel - _kernel, du = user - _user;
        var first = _kernel == 0;
        (_idle, _kernel, _user) = (idle, kernel, user);
        var total = dk + du; // kernel time includes idle
        return first || total == 0 ? 0 : Math.Clamp(100.0 * (total - di) / total, 0, 100);
    }

    public static double RamPercent()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? status.MemoryLoad : 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);
}
