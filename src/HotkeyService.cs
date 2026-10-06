using System.Runtime.InteropServices;

namespace FloatScreen;

internal enum PlayerAction { TogglePlay, Backward, Forward, ToggleWindow, TogglePassthrough, ToggleDanmaku }

internal sealed record HotkeyOptions
{
    public string TogglePlay { get; init; } = "Ctrl+Alt+Space";
    public string Backward { get; init; } = "Ctrl+Alt+Left";
    public string Forward { get; init; } = "Ctrl+Alt+Right";
    public string ToggleWindow { get; init; } = "Ctrl+Alt+H";
    public string TogglePassthrough { get; init; } = "Ctrl+Alt+F10";
    public string ToggleDanmaku { get; init; } = "Ctrl+Alt+D";

    public string Get(PlayerAction action) => action switch
    {
        PlayerAction.TogglePlay => TogglePlay, PlayerAction.Backward => Backward, PlayerAction.Forward => Forward,
        PlayerAction.ToggleWindow => ToggleWindow, PlayerAction.TogglePassthrough => TogglePassthrough, _ => ToggleDanmaku
    };

    public bool TryGetBindings(out Dictionary<PlayerAction, HotkeyBinding> bindings, out string error)
    {
        bindings = [];
        error = "";
        foreach (var action in Enum.GetValues<PlayerAction>())
        {
            var text = Get(action);
            if (!HotkeyBinding.TryParse(text, out var binding))
            {
                error = "请输入键盘按键或鼠标中键、侧键、滚轮。鼠标左键、右键需要搭配修饰键。";
                return false;
            }
            if (bindings.Values.Contains(binding))
            {
                error = "不同操作不能使用同一个快捷键。";
                return false;
            }
            bindings.Add(action, binding);
        }
        return true;
    }
}

internal readonly record struct HotkeyBinding(uint Modifiers, Keys Key)
{
    public const Keys WheelUp = (Keys)0x100;
    public const Keys WheelDown = (Keys)0x101;
    public bool IsMouse => Key is Keys.LButton or Keys.RButton or Keys.MButton or Keys.XButton1 or Keys.XButton2
        || Key == WheelUp || Key == WheelDown;

    public static bool TryParse(string? text, out HotkeyBinding binding)
    {
        binding = default;
        var parts = text?.Split('+', StringSplitOptions.TrimEntries);
        if (parts == null || parts.Length == 0) return false;
        uint modifiers = 0;
        foreach (var part in parts[..^1])
        {
            var modifier = part.ToLowerInvariant() switch { "alt" => 1u, "ctrl" => 2u, "shift" => 4u, _ => 0u };
            if (modifier == 0 || (modifiers & modifier) != 0) return false;
            modifiers |= modifier;
        }
        var keyText = parts[^1];
        if (keyText.Length == 1 && char.IsAsciiDigit(keyText[0])) keyText = "D" + keyText;
        var key = keyText.ToLowerInvariant() switch
        {
            "mouseleft" => Keys.LButton, "mouseright" => Keys.RButton, "mousemiddle" => Keys.MButton,
            "mousex1" => Keys.XButton1, "mousex2" => Keys.XButton2,
            "wheelup" => WheelUp, "wheeldown" => WheelDown, _ => Keys.None
        };
        if (key == Keys.None && (!Enum.TryParse(keyText, ignoreCase: true, out key)
            || !(key is >= Keys.A and <= Keys.Z or >= Keys.D0 and <= Keys.D9
                or >= Keys.F1 and <= Keys.F11 or Keys.Space or Keys.Left or Keys.Right
                or Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown
                or Keys.Enter or Keys.Tab or Keys.Escape or Keys.Back or Keys.Insert or Keys.Delete
                or Keys.OemMinus or Keys.Oemplus or Keys.Oemcomma or Keys.OemPeriod
                or Keys.OemOpenBrackets or Keys.OemCloseBrackets or Keys.OemBackslash
                or Keys.OemPipe or Keys.OemSemicolon or Keys.OemQuotes or Keys.Oemtilde))) return false;
        if ((key is Keys.LButton or Keys.RButton) && modifiers == 0) return false;
        binding = new HotkeyBinding(modifiers, key);
        return true;
    }

    public static string Format(Keys key, uint modifiers)
    {
        var parts = new List<string>();
        if ((modifiers & 2) != 0) parts.Add("Ctrl");
        if ((modifiers & 1) != 0) parts.Add("Alt");
        if ((modifiers & 4) != 0) parts.Add("Shift");
        parts.Add(key switch
        {
            Keys.LButton => "MouseLeft", Keys.RButton => "MouseRight", Keys.MButton => "MouseMiddle",
            Keys.XButton1 => "MouseX1", Keys.XButton2 => "MouseX2",
            WheelUp => "WheelUp", WheelDown => "WheelDown", _ => key.ToString()
        });
        return string.Join('+', parts);
    }
}

