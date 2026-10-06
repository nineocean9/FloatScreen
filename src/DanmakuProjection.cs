using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Web.WebView2.Core;

namespace FloatScreen;

internal sealed class DanmakuProjection : IDisposable
{
    private static readonly string ClockScript = ReadClockScript();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowReadingFromString
    };
    private readonly MainForm _owner;
    private readonly CoreWebView2 _browser;
    private readonly Func<DanmakuOptions> _options;
    private readonly Action<string, bool> _status;
    private readonly Action<bool> _enabledChanged;
    private readonly BilibiliDanmakuSource _source = new();
    private readonly DanmakuOverlayWindow _overlay = new();
    private readonly DanmakuTimeline _timeline = new();
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 200 };
    private readonly AnimationFramePump _render;
    private readonly MediaAnimationClock _animationClock = new();
    private CancellationTokenSource? _loadCancel;
    private VideoClock? _clock;
    private int _generation;
    private string _loadedKey = "", _loadingKey = "";
    private IReadOnlyList<DanmakuEntry> _loadedEntries = [];
    private bool _busy, _disposed, _suspended, _resetOnSample;
    private int _unavailableSamples;
    private double _lastRendered = double.NaN;
    private Rectangle? _lastExcluded;
    public bool Enabled { get; private set; }

    public DanmakuProjection(MainForm owner, CoreWebView2 browser, Func<DanmakuOptions> options,
        Action<string, bool> status, Action<bool> enabledChanged)
    {
        _owner = owner; _browser = browser; _options = options; _status = status; _enabledChanged = enabledChanged;
        DanmakuRuleMatcher.TryCreate(_options().AliasRules, out var matcher, out _);
        _source.SetRules(matcher);
        _poll.Tick += async (_, _) => await Sample();
        _render = new AnimationFramePump(Render);
        _owner.VisibleChanged += OnOwnerVisibility;
        _owner.SizeChanged += OnOwnerVisibility;
    }

    private static string ReadClockScript()
    {
        using var stream = typeof(DanmakuProjection).Assembly.GetManifestResourceStream("FloatScreen.video-clock.js")!;
        using var reader = new StreamReader(stream);
        return "(" + reader.ReadToEnd() + ")()";
    }

    public void SetEnabled(bool enabled)
    {
        if (_disposed || Enabled == enabled) return;
        Enabled = enabled;
        _enabledChanged(enabled);
        if (!enabled)
        {
            _generation++;
            _loadCancel?.Cancel();
            _poll.Stop(); _render.Stop(); _overlay.Hide(); _timeline.ClearSprites();
            _loadingKey = ""; _lastRendered = double.NaN;
            _status("弹幕投影已关闭", false);
            return;
        }
        _resetOnSample = true;
        _unavailableSamples = 0;
        _status("弹幕投影已开启，正在读取当前视频…", false);
        ResumeIfVisible();
    }

    public void Navigating()
    {
        _generation++;
        _loadCancel?.Cancel();
        _clock = null; _loadingKey = _loadedKey = "";
        _loadedEntries = [];
        _unavailableSamples = 0;
        _timeline.SetEntries([], 0); _overlay.Hide(); _resetOnSample = true;
    }

    public void SetSuspended(bool suspended)
    {
        _suspended = suspended;
        ResumeIfVisible();
    }

    public void OptionsChanged()
    {
        DanmakuRuleMatcher.TryCreate(_options().AliasRules, out var matcher, out _);
        _source.SetRules(matcher);
        _loadedEntries = _loadedEntries.Select(entry => entry with { MergeKey = matcher.Create(entry.Text) }).ToArray();
        _timeline.SetEntries(_loadedEntries, MediaTime());
        _resetOnSample = true;
        _lastRendered = double.NaN;
        _timeline.ClearSprites();
        _overlay.Hide();
    }

    private void OnOwnerVisibility(object? sender, EventArgs args) => ResumeIfVisible();

    private bool CanProject => Enabled && !_suspended && _owner.Visible && _owner.WindowState != FormWindowState.Minimized;

    private void ResumeIfVisible()
    {
        if (_disposed) return;
        if (!CanProject)
        {
            _poll.Stop(); _render.Stop(); _overlay.Hide(); _resetOnSample = true;
            return;
        }
        _poll.Start(); _render.Start();
    }

    private double MediaTime()
    {
        if (_clock == null) return 0;
        return _animationClock.Time;
    }

    private async Task Sample()
    {
        if (_busy || _disposed || !CanProject) return;
        _busy = true;
        var generation = _generation;
        var queryStarted = Stopwatch.GetTimestamp();
        try
        {
            var raw = await _browser.ExecuteScriptAsync(ClockScript);
            if (_disposed || !Enabled || generation != _generation) return;
            var clock = JsonSerializer.Deserialize<VideoClock>(raw, JsonOptions);
            if (clock == null || !clock.Available || !double.IsFinite(clock.Time) || clock.Time < 0
                || !double.IsFinite(clock.Rate) || clock.Rate <= 0)
            {
                _overlay.Hide(); _timeline.ClearSprites(); _resetOnSample = true;
                if (++_unavailableSamples == 15)
                    _status("尚未找到可同步的 B站视频，请打开视频并开始播放。", false);
                return;
            }
            _unavailableSamples = 0;
            var oldTime = MediaTime();
            var changed = _clock?.Key != clock.Key;
            var jumped = changed || _resetOnSample || clock.Seeking || Math.Abs(clock.Time - oldTime) > 1.0
                || (_clock != null && clock.Time < _clock.Time - 0.15);
            var running = !clock.Paused && !clock.Buffering && !clock.Seeking;
            var sampledTime = clock.Time + (running ? Math.Min(0.1, Stopwatch.GetElapsedTime(queryStarted).TotalSeconds / 2) * clock.Rate : 0);
            _animationClock.Update(sampledTime, clock.Rate, running, jumped);
            _clock = clock;
            if (changed)
            {
                _loadCancel?.Cancel(); _loadedKey = _loadingKey = "";
                _loadedEntries = [];
                _timeline.SetEntries([], clock.Time); _overlay.Hide();
            }
            if (jumped) { _timeline.Reset(clock.Time); _lastRendered = double.NaN; _resetOnSample = false; }
            if (_loadedKey != clock.Key && _loadingKey != clock.Key) _ = Load(clock, generation);
        }
        catch (Exception error) when (error is COMException or InvalidOperationException or JsonException)
        {
            if (!_disposed && Enabled && generation == _generation)
                StopWithError("无法同步视频时间，请刷新视频后重新开启弹幕投影。" );
        }
        finally { _busy = false; }
    }

    private async Task Load(VideoClock clock, int generation)
    {
        _loadCancel?.Cancel(); _loadCancel?.Dispose();
        var cancellation = _loadCancel = new CancellationTokenSource();
        _loadingKey = clock.Key;
        try
        {
            var result = await _source.Load(clock, cancellation.Token);
            if (_disposed || !Enabled || generation != _generation || _clock?.Key != clock.Key || cancellation.IsCancellationRequested) return;
            _loadedKey = clock.Key;
            _loadedEntries = result.Entries;
            _timeline.SetEntries(result.Entries, MediaTime());
            _lastRendered = double.NaN;
            _status(result.Entries.Count == 0 ? "当前视频没有可投影的文字弹幕。" : $"已读取 {result.Entries.Count} 条弹幕，按视频时间投影。", false);
        }
        catch (OperationCanceledException)
        {
            if (!_disposed && Enabled && !cancellation.IsCancellationRequested && generation == _generation && _clock?.Key == clock.Key)
                StopWithError("读取弹幕超时，请稍后重新开启投影。" );
        }
        catch (Exception error) when (error is System.Net.Http.HttpRequestException or IOException
            or System.Xml.XmlException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            if (!_disposed && Enabled && generation == _generation && _clock?.Key == clock.Key)
                StopWithError("弹幕读取失败，视频仍可正常播放。请稍后重新开启投影。" );
        }
        finally { if (!_disposed && ReferenceEquals(_loadCancel, cancellation)) _loadingKey = ""; }
    }

    private void Render()
    {
        if (_disposed || !CanProject || _resetOnSample || _clock == null || _loadedKey != _clock.Key) return;
        try
        {
            var options = _options(); // Validated at load/save; rule compilation never runs per frame.
            var screen = Screen.AllScreens.FirstOrDefault(item => item.DeviceName == options.ScreenDeviceName)
                ?? Screen.FromControl(_owner);
            var radius = Math.Min(screen.Bounds.Width / 2, options.DisplayRadius);
            var width = Math.Max(200, Math.Min(screen.Bounds.Width, radius * 2));
            var bounds = new Rectangle(screen.Bounds.X + (screen.Bounds.Width - width) / 2, screen.Bounds.Y, width,
                Math.Max(40, screen.Bounds.Height * options.AreaPercent / 100));
            var changed = _overlay.Configure(bounds);
            var time = MediaTime();
            Rectangle? excluded = _owner.Visible ? _owner.Bounds : null;
            if (!changed && Math.Abs(time - _lastRendered) < 0.000001 && excluded == _lastExcluded) return;
            if (changed) { _timeline.Reset(time); _lastRendered = double.NaN; }
            _timeline.Advance(time, bounds.Size, options, _overlay.DrawingScale);
            _overlay.Present(_timeline.Active, time, excluded);
            _lastRendered = time; _lastExcluded = excluded;
        }
        catch (Exception error) when (error is Win32Exception or ExternalException or ArgumentException or OutOfMemoryException)
        {
            StopWithError("弹幕投影绘制失败，请关闭投影后检查显示设置。" );
        }
    }

    private void StopWithError(string message)
    {
        SetEnabled(false);
        _status(message, true);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Enabled = false; _generation++;
        _loadCancel?.Cancel();
        _poll.Stop(); _render.Stop(); _poll.Dispose(); _render.Dispose();
        _owner.VisibleChanged -= OnOwnerVisibility; _owner.SizeChanged -= OnOwnerVisibility;
        _timeline.Dispose(); _overlay.Close(); _overlay.Dispose(); _source.Dispose();
        // In-flight requests observe cancellation; disposing the CTS here is safe for its copied token.
        _loadCancel?.Dispose();
    }
}
