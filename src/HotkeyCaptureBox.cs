namespace FloatScreen;

internal sealed class HotkeyCaptureBox : TextBox
{
    private bool _capturing;
    private string _previous = "";
    private MouseInputHook? _mouseHook;
    public event Action<string>? CaptureMessage;

    public HotkeyCaptureBox()
    {
        ReadOnly = true;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseDown(MouseEventArgs args)
    {
        if (!_capturing && args.Button == MouseButtons.Left)
        {
            Focus();
            _previous = Text;
            _capturing = true;
            Text = "请按键或操作鼠标…";
            BackColor = Color.FromArgb(231, 245, 237);
            CaptureMessage?.Invoke("正在读取下一次输入；Esc 取消，鼠标左键／右键需配合 Ctrl、Alt 或 Shift。");
            try
            {
                _mouseHook = new MouseInputHook(binding =>
                {
                    if (!_capturing) return false;
                    var text = HotkeyBinding.Format(binding.Key, binding.Modifiers);
                    if (!HotkeyBinding.TryParse(text, out _)) return false;
                    // Avoid changing native controls or unhooking inside the hook callback.
                    BeginInvoke(() => Accept(text));
                    return true;
                });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                CaptureMessage?.Invoke("鼠标监听未能启动，仍可读取键盘输入。");
            }
        }
        base.OnMouseDown(args);
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (!_capturing) return base.ProcessCmdKey(ref message, keyData);
        var key = keyData & Keys.KeyCode;
        if (key == Keys.Escape && (keyData & Keys.Modifiers) == 0) { CancelCapture(); return true; }
        if (key is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or Keys.Menu or Keys.LMenu
            or Keys.RMenu or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or Keys.LWin or Keys.RWin) return true;
        uint modifiers = 0;
        if ((keyData & Keys.Control) != 0) modifiers |= 2;
        if ((keyData & Keys.Alt) != 0) modifiers |= 1;
        if ((keyData & Keys.Shift) != 0) modifiers |= 4;
        Accept(HotkeyBinding.Format(key, modifiers));
        return true;
    }

    private void Accept(string text)
    {
        if (!_capturing) return;
        if (!HotkeyBinding.TryParse(text, out _))
        {
            CaptureMessage?.Invoke("此输入不可用；F12 不可使用，鼠标左键／右键需要修饰键。");
            return;
        }
        EndCapture();
        Text = text;
        CaptureMessage?.Invoke("已读取 " + text + "，点击保存后生效。");
    }

    private void CancelCapture()
    {
        EndCapture();
        Text = _previous;
        CaptureMessage?.Invoke("已取消读取。" );
    }

    private void EndCapture()
    {
        _capturing = false;
        _mouseHook?.Dispose();
        _mouseHook = null;
        BackColor = SystemColors.Window;
    }

    protected override void OnLostFocus(EventArgs args)
    {
        if (_capturing) CancelCapture();
        base.OnLostFocus(args);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) EndCapture();
        base.Dispose(disposing);
    }
}