internal sealed class HotkeyService(nint window) : IDisposable
{
    public const int MessageId = 0x0312;
    private const int BaseId = 0x6100;
    private Dictionary<PlayerAction, HotkeyBinding> _bindings = [];
    private MouseInputHook? _mouseHook;
    public event Action<PlayerAction>? Invoked;
    public bool HasBinding(PlayerAction action) => _bindings.ContainsKey(action);

    public bool ApplyStartup(HotkeyOptions options, out string error)
    {
        if (!options.TryGetBindings(out var bindings, out error)) return false;
        ReleaseAll();
        var failures = new List<string>();
        foreach (var (action, binding) in bindings)
        {
            if (TryRegister(action, binding, out var failure)) continue;
            failures.Add(failure);
        }
        error = string.Join("\n", failures);
        return failures.Count == 0;
    }

    public bool Apply(HotkeyOptions options, out string error)
    {
        if (!options.TryGetBindings(out var next, out error)) return false;
        var old = _bindings;
        ReleaseAll();
        if (RegisterAll(next, out error)) return true;
        var failure = error;
        ReleaseAll();
        if (!RegisterAll(old, out var restoreError))
            failure += " 原快捷键也未能恢复：" + restoreError;
        error = failure;
        return false;
    }

    private bool RegisterAll(Dictionary<PlayerAction, HotkeyBinding> bindings, out string error)
    {
        error = "";
        foreach (var (action, binding) in bindings)
        {
            if (!TryRegister(action, binding, out error)) return false;
        }
        return true;
    }

    private bool TryRegister(PlayerAction action, HotkeyBinding binding, out string error)
    {
        error = "";
        if (binding.IsMouse)
        {
            try
            {
                _mouseHook ??= new MouseInputHook(mouse =>
                {
                    foreach (var (candidate, assigned) in _bindings)
                    {
                        if (assigned != mouse) continue;
                        // Keep the low-level hook short; the window handles the action later.
                        return PostMessage(window, MessageId, BaseId + (int)candidate, 0);
                    }
                    return false;
                });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                error = $"{Describe(action)}：鼠标监听无法启动。";
                return false;
            }
        }
        else if (!RegisterHotKey(window, BaseId + (int)action, binding.Modifiers | 0x4000, (uint)binding.Key))
        {
            error = $"{Describe(action)}：{HotkeyBinding.Format(binding.Key, binding.Modifiers)} 被占用或不可注册（{Marshal.GetLastWin32Error()}），请更换。";
            return false;
        }
        _bindings.Add(action, binding);
        return true;
    }

    public bool Dispatch(int id)
    {
        var action = (PlayerAction)(id - BaseId);
        if (!_bindings.ContainsKey(action)) return false;
        Invoked?.Invoke(action);
        return true;
    }

    public void ReleaseAll()
    {
        foreach (var (action, binding) in _bindings)
            if (!binding.IsMouse) UnregisterHotKey(window, BaseId + (int)action);
        _mouseHook?.Dispose();
        _mouseHook = null;
        _bindings = [];
    }

    public static string Describe(PlayerAction action) => action switch
    {
        PlayerAction.TogglePlay => "播放／暂停", PlayerAction.Backward => "向后跳转",
        PlayerAction.Forward => "向前跳转", PlayerAction.ToggleWindow => "隐藏／显示",
        PlayerAction.TogglePassthrough => "沉浸模式", _ => "弹幕投影"
    };

    public void Dispose() => ReleaseAll();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, int message, nint wParam, nint lParam);
}
