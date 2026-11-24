using System.Management;

namespace NitroCustom.Core.Hardware;

public enum ModelStatus { Verified, Unverified }

/// <summary>What this computer says it is (SMBIOS), and whether NitroCustom was verified on it.</summary>
public sealed record ModelInfo(string Manufacturer, string Model, string Bios)
{
    public ModelStatus Status => SupportedModels.Classify(Manufacturer, Model);

    /// <summary>Model name for messages, never empty.</summary>
    public string Display => string.IsNullOrWhiteSpace(Model) ? "unknown model" : Model.Trim();
}

/// <summary>
/// Laptop models NitroCustom was tested on, with real hardware. Writing to the firmware of any other
/// model is blocked until the user allows it. Add a model here only after a hardware report confirms it.
/// </summary>
public static class SupportedModels
{
    /// <summary>Model name fragments as SMBIOS reports them (the part after "Nitro ").</summary>
    public static readonly IReadOnlyList<string> Verified = ["ANV16S-41"];

    /// <summary>Unknown or unreadable model counts as unverified.</summary>
    public static ModelStatus Classify(string? manufacturer, string? model) =>
        Has(manufacturer, "Acer") && Verified.Any(id => Has(model, id)) ? ModelStatus.Verified : ModelStatus.Unverified;

    private static bool Has(string? text, string part) =>
        text?.Contains(part, StringComparison.OrdinalIgnoreCase) == true;

    public static ModelInfo Detect()
    {
        try
        {
            return new ModelInfo(
                Read("Win32_ComputerSystem", "Manufacturer"),
                Read("Win32_ComputerSystem", "Model"),
                Read("Win32_BIOS", "SMBIOSBIOSVersion"));
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return new ModelInfo("", "", "");
        }
    }

    private static string Read(string wmiClass, string property)
    {
        using var searcher = new ManagementObjectSearcher($"SELECT {property} FROM {wmiClass}");
        foreach (var item in searcher.Get())
            using (item) return item[property]?.ToString()?.Trim() ?? "";
        return "";
    }
}
