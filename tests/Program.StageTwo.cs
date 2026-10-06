using System.Runtime.InteropServices;
using System.Text.Json;
using FloatScreen;

namespace FloatScreen.TestHarness;

internal static partial class Program
{
    private static void CheckStageTwoCore(string artifacts)
    {
        Assert(new HotkeyOptions().TryGetBindings(out var bindings, out _) && bindings.Count == 6,
            "six distinct default hotkeys are valid");
        foreach (var invalid in new[] { "", "MouseLeft", "MouseRight", "Ctrl+Ctrl+H", "Ctrl+Alt+F12", "Win+Alt+H", "Ctrl+Alt+None" })
            Assert(!HotkeyBinding.TryParse(invalid, out _), $"unsafe or unsupported hotkey rejected: {invalid}");
        Assert(!(new HotkeyOptions { ToggleWindow = "alt+ctrl+space" }).TryGetBindings(out _, out _),
            "duplicate hotkeys rejected regardless of modifier order or case");

        var directory = Path.Combine(artifacts, "stage2-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "settings.json"), "{\"Width\":740,\"AlwaysOnTop\":false}");
        var old = new SettingsStore(directory).Load();
        Assert(old.Width == 740 && old.SkipSeconds == 5 && !old.MousePassthrough && old.Hotkeys == new HotkeyOptions(),
            "stage-one settings migrate without losing window preferences");
        var preferences = old with { SkipSeconds = 12, MousePassthrough = true,
            Hotkeys = new HotkeyOptions { TogglePlay = "Ctrl+Shift+Space" } };
        new SettingsStore(directory).Save(preferences);
        Assert(SameSettings(new SettingsStore(directory).Load(), preferences), "stage-two settings persist across restart");
        File.WriteAllText(Path.Combine(directory, "settings.json"), "{\"Width\":740,\"SkipSeconds\":-10,\"Hotkeys\":null}");
        var repaired = new SettingsStore(directory).Load();
        Assert(repaired.Width == 740 && repaired.SkipSeconds == 5 && repaired.Hotkeys == new HotkeyOptions(),
            "invalid new preferences repaired without resetting window geometry");

        var bookmarks = new BookmarkStore(directory);
        Assert(bookmarks.Add("bilibili.com/video/example", "我的攻略"), "bookmark added with normalized URL");
        Assert(!bookmarks.Add("https://bilibili.com/video/example", "重复") && bookmarks.Items.Count == 1,
            "duplicate bookmark does not create another entry");
        var reopened = new BookmarkStore(directory);
        Assert(reopened.Items.Single().Title == "我的攻略", "bookmark survives reopening store");
        reopened.Remove(reopened.Items.Single());
        Assert(new BookmarkStore(directory).Items.Count == 0, "bookmark deletion is immediately persisted");
        var bookmarkPath = Path.Combine(directory, "bookmarks.json");
        File.WriteAllText(bookmarkPath, "{corrupt");
        var corrupt = new BookmarkStore(directory);
        var refused = false;
        try { corrupt.Add("https://example.com", "新收藏"); }
        catch (InvalidDataException) { refused = true; }
        Assert(refused && File.ReadAllText(bookmarkPath) == "{corrupt", "corrupt bookmark file is never overwritten");
    }

    private static async Task CheckStageTwoBrowser(MainForm form, SettingsStore store, string artifacts)
    {
        await form.ExecuteActionAsync(PlayerAction.TogglePlay);
        Assert(await form.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('video').paused") == "true",
            "player command pauses the real video");
        await form.ExecuteActionAsync(PlayerAction.TogglePlay);
        await Task.Delay(100);
        Assert(await form.Browser.CoreWebView2.ExecuteScriptAsync("!document.querySelector('video').paused") == "true",
            "player command resumes the real video");
        await form.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('video').pause(); document.querySelector('video').currentTime=1");
        await form.ExecuteActionAsync(PlayerAction.Backward);
        Assert(await form.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('video').currentTime === 0") == "true",
            "backward seek clamps at the beginning");
        await form.ExecuteActionAsync(PlayerAction.Forward);
        Console.WriteLine("SEEK STATE: " + await form.Browser.CoreWebView2.ExecuteScriptAsync(
            "JSON.stringify({time:document.querySelector('video').currentTime,duration:document.querySelector('video').duration,paused:document.querySelector('video').paused,seeking:document.querySelector('video').seeking,end:document.querySelector('video').seekable.length ? document.querySelector('video').seekable.end(0) : null})"));
        Console.WriteLine("SEEK MESSAGE: " + form.StatusText);
        Assert(await form.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('video').currentTime <= document.querySelector('video').duration && document.querySelector('video').currentTime > 1") == "true",
            "forward seek clamps at video duration");
        Assert(form.AddCurrentBookmark() && !form.AddCurrentBookmark(), "current-page bookmark added once");
        Assert(new BookmarkStore(store.DataDirectory).Items.Count == 1, "current-page bookmark is saved immediately");

        using var host = new TestMessageHost();
        using var competitor = new HotkeyService(host.Handle);
        Assert(!competitor.Apply(form.Preferences.Hotkeys, out var collision) && collision.Contains("占用"),
            "Windows detects an already registered global hotkey");
        var other = new HotkeyOptions { TogglePlay = "Ctrl+Shift+F6", Backward = "Ctrl+Shift+F7",
            Forward = "Ctrl+Shift+F8", ToggleWindow = "Ctrl+Shift+F9", TogglePassthrough = "Ctrl+Shift+F10", ToggleDanmaku = "Ctrl+Shift+F11" };
        Assert(competitor.Apply(other, out _), "independent native hotkey group registers");
        var previous = form.Preferences;
        Assert(!form.ApplyPreferences(previous.Hotkeys with { TogglePlay = other.TogglePlay }, 8, out _)
            && form.Preferences == previous, "hotkey collision leaves preferences unchanged");
        using var probe = new HotkeyService(host.Handle);
        Assert(!probe.Apply(previous.Hotkeys, out _), "old native hotkeys restored after a conflicting update");
        competitor.Dispose();

        var newKeys = previous.Hotkeys with { TogglePlay = "Ctrl+Shift+Space" };
        Assert(form.ApplyPreferences(newKeys, 8, out _) && new SettingsStore(store.DataDirectory).Load().SkipSeconds == 8,
            "new hotkeys and seek interval saved and applied");
        // Dispatch the same Windows message RegisterHotKey delivers, without sending keys to other apps.
        PostMessage(form.Handle, HotkeyService.MessageId, 0x6100, 0);
        await Task.Delay(100);
        Assert(await form.Browser.CoreWebView2.ExecuteScriptAsync("!document.querySelector('video').paused") == "true",
            "WM_HOTKEY reaches the video controller");

        var point = form.Browser.PointToScreen(new Point(30, 30));
        var before = RootAt(point);
        Console.WriteLine($"HITTEST before={before:X} player={form.Handle:X}");
        Assert(before == form.Handle, "native hit test reaches browser inside the player before passthrough");
        Assert(form.SetMousePassthrough(true) && form.MousePassthrough, "mouse passthrough enabled");
        Assert((GetWindowLong(form.Handle, -20) & 0x80020) == 0x80020, "layered and transparent native window flags set");
        var after = RootAt(point);
        Console.WriteLine($"HITTEST after={after:X}");
        Assert(after != form.Handle, "native hit test passes through the WebView2 area");
        await Capture(form, Path.Combine(artifacts, "player-passthrough.png"));
        PostMessage(form.Handle, HotkeyService.MessageId, 0x6104, 0);
        await Task.Delay(80);
        Assert(!form.MousePassthrough && (GetWindowLong(form.Handle, -20) & 0x20) == 0,
            "WM_HOTKEY disables passthrough and restores mouse input");
        PostMessage(form.Handle, HotkeyService.MessageId, 0x6103, 0);
        await Task.Delay(80);
        Assert(!form.Visible, "WM_HOTKEY hides the window");
        PostMessage(form.Handle, HotkeyService.MessageId, 0x6103, 0);
        await Task.Delay(80);
        Assert(form.Visible, "global hotkey route still works while window is hidden");
        await CaptureDialog(new SettingsDialog(form.Preferences), Path.Combine(artifacts, "settings.png"));
        await CaptureDialog(new BookmarksDialog(form.Bookmarks), Path.Combine(artifacts, "bookmarks.png"));
        await form.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('video').remove()");
        await form.ExecuteActionAsync(PlayerAction.TogglePlay);
        Assert(form.StatusText.Contains("未找到"), "page without video gives a useful message");
    }

    private static async Task CaptureDialog(Form dialog, string output)
    {
        using (dialog)
        {
            dialog.ShowInTaskbar = false;
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Location = new Point(-16000, -16000);
            dialog.Show();
            await Task.Delay(50);
            using var image = new Bitmap(dialog.Width, dialog.Height);
            dialog.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            image.Save(output);
            dialog.Close();
            Console.WriteLine($"RENDER: {output}");
        }
    }

    private static nint RootAt(Point point)
    {
        var window = WindowFromPoint(new NativePoint(point.X, point.Y));
        return window == 0 ? 0 : GetAncestor(window, 2);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint(int x, int y) { public readonly int X = x; public readonly int Y = y; }
    private sealed class TestMessageHost : NativeWindow, IDisposable
    {
        public TestMessageHost() => CreateHandle(new CreateParams { Parent = new nint(-3) });
        public void Dispose() => DestroyHandle();
    }
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, int message, nint wParam, nint lParam);
}
