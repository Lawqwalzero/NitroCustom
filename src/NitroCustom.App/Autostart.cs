using Microsoft.Win32;

namespace NitroCustom.App;

/// <summary>
/// The service starts NitroCustom.Agent at logon, and the agent starts the app in the tray, so there is no per-user autostart any more.
/// Older builds wrote HKCU\...\Run; that value is removed on start.
/// </summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "NitroCustom";
    public const string TrayArgument = "--tray";

    public static void RemoveLegacyRunEntry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception) { } // not critical
    }
}
