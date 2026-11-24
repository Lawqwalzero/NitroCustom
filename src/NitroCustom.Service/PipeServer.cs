using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using NitroCustom.Core.Hardware;
using NitroCustom.Core.Ipc;

namespace NitroCustom.Service;

/// <summary>Named-pipe API for the tray app and nitroctl. Local authenticated users may connect.</summary>
public sealed class PipeServer(NitroService nitro, EventHub hub, ILogger<PipeServer> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                log.LogError(ex, "Pipe listener failed");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                continue;
            }
            _ = HandleClientAsync(pipe, stoppingToken);
        }
    }

    private static NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(
            IpcProtocol.PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security);
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        await using var pipeScope = pipe;
        var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
        var writeLock = new SemaphoreSlim(1, 1);

        async Task SendAsync(IpcMessage msg)
        {
            await writeLock.WaitAsync(ct);
            try { await writer.WriteLineAsync(IpcProtocol.Serialize(msg)); }
            finally { writeLock.Release(); }
        }

        void OnEvent(IpcMessage msg) => _ = SendAsync(msg).ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);

        hub.Published += OnEvent;
        try
        {
            using var reader = new StreamReader(pipe, Encoding.UTF8);
            while (!ct.IsCancellationRequested && await reader.ReadLineAsync(ct) is { } line)
            {
                IpcMessage? request;
                try { request = IpcProtocol.Deserialize(line); }
                catch (JsonException) { continue; }
                if (request is not { Type: IpcMessageType.Request, Name: { } name }) continue;

                IpcMessage response;
                try
                {
                    var data = await Task.Run(() => Dispatch(name, request.Data), ct);
                    response = new IpcMessage { Type = IpcMessageType.Response, Id = request.Id, Data = data is null ? null : IpcProtocol.ToElement(data) };
                }
                catch (Exception ex) when (ex is AcerWmiException or LightingHidException or IpcException or ArgumentException or FormatException)
                {
                    response = new IpcMessage { Type = IpcMessageType.Response, Id = request.Id, Error = ex.Message };
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogError(ex, "Command {Command} failed", name);
                    response = new IpcMessage { Type = IpcMessageType.Response, Id = request.Id, Error = "Internal error: " + ex.Message };
                }
                await SendAsync(response);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
        finally
        {
            hub.Published -= OnEvent;
        }
    }

    private object? Dispatch(string command, JsonElement? data) => command switch
    {
        IpcCommands.GetState => nitro.GetState(),
        IpcCommands.GetSensors => nitro.GetSensors(),
        IpcCommands.SetProfile => new ProfileDto(nitro.SetProfile(Require<SetProfileArgs>(data).Profile)),
        IpcCommands.CycleProfile => new ProfileDto(nitro.CycleProfile()),
        IpcCommands.SetLighting => nitro.SetLighting(Require<KeyboardLighting>(data)),
        IpcCommands.SetModeKeyColor => SetModeKey(Require<SetModeKeyColorArgs>(data)),
        _ => throw new IpcException($"Unknown command '{command}'."),
    };

    private LightingDto SetModeKey(SetModeKeyColorArgs args) => nitro.SetModeKeyColor(args.Profile, Rgb.Parse(args.Color));

    private static T Require<T>(JsonElement? data) =>
        IpcProtocol.FromElement<T>(data) ?? throw new IpcException($"Missing arguments ({typeof(T).Name}).");
}
