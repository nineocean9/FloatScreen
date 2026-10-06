using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace FloatScreen;

internal sealed class DanmakuOverlayWindow : Form
{
    private Bitmap? _canvas;
    private nint _memoryDc, _dib, _oldBitmap, _pixels;
    public Rectangle ProjectionBounds { get; private set; }
    public float DrawingScale => DeviceDpi / 96f;
    protected override bool ShowWithoutActivation => true;

    public DanmakuOverlayWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        AutoScaleMode = AutoScaleMode.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Text = "浮幕弹幕投影";
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x80000 | 0x20 | 0x08000000 | 0x80; // Layered, transparent, no activate, tool window.
            return parameters;
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x84) { message.Result = -1; return; } // HTTRANSPARENT.
        if (message.Msg == 0x21) { message.Result = 3; return; } // MA_NOACTIVATE.
        base.WndProc(ref message);
    }

    protected override void OnHandleCreated(EventArgs args)
    {
        base.OnHandleCreated(args);
        // Reset any framework-created constant-alpha state before per-pixel presentation.
        var style = GetWindowLong(Handle, -20);
        SetWindowLong(Handle, -20, style & ~0x80000);
        SetWindowLong(Handle, -20, style | 0x80000);
    }

    public bool Configure(Rectangle bounds)
    {
        if (ProjectionBounds == bounds && _canvas != null) return false;
        Hide();
        ProjectionBounds = bounds;
        Bounds = bounds;
        _ = Handle;
        ReleaseBuffer();
        _canvas = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
        var screenDc = GetDC(0);
        try
        {
            _memoryDc = CreateCompatibleDC(screenDc);
            var info = new BitmapInfo
            {
                Header = new BitmapHeader { Size = 40, Width = bounds.Width, Height = -bounds.Height,
                    Planes = 1, BitCount = 32, ImageSize = checked((uint)(bounds.Width * bounds.Height * 4)) }
            };
            _dib = CreateDIBSection(screenDc, ref info, 0, out _pixels, 0, 0);
            if (_memoryDc == 0 || _dib == 0 || _pixels == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            _oldBitmap = SelectObject(_memoryDc, _dib);
            if (_oldBitmap == 0 || _oldBitmap == -1) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        catch { ReleaseBuffer(); throw; }
        finally { if (screenDc != 0) ReleaseDC(0, screenDc); }
        return true;
    }

    public void Present(IReadOnlyList<DanmakuSprite> sprites, double time, Rectangle? excluded)
    {
        if (_canvas == null || IsDisposed) return;
        if (sprites.Count == 0) { Hide(); return; }
        using (var graphics = Graphics.FromImage(_canvas))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            if (excluded is { } rectangle)
                graphics.SetClip(new Rectangle(rectangle.X - ProjectionBounds.X, rectangle.Y - ProjectionBounds.Y,
                    rectangle.Width, rectangle.Height), CombineMode.Exclude);
            foreach (var sprite in sprites)
                graphics.DrawImage(sprite.Image,
                    new RectangleF((float)sprite.X(time, _canvas.Width), sprite.Y, sprite.Image.Width, sprite.Image.Height),
                    new RectangleF(0, 0, sprite.Image.Width, sprite.Image.Height), GraphicsUnit.Pixel);
        }
        var data = _canvas.LockBits(new Rectangle(Point.Empty, _canvas.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            unsafe
            {
                var stride = _canvas.Width * 4;
                if (data.Stride == stride)
                    Buffer.MemoryCopy(data.Scan0.ToPointer(), _pixels.ToPointer(), (long)stride * _canvas.Height, (long)stride * _canvas.Height);
                else
                    for (var row = 0; row < _canvas.Height; row++)
                        Buffer.MemoryCopy((data.Scan0 + row * data.Stride).ToPointer(), (_pixels + row * stride).ToPointer(), stride, stride);
            }
        }
        finally { _canvas.UnlockBits(data); }
        var destination = new NativePoint(ProjectionBounds.X, ProjectionBounds.Y);
        var size = new NativeSize(_canvas.Width, _canvas.Height);
        var origin = new NativePoint(0, 0);
        var blend = new BlendFunction { Operation = 0, Alpha = 255, Format = 1 };
        // Do not call SetLayeredWindowAttributes on this window; it uses per-pixel alpha.
        if (!UpdateLayeredWindow(Handle, 0, ref destination, ref size, _memoryDc, ref origin, 0, ref blend, 2))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!Visible) Show();
    }

    private void ReleaseBuffer()
    {
        _canvas?.Dispose();
        _canvas = null;
        if (_memoryDc != 0 && _oldBitmap != 0 && _oldBitmap != -1) SelectObject(_memoryDc, _oldBitmap);
        if (_dib != 0) DeleteObject(_dib);
        if (_memoryDc != 0) DeleteDC(_memoryDc);
        _dib = _memoryDc = _oldBitmap = _pixels = 0;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) ReleaseBuffer();
        base.Dispose(disposing);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint(int x, int y) { public int X = x, Y = y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeSize(int width, int height) { public int Width = width, Height = height; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct BlendFunction { public byte Operation, Flags, Alpha, Format; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapHeader
    {
        public uint Size; public int Width, Height; public ushort Planes, BitCount;
        public uint Compression, ImageSize; public int XPelsPerMeter, YPelsPerMeter; public uint ColorsUsed, ColorsImportant;
    }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public BitmapHeader Header; public uint Color; }
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(nint window, int index, int value);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint pixels, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint item);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint item);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(nint dc);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateLayeredWindow(nint window, nint destinationDc, ref NativePoint destination,
        ref NativeSize size, nint sourceDc, ref NativePoint source, uint color, ref BlendFunction blend, uint flags);
}
