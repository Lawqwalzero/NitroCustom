using NitroCustom.Core.Hardware;
using NitroCustom.Core.Ipc;
using Xunit;

namespace NitroCustom.Core.Tests;

public class SupportedModelsTests
{
    [Theory]
    [InlineData("Acer", "Nitro ANV16S-41", ModelStatus.Verified)]
    [InlineData("Acer", "nitro anv16s-41", ModelStatus.Verified)]
    [InlineData("Acer", "Nitro ANV16S-41 ", ModelStatus.Verified)]
    [InlineData("Acer", "Nitro ANV16-71", ModelStatus.Unverified)]
    [InlineData("Acer", "Predator PHN16-72", ModelStatus.Unverified)]
    [InlineData("Acer", "", ModelStatus.Unverified)]
    [InlineData("", "", ModelStatus.Unverified)]
    [InlineData(null, null, ModelStatus.Unverified)]
    [InlineData("ASUSTeK COMPUTER INC.", "Nitro ANV16S-41", ModelStatus.Unverified)]
    public void OnlyVerifiedAcerModelsAreVerified(string? maker, string? model, ModelStatus expected) =>
        Assert.Equal(expected, SupportedModels.Classify(maker, model));

    [Fact]
    public void ModelInfoUsesTheSameRuleAndNeverShowsAnEmptyName()
    {
        Assert.Equal(ModelStatus.Verified, new ModelInfo("Acer", "Nitro ANV16S-41", "V1.12").Status);
        Assert.Equal("unknown model", new ModelInfo("", "", "").Display);
        Assert.Equal("Nitro ANV16S-41", new ModelInfo("Acer", " Nitro ANV16S-41 ", "V1.12").Display);
    }

    [Fact]
    public void StateWithoutModelStillRoundTripsForOlderServices()
    {
        var lighting = new LightingDto(new KeyboardLighting(), new Dictionary<ThermalProfile, string>());
        var old = new StateDto(ThermalProfile.Turbo, [], [], [], lighting);
        var back = IpcProtocol.FromElement<StateDto>(IpcProtocol.ToElement(old));
        Assert.NotNull(back);
        Assert.Null(back!.Model);

        var withModel = old with { Model = new ModelDto("Nitro ANV16-71", "V1.0", false, false) };
        var model = IpcProtocol.FromElement<StateDto>(IpcProtocol.ToElement(withModel))!.Model;
        Assert.Equal(new ModelDto("Nitro ANV16-71", "V1.0", false, false), model);
    }
}
