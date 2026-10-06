using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace FloatScreen;

internal sealed partial class MainForm : Form
{
    private static readonly Color Canvas = Color.FromArgb(19, 22, 28);
    private static readonly Color Surface = Color.FromArgb(31, 38, 49);
    private static readonly Color Ink = Color.FromArgb(226, 232, 240);
    private static readonly Color Muted = Color.FromArgb(156, 169, 185);
    private static readonly Color Accent = Color.FromArgb(132, 220, 185);
    private readonly SettingsStore _store;
    private AppSettings _settings;
    private readonly ToolStrip _toolbar = new() { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
    private readonly ToolStripButton _back = new("返回");
    private readonly ToolStripButton _refresh = new("刷新");
    private readonly ToolStripTextBox _address = new() { AutoSize = false, AccessibleName = "攻略链接" };
    private readonly ToolStripButton _historyButton = new("历史");
    private readonly ToolStripButton _bookmarkListButton = new("收藏列表");
    private readonly ToolStripButton _bookmarkPageButton = new("收藏此页");
    private readonly ToolStripButton _pin = new("置顶") { CheckOnClick = true };
    private readonly ToolStripDropDownButton _more = new("更多") { AutoSize = false, Size = new Size(56, 32), ForeColor = Ink };
    private readonly Panel _content = new() { Dock = DockStyle.Fill };
    private readonly Panel _empty = new() { Dock = DockStyle.Fill };
    private readonly Label _emptyTitle = new() { Text = "把攻略放在手边", AutoSize = true };
    private readonly Label _emptyDetail = new() { Text = "粘贴视频链接，按 Enter 打开。", AutoSize = true };
    private readonly LinkLabel _runtimeLink = new()
    {
        Text = "下载 WebView2 运行时", AutoSize = true, Visible = false
    };
    private readonly StatusStrip _statusBar = new() { SizingGrip = true };
    private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private Task? _initialization;
    private bool _ready;
    private bool _closing;
    private ulong _navigationId;
    private double _startupZoomFactor;
    private bool _restoreStartupZoom = true;
    internal WebView2 Browser { get; } = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Canvas };
    internal string StatusText => _status.Text ?? "";

    public MainForm(SettingsStore store, bool enableTray = true)
    {
        _store = store;
        _settings = store.Load();
        _startupZoomFactor = _settings.ZoomFactor;
        _bookmarks = new BookmarkStore(store.DataDirectory);
        _bookmarks.Changed += UpdateBookmarkButton;
        _history = new HistoryStore(store.DataDirectory);
        InitializeHistory();
        _enableTray = enableTray;
        Text = "浮幕";
        Icon = UiTheme.AppIcon;
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Canvas;
        ForeColor = Ink;
        MinimumSize = new Size(520, 340);
        StartPosition = FormStartPosition.Manual;
        var screens = Screen.AllScreens.OrderByDescending(screen => screen.Primary)
            .Select(screen => screen.WorkingArea).ToArray();
        Bounds = WindowPlacement.Restore(_settings, screens);
        TopMost = _settings.AlwaysOnTop;
        BuildInterface();
        _pin.Checked = TopMost;
        _address.Text = _settings.LastUrl;
        SetStatus("正在准备播放器…");
        Shown += async (_, _) =>
        {
            RestoreSavedWindowPlacement();
            InitializeDesktopServices();
            await InitializeBrowserAsync();
        };
        FormClosing += OnClosing;
    }

