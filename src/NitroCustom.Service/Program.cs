using Microsoft.Extensions.Hosting.WindowsServices;
using NitroCustom.Core.Hardware;
using NitroCustom.Service;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(o => o.ServiceName = "NitroCustom");
if (WindowsServiceHelpers.IsWindowsService())
{
    builder.Services.AddSingleton<IHostLifetime, PowerAwareLifetime>();
    builder.Services.AddHostedService<AppLauncher>(); // needs SYSTEM (WTSQueryUserToken)
}

builder.Services.AddSingleton(_ => SupportedModels.Detect());
builder.Services.AddSingleton<PowerEvents>();
builder.Services.AddSingleton<EventHub>();
builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddSingleton<AcerWmi>();
builder.Services.AddSingleton<IAcerWmi>(sp => sp.GetRequiredService<AcerWmi>());
builder.Services.AddSingleton(sp => new NitroController(
    sp.GetRequiredService<IAcerWmi>(), new EcMode(new ReconnectingLightingDevice(EcHid.Open))));
builder.Services.AddSingleton(_ => new ReconnectingLightingDevice());
builder.Services.AddSingleton(sp => new LightingController(sp.GetRequiredService<ReconnectingLightingDevice>()));
builder.Services.AddSingleton<NitroService>();
builder.Services.AddHostedService<HardwareHost>();
builder.Services.AddHostedService<PipeServer>();

builder.Build().Run();
