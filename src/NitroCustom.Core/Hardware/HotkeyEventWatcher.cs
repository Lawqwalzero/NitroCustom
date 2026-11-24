using System.Management;

namespace NitroCustom.Core.Hardware;

/// <summary>Listens to firmware hotkey events (root\WMI:APGeEvent). Requires administrator rights.</summary>
public sealed class HotkeyEventWatcher : IDisposable
{
    private readonly ManagementEventWatcher _watcher;

    public event Action<HotkeyEvent>? EventReceived;

    public HotkeyEventWatcher()
    {
        _watcher = new ManagementEventWatcher(new ManagementScope(AcerWmi.Namespace), new WqlEventQuery("SELECT * FROM APGeEvent"));
        _watcher.EventArrived += (_, e) =>
        {
            if (e.NewEvent["EventDetail"] is byte[] detail)
                EventReceived?.Invoke(AcerProtocol.DecodeEvent(detail));
        };
    }

    public void Start() => _watcher.Start();

    public void Stop() => _watcher.Stop();

    public void Dispose()
    {
        try { _watcher.Stop(); } catch (ManagementException) { }
        _watcher.Dispose();
    }
}
