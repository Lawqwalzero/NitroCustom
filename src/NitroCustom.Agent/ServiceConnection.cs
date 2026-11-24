using System.Diagnostics;
using NitroCustom.Core.Ipc;

namespace NitroCustom.Agent;

/// <summary>One event subscription and command channel for the lifetime of the agent.</summary>
internal sealed class ServiceConnection(Action<Action> post, ModeOsd? osd) : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private IpcClient? _client;
    private Task? _loop;

    public void Start() => _loop = Task.Run(RunAsync);

    public async Task CycleAsync()
    {
        var client = Volatile.Read(ref _client);
        if (client is null) throw new IpcException("NitroCustom service is not connected.");
        // A lost response may mean the switch succeeded. Never replay this request.
        await client.RequestAsync<ProfileDto>(IpcCommands.CycleProfile, ct: _stop.Token);
    }

    private async Task RunAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            var client = new IpcClient();
            var lost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            client.Disconnected += () => lost.TrySetResult();
            client.EventReceived += (name, data) =>
            {
                try
                {
                    if (name == IpcEvents.ProfileChanged && IpcProtocol.FromElement<ProfileDto>(data) is { } profile)
                        post(() => osd?.Changed(profile));
                    if (name == IpcEvents.LightingChanged && IpcProtocol.FromElement<LightingDto>(data) is { } lighting)
                        post(() => osd?.Lighting(lighting));
                }
                catch (System.Text.Json.JsonException ex) { Trace.WriteLine(ex); }
            };
            try
            {
                post(() => osd?.BeginConnection());
                await client.ConnectAsync(TimeSpan.FromSeconds(2), _stop.Token);
                Volatile.Write(ref _client, client);
                var state = await client.RequestAsync<StateDto>(IpcCommands.GetState, ct: _stop.Token);
                if (state is not null) post(() => osd?.Snapshot(state));
                await lost.Task.WaitAsync(_stop.Token);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
            catch (Exception ex) { Trace.WriteLine($"NitroCustom.Agent: connection: {ex.Message}"); }
            finally
            {
                Interlocked.CompareExchange(ref _client, null, client);
                // Old reader must stop publishing before the next session begins.
                await client.DisposeAsync();
            }
            try { await Task.Delay(1500, _stop.Token); }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_loop is not null) await _loop;
        _stop.Dispose();
    }
}
