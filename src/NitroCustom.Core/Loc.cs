using System.Globalization;

namespace NitroCustom.Core;

/// <summary>
/// Interface language: Russian when Windows is displayed in Russian, English otherwise.
/// Every process (app, agent) picks it once at start from the user's display language.
/// </summary>
public static class Loc
{
    public static bool Russian { get; } = IsRussian(CultureInfo.CurrentUICulture);

    public static bool IsRussian(CultureInfo culture) => culture.TwoLetterISOLanguageName == "ru";

    public static string T(string ru, string en) => Russian ? ru : en;
}
