using System.Globalization;

namespace NitroCustom.Core;

/// <summary>
/// Interface language: Russian when Windows is displayed in Russian, English otherwise.
/// Every process (app, agent) picks it once at start from the user's display language.
/// The environment variable NITROCUSTOM_LANGUAGE (<c>en</c> or <c>ru</c>) overrides it, e.g. for testing.
/// </summary>
public static class Loc
{
    public const string OverrideVariable = "NITROCUSTOM_LANGUAGE";

    public static bool Russian { get; } = Resolve(Environment.GetEnvironmentVariable(OverrideVariable), CultureInfo.CurrentUICulture);

    public static bool IsRussian(CultureInfo culture) => culture.TwoLetterISOLanguageName == "ru";

    public static bool Resolve(string? overrideValue, CultureInfo culture) => overrideValue?.Trim().ToLowerInvariant() switch
    {
        "ru" => true,
        "en" => false,
        _ => IsRussian(culture),
    };

    public static string T(string ru, string en) => Russian ? ru : en;
}
