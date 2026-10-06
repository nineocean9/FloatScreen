using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FloatScreen;

internal sealed class WindowModes(nint window)
{
    private const int ExtendedStyle = -20;
    private const int WindowStyle = -16;
    private const int BorderFlags = 0x00C00000 | 0x00040000;
    private const int Transparent = 0x20;
    private const int Layered = 0x80000;
    private int _ownedFlags;
    private int _removedBorderFlags;
    public bool MousePassthrough { get; private set; }
    public bool Borderless { get; private set; }

    public void SetBorderless(bool enabled)
    {
        if (enabled == Borderless) return;
        var original = GetWindowLong(window, WindowStyle);
        var removed = enabled ? original & BorderFlags : _removedBorderFlags;
        SetStyle(enabled ? original & ~BorderFlags : original | removed, WindowStyle);
        try { RefreshFrame(); }
        catch { SetStyle(original, WindowStyle); throw; }
        _removedBorderFlags = enabled ? removed : 0;
        Borderless = enabled;
    }

    public void SetPassthrough(bool enabled)
    {
        if (enabled == MousePassthrough) return;
        var original = GetWindowLong(window, ExtendedStyle);
        var owned = enabled ? (Transparent | Layered) & ~original : _ownedFlags;
        var next = enabled ? original | Transparent | Layered : original & ~owned;
        SetStyle(next);
        try
        {
            if (enabled && !SetLayeredWindowAttributes(window, 0, 255, 0x2))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            RefreshFrame();
        }
        catch
        {
            SetStyle(original);
            throw;
        }
        _ownedFlags = enabled ? owned : 0;
        MousePassthrough = enabled;
    }

    private void RefreshFrame()
    {
        if (!SetWindowPos(window, 0, 0, 0, 0, 0, 0x1 | 0x2 | 0x4 | 0x10 | 0x20))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private void SetStyle(int style, int index = ExtendedStyle)
    {
        if (SetWindowLong(window, index, style) == 0 && Marshal.GetLastWin32Error() != 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(nint window, int index, int style);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(nint window, uint color, byte alpha, uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}
