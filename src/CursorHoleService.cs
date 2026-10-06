using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FloatScreen;

internal sealed class CursorHoleService : IDisposable
{
    private readonly Form _owner;
    private readonly Func<int> _radius;
    private readonly Action<string> _onError;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 300 };
    private bool _enabled;
    private bool _hasHole;
    private bool _updating;
    private bool _disposed;
    private Point _lastCenter;
    private Size _lastSize;
    private int _lastRadius;

    public CursorHoleService(Form owner, Func<int> radius, Action<string> onError)
    {
        _owner = owner;
        _radius = radius;
        _onError = onError;
        _timer.Tick += OnGeometryChanged;
        _owner.LocationChanged += OnGeometryChanged;
        _owner.SizeChanged += OnGeometryChanged;
        _owner.VisibleChanged += OnGeometryChanged;
        _owner.DpiChanged += OnDpiChanged;
    }

    public void SetEnabled(bool enabled)
    {
        if (_disposed) return;
        _enabled = enabled;
        Refresh();
    }

    public void Refresh()
    {
        if (_disposed || _updating) return;
        _updating = true;
        try
        {
            if (!_enabled || !_owner.Visible || _owner.WindowState == FormWindowState.Minimized
                || !_owner.IsHandleCreated)
            {
                _timer.Stop();
                RestoreShape();
                return;
            }

            if (!GetWindowRect(_owner.Handle, out var bounds) || !GetCursorPos(out var cursor))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var radius = (int)Math.Round(Math.Clamp(_radius(), 40, 800) * _owner.DeviceDpi / 96d);
            var dx = cursor.X < bounds.Left ? bounds.Left - cursor.X : Math.Max(0, cursor.X - bounds.Right);
            var dy = cursor.Y < bounds.Top ? bounds.Top - cursor.Y : Math.Max(0, cursor.Y - bounds.Bottom);
            var near = (long)dx * dx + (long)dy * dy < (long)radius * radius;
            _timer.Interval = near ? 50 : 300;
            _timer.Start();
            if (!near) { RestoreShape(); return; }

            var center = new Point(cursor.X - bounds.Left, cursor.Y - bounds.Top);
            var size = new Size(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
            if (_hasHole && center == _lastCenter && size == _lastSize && radius == _lastRadius) return;
            ApplyHole(center, size, radius);
            _hasHole = true;
            _lastCenter = center;
            _lastSize = size;
            _lastRadius = radius;
        }
        catch (Win32Exception)
        {
            _enabled = false;
            _timer.Stop();
            try { RestoreShape(); } catch (Win32Exception) { }
            _onError("透明圆洞未能更新，请关闭沉浸模式后重新开启。" );
        }
        finally { _updating = false; }
    }

    private void ApplyHole(Point center, Size size, int radius)
    {
        var full = CreateRectRgn(0, 0, size.Width, size.Height);
        var circle = CreateEllipticRgn(center.X - radius, center.Y - radius,
            center.X + radius, center.Y + radius);
        try
        {
            if (full == 0 || circle == 0 || CombineRgn(full, full, circle, 4) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (SetWindowRgn(_owner.Handle, full, true) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            // Windows owns the applied region after SetWindowRgn succeeds.
            full = 0;
        }
        finally
        {
            if (circle != 0) DeleteObject(circle);
            if (full != 0) DeleteObject(full);
        }
    }

    private void RestoreShape()
    {
        if (!_hasHole) return;
        if (_owner.IsHandleCreated && SetWindowRgn(_owner.Handle, 0, true) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        _hasHole = false;
    }

    private void OnGeometryChanged(object? sender, EventArgs args) => Refresh();
    private void OnDpiChanged(object? sender, DpiChangedEventArgs args) => Refresh();

    public void Dispose()
    {
        if (_disposed) return;
        _enabled = false;
        _disposed = true;
        _timer.Stop();
        try { RestoreShape(); } catch (Win32Exception) { }
        _timer.Dispose();
        _owner.LocationChanged -= OnGeometryChanged;
        _owner.SizeChanged -= OnGeometryChanged;
        _owner.VisibleChanged -= OnGeometryChanged;
        _owner.DpiChanged -= OnDpiChanged;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint window, out NativeRect bounds);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out NativePoint cursor);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(nint window, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateEllipticRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern int CombineRgn(nint result, nint first, nint second, int mode);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint region);
}
