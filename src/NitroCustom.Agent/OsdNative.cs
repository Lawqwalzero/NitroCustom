using System.Runtime.InteropServices;

namespace NitroCustom.Agent;

internal static class OsdNative
{
    internal delegate IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WindowClass
    {
        public uint Size, Style;
        public IntPtr Proc;
        public int ClassExtra, WindowExtra;
        public IntPtr Instance, Icon, Cursor, Background, Menu;
        public string Name;
        public IntPtr SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Point(int x, int y) { public int X = x, Y = y; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct PointF(float x, float y) { public float X = x, Y = y; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RectF(float x, float y, float width, float height) { public float X = x, Y = y, Width = width, Height = height; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo { public uint Size; public Rect Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct Blend { public byte Operation, Flags, Alpha, Format; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, Bits;
        public uint Compression, ImageSize;
        public int XPels, YPels;
        public uint ColorsUsed, ColorsImportant;
        public uint Color;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Startup { public uint Version; public IntPtr Callback; public int SuppressThread, SuppressCodecs; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern ushort RegisterClassEx(ref WindowClass cls);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern IntPtr CreateWindowEx(uint exStyle, string cls, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] internal static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool SetWindowText(IntPtr hwnd, string text);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern IntPtr MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("shcore.dll")] internal static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("shell32.dll")] internal static extern int SHQueryUserNotificationState(out int state);
    [DllImport("user32.dll")] internal static extern nuint SetTimer(IntPtr hwnd, nuint id, uint milliseconds, IntPtr callback);
    [DllImport("user32.dll")] internal static extern bool KillTimer(IntPtr hwnd, nuint id);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr destinationDc, ref Point destination, ref Point size, IntPtr sourceDc, ref Point source, uint key, ref Blend blend, uint flags);
    [DllImport("gdi32.dll")] internal static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] internal static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] internal static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] internal static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdiplusStartup(out nuint token, ref Startup startup, IntPtr output);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern void GdiplusShutdown(nuint token);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipCreateBitmapFromScan0(int width, int height, int stride, int format, IntPtr pixels, out IntPtr image);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipDisposeImage(IntPtr image);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipGetImageGraphicsContext(IntPtr image, out IntPtr graphics);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipDeleteGraphics(IntPtr graphics);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipGraphicsClear(IntPtr graphics, uint color);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipSetSmoothingMode(IntPtr graphics, int mode);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipSetPixelOffsetMode(IntPtr graphics, int mode);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipSetTextRenderingHint(IntPtr graphics, int hint);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipScaleWorldTransform(IntPtr graphics, float x, float y, int order);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipTranslateWorldTransform(IntPtr graphics, float x, float y, int order);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipCreateSolidFill(uint color, out IntPtr brush);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipDeleteBrush(IntPtr brush);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipCreateStringFormat(int attributes, ushort language, out IntPtr format);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipSetStringFormatAlign(IntPtr format, int alignment);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipDeleteStringFormat(IntPtr format);
    [DllImport("gdiplus.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] internal static extern int GdipMeasureString(IntPtr graphics, string text, int length, IntPtr font, ref RectF layout, IntPtr format, out RectF measured, out int fitted, out int lines);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipCreatePath(int fillMode, out IntPtr path);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipAddPathLine(IntPtr path, float x1, float y1, float x2, float y2);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipClosePathFigure(IntPtr path);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipStartPathFigure(IntPtr path);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipAddPathLine2(IntPtr path, PointF[] points, int count);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipAddPathBeziers(IntPtr path, PointF[] points, int count);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipAddPathArc(IntPtr path, float x, float y, float width, float height, float startAngle, float sweepAngle);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipSetPenLineJoin(IntPtr pen, int lineJoin);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipDeletePath(IntPtr path);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipFillPath(IntPtr graphics, IntPtr brush, IntPtr path);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipDrawPath(IntPtr graphics, IntPtr pen, IntPtr path);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipCreateLineBrushFromRect(ref RectF rect, uint firstColor, uint lastColor, int mode, int wrap, out IntPtr brush);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipCreatePen1(uint color, float width, int unit, out IntPtr pen);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipDeletePen(IntPtr pen);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipDrawLines(IntPtr graphics, IntPtr pen, PointF[] points, int count);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipDrawBeziers(IntPtr graphics, IntPtr pen, PointF[] points, int count);
    [DllImport("gdiplus.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] internal static extern int GdipCreateFontFamilyFromName(string name, IntPtr collection, out IntPtr family);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipDeleteFontFamily(IntPtr family);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipCreateFont(IntPtr family, float size, int style, int unit, out IntPtr font);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipDeleteFont(IntPtr font);
    [DllImport("gdiplus.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] internal static extern int GdipDrawString(IntPtr graphics, string text, int length, IntPtr font, ref RectF layout, IntPtr format, IntPtr brush);
}
