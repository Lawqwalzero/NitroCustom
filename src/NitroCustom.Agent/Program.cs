using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using NitroCustom.Core.Ipc;

namespace NitroCustom.Agent;

/// <summary>
/// Light background agent in the user session, kept running by the service (like Acer's agent for NitroSense).
/// It owns the laptop keys only Acer software used to handle, so they work while the full app is closed:
/// - NitroSense key (lightning, scan code E0 75): low-level keyboard hook, opens/hides the app window
///   (starts NitroCustom.exe when it is not running);
/// - mode button above the keyboard: HID 1025174B Col06 (page 0x88, usage 0x01) report [04 85 FF] via Raw Input,
///   asks the service for the next profile.
/// At its own start it also starts the app in the tray (logon); after "Exit" in the tray the app stays closed.
/// </summary>
internal static class Program
{
    private const string MutexName = @"Local\NitroCustom.Agent";
    private const string AppMutexName = @"Local\NitroCustom.App";
    private const string AppToggleEventName = @"Local\NitroCustom.App.Toggle";
    private const string AppExe = "NitroCustom.exe";

    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100, WmKeyUp = 0x0101, WmSysKeyDown = 0x0104, WmSysKeyUp = 0x0105, WmInput = 0x00FF;
    private const uint NitroScanCode = 0x75, LlkhfExtended = 0x01;
    private const ushort VendorPage = 0x0088, VendorUsage = 0x0001;
    private const uint RidevInputSink = 0x00000100, RidInput = 0x10000003, RimTypeHid = 2;
    private static readonly IntPtr HwndMessage = new(-3);

    private static readonly SemaphoreSlim CycleGate = new(1, 1);
    private static LowLevelKeyboardProc? _hookProc; // keep the delegates alive
    private static WndProc? _wndProc;
    private static IntPtr _hook;
    private static readonly ConcurrentQueue<Action> UiActions = new();
    private static ServiceConnection? _connection;
    private const uint WmOsd = 0x8001;

    [STAThread]
    private static int Main(string[] args)
    {
        using var single = new Mutex(true, MutexName, out var first);
        if (!first) return 0;

        if (!args.Contains("--no-app", StringComparer.OrdinalIgnoreCase) && !AppRunning()) StartApp("--tray");

        _wndProc = WindowProc;
        var cls = new WndClassEx
        {
            cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = GetModuleHandle(null),
            lpszClassName = "NitroCustom.Agent",
        };
        if (RegisterClassEx(ref cls) == 0) return Fail("RegisterClassEx");
        var hwnd = CreateWindowEx(0, cls.lpszClassName, "NitroCustom.Agent", 0, 0, 0, 0, 0, HwndMessage, IntPtr.Zero, cls.hInstance, IntPtr.Zero);
        if (hwnd == IntPtr.Zero) return Fail("CreateWindowEx");
        ModeOsd? osd = null;
        try { osd = new ModeOsd(); }
        catch (Exception ex) { Trace.WriteLine($"NitroCustom.Agent: OSD initialization: {ex.Message}"); }
        _connection = new ServiceConnection(action =>
        {
            UiActions.Enqueue(action);
            PostMessage(hwnd, WmOsd, 0, 0);
        }, osd);
        _connection.Start();

        var device = new RawInputDevice { UsagePage = VendorPage, Usage = VendorUsage, Flags = RidevInputSink, Target = hwnd };
        if (!RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<RawInputDevice>()))
            Trace.WriteLine($"NitroCustom.Agent: mode key raw input not registered ({Marshal.GetLastWin32Error()})");

