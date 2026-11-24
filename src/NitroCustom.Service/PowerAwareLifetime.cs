using System.ServiceProcess;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;

namespace NitroCustom.Service;

/// <summary>Relays power events (sleep/resume, AC change) from the SCM to the app.</summary>
public sealed class PowerEvents
{
    public event Action<PowerBroadcastStatus>? Changed;

    public void Raise(PowerBroadcastStatus status) => Changed?.Invoke(status);
}

internal sealed class PowerAwareLifetime : WindowsServiceLifetime
{
    private readonly PowerEvents _events;

    public PowerAwareLifetime(
        IHostEnvironment environment,
        IHostApplicationLifetime applicationLifetime,
        ILoggerFactory loggerFactory,
        IOptions<HostOptions> optionsAccessor,
        IOptions<WindowsServiceLifetimeOptions> windowsServiceOptionsAccessor,
        PowerEvents events)
        : base(environment, applicationLifetime, loggerFactory, optionsAccessor, windowsServiceOptionsAccessor)
    {
        _events = events;
        CanHandlePowerEvent = true;
    }

    protected override bool OnPowerEvent(PowerBroadcastStatus powerStatus)
    {
        _events.Raise(powerStatus);
        return base.OnPowerEvent(powerStatus);
    }
}
