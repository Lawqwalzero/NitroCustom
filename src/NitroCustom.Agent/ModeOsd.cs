using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NitroCustom.Core;
using NitroCustom.Core.Hardware;
using NitroCustom.Core.Ipc;

namespace NitroCustom.Agent;

/// <summary>A single click-through, nonactivating layered window, owned by the message loop.</summary>
internal sealed class ModeOsd : IDisposable
{
    private readonly OsdState _state = new();
    private readonly OsdNative.WindowProc _proc;
    private readonly IntPtr _window;
    private readonly nuint _gdiplus;
    private IntPtr _dc, _bitmap, _previousBitmap;
    private OsdBounds _bounds;

    public ModeOsd()
    {
        _proc = WindowProc;
        var startup = new OsdNative.Startup { Version = 1 };
        Check(OsdNative.GdiplusStartup(out _gdiplus, ref startup, 0));
        var cls = new OsdNative.WindowClass
        {
            Size = (uint)Marshal.SizeOf<OsdNative.WindowClass>(),
            Proc = Marshal.GetFunctionPointerForDelegate(_proc),
            Instance = OsdNative.GetModuleHandle(null),
            Name = "NitroCustom.ModeOsd",
        };
        if (OsdNative.RegisterClassEx(ref cls) == 0) throw new Win32Exception();
        // TOPMOST | TOOLWINDOW | NOACTIVATE | TRANSPARENT | LAYERED
        _window = OsdNative.CreateWindowEx(0x080800A8, cls.Name, "", 0x80000000,
            0, 0, 0, 0, 0, 0, cls.Instance, 0);
        if (_window == 0) throw new Win32Exception();
    }

    public void BeginConnection()
    {
        Hide();
        _state.BeginConnection();
    }

    public void Snapshot(StateDto snapshot)
    {
        _state.ApplySnapshot(snapshot, 0);
        if (!_state.Finished(Environment.TickCount64)) Render();
    }

    public void Lighting(LightingDto lighting)
    {
        _state.Lighting(lighting);
        if (!_state.Finished(Environment.TickCount64)) Render();
    }

    public void Changed(ProfileDto profile)
    {
        bool allowed = CanShow();
        if (!_state.Changed(profile, Environment.TickCount64, allowed))
        {
            if (!allowed || profile.Reason is "startup" or "resume") Hide();
            return;
        }
        OsdNative.SetWindowText(_window, _state.Title);
        OsdNative.GetCursorPos(out var cursor);
        var monitor = OsdNative.MonitorFromPoint(cursor, 2);
        var info = new OsdNative.MonitorInfo { Size = (uint)Marshal.SizeOf<OsdNative.MonitorInfo>() };
        if (!OsdNative.GetMonitorInfo(monitor, ref info)) throw new Win32Exception();
        uint dpi = 96;
        if (OsdNative.GetDpiForMonitor(monitor, 0, out var x, out _) == 0) dpi = x;
        var work = info.Work;
        _bounds = OsdState.Place(new(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top), dpi);
        Render();
        Present(1);
        OsdNative.SetWindowPos(_window, new IntPtr(-1), _bounds.X, _bounds.Y,
            _bounds.Width, _bounds.Height, 0x0010 | 0x0040);
        if (OsdNative.SetTimer(_window, 1, 16, 0) == 0) Hide();
    }

    private static bool CanShow() =>
        OsdNative.SHQueryUserNotificationState(out var state) == 0 && OsdState.NotificationsAllowed(state);

