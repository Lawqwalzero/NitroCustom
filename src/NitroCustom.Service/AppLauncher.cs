using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NitroCustom.Service;

/// <summary>
/// Keeps the light agent running in the active console session, the way Acer's agent does for NitroSense.
/// The Nitro key and the mode button are seen only by a user-session process, so the service (session 0)
/// starts NitroCustom.Agent.exe right after install, at logon and whenever it is gone. The agent starts the
/// tray app itself; the app may be closed from the tray, the agent stays.
/// </summary>
public sealed class AppLauncher(ILogger<AppLauncher> log) : BackgroundService
{
    private const string ProcessName = "NitroCustom.Agent";
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(3), AfterLaunch = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, ProcessName + ".exe");
        if (!File.Exists(exe))
        {
            log.LogInformation("Agent not found next to the service ({Path}); launcher is off", exe);
            return;
        }
        while (!ct.IsCancellationRequested)
        {
            var delay = Poll;
            try { if (EnsureRunning(exe)) delay = AfterLaunch; }
            catch (Exception ex) { log.LogWarning(ex, "Could not start the agent"); delay = AfterLaunch; }
            try { await Task.Delay(delay, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Starts the app in the console session if a user is logged on there and it is not running. True when started.</summary>
    private bool EnsureRunning(string exe)
    {
        var session = WTSGetActiveConsoleSessionId();
        if (session is 0 or uint.MaxValue || IsRunning((int)session)) return false;
        if (!WTSQueryUserToken(session, out var token)) return false; // nobody logged on yet

        var env = IntPtr.Zero;
        try
        {
            if (!CreateEnvironmentBlock(out env, token, false)) env = IntPtr.Zero;
            var si = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>(), lpDesktop = @"winsta0\default" };
            var cmd = $"\"{exe}\"";
            if (!CreateProcessAsUser(token, exe, cmd, IntPtr.Zero, IntPtr.Zero, false, CreateUnicodeEnvironment, env,
                    Path.GetDirectoryName(exe), ref si, out var pi))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);
            log.LogInformation("Started agent in session {Session} (pid {Pid})", session, pi.dwProcessId);
            return true;
        }
        finally
        {
            if (env != IntPtr.Zero) DestroyEnvironmentBlock(env);
            CloseHandle(token);
        }
    }

    private static bool IsRunning(int session)
    {
        var running = false;
        foreach (var p in Process.GetProcessesByName(ProcessName))
        {
            try { running |= p.SessionId == session; }
            catch (InvalidOperationException) { } // exited meanwhile
            finally { p.Dispose(); }
        }
        return running;
    }

    private const uint CreateUnicodeEnvironment = 0x00000400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("kernel32.dll")] private static extern uint WTSGetActiveConsoleSessionId();
    [DllImport("wtsapi32.dll", SetLastError = true)] private static extern bool WTSQueryUserToken(uint session, out IntPtr token);
    [DllImport("userenv.dll", SetLastError = true)] private static extern bool CreateEnvironmentBlock(out IntPtr env, IntPtr token, bool inherit);
    [DllImport("userenv.dll", SetLastError = true)] private static extern bool DestroyEnvironmentBlock(IntPtr env);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateProcessAsUserW")]
    private static extern bool CreateProcessAsUser(IntPtr token, string application, string commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint flags, IntPtr environment, string? currentDirectory,
        ref StartupInfo startupInfo, out ProcessInformation processInformation);
}