    private void BuildInterface()
    {
        _toolbar.BackColor = Surface;
        _toolbar.ForeColor = Ink;
        _toolbar.Padding = new Padding(8, 7, 8, 7);
        _toolbar.AutoSize = false;
        _toolbar.Height = 48;
        _toolbar.Renderer = new ToolbarRenderer();
        _toolbar.Font = Font;
        _toolbar.CanOverflow = false;
        _toolbar.ImageScalingSize = new Size(16, 16);
        _address.Font = Font;
        _address.BackColor = Canvas;
        _address.ForeColor = Ink;
        _address.BorderStyle = BorderStyle.FixedSingle;
        _address.TextBox.PlaceholderText = "攻略链接 · Enter 打开";
        _address.ToolTipText = "输入或粘贴网页链接，按 Enter 打开";
        _address.Margin = new Padding(8, 0, 8, 0);
        foreach (var button in new[] { _back, _refresh, _historyButton, _bookmarkListButton, _bookmarkPageButton, _pin })
        {
            button.AutoSize = true;
            button.Padding = new Padding(5, 5, 5, 5);
            button.Margin = new Padding(1, 0, 1, 0);
            button.DisplayStyle = ToolStripItemDisplayStyle.Text;
            button.AccessibleName = button.Text;
        }
        _back.Image = UiTheme.Glyph(ToolbarGlyph.Back); _refresh.Image = UiTheme.Glyph(ToolbarGlyph.Refresh);
        _back.DisplayStyle = _refresh.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _historyButton.Image = UiTheme.Glyph(ToolbarGlyph.History);
        _bookmarkListButton.Image = UiTheme.Glyph(ToolbarGlyph.Bookmarks);
        _bookmarkPageButton.Image = UiTheme.Glyph(ToolbarGlyph.AddBookmark);
        _historyButton.DisplayStyle = _bookmarkListButton.DisplayStyle = _bookmarkPageButton.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
        _more.AutoSize = true; _more.Padding = new Padding(5, 5, 5, 5);
        _back.ToolTipText = "返回上一页";
        _refresh.ToolTipText = "重新打开当前链接";
        _pin.ToolTipText = "让播放器保持在其他窗口上方";
        _historyButton.ToolTipText = "查看、搜索和管理历史记录";
        _historyButton.AccessibleName = "历史记录";
        _bookmarkListButton.ToolTipText = "打开收藏列表";
        _bookmarkPageButton.ToolTipText = "收藏当前网页";
        BuildMenu();
        _toolbar.Items.AddRange([_back, _refresh, _address, _historyButton, _bookmarkListButton, _bookmarkPageButton, _pin, _more]);
        _toolbar.Layout += (_, _) => ResizeAddressBox();
        _toolbar.SizeChanged += (_, _) => ResizeAddressBox();
        _back.Enabled = false;
        _refresh.Enabled = false;
        _bookmarkPageButton.Enabled = false;
        _back.Click += (_, _) => { if (_ready && Browser.CanGoBack) Browser.GoBack(); };
        _refresh.Click += (_, _) => Navigate(_address.Text);
        _bookmarkListButton.Click += (_, _) => ShowBookmarks();
        _bookmarkPageButton.Click += (_, _) => AddCurrentBookmark();
        _historyButton.Click += (_, _) => ShowHistory();
        _pin.CheckedChanged += (_, _) => TopMost = _pin.Checked;
        _address.KeyDown += (_, args) =>
        {
            if (args.KeyCode != Keys.Enter) return;
            args.SuppressKeyPress = true;
            if (_ready) Navigate(_address.Text);
        };

        _empty.BackColor = Canvas;
        _emptyTitle.ForeColor = Ink;
        _emptyTitle.Font = new Font(Font.FontFamily, 19F, FontStyle.Bold);
        _emptyDetail.ForeColor = Muted;
        _runtimeLink.LinkColor = Accent;
        _runtimeLink.ActiveLinkColor = Ink;
        _runtimeLink.LinkClicked += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/")
                    { UseShellExecute = true });
            }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                SetStatus("无法打开下载页面，请在浏览器中搜索 WebView2 运行时。", error: true);
            }
        };
        _empty.Controls.AddRange([_emptyTitle, _emptyDetail, _runtimeLink]);
        _empty.Resize += (_, _) => PositionEmptyMessage();
        _content.Controls.Add(Browser);
        _content.Controls.Add(_empty);
        _statusBar.BackColor = Surface;
        _statusBar.ForeColor = Muted;
        _statusBar.Items.Add(_status);
        Controls.Add(_content);
        Controls.Add(_toolbar);
        Controls.Add(_statusBar);
        PositionEmptyMessage();
    }

    private void PositionEmptyMessage()
    {
        var y = Math.Max(20, (_empty.Height - 110) / 2);
        _emptyTitle.Location = new Point(Math.Max(16, (_empty.Width - _emptyTitle.Width) / 2), y);
        _emptyDetail.Location = new Point(Math.Max(16, (_empty.Width - _emptyDetail.Width) / 2), y + 54);
        _runtimeLink.Location = new Point(Math.Max(16, (_empty.Width - _runtimeLink.Width) / 2), y + 85);
    }

    internal Task InitializeBrowserAsync() => _initialization ??= InitializeCoreAsync();

    private async Task InitializeCoreAsync()
    {
        try
        {
            Directory.CreateDirectory(_store.DataDirectory);
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder:
                Path.Combine(_store.DataDirectory, "WebView2"));
            if (_closing) return;
            await Browser.EnsureCoreWebView2Async(environment);
            if (_closing) return;
            var core = Browser.CoreWebView2;
            Browser.ZoomFactor = _startupZoomFactor;
            Browser.ZoomFactorChanged += (_, _) =>
            {
                if (!_closing) _settings = _settings with { ZoomFactor = Browser.ZoomFactor };
            };
            _videoController = new VideoController(core);
            InitializeProjection(core);
            core.WebMessageReceived += (_, args) =>
            {
                try
                {
                    using var message = System.Text.Json.JsonDocument.Parse(args.WebMessageAsJson);
                    if (message.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                        && message.RootElement.TryGetProperty("type", out var type)
                        && type.ValueKind == System.Text.Json.JsonValueKind.String
                        && type.GetString() == "floatscreen-play-error")
                        SetStatus("网页阻止了播放，请先点击网页播放器后重试。", error: true);
                }
                catch (System.Text.Json.JsonException) { }
            };
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.NavigationStarting += OnNavigationStarting;
            core.NavigationCompleted += OnNavigationCompleted;
            core.SourceChanged += (_, args) =>
            {
                if (WebAddress.TryParse(core.Source, out var uri)) _address.Text = uri!.AbsoluteUri;
                UpdateBookmarkButton();
                if (!args.IsNewDocument)
                {
                    RecordHistoryPage(newVisit: false);
                    ScheduleHistoryUpdate();
                }
            };
            core.HistoryChanged += (_, _) => _back.Enabled = core.CanGoBack;
            core.DocumentTitleChanged += (_, _) =>
            {
                var title = core.DocumentTitle;
                Text = string.IsNullOrWhiteSpace(title) ? "浮幕" : $"{title} · 浮幕";
                ScheduleHistoryUpdate();
            };
            core.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                Navigate(args.Uri);
            };
            core.ProcessFailed += (_, _) =>
            {
                _projection?.SetEnabled(false);
                _historyTimer.Stop(); _historyDocumentAvailable = false;
                _ready = false;
                _bookmarkPageButton.Enabled = _refresh.Enabled = _back.Enabled = false;
                ShowEmpty("播放器已停止", "请关闭窗口后重新启动。", error: true);
            };
            _ready = true;
            _refresh.Enabled = true;
            SetStatus(_store.LoadWarning ?? _hotkeyWarning ?? "就绪 · 输入链接后按 Enter，右侧可查看历史和收藏");
            if (WebAddress.TryParse(_settings.LastUrl, out var lastAddress))
                Navigate(lastAddress!.AbsoluteUri);
        }
        catch (Exception error) when (error is WebView2RuntimeNotFoundException or IOException
            or UnauthorizedAccessException or System.Runtime.InteropServices.COMException
            or InvalidOperationException or ArgumentException)
        {
            if (_closing) return;
            var missingRuntime = error is WebView2RuntimeNotFoundException;
            _runtimeLink.Visible = missingRuntime;
            ShowEmpty("播放器未能启动", missingRuntime
                ? "安装 WebView2 运行时后重新打开。"
                : "请检查运行环境和用户目录权限后重新打开。", error: true);
        }
    }

    internal bool Navigate(string input)
    {
        if (!_ready) return false;
        if (!WebAddress.TryParse(input, out var address))
        {
            SetStatus("链接无效，请输入 http:// 或 https:// 网页链接。", error: true);
            _address.Focus();
            return false;
        }
        _address.Text = address!.AbsoluteUri;
        try
        {
            Browser.CoreWebView2.Navigate(address.AbsoluteUri);
            return true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException
            or System.Runtime.InteropServices.COMException)
        {
            SetStatus("页面未能打开，请重新启动播放器后再试。", error: true);
            return false;
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (!WebAddress.TryParse(args.Uri, out _))
        {
            args.Cancel = true;
            SetStatus("此链接需要其他应用打开，播放器仅支持网页链接。", error: true);
            return;
        }
        _navigationId = args.NavigationId;
        _historyTimer.Stop(); _historyNavigationPending = true; _historyDocumentAvailable = false;
        UpdateBookmarkButton();
        _projection?.Navigating();
        _empty.Visible = false;
        SetStatus("正在打开网页…");
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (_closing || args.NavigationId != _navigationId) return;
        _historyNavigationPending = false;
        _historyDocumentAvailable = args.IsSuccess;
        UpdateBookmarkButton();
        if (args.IsSuccess)
        {
            if (_restoreStartupZoom)
            {
                // A site's remembered zoom may change during navigation; restore the
                // last session's value once the first page has finished loading.
                Browser.ZoomFactor = _startupZoomFactor;
                _settings = _settings with { ZoomFactor = Browser.ZoomFactor };
                _restoreStartupZoom = false;
            }
            _settings = _settings with { LastUrl = Browser.Source?.AbsoluteUri ?? "" };
            SetStatus("已打开 · 使用网页上的播放器控制视频");
            RecordHistoryPage(newVisit: true);
        }
        else if (args.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
        {
            var detail = args.WebErrorStatus switch
            {
                CoreWebView2WebErrorStatus.HostNameNotResolved => "找不到网站，请检查链接。",
                CoreWebView2WebErrorStatus.ConnectionAborted or CoreWebView2WebErrorStatus.ConnectionReset
                    or CoreWebView2WebErrorStatus.CannotConnect => "无法连接网站，请检查网络后点击刷新。",
                CoreWebView2WebErrorStatus.Timeout => "打开超时，请点击刷新重试。",
                _ => "页面未能打开，请检查链接或网络后点击刷新。"
            };
            ShowEmpty("页面未能打开", detail, error: true);
        }
    }

    private void ShowEmpty(string title, string detail, bool error)
    {
        _emptyTitle.Text = title;
        _emptyDetail.Text = detail;
        _empty.Visible = true;
        PositionEmptyMessage();
        SetStatus(detail, error);
    }

    private void SetStatus(string message, bool error = false)
    {
        _status.Text = _hotkeyWarning == null ? message : message + " · 部分快捷键冲突，请打开设置";
        _status.ToolTipText = _hotkeyWarning == null ? message : message + "\n" + _hotkeyWarning;
        _status.ForeColor = error ? Color.FromArgb(247, 168, 153) : Muted;
    }

    private void OnClosing(object? sender, FormClosingEventArgs args)
    {
        _historyTimer.Stop();
        RecordHistoryPage(newVisit: false);
        _historyTimer.Dispose();
        _closing = true;
        _projection?.Dispose();
        var currentSettings = CaptureWindowPreferences();
        _cursorHole?.Dispose();
        _hotkeys?.Dispose();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        try
        {
            _store.Save(currentSettings);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "窗口设置未能保存，下次启动将使用上次保存的位置。", "浮幕",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private sealed class ToolbarRenderer : ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs args)
        {
            using var brush = new SolidBrush(args.ToolStrip is ToolStripDropDown ? Color.White : Surface);
            args.Graphics.FillRectangle(brush, args.AffectedBounds);
        }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs args)
        {
            if (args.ToolStrip is not ToolStripDropDown) return;
            using var pen = new Pen(UiTheme.Border);
            args.Graphics.DrawRectangle(pen, 0, 0, args.ToolStrip.Width - 1, args.ToolStrip.Height - 1);
        }
        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs args)
        {
            if (args.Item is not ToolStripButton button) return;
            if (!button.Selected && !button.Checked && !button.Pressed) return;
            using var brush = new SolidBrush(button.Checked ? Color.FromArgb(44, 78, 68) : Color.FromArgb(49, 58, 71));
            args.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, button.Size));
        }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs args)
        {
            if (!args.Item.Selected) return;
            using var brush = new SolidBrush(UiTheme.Selection);
            args.Graphics.FillRectangle(brush, new Rectangle(2, 1, args.Item.Width - 4, args.Item.Height - 2));
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs args)
        {
            if (args.ToolStrip is ToolStripDropDown) args.TextColor = args.Item.Enabled ? UiTheme.Text : UiTheme.Muted;
            else args.TextColor = args.Item.Enabled ? args.Item is ToolStripButton { Checked: true } ? Accent : Ink : Color.FromArgb(108, 122, 139);
            base.OnRenderItemText(args);
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs args)
        {
            using var pen = new Pen(UiTheme.Border);
            args.Graphics.DrawLine(pen, 28, args.Item.Height / 2, args.Item.Width - 8, args.Item.Height / 2);
        }
    }
}