    private IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (msg == 0x0113) // WM_TIMER, active only while visible
            {
                long now = Environment.TickCount64;
                if (_state.Finished(now) || !CanShow()) Hide();
                else Present(_state.Alpha(now));
                return 0;
            }
            if (msg == 0x0021) return 3; // MA_NOACTIVATE
            if (msg == 0x0084) return new IntPtr(-1); // HTTRANSPARENT
            if (msg == 0x02E0) { Hide(); return 0; } // next event repositions for new DPI
        }
        catch (Exception ex) { Trace.WriteLine(ex); Hide(); }
        return OsdNative.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void Hide()
    {
        OsdNative.KillTimer(_window, 1);
        OsdNative.ShowWindow(_window, 0);
    }

    private void Present(byte alpha)
    {
        if (_dc == 0) return;
        var destination = new OsdNative.Point(_bounds.X, _bounds.Y);
        var size = new OsdNative.Point(_bounds.Width, _bounds.Height);
        var source = new OsdNative.Point(0, 0);
        var blend = new OsdNative.Blend { Alpha = alpha, Format = 1 };
        if (!OsdNative.UpdateLayeredWindow(_window, 0, ref destination, ref size, _dc,
                ref source, 0, ref blend, 2)) throw new Win32Exception();
    }

    private void ReleaseSurface()
    {
        if (_dc != 0)
        {
            OsdNative.SelectObject(_dc, _previousBitmap);
            OsdNative.DeleteObject(_bitmap);
            OsdNative.DeleteDC(_dc);
        }
        _dc = _bitmap = _previousBitmap = 0;
    }

    private void Render()
    {
        ReleaseSurface();
        _dc = OsdNative.CreateCompatibleDC(0);
        var info = new OsdNative.BitmapInfo
        {
            Size = 40, Width = _bounds.Width, Height = -_bounds.Height,
            Planes = 1, Bits = 32,
        };
        _bitmap = OsdNative.CreateDIBSection(_dc, ref info, 0, out var pixels, 0, 0);
        if (_dc == 0 || _bitmap == 0) throw new Win32Exception();
        _previousBitmap = OsdNative.SelectObject(_dc, _bitmap);
        // GDI+ writes premultiplied BGRA directly into the DIB used by UpdateLayeredWindow.
        Check(OsdNative.GdipCreateBitmapFromScan0(_bounds.Width, _bounds.Height,
            _bounds.Width * 4, 0x000E200B, pixels, out var image));
        try
        {
            Check(OsdNative.GdipGetImageGraphicsContext(image, out var graphics));
            try { Draw(graphics); }
            finally { OsdNative.GdipDeleteGraphics(graphics); }
        }
        finally { OsdNative.GdipDisposeImage(image); }
    }

    // Card layout in canvas units (OsdState.CanvasWidth x CanvasHeight at 96 DPI).
    private const float CardTop = 8, CardBottom = 80, Pad = 18, IconSize = 40, Gap = 14;
    private const uint TextColor = 0xFFF6F0EC, CaptionColor = 0xFFA08A7E, ThemeAccent = 0xFFFF5A1F;

    private void Draw(IntPtr graphics)
    {
        Check(OsdNative.GdipGraphicsClear(graphics, 0));
        Check(OsdNative.GdipSetSmoothingMode(graphics, 4));
        Check(OsdNative.GdipSetPixelOffsetMode(graphics, 4)); // half: edges land on the pixel grid
        Check(OsdNative.GdipSetTextRenderingHint(graphics, 3)); // grid-fit antialias keeps stems dense
        Check(OsdNative.GdipScaleWorldTransform(graphics,
            _bounds.Width / (float)OsdState.CanvasWidth, _bounds.Height / (float)OsdState.CanvasHeight, 0));
        var color = _state.Color;
        uint accent = 0xFF000000u | (uint)(color.R << 16 | color.G << 8 | color.B);
        string title = _state.Title;

        // Same typeface as the main window: Bahnschrift SemiBold for the name.
        using var titleFamily = Family("Bahnschrift SemiBold", out bool bahnschrift);
        using var titleFont = Font(titleFamily, 24, bahnschrift ? 0 : 1);
        using var captionFamily = Family("Bahnschrift", out _);
        using var captionFont = Font(captionFamily, 11, 0);
        Check(OsdNative.GdipCreateStringFormat(0x1000, 0, out var formatHandle)); // NoWrap
        using var format = new Gdi(formatHandle, OsdNative.GdipDeleteStringFormat);

        var probe = new OsdNative.RectF(0, 0, 1000, 40);
        Check(OsdNative.GdipMeasureString(graphics, title, -1, titleFont.Handle, ref probe, format.Handle,
            out var measured, out _, out _));
        float content = Pad + IconSize + Gap * 2 + measured.Width + Pad + 4;
        float width = Math.Clamp(content, 200, OsdState.CanvasWidth - 16);
        float left = (OsdState.CanvasWidth - width) / 2;
        DrawCard(graphics, left, width);

        // Short names keep the icon and text together in the middle of the card.
        float iconX = left + Pad + Math.Max(0, width - content) / 2, divider = iconX + IconSize + Gap, textX = divider + Gap;
        Line(graphics, WithAlpha(ThemeAccent, 0x50), 1, [new(divider, 26), new(divider, 62)]);
        Text(graphics, Loc.T("РЕЖИМ", "MODE"), captionFont, format, CaptionColor, new(textX, 22, width, 16));
        Text(graphics, title, titleFont, format, TextColor, new(textX, 35, width, 34));
        DrawIcon(graphics, _state.Profile, accent, iconX, (CardTop + CardBottom - IconSize) / 2);
    }

    private static Gdi Family(string name, out bool found)
    {
        found = OsdNative.GdipCreateFontFamilyFromName(name, 0, out var family) == 0;
        if (!found) Check(OsdNative.GdipCreateFontFamilyFromName("Segoe UI", 0, out family));
        return new Gdi(family, OsdNative.GdipDeleteFontFamily);
    }

    private static Gdi Font(Gdi family, float pixels, int style)
    {
        Check(OsdNative.GdipCreateFont(family.Handle, pixels, style, 2, out var font)); // pixel units
        return new Gdi(font, OsdNative.GdipDeleteFont);
    }

    private static void Text(IntPtr g, string text, Gdi font, Gdi format, uint color, OsdNative.RectF layout)
    {
        Check(OsdNative.GdipCreateSolidFill(color, out var brush));
        try { Check(OsdNative.GdipDrawString(g, text, -1, font.Handle, ref layout, format.Handle, brush)); }
        finally { OsdNative.GdipDeleteBrush(brush); }
    }

    private static void DrawCard(IntPtr graphics, float left, float width)
    {
        // Chamfered frame and warm surfaces follow the main application's theme.
        float right = left + width, top = CardTop, bottom = CardBottom;
        var rect = new OsdNative.RectF(left, top, width, bottom - top);
        using var path = NewPath();
        Check(OsdNative.GdipAddPathLine2(path.Handle,
        [
            new(left + 8, top), new(right - 8, top), new(right, top + 8), new(right, bottom - 8),
            new(right - 8, bottom), new(left + 8, bottom), new(left, bottom - 8), new(left, top + 8),
        ], 8));
        Check(OsdNative.GdipClosePathFigure(path.Handle));
        // Small offset shadow, then a translucent highlight-to-darkness gradient.
        Check(OsdNative.GdipTranslateWorldTransform(graphics, 0, 3, 0));
        Fill(graphics, path, 0x30000000);
        Check(OsdNative.GdipTranslateWorldTransform(graphics, 0, -3, 0));
        Check(OsdNative.GdipCreateLineBrushFromRect(ref rect, 0xD22E150C, 0xE60E0705, 1, 0, out var surface));
        try { Check(OsdNative.GdipFillPath(graphics, surface, path.Handle)); }
        finally { OsdNative.GdipDeleteBrush(surface); }
        Check(OsdNative.GdipCreatePen1(0xFF4A2414, 1, 2, out var edge));
        try { Check(OsdNative.GdipDrawPath(graphics, edge, path.Handle)); }
        finally { OsdNative.GdipDeletePen(edge); }
        Line(graphics, 0xE6FF5A1F, 1.4f, [new(left, top + 20), new(left, top + 8), new(left + 8, top), new(left + 28, top)]);
        Line(graphics, 0xE6FF5A1F, 1.4f, [new(right, bottom - 20), new(right, bottom - 8), new(right - 8, bottom), new(right - 28, bottom)]);
    }

    // Filled versions of the Icons.xaml motifs: leaf, crescent, scales, lightning and flame.
    private static void DrawIcon(IntPtr g, ThermalProfile profile, uint color, float x, float y)
    {
        Check(OsdNative.GdipTranslateWorldTransform(g, x, y, 0));
        Check(OsdNative.GdipScaleWorldTransform(g, IconSize / 24f, IconSize / 24f, 0));
        using var path = NewPath();
        var p = path.Handle;
        switch (profile)
        {
            case ThermalProfile.Eco:
                Check(OsdNative.GdipAddPathBeziers(p, [new(4, 20), new(4, 11), new(10.5f, 4), new(20, 4),
                    new(20, 13.5f), new(13, 20), new(4, 20)], 7));
                break;
            case ThermalProfile.Quiet:
                Crescent(p, 11, 13, 9, 16.5f, 7.5f, 7.5f);
                Check(OsdNative.GdipStartPathFigure(p));
                Check(OsdNative.GdipAddPathLine2(p, Star(20, 4, 2.8f, 0.8f), 8));
                break;
            case ThermalProfile.Balanced:
                Polygon(p, [new(11, 3.5f), new(13, 3.5f), new(13, 18.5f), new(11, 18.5f)]);
                Polygon(p, [new(3.5f, 6.5f), new(20.5f, 6.5f), new(20.5f, 8.5f), new(3.5f, 8.5f)]);
                Polygon(p, [new(7, 18.5f), new(17, 18.5f), new(18, 21), new(6, 21)]);
                Check(OsdNative.GdipStartPathFigure(p));
                Check(OsdNative.GdipAddPathArc(p, 1, 9.5f, 8, 8, 0, 180));
                Check(OsdNative.GdipClosePathFigure(p));
                Check(OsdNative.GdipStartPathFigure(p));
                Check(OsdNative.GdipAddPathArc(p, 15, 9.5f, 8, 8, 0, 180));
                Check(OsdNative.GdipClosePathFigure(p));
                break;
            case ThermalProfile.Performance:
                Polygon(p, [new(14.5f, 2), new(4.5f, 13.8f), new(11, 13.8f), new(9.5f, 22),
                    new(19.5f, 9.6f), new(13, 9.6f)]);
                break;
            default:
                Check(OsdNative.GdipAddPathBeziers(p, [new(12, 21), new(7.6f, 21), new(5, 18.2f), new(5, 14.6f),
                    new(5, 10.6f), new(8.4f, 8.6f), new(9, 4.2f),
                    new(11.6f, 6.2f), new(12.8f, 8.8f), new(12.8f, 11.2f),
                    new(13.8f, 10.2f), new(14.4f, 9), new(14.4f, 7.4f),
                    new(17.2f, 9.4f), new(19, 12), new(19, 15),
                    new(19, 18.5f), new(16.4f, 21), new(12, 21)], 19));
                break;
        }
        Check(OsdNative.GdipClosePathFigure(p));

        // Bright-to-pure gradient keeps the lower edge off the dark card; a thin light rim
        // gives the silhouette a crisp edge instead of a halo.
        Gradient(g, path, Mix(color, 0xFFFFFFFF, 0.3f), Mix(color, 0xFF000000, 0.08f));
        Stroke(g, path, WithAlpha(Mix(color, 0xFFFFFFFF, 0.55f), 0xB0), 0.6f);

        switch (profile)
        {
            case ThermalProfile.Eco:
                Line(g, 0xF0180B06, 1.5f, [new(6, 18), new(14.5f, 9.5f)]);
                break;
            case ThermalProfile.Balanced:
                uint hanger = Mix(color, 0xFFFFFFFF, 0.15f);
                Line(g, hanger, 1.2f, [new(2, 13.5f), new(5, 8.5f), new(8, 13.5f)]);
                Line(g, hanger, 1.2f, [new(16, 13.5f), new(19, 8.5f), new(22, 13.5f)]);
                break;
            case ThermalProfile.Turbo:
                using (var core = NewPath())
                {
                    Check(OsdNative.GdipAddPathBeziers(core.Handle, [new(12, 20.2f), new(9.9f, 20.2f),
                        new(8.7f, 18.7f), new(8.7f, 17), new(8.7f, 15), new(10.6f, 13.8f), new(11.2f, 11.8f),
                        new(13.4f, 13.2f), new(15.3f, 15), new(15.3f, 17), new(15.3f, 18.8f),
                        new(13.9f, 20.2f), new(12, 20.2f)], 13));
                    Check(OsdNative.GdipClosePathFigure(core.Handle));
                    Gradient(g, core, 0xFFFFF4E0, Mix(color, 0xFFFFFFFF, 0.5f));
                }
                break;
        }
    }

    /// <summary>Outer arc of circle 1 outside circle 2, then circle 2's arc back inside circle 1.</summary>
    private static void Crescent(IntPtr path, float x1, float y1, float r1, float x2, float y2, float r2)
    {
        double d = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
        double a = (r1 * r1 - r2 * r2 + d * d) / (2 * d);
        double theta = Math.Atan2(y2 - y1, x2 - x1) * 180 / Math.PI;
        float alpha = (float)(Math.Acos(a / r1) * 180 / Math.PI);
        float beta = (float)(Math.Acos((d - a) / r2) * 180 / Math.PI);
        Check(OsdNative.GdipAddPathArc(path, x1 - r1, y1 - r1, 2 * r1, 2 * r1, (float)theta + alpha, 360 - 2 * alpha));
        Check(OsdNative.GdipAddPathArc(path, x2 - r2, y2 - r2, 2 * r2, 2 * r2, (float)theta + 180 + beta, -2 * beta));
    }

    private static OsdNative.PointF[] Star(float cx, float cy, float outer, float inner)
    {
        var points = new OsdNative.PointF[8];
        for (int i = 0; i < 8; i++)
        {
            double angle = Math.PI / 4 * i - Math.PI / 2;
            float r = i % 2 == 0 ? outer : inner;
            points[i] = new((float)(cx + r * Math.Cos(angle)), (float)(cy + r * Math.Sin(angle)));
        }
        return points;
    }

    private static void Polygon(IntPtr path, OsdNative.PointF[] points)
    {
        Check(OsdNative.GdipStartPathFigure(path));
        Check(OsdNative.GdipAddPathLine2(path, points, points.Length));
        Check(OsdNative.GdipClosePathFigure(path));
    }

    private static Gdi NewPath()
    {
        Check(OsdNative.GdipCreatePath(1, out var path)); // winding: overlapping parts stay filled
        return new Gdi(path, OsdNative.GdipDeletePath);
    }

    private static void Fill(IntPtr g, Gdi path, uint color)
    {
        Check(OsdNative.GdipCreateSolidFill(color, out var brush));
        try { Check(OsdNative.GdipFillPath(g, brush, path.Handle)); }
        finally { OsdNative.GdipDeleteBrush(brush); }
    }

    private static void Gradient(IntPtr g, Gdi path, uint top, uint bottom)
    {
        var rect = new OsdNative.RectF(0, 2, 24, 20);
        Check(OsdNative.GdipCreateLineBrushFromRect(ref rect, top, bottom, 1, 0, out var brush));
        try { Check(OsdNative.GdipFillPath(g, brush, path.Handle)); }
        finally { OsdNative.GdipDeleteBrush(brush); }
    }

    private static void Stroke(IntPtr g, Gdi path, uint color, float width)
    {
        Check(OsdNative.GdipCreatePen1(color, width, 2, out var pen));
        try
        {
            Check(OsdNative.GdipSetPenLineJoin(pen, 2)); // round, no miter spikes on the bolt
            Check(OsdNative.GdipDrawPath(g, pen, path.Handle));
        }
        finally { OsdNative.GdipDeletePen(pen); }
    }

    private static void Line(IntPtr g, uint color, float width, OsdNative.PointF[] points)
    {
        Check(OsdNative.GdipCreatePen1(color, width, 2, out var pen));
        try { Check(OsdNative.GdipDrawLines(g, pen, points, points.Length)); }
        finally { OsdNative.GdipDeletePen(pen); }
    }

    private static uint WithAlpha(uint color, byte alpha) => (color & 0x00FFFFFF) | (uint)alpha << 24;

    private static uint Mix(uint color, uint target, float t)
    {
        uint Channel(int shift) => (uint)Math.Round(((color >> shift) & 0xFF) * (1 - t) + ((target >> shift) & 0xFF) * t);
        return 0xFF000000 | Channel(16) << 16 | Channel(8) << 8 | Channel(0);
    }

    private readonly struct Gdi(IntPtr handle, Func<IntPtr, int> delete) : IDisposable
    {
        public IntPtr Handle => handle;
        public void Dispose() { if (handle != 0) delete(handle); }
    }

    private static void Check(int status)
    {
        if (status != 0) throw new InvalidOperationException($"GDI+ status {status}");
    }

    public void Dispose()
    {
        Hide();
        OsdNative.DestroyWindow(_window);
        ReleaseSurface();
        OsdNative.GdiplusShutdown(_gdiplus);
    }
}
