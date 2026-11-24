using System.Globalization;
using NitroCustom.Core;
using NitroCustom.Core.Hardware;
using Xunit;

namespace NitroCustom.Core.Tests;

public class LocTests
{
    [Theory]
    [InlineData("ru-RU", true)]
    [InlineData("ru", true)]
    [InlineData("en-US", false)]
    [InlineData("de-DE", false)]
    [InlineData("uk-UA", false)]
    [InlineData("", false)]
    public void OnlyRussianDisplayLanguageSelectsRussian(string culture, bool russian) =>
        Assert.Equal(russian, Loc.IsRussian(CultureInfo.GetCultureInfo(culture)));

    [Theory]
    [InlineData(ThermalProfile.Eco, "Эко", "Eco")]
    [InlineData(ThermalProfile.Quiet, "Тихий", "Quiet")]
    [InlineData(ThermalProfile.Balanced, "Сбалансированный", "Balanced")]
    [InlineData(ThermalProfile.Performance, "Производительность", "Performance")]
    [InlineData(ThermalProfile.Turbo, "Турбо", "Turbo")]
    public void ProfileNamesFollowNitroSenseInBothLanguages(ThermalProfile profile, string ru, string en)
    {
        Assert.Equal(ru, ProfileNames.Title(profile, true));
        Assert.Equal(en, ProfileNames.Title(profile, false));
    }
}
