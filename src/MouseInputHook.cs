using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FloatScreen;

internal sealed class MouseInputHook : IDisposable
{
    private readonly HookCallback _callback;
    private readonly HashSet<Keys> _suppressedUps = [];
    private nint _handle;

    public MouseInputHook(Func<HotkeyBinding, bool> onInput)
    {
        _callback = (code, message, dataPointer) =>
        {
            if (code >= 0)
            {
                var data = Marshal.PtrToStructure<MouseData>(dataPointer);
                if ((data.Flags & 1) == 0)
                {
                    var released = message.ToInt32() switch
                    {
                        0x202 => Keys.LButton, 0x205 => Keys.RButton, 0x208 => Keys.MButton,
                        0x20C => (data.Data >> 16) == 1 ? Keys.XButton1 : Keys.XButton2, _ => Keys.None
                    };
                    if (released != Keys.None && _suppressedUps.Remove(released)) return 1;
                    var key = message.ToInt32() switch
                    {
                        0x201 => Keys.LButton, 0x204 => Keys.RButton, 0x207 => Keys.MButton,
                        0x20B => (data.Data >> 16) == 1 ? Keys.XButton1 : Keys.XButton2,
                        0x20A => unchecked((short)(data.Data >> 16)) > 0 ? HotkeyBinding.WheelUp : HotkeyBinding.WheelDown,
                        _ => Keys.None
                    };
                    if (key != Keys.None && onInput(new HotkeyBinding(CurrentModifiers(), key)))
                    {
                        if (key != HotkeyBinding.WheelUp && key != HotkeyBinding.WheelDown) _suppressedUps.Add(key);
                        return 1;
                    }
                }
            }
            return CallNextHookEx(_handle, code, message, dataPointer);
        };
        _handle = SetWindowsHookEx(14, _callback, GetModuleHandle(null), 0);
        if (_handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public static uint CurrentModifiers()
    {
        uint value = 0;
        if ((GetAsyncKeyState(0x12) & 0x8000) != 0) value |= 1;
        if ((GetAsyncKeyState(0x11) & 0x8000) != 0) value |= 2;
        if ((GetAsyncKeyState(0x10) & 0x8000) != 0) value |= 4;
        return value;
    }

    public void Dispose()
    {
        if (_handle == 0) return;
        UnhookWindowsHookEx(_handle);
        _handle = 0;
        GC.KeepAlive(_callback);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseData
    {
        public int X, Y;
        public uint Data, Flags, Time;
        public nuint ExtraInfo;
    }
    private delegate nint HookCallback(int code, nint message, nint data);
    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int kind, HookCallback callback, nint module, uint thread);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint handle, int code, nint message, nint data);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(nint handle);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);
}
