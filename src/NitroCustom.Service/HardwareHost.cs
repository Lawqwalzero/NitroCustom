using System.ServiceProcess;
using NitroCustom.Core.Hardware;

namespace NitroCustom.Service;

/// <summary>Restores settings, follows power events and firmware hotkeys.</summary>
public sealed class HardwareHost(NitroService nitro, PowerEvents power, ILogger<HardwareHost> log) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ResumeDelay = TimeSpan.FromSeconds(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        nitro.ApplySaved("startup");

        power.Changed += OnPower;
        using var watcher = StartHotkeys();
        try
        {
            using var timer = new PeriodicTimer(PollInterval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { nitro.PollProfile(); }
                catch (Exception ex) { log.LogDebug(ex, "Profile poll failed"); }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            power.Changed -= OnPower;
        }
    }

    private HotkeyEventWatcher? StartHotkeys()
    {
        try
        {
            var w = new HotkeyEventWatcher();
            w.EventReceived += e =>
            {
                switch (e)
                {
                    case HotkeyEvent.TouchpadToggled t:
                        log.LogInformation("Touchpad key: {State}", t.Enabled ? "on" : "off");
                        nitro.PublishTouchpad(t.Enabled);
                        break;
                    default:
                        log.LogInformation("Unhandled firmware event {Event}", e);
                        break;
                }
            };
            w.Start();
            return w;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Firmware hotkey events are unavailable");
            return null;
        }
    }

    private void OnPower(PowerBroadcastStatus status)
    {
        log.LogInformation("Power event {Status}", status);
        if (status is PowerBroadcastStatus.ResumeSuspend or PowerBroadcastStatus.ResumeAutomatic)
            _ = Task.Delay(ResumeDelay).ContinueWith(_ => nitro.ApplySaved("resume"), TaskScheduler.Default);
    }
}
