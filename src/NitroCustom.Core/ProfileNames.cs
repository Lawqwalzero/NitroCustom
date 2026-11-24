using NitroCustom.Core.Hardware;

namespace NitroCustom.Core;

public static class ProfileNames
{
    /// <summary>Mode names as Acer NitroSense shows them, in the interface language.</summary>
    public static string Title(ThermalProfile profile) => Title(profile, Loc.Russian);

    public static string Title(ThermalProfile profile, bool russian) => profile switch
    {
        ThermalProfile.Eco => russian ? "Эко" : "Eco",
        ThermalProfile.Quiet => russian ? "Тихий" : "Quiet",
        ThermalProfile.Balanced => russian ? "Сбалансированный" : "Balanced",
        ThermalProfile.Performance => russian ? "Производительность" : "Performance",
        ThermalProfile.Turbo => russian ? "Турбо" : "Turbo",
        _ => profile.ToString(),
    };
}
