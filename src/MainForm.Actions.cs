using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FloatScreen;

internal sealed partial class MainForm
{
    private readonly BookmarkStore _bookmarks;
    private readonly bool _enableTray;
    private HotkeyService? _hotkeys;
    private WindowModes? _windowModes;
    private CursorHoleService? _cursorHole;
    private VideoController? _videoController;
    private NotifyIcon? _tray;
    private string? _hotkeyWarning;
    private bool _desktopServicesActive;
    private bool _immersiveMode;
    private bool _changingMode;
    private Rectangle _normalWindowBounds;
    private Rectangle _immersiveWindowBounds;
    private Rectangle _immersiveInitialBounds;
    private bool _restoreInitialImmersiveBounds = true;
    private Size _normalMinimumSize;
    private FormWindowState _normalWindowState;
    private readonly Dictionary<Type, Form> _playerWindows = [];
    private bool _windowStateQueued, _hotkeysPausedForWindow;
    private readonly ToolStripMenuItem _passthroughItem = new("沉浸模式：已关闭");
    private ToolStripMenuItem? _trayPassthrough;
    internal bool MousePassthrough => _immersiveMode;
    internal AppSettings Preferences => _settings;
    internal BookmarkStore Bookmarks => _bookmarks;

    private void ResizeAddressBox()
    {
        // Measure the actual items after DPI scaling rather than subtracting a fixed width.
        var otherWidth = _toolbar.Items.Cast<ToolStripItem>().Where(item => item != _address)
            .Sum(item => item.Width + item.Margin.Horizontal);
        var width = Math.Max(90, _toolbar.ClientSize.Width - _toolbar.Padding.Horizontal
            - otherWidth - _address.Margin.Horizontal - 4);
        if (_address.Width != width) _address.Width = width;
    }

    private void BuildMenu()
    {
        Activated += (_, _) => QueuePlayerWindowState();
        Deactivate += (_, _) => QueuePlayerWindowState();
        _pin.CheckedChanged += (_, _) => QueuePlayerWindowState();
        _more.AccessibleName = "更多功能";
        _more.DropDown.ForeColor = SystemColors.ControlText;
        _more.DropDown.Renderer = new ToolbarRenderer();
        if (_more.DropDown is ToolStripDropDownMenu dropdown) dropdown.ShowCheckMargin = true;
        _passthroughItem.Click += (_, _) => SetMousePassthrough(!MousePassthrough);
        _more.DropDownItems.Add(_passthroughItem);
        _danmakuItem.Click += (_, _) => ToggleDanmaku();
        _more.DropDownItems.Add(_danmakuItem);
        _more.DropDownItems.Add(_danmakuStatusItem);
        _more.DropDownItems.Add("设置", null, (_, _) => ShowSettings());
    }

