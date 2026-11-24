using NitroCustom.Core.Hardware;

namespace NitroCustom.App;

/// <summary>UI names (Russian or English by Windows display language) and display order.</summary>
public static class Texts
{
    /// <summary>Profiles from coolest to hottest.</summary>
    public static readonly ThermalProfile[] ProfileOrder =
        [ThermalProfile.Eco, ThermalProfile.Quiet, ThermalProfile.Balanced, ThermalProfile.Performance, ThermalProfile.Turbo];

    public static string Title(ThermalProfile p) => Core.ProfileNames.Title(p);

    public static string Long(ThermalProfile p) => p switch
    {
        ThermalProfile.Eco => Core.Loc.T("Эко: минимум шума и потребления", "Eco: lowest noise and power use"),
        ThermalProfile.Turbo => Core.Loc.T("Турбо: максимум мощности, вентиляторы громче", "Turbo: maximum power, louder fans"),
        _ => Title(p),
    };

    public static readonly LightingEffect[] Effects =
    [
        LightingEffect.Static, LightingEffect.Direct, LightingEffect.Breathing, LightingEffect.Wave, LightingEffect.Neon,
        LightingEffect.Shifting, LightingEffect.Zoom, LightingEffect.Meteor, LightingEffect.Twinkling,
    ];

    public static string Name(LightingEffect e) => e switch
    {
        LightingEffect.Static => Core.Loc.T("Статичный", "Static"),
        LightingEffect.Direct => Core.Loc.T("По зонам", "Per zone"),
        LightingEffect.Breathing => Core.Loc.T("Дыхание", "Breathing"),
        LightingEffect.Neon => Core.Loc.T("Неон", "Neon"),
        LightingEffect.Wave => Core.Loc.T("Волна", "Wave"),
        LightingEffect.Shifting => Core.Loc.T("Переливы", "Shifting"),
        LightingEffect.Zoom => Core.Loc.T("Зум", "Zoom"),
        LightingEffect.Meteor => Core.Loc.T("Метеор", "Meteor"),
        LightingEffect.Twinkling => Core.Loc.T("Мерцание", "Twinkling"),
        _ => e.ToString(),
    };

    /// <summary>Resource key of the line icon (Icons.xaml).</summary>
    public static string IconKey(ThermalProfile p) => p switch
    {
        ThermalProfile.Eco => "IconEco",
        ThermalProfile.Quiet => "IconQuiet",
        ThermalProfile.Balanced => "IconBalanced",
        ThermalProfile.Performance => "IconPerformance",
        _ => "IconTurbo",
    };

    public static string IconKey(LightingEffect e) => "Icon" + e;

    /// <summary>Effects whose colour comes from the firmware (rainbow), so the colour picker is hidden.</summary>
    public static bool UsesColor(LightingEffect e) => e is not (LightingEffect.Wave or LightingEffect.Neon);

    public static bool IsAnimated(LightingEffect e) => e is not (LightingEffect.Static or LightingEffect.Direct);
}
