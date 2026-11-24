using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NitroCustom.Core.Hardware;
using NitroCustom.Core.Ipc;

namespace NitroCustom.App;

public partial class App : Application
{
    private const string MutexName = @"Local\NitroCustom.App";
    private const string ShowEventName = @"Local\NitroCustom.App.Show";
    /// <summary>Set by NitroCustom.Agent on the Nitro key: show or hide the window.</summary>
    private const string ToggleEventName = @"Local\NitroCustom.App.Toggle";

    private Mutex? _single;
    private EventWaitHandle? _showEvent;
    private EventWaitHandle? _toggleEvent;
    private ServiceConnection? _service;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private DispatcherTimer? _sensorTimer;
    private ThermalProfile? _profile;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single instance: a second launch just asks the first one to show its window.
        _single = new Mutex(true, MutexName, out var first);
        if (!first)
        {
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch (WaitHandleCannotBeOpenedException) { }
            Shutdown();
            return;
        }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => Dispatcher.BeginInvoke(ShowWindow), null, Timeout.Infinite, false);
        _toggleEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ToggleEventName);
        ThreadPool.RegisterWaitForSingleObject(_toggleEvent, (_, _) => Dispatcher.BeginInvoke(() => _window?.ToggleFromHotkey()), null, Timeout.Infinite, false);

        _service = new ServiceConnection(Dispatcher);
        _window = new MainWindow(_service);
        _window.Icon = BitmapFrame.Create(AppIcon.Uri);

        _tray = new TrayIcon();
        _tray.OpenRequested += ShowWindow;
        _tray.ProfileRequested += p => _ = SetProfileAsync(p);
        _tray.ExitRequested += () => _ = ExitAsync();

        _service.ConnectionChanged += connected =>
        {
            _tray.SetEnabled(connected);
            if (!connected) { _profile = null; _tray.SetTooltip(Core.Loc.T("NitroCustom: служба не запущена", "NitroCustom: service not running")); }
        };
        _service.StateReceived += s =>
        {
            _tray.SetProfiles(s.SupportedProfiles);
            OnProfile(s.Profile);
            OnSensors(s.Sensors);
        };
        _service.ProfileChanged += OnProfile;
        _service.Start();

        _sensorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _sensorTimer.Tick += async (_, _) => await PollSensorsAsync();
        _sensorTimer.Start();

        Autostart.RemoveLegacyRunEntry();
        if (!e.Args.Contains(Autostart.TrayArgument, StringComparer.OrdinalIgnoreCase)) ShowWindow();
    }

    private void ShowWindow() => _window?.ShowNearTray();

    private void OnProfile(ThermalProfile profile)
    {
        _profile = profile;
        _tray?.SetCurrentProfile(profile);
    }

    private async Task SetProfileAsync(ThermalProfile profile)
    {
        try { await _service!.RequestAsync<ProfileDto>(IpcCommands.SetProfile, new SetProfileArgs(profile)); }
        catch (IpcException) { ShowWindow(); }
    }

    /// <summary>Tray "Exit": the app closes; NitroCustom.Agent keeps the Nitro key and the mode button working.</summary>
    private async Task ExitAsync()
    {
        _sensorTimer?.Stop();
        _tray?.Dispose();
        _tray = null;
        if (_service is not null) await _service.DisposeAsync();
        Shutdown();
    }

    private async Task PollSensorsAsync()
    {
        if (_service is not { IsConnected: true }) return;
        // Hidden window: refresh only the tooltip, and less often.
        if (_window is { IsVisible: false } && DateTime.Now.Second % 10 > 1) return;
        try
        {
            var sensors = await _service.RequestAsync<List<SensorDto>>(IpcCommands.GetSensors);
            if (sensors is not null) OnSensors(sensors);
        }
        catch (IpcException) { }
    }

    private void OnSensors(IReadOnlyList<SensorDto> sensors)
    {
        _window?.UpdateSensors(sensors);
        int? Get(Sensor s) => sensors.FirstOrDefault(x => x.Sensor == s)?.Value;
        var profile = _profile is { } p ? Texts.Title(p) : "—";
        _tray?.SetTooltip($"NitroCustom | {profile}\nCPU {Get(Sensor.CpuTemperature)}°C | GPU {Get(Sensor.GpuTemperature)}°C");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _showEvent?.Dispose();
        _toggleEvent?.Dispose();
        _single?.Dispose();
        base.OnExit(e);
    }
}
