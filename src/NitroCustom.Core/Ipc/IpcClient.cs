using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace NitroCustom.Core.Ipc;

public sealed class IpcException(string message) : Exception(message);

/// <summary>Client side of the NitroCustom service pipe.</summary>
public sealed class IpcClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream _pipe = new(".", IpcProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<IpcMessage>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private StreamWriter? _writer;
    private Task? _readLoop;
    private int _nextId;

    /// <summary>Raised on the reader thread for every pushed event (name, data).</summary>
    public event Action<string, JsonElement?>? EventReceived;

    /// <summary>Raised once when the connection is lost.</summary>
    public event Action? Disconnected;

    public bool IsConnected => _pipe.IsConnected;

    public async Task ConnectAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        try
        {
            await _pipe.ConnectAsync((int)timeout.TotalMilliseconds, ct);
        }
        catch (TimeoutException)
        {
            throw new IpcException("NitroCustom service is not running.");
        }
        _writer = new StreamWriter(_pipe, new UTF8Encoding(false)) { AutoFlush = true };
        _readLoop = Task.Run(ReadLoopAsync);
    }

    public async Task<T?> RequestAsync<T>(string command, object? args = null, CancellationToken ct = default)
    {
        var response = await SendAsync(command, args, ct);
        return IpcProtocol.FromElement<T>(response.Data);
    }

    public async Task<IpcMessage> SendAsync(string command, object? args = null, CancellationToken ct = default)
    {
        if (_writer is null) throw new InvalidOperationException("Not connected.");
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<IpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        var request = new IpcMessage
        {
            Type = IpcMessageType.Request,
            Id = id,
            Name = command,
            Data = args is null ? null : IpcProtocol.ToElement(args),
        };
        await _writeLock.WaitAsync(ct);
        try { await _writer.WriteLineAsync(IpcProtocol.Serialize(request).AsMemory(), ct); }
        finally { _writeLock.Release(); }

        using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
        var response = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
        if (response.Error is not null) throw new IpcException(response.Error);
        return response;
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            using var reader = new StreamReader(_pipe, Encoding.UTF8);
            while (!_cts.IsCancellationRequested && await reader.ReadLineAsync(_cts.Token) is { } line)
            {
                var msg = IpcProtocol.Deserialize(line);
                if (msg is null) continue;
                if (msg.Type == IpcMessageType.Response && _pending.TryRemove(msg.Id, out var tcs))
                    tcs.TrySetResult(msg);
                else if (msg.Type == IpcMessageType.Event && msg.Name is not null)
                    EventReceived?.Invoke(msg.Name, msg.Data);
            }
        }
        catch (Exception) when (_cts.IsCancellationRequested) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            foreach (var p in _pending.Values) p.TrySetException(new IpcException("Connection to the service was lost."));
            _pending.Clear();
            Disconnected?.Invoke();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        await _pipe.DisposeAsync();
        if (_readLoop is not null) await _readLoop.ConfigureAwait(false);
        _cts.Dispose();
        _writeLock.Dispose();
    }
}