        _hookProc = KeyboardHook;
        _hook = SetWindowsHookEx(WhKeyboardLl, _hookProc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) Trace.WriteLine($"NitroCustom.Agent: keyboard hook failed ({Marshal.GetLastWin32Error()})");

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        osd?.Dispose();
        return 0;
    }

    private static int Fail(string what)
    {
        Trace.WriteLine($"NitroCustom.Agent: {what} failed ({Marshal.GetLastWin32Error()})");
        return 1;
    }

    // ---- Nitro key ----

    private static IntPtr KeyboardHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var key = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            if (key.ScanCode == NitroScanCode && (key.Flags & LlkhfExtended) != 0)
            {
                var msg = (int)wParam;
                if (msg is WmKeyDown or WmSysKeyDown) ThreadPool.QueueUserWorkItem(_ => ToggleApp());
                if (msg is WmKeyDown or WmSysKeyDown or WmKeyUp or WmSysKeyUp) return 1; // swallow: the key has no other meaning
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    /// <summary>Running app: show/hide its window. Not running: start it with the window open.</summary>
    private static void ToggleApp()
    {
        if (EventWaitHandle.TryOpenExisting(AppToggleEventName, out var toggle))
        {
            using (toggle) toggle.Set();
            return;
        }
        if (!AppRunning()) StartApp(null);
    }

    private static bool AppRunning()
    {
        if (!Mutex.TryOpenExisting(AppMutexName, out var m)) return false;
        m.Dispose();
        return true;
    }

    private static void StartApp(string? arguments)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, AppExe);
        try
        {
            Process.Start(new ProcessStartInfo(exe) { Arguments = arguments ?? "", UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory })?.Dispose();
        }
        catch (Exception ex) { Trace.WriteLine($"NitroCustom.Agent: cannot start {exe}: {ex.Message}"); }
    }

    // ---- mode button ----

    private static IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmInput && ReadModeKey(lParam)) _ = CycleProfileAsync();
        if (msg == WmOsd)
        {
            while (UiActions.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception ex) { Trace.WriteLine($"NitroCustom.Agent: OSD: {ex.Message}"); }
            }
            return 0;
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private static bool ReadModeKey(IntPtr lParam)
    {
        var headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
        uint size = 0;
        GetRawInputData(lParam, RidInput, IntPtr.Zero, ref size, headerSize);
        if (size == 0) return false;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(lParam, RidInput, buffer, ref size, headerSize) != size) return false;
            var header = Marshal.PtrToStructure<RawInputHeader>(buffer);
            if (header.Type != RimTypeHid) return false;
            // RAWHID follows the header: dwSizeHid, dwCount, then dwCount reports of dwSizeHid bytes.
            var sizeHid = Marshal.ReadInt32(buffer, (int)headerSize);
            var count = Marshal.ReadInt32(buffer, (int)headerSize + 4);
            var pressed = false;
            for (var i = 0; i < count; i++)
            {
                var report = new byte[sizeHid];
                Marshal.Copy(buffer + (int)headerSize + 8 + i * sizeHid, report, 0, sizeHid);
                pressed |= IsModeKey(report);
            }
            return pressed;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    /// <summary>[04 85 ..] = mode button pressed (other codes on this collection: 7C/7D backlight up/down).</summary>
    private static bool IsModeKey(ReadOnlySpan<byte> report) => report.Length >= 2 && report[0] == 0x04 && report[1] == 0x85;

    /// <summary>Next profile in the service's cycle order; the service sets the mode-key colour and tells the app.</summary>
    private static async Task CycleProfileAsync()
    {
        await CycleGate.WaitAsync();
        try
        {
            if (_connection is { } connection) await connection.CycleAsync();
        }
        catch (Exception ex) { Trace.WriteLine($"NitroCustom.Agent: cycle failed: {ex.Message}"); }
        finally { CycleGate.Release(); }
    }

    // ---- Win32 ----

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);
    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct { public uint VkCode, ScanCode, Flags, Time; public IntPtr ExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice { public ushort UsagePage, Usage; public uint Flags; public IntPtr Target; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader { public uint Type, Size; public IntPtr Device, WParam; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint cbSize, style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, LowLevelKeyboardProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices(RawInputDevice[] devices, uint count, uint size);
    [DllImport("user32.dll")] private static extern uint GetRawInputData(IntPtr rawInput, uint command, IntPtr data, ref uint size, uint headerSize);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern ushort RegisterClassEx(ref WndClassEx cls);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetMessage(out Msg msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Msg msg);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DispatchMessage(ref Msg msg);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
