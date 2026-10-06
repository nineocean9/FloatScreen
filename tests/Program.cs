using System.Net;
using System.Net.Sockets;
using System.Text;
using FloatScreen;
using Microsoft.Web.WebView2.Core;

namespace FloatScreen.TestHarness;

internal static partial class Program
{
    private static int _passed;

    [STAThread]
    private static int Main(string[] args)
    {
        var artifacts = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../artifacts"));
        Directory.CreateDirectory(artifacts);
        try
        {
            CheckCore(artifacts);
            CheckStageTwoCore(artifacts);
            if (args.Contains("--ui"))
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                RunBrowserSmoke(artifacts);
            }
            Console.WriteLine($"PASS: {_passed} checks");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"FAIL: {error}");
            return 1;
        }
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        _passed++;
        Console.WriteLine($"PASS: {name}");
    }

    private static bool SameSettings(AppSettings left, AppSettings right)
        => System.Text.Json.JsonSerializer.Serialize(left) == System.Text.Json.JsonSerializer.Serialize(right);

    private static void CheckCore(string artifacts)
    {
        Assert(WebAddress.TryParse(" bilibili.com/video/BV1example ", out var uri)
            && uri!.Scheme == "https", "bare links normalize to HTTPS");
        Assert(WebAddress.TryParse("http://127.0.0.1:8123/video", out _), "HTTP links and ports accepted");
        foreach (var bad in new[] { "", "   ", "not a link", "javascript:alert(1)", "file:///C:/Windows",
            "data:text/html,hello", "ftp://example.com", "https://user:pass@example.com", "https://", "https://x\n.example.com" })
            Assert(!WebAddress.TryParse(bad, out _), $"invalid or non-web address rejected: {bad.Replace('\n', ' ')}");

        var store = new SettingsStore(Path.Combine(artifacts, "config-" + Guid.NewGuid().ToString("N")));
        Assert(store.Load().AlwaysOnTop, "first launch defaults to topmost");
        var expected = new AppSettings { X = -1024, Y = 80, Width = 700, Height = 480,
            HasPosition = true, AlwaysOnTop = false, LastUrl = "https://www.bilibili.com/" };
        store.Save(expected);
        Assert(SameSettings(new SettingsStore(store.DataDirectory).Load(), expected), "settings round-trip across instances");
        store.Save(expected with { Width = 900 });
        Assert(store.Load().Width == 900 && !Directory.EnumerateFiles(store.DataDirectory, "*.tmp").Any(),
            "settings replacement completes without temporary residue");
        File.WriteAllText(Path.Combine(store.DataDirectory, "settings.json"), "{broken");
        Assert(SameSettings(store.Load(), new AppSettings()) && store.LoadWarning != null,
            "corrupt settings recover to defaults with a warning");

        Rectangle[] monitors = [new(0, 0, 1920, 1040), new(-1280, 0, 1280, 984)];
        Assert(WindowPlacement.Restore(expected, monitors).Location == new Point(-1024, 80),
            "negative coordinates on a second monitor restored");
        var disconnected = WindowPlacement.Restore(expected, [monitors[0]]);
        Assert(monitors[0].Contains(disconnected), "disconnected monitor relocates window into visible workspace");
        var extreme = WindowPlacement.Restore(expected with { X = int.MaxValue, Y = int.MinValue,
            Width = int.MaxValue, Height = -1 }, monitors);
        Assert(monitors[0].Contains(extreme) && extreme.Height >= 340,
            "out-of-range saved geometry clamped without overflow");
    }

    private static void RunBrowserSmoke(string artifacts)
    {
        using var server = new SmokeServer();
        var store = new SettingsStore(Path.Combine(artifacts, "webview-profile-" + Guid.NewGuid().ToString("N")));
        using var form = new MainForm(store, enableTray: false) { ShowInTaskbar = false, TopMost = false };
        // The smoke window stays outside the desktop and never takes foreground focus.
        form.Location = new Point(-16000, -16000);
        Exception? failure = null;
        using var timeout = new System.Windows.Forms.Timer { Interval = 45000 };
        timeout.Tick += (_, _) =>
        {
            failure = new TimeoutException("WebView2 smoke exceeded 45 seconds.");
            form.Close();
        };
        form.Shown += async (_, _) =>
        {
            timeout.Start();
            try
            {
                await form.InitializeBrowserAsync();
                Assert(form.Browser.CoreWebView2 != null, "native WebView2 initializes");
                await Capture(form, Path.Combine(artifacts, "player-empty.png"));
                Assert(!form.Navigate("javascript:alert('blocked')") && form.StatusText.Contains("链接无效"),
                    "invalid input produces a visible error without navigation");
                await LoadPage(form, server.Url + "one");
                Assert(form.Browser.CoreWebView2!.DocumentTitle == "本地视频验收", "HTTP page renders in the real browser");
                for (var attempt = 0; attempt < 50; attempt++)
                {
                    var ready = await form.Browser.CoreWebView2.ExecuteScriptAsync("window.clipReady === true");
                    if (ready == "true") break;
                    await Task.Delay(100);
                }
                Assert(await form.Browser.CoreWebView2.ExecuteScriptAsync(
                    "window.clipReady === true && !document.querySelector('video').paused && document.querySelector('video').currentTime > 0") == "true",
                    "fixed local WebM video really plays");
                await CheckStageTwoBrowser(form, store, artifacts);
                await Capture(form, Path.Combine(artifacts, "player-820.png"));
                form.Size = new Size(520, 340);
                await Task.Delay(150);
                Assert(form.Controls.OfType<ToolStrip>().First(strip => strip is not StatusStrip)
                    .Items.Cast<ToolStripItem>().All(item => item.Placement == ToolStripItemPlacement.Main), "compact toolbar keeps all actions visible without overflow");
                var toolbar = form.Controls.OfType<ToolStrip>().First(strip => strip is not StatusStrip);
                var browserClientPosition = form.PointToClient(form.Browser.PointToScreen(Point.Empty));
                Assert(browserClientPosition.Y >= toolbar.Bottom, "browser does not overlap compact toolbar");
                await Capture(form, Path.Combine(artifacts, "player-520.png"));
                await LoadPage(form, server.Url + "two");
                Assert(form.Browser.CoreWebView2.CanGoBack, "browser navigation builds back history");
                var refused = new TcpListener(IPAddress.Loopback, 0);
                refused.Start();
                var refusedPort = ((IPEndPoint)refused.LocalEndpoint).Port;
                refused.Stop();
                await LoadPage(form, $"http://127.0.0.1:{refusedPort}/", expectSuccess: false);
                Assert(form.StatusText.Contains("无法连接") || form.StatusText.Contains("未能打开"),
                    "network failure surfaces a retryable message");
                await Capture(form, Path.Combine(artifacts, "player-error.png"), includeBrowser: false);
                form.TopMost = true;
                Assert(form.TopMost, "native window topmost toggles on");
                form.TopMost = false;
                Assert(form.Controls.OfType<StatusStrip>().Single().Items[0].Width > 100,
                    "status text has usable display width");
                form.Bounds = new Rectangle(120, 100, 740, 480);
            }
            catch (Exception error) { failure = error; }
            finally
            {
                timeout.Stop();
                form.Close();
            }
        };
        Application.Run(form);
        if (failure != null) throw failure;
        var saved = new SettingsStore(store.DataDirectory).Load();
        Assert(saved.X == 120 && saved.Y == 100 && saved.Width == 740 && !saved.AlwaysOnTop,
            "closing the real window persists its position and pin state");
        Assert(saved.LastUrl == server.Url + "two", "failed navigation does not replace the last successful URL");
    }

    private static async Task LoadPage(MainForm form, string url, bool expectSuccess = true)
    {
        var completion = new TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>();
        void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs args) => completion.TrySetResult(args);
        form.Browser.CoreWebView2.NavigationCompleted += Handler;
        try
        {
            Assert(form.Navigate(url), "navigation request accepted");
            var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert(result.IsSuccess == expectSuccess, expectSuccess ? "navigation completes successfully" : "connection failure reported by browser");
        }
        finally { form.Browser.CoreWebView2.NavigationCompleted -= Handler; }
    }

    private static async Task Capture(MainForm form, string output, bool includeBrowser = true)
    {
        // Draw native chrome and capture the browser separately; off-screen child HWNDs
        // are not included by WinForms DrawToBitmap.
        using var browserPng = new MemoryStream();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        if (includeBrowser && !form.Controls.OfType<Panel>().Single().Controls.OfType<Panel>().Single().Visible)
        {
            await form.Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, browserPng);
            browserPng.Position = 0;
            using var browserImage = Image.FromStream(browserPng);
            var screenLocation = form.Browser.PointToScreen(Point.Empty);
            // DrawToBitmap includes native non-client chrome; use window coordinates,
            // not client coordinates, when placing the independently captured browser.
            var location = new Point(screenLocation.X - form.Left, screenLocation.Y - form.Top);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.DrawImage(browserImage, new Rectangle(location, form.Browser.Size));
        }
        bitmap.Save(output);
        Console.WriteLine($"RENDER: {output}");
    }

    private sealed class SmokeServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cancel = new();
        private readonly byte[] _video = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures/smoke.webm"));
        public string Url { get; }

        public SmokeServer()
        {
            _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
            _ = Serve();
        }

        private async Task Serve()
        {
            try
            {
                while (!_cancel.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_cancel.Token);
                    await using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                    var request = await reader.ReadLineAsync(_cancel.Token) ?? "";
                    string? range = null;
                    while (await reader.ReadLineAsync(_cancel.Token) is { Length: > 0 } header)
                        if (header.StartsWith("Range: bytes=", StringComparison.OrdinalIgnoreCase)) range = header[13..];
                    var html = """
                        <!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>本地视频验收</title>
                        <style>html,body{margin:0;background:#13161c;color:#e2e8f0;font-family:'Microsoft YaHei UI',sans-serif}main{padding:28px}h1{font-size:20px;margin:0 0 8px}p{color:#9ca9b9;font-size:13px}video{width:100%;max-height:300px;background:#1f242d}</style>
                        <main><h1>视频播放验收</h1><p>浏览器正在播放本地生成的 WebM 视频。</p><video controls muted loop playsinline src="/smoke.webm"></video></main>
                        <script>const v=document.querySelector('video');v.play().then(()=>window.clipReady=true).catch(error=>window.clipError=String(error));</script>
                        </html>
                        """;
                    var isVideo = request.StartsWith("GET /smoke.webm ", StringComparison.Ordinal);
                    var body = isVideo ? _video : Encoding.UTF8.GetBytes(html);
                    var contentType = isVideo ? "video/webm" : "text/html; charset=utf-8";
                    var response = "200 OK";
                    var rangeHeaders = isVideo ? "Accept-Ranges: bytes\r\n" : "";
                    if (isVideo && range != null)
                    {
                        var endpoints = range.Split('-');
                        if (endpoints.Length == 2 && int.TryParse(endpoints[0], out var start) && start >= 0 && start < body.Length)
                        {
                            var end = int.TryParse(endpoints[1], out var parsedEnd) ? Math.Clamp(parsedEnd, start, body.Length - 1) : body.Length - 1;
                            rangeHeaders += $"Content-Range: bytes {start}-{end}/{body.Length}\r\n";
                            body = body[start..(end + 1)];
                            response = "206 Partial Content";
                        }
                    }
                    var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {response}\r\nContent-Type: {contentType}\r\n{rangeHeaders}Content-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(headers, _cancel.Token);
                    await stream.WriteAsync(body, _cancel.Token);
                }
            }
            catch (Exception error) when (error is OperationCanceledException or SocketException or IOException) { }
        }

        public void Dispose()
        {
            _cancel.Cancel();
            _listener.Stop();
            _cancel.Dispose();
        }
    }
}