    private void InitializeDesktopServices()
    {
        if (_hotkeys != null) return;
        _desktopServicesActive = true;
        _windowModes = new WindowModes(Handle);
        _cursorHole = new CursorHoleService(this, () => _settings.HoleRadius,
            message => { if (!_closing) SetStatus(message, error: true); });
        _hotkeys = new HotkeyService(Handle);
        _hotkeys.Invoked += async action =>
        {
            if (!PlayerWindowHasFocus()) await ExecuteActionAsync(action);
        };
        _hotkeys.ApplyStartup(_settings.Hotkeys, out var error);
        _hotkeyWarning = string.IsNullOrEmpty(error) ? null : error;
        if (_enableTray && _tray == null)
        {
            var menu = new ContextMenuStrip { ShowCheckMargin = true, Renderer = new ToolbarRenderer() };
            menu.Items.Add("显示播放器", null, (_, _) =>
            {
                SetMousePassthrough(false);
                RestoreWindow();
            });
            _trayPassthrough = new ToolStripMenuItem("沉浸模式：已关闭");
            _trayPassthrough.Click += (_, _) => SetMousePassthrough(!MousePassthrough);
            menu.Items.Add(_trayPassthrough);
            _trayDanmakuItem = new ToolStripMenuItem("弹幕投影：已关闭") { Enabled = _projection != null };
            _trayDanmakuItem.Click += (_, _) => ToggleDanmaku();
            menu.Items.Add(_trayDanmakuItem);
            _trayDanmakuStatusItem = new ToolStripMenuItem("投影状态：已关闭") { Enabled = false };
            menu.Items.Add(_trayDanmakuStatusItem);
            menu.Items.Add("设置", null, (_, _) => { RestoreWindow(); ShowSettings(); });
            menu.Items.Add("历史记录", null, (_, _) => { RestoreWindow(); ShowHistory(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, (_, _) => Close());
            _tray = new NotifyIcon { Text = "浮幕", Icon = UiTheme.AppIcon, ContextMenuStrip = menu, Visible = true };
            _tray.DoubleClick += (_, _) => { SetMousePassthrough(false); RestoreWindow(); };
        }
        if (!_changingMode && _settings.MousePassthrough
            && (_hotkeys.HasBinding(PlayerAction.TogglePassthrough) || _enableTray)) SetMousePassthrough(true);
        UpdatePlayerWindowState();
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == HotkeyService.MessageId && _hotkeys?.Dispatch(message.WParam.ToInt32()) == true)
            return;
        base.WndProc(ref message);
    }

    protected override void OnHandleDestroyed(EventArgs args)
    {
        if (!_closing && !_changingMode && _windowModes != null)
        {
            _settings = _settings with { MousePassthrough = MousePassthrough };
            if (_immersiveMode && WindowState == FormWindowState.Normal) _immersiveWindowBounds = Bounds;
        }
        _hotkeys?.Dispose();
        _cursorHole?.Dispose();
        _hotkeys = null;
        _windowModes = null;
        _cursorHole = null;
        base.OnHandleDestroyed(args);
    }

    protected override void OnHandleCreated(EventArgs args)
    {
        base.OnHandleCreated(args);
        if (_desktopServicesActive && !_closing) InitializeDesktopServices();
    }

    internal async Task ExecuteActionAsync(PlayerAction action)
    {
        if (_closing) return;
        if (action == PlayerAction.ToggleWindow)
        {
            if (Visible) Hide(); else RestoreWindow();
            return;
        }
        if (action == PlayerAction.TogglePassthrough)
        {
            SetMousePassthrough(!MousePassthrough);
            return;
        }
        if (action == PlayerAction.ToggleDanmaku) { ToggleDanmaku(); return; }
        if (!_ready || _videoController == null)
        {
            SetStatus("播放器尚未准备好。", error: true);
            return;
        }
        try
        {
            var message = await _videoController.Execute(action, _settings.SkipSeconds);
            if (!_closing) SetStatus(message);
        }
        catch (Exception error) when (error is InvalidOperationException or COMException or System.Text.Json.JsonException)
        {
            if (!_closing) SetStatus("视频操作未能完成，请刷新网页后重试。", error: true);
        }
    }

    private void RestoreWindow()
    {
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Show();
        Activate();
    }

    internal bool SetMousePassthrough(bool enabled)
    {
        if (_windowModes == null || _changingMode) return false;
        if (enabled == _immersiveMode && _windowModes.MousePassthrough == enabled
            && _windowModes.Borderless == enabled)
        {
            _cursorHole?.SetEnabled(enabled);
            return true;
        }
        _changingMode = true;
        try
        {
            // Clear clipping and input transparency before changing native frame geometry.
            _cursorHole?.SetEnabled(false);
            _windowModes.SetPassthrough(false);
            ApplyImmersiveAppearance(enabled);
            _windowModes.SetPassthrough(enabled);
            _immersiveMode = enabled;
            _cursorHole?.SetEnabled(enabled);
            _passthroughItem.Checked = enabled;
            _passthroughItem.Text = enabled ? "沉浸模式：已开启" : "沉浸模式：已关闭";
            if (_trayPassthrough != null)
            {
                _trayPassthrough.Checked = enabled;
                _trayPassthrough.Text = _passthroughItem.Text;
            }
            SetStatus(enabled ? $"沉浸模式已开启 · {_settings.Hotkeys.TogglePassthrough} 退出，或使用托盘菜单"
                : "沉浸模式已关闭");
            Invalidate(true);
            return true;
        }
        catch (Win32Exception)
        {
            try
            {
                _windowModes?.SetPassthrough(false);
                ApplyImmersiveAppearance(false);
            }
            catch (Win32Exception) { }
            _immersiveMode = false;
            _passthroughItem.Checked = false;
            _passthroughItem.Text = "沉浸模式：已关闭";
            if (_trayPassthrough != null) { _trayPassthrough.Checked = false; _trayPassthrough.Text = _passthroughItem.Text; }
            SetStatus("沉浸模式切换失败，请重启播放器后再试。", error: true);
            return false;
        }
        finally { _changingMode = false; }
    }

    private void ApplyImmersiveAppearance(bool enabled)
    {
        if (enabled && !_immersiveMode)
        {
            _normalWindowBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            _normalMinimumSize = MinimumSize;
            _normalWindowState = WindowState == FormWindowState.Minimized ? FormWindowState.Normal : WindowState;
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            // Keep the video viewport at its existing screen position and size.
            _immersiveWindowBounds = new Rectangle(_content.PointToScreen(Point.Empty), _content.Size);
            _immersiveInitialBounds = _immersiveWindowBounds;
            if (_restoreInitialImmersiveBounds && _settings.MousePassthrough && _settings.ImmersiveBounds != null)
                _immersiveWindowBounds = RestoreImmersiveBounds(_settings.ImmersiveBounds);
            _restoreInitialImmersiveBounds = false;
        }

        SuspendLayout();
        try
        {
            if (enabled)
            {
                if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
                MinimumSize = new Size(160, 90);
                _toolbar.Visible = false;
                _statusBar.Visible = false;
                // Native frame flags avoid recreating WebView2's parent window just to hide chrome.
                _windowModes?.SetBorderless(true);
                if (!_immersiveWindowBounds.IsEmpty) Bounds = _immersiveWindowBounds;
            }
            else
            {
                if (_immersiveMode && !_normalWindowBounds.IsEmpty)
                {
                    var current = CurrentImmersiveBounds();
                    _normalWindowBounds = AdjustedNormalBounds(current);
                    _immersiveInitialBounds = current;
                }
                _windowModes?.SetBorderless(false);
                _toolbar.Visible = true;
                _statusBar.Visible = true;
                if (!_normalWindowBounds.IsEmpty)
                {
                    WindowState = FormWindowState.Normal;
                    MinimumSize = _normalMinimumSize;
                    Bounds = _normalWindowBounds;
                    WindowState = _normalWindowState;
                }
            }
        }
        finally { ResumeLayout(performLayout: true); }
    }

    internal bool AddCurrentBookmark()
    {
        if (!_ready || !_historyDocumentAvailable || _empty.Visible || !WebAddress.TryParse(Browser.Source?.AbsoluteUri, out _))
        {
            SetStatus("请先打开需要收藏的网页。", error: true);
            return false;
        }
        try
        {
            var added = _bookmarks.Add(Browser.Source!.AbsoluteUri, Browser.CoreWebView2.DocumentTitle);
            SetStatus(added ? "已收藏当前网页" : "这个网页已经收藏。" );
            return added;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            SetStatus(_bookmarks.LoadWarning ?? "收藏未能保存，请检查用户目录权限。", error: true);
            return false;
        }
    }

    private void UpdateBookmarkButton()
    {
        if (_closing) return;
        var url = _ready ? Browser.Source?.AbsoluteUri : null;
        _bookmarkPageButton.Enabled = _ready && _historyDocumentAvailable && WebAddress.TryParse(url, out _);
        _bookmarkPageButton.Checked = url != null && _bookmarks.Items.Any(item => item.Url == url);
        _bookmarkPageButton.ToolTipText = _bookmarkPageButton.Checked ? "当前网页已收藏，可在收藏列表中管理" : "收藏当前网页";
    }

    private void ShowBookmarks()
    {
        ShowPlayerWindow(() => new BookmarksDialog(_bookmarks), dialog =>
        {
            if (dialog.DialogResult == DialogResult.OK && dialog.SelectedUrl != null) Navigate(dialog.SelectedUrl);
        });
    }

    private void ShowSettings()
    {
        ShowPlayerWindow(() =>
        {
            var dialog = new SettingsDialog(_settings, _hotkeyWarning);
            dialog.ApplySettings = (options, seconds, radius, danmaku) =>
            {
                var applied = ApplyPreferences(options, seconds, out var error, radius, danmaku);
                UpdatePlayerWindowState();
                return applied ? null : error;
            };
            return dialog;
        });
    }

    private void ShowPlayerWindow<T>(Func<T> create, Action<T>? closed = null) where T : Form
    {
        if (_closing) return;
        if (_playerWindows.TryGetValue(typeof(T), out var existing) && !existing.IsDisposed)
        {
            if (!existing.Visible) existing.Show(this);
            existing.BringToFront(); existing.Activate();
            return;
        }
        var dialog = create();
        _playerWindows[typeof(T)] = dialog;
        dialog.TopMost = TopMost;
        dialog.StartPosition = FormStartPosition.Manual;
        dialog.Shown += (_, _) =>
        {
            var area = Screen.FromControl(this).WorkingArea;
            dialog.Location = new Point(
                Math.Clamp(Left + (Width - dialog.Width) / 2, area.Left, Math.Max(area.Left, area.Right - dialog.Width)),
                Math.Clamp(Top + (Height - dialog.Height) / 2, area.Top, Math.Max(area.Top, area.Bottom - dialog.Height)));
            dialog.BringToFront(); dialog.Activate();
        };
        dialog.Activated += (_, _) => QueuePlayerWindowState();
        dialog.Deactivate += (_, _) => QueuePlayerWindowState();
        dialog.FormClosed += (_, _) =>
        {
            _playerWindows.Remove(typeof(T));
            if (!_closing) closed?.Invoke(dialog);
            QueuePlayerWindowState();
        };
        try
        {
            // Ownership keeps the window above the player; Show leaves the player enabled.
            dialog.Show(this);
            UpdatePlayerWindowState();
        }
        catch
        {
            _playerWindows.Remove(typeof(T)); dialog.Dispose();
            UpdatePlayerWindowState();
            throw;
        }
    }

    private void QueuePlayerWindowState()
    {
        if (_closing || IsDisposed || !IsHandleCreated || _windowStateQueued) return;
        _windowStateQueued = true;
        BeginInvoke(() =>
        {
            _windowStateQueued = false;
            if (!_closing && !IsDisposed) UpdatePlayerWindowState();
        });
    }

    private bool PlayerWindowHasFocus()
    {
        // Include native dialogs owned by a settings/history window, such as the color picker.
        var foreground = GetForegroundWindow();
        for (var depth = 0; foreground != 0 && depth < 16; depth++, foreground = GetWindow(foreground, 4))
            if (_playerWindows.Values.Any(window => !window.IsDisposed && window.IsHandleCreated && window.Handle == foreground))
                return true;
        return false;
    }

    private void UpdatePlayerWindowState()
    {
        if (_closing || IsDisposed) return;
        foreach (var window in _playerWindows.Values)
            if (!window.IsDisposed && window.TopMost != TopMost) window.TopMost = TopMost;
        var editing = PlayerWindowHasFocus();
        _projection?.SetSuspended(editing);
        if (_hotkeys == null) return;
        if (editing)
        {
            _hotkeys.ReleaseAll();
            _hotkeysPausedForWindow = true;
        }
        else if (_hotkeysPausedForWindow)
        {
            _hotkeys.ApplyStartup(_settings.Hotkeys, out var error);
            _hotkeyWarning = string.IsNullOrEmpty(error) ? null : error;
            _hotkeysPausedForWindow = false;
        }
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);

    internal bool ApplyPreferences(HotkeyOptions options, int seconds, out string error, int? holeRadius = null, DanmakuOptions? danmaku = null)
    {
        if (seconds is < 1 or > 60) { error = "跳转秒数需要在 1 到 60 之间。"; return false; }
        if (holeRadius is < 40 or > 800) { error = "圆洞半径需要在 40 到 800 之间。"; return false; }
        if (danmaku != null && !DanmakuRuleMatcher.TryCreate(danmaku.AliasRules, out _, out error)) return false;
        if (!options.TryGetBindings(out _, out error)) return false;
        if (_hotkeys != null && !_hotkeys.Apply(options, out error)) return false;
        var previous = _settings;
        var next = CaptureWindowPreferences() with
        {
            Hotkeys = options, SkipSeconds = seconds, HoleRadius = holeRadius ?? _settings.HoleRadius,
            Danmaku = (danmaku ?? _settings.Danmaku).Sanitize()
        };
        try { _store.Save(next); }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            _hotkeys?.Apply(previous.Hotkeys, out _);
            error = "设置未能保存，请检查用户目录权限。";
            return false;
        }
        _settings = next;
        _cursorHole?.Refresh();
        _projection?.OptionsChanged();
        _hotkeyWarning = null;
        SetStatus("设置已保存，快捷键立即生效。");
        error = "";
        return true;
    }
}
