using System.Runtime.InteropServices;

namespace NitroCustom.Core.Hardware;

/// <summary>
/// Windows "power mode" (Settings > Power > Power mode overlay). NitroSense switches it together with the
/// firmware profile; without it the CPU keeps the old behaviour and the profiles feel the same.
/// </summary>
public static class WindowsPowerMode
{
    public static readonly Guid BestEfficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    public static readonly Guid Balanced = Guid.Empty;
    public static readonly Guid BestPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");

    public static Guid For(ThermalProfile profile) => profile switch
    {
        ThermalProfile.Eco or ThermalProfile.Quiet => BestEfficiency,
        ThermalProfile.Balanced => Balanced,
        _ => BestPerformance,
    };

    public static Guid Get() => PowerGetEffectiveOverlayScheme(out var g) == 0 ? g : Guid.Empty;

    /// <summary>Returns the Win32 error code (0 = success).</summary>
    public static uint Set(Guid overlay) => PowerSetActiveOverlayScheme(overlay);

    [DllImport("powrprof.dll")] private static extern uint PowerGetEffectiveOverlayScheme(out Guid overlay);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveOverlayScheme(Guid overlay);
}
