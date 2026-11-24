using System.Text.Json;
using System.Windows.Threading;
using NitroCustom.Core.Hardware;
using NitroCustom.Core.Ipc;

namespace NitroCustom.App;

/// <summary>Keeps a pipe connection to the service, reconnects on loss and raises events on the UI thread.</summary>
public sealed class ServiceConnection(Dispatcher ui) : IAsyncDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);
    private readonly CancellationTokenSource _cts = new();
    private IpcClient? _client;
    private Task? _loop;

    public event Action<bool>? ConnectionChanged;
    public event Action<StateDto>? StateReceived;
    public event Action<ThermalProfile>? ProfileChanged;
    public event Action<LightingDto>? LightingChanged;

    public bool IsConnected => _client?.IsConnected == true;

    public void Start() => _loop = Task.Run(LoopAsync);

    public async Task<T?> RequestAsync<T>(string command, object? args = null)
    {
        var client = _client;
        if (client is null || !client.IsConnected) throw new IpcException("NitroCustom service is not running.");
        return await client.RequestAsync<T>(command, args, _cts.Token);
    }

    private async Task LoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            var client = new IpcClient();
            var lost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            client.Disconnected += () => lost.TrySetResult();
            client.EventReceived += OnEvent;
            try
            {
                await client.ConnectAsync(TimeSpan.FromSeconds(2), _cts.Token);
                _client = client;
                Post(() => ConnectionChanged?.Invoke(true));
                var state = await client.RequestAsync<StateDto>(IpcCommands.GetState, ct: _cts.Token);
                if (state is not null) Post(() => StateReceived?.Invoke(state));
                await lost.Task.WaitAsync(_cts.Token);
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested) { break; }
            catch (Exception) { /* service missing or dropped: retry below */ }
            finally
            {
                if (_client == client)
                {
                    _client = null;
                    Post(() => ConnectionChanged?.Invoke(false));
                }
                await client.DisposeAsync();
            }
            try { await Task.Delay(RetryDelay, _cts.Token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void OnEvent(string name, JsonElement? data)
    {
        switch (name)
        {
            case IpcEvents.ProfileChanged when IpcProtocol.FromElement<ProfileDto>(data) is { } p:
                Post(() => ProfileChanged?.Invoke(p.Profile));
                break;
            case IpcEvents.LightingChanged when IpcProtocol.FromElement<LightingDto>(data) is { } l:
                Post(() => LightingChanged?.Invoke(l));
                break;
        }
    }

    private void Post(Action action) => ui.BeginInvoke(action);

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_client is { } c) await c.DisposeAsync();
        if (_loop is not null)
        {
            try { await _loop; } catch { /* shutting down */ }
        }
        _cts.Dispose();
    }
}
