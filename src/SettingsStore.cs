using System.Text.Json;

namespace FloatScreen;

internal sealed record AppSettings
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; } = 820;
    public int Height { get; init; } = 540;
    public bool HasPosition { get; init; }
    public bool WindowMaximized { get; init; }
    public double ZoomFactor { get; init; } = 1.0;
    public SavedWindowBounds? ImmersiveBounds { get; init; }
    public bool AlwaysOnTop { get; init; } = true;
    public bool MousePassthrough { get; init; }
    public int HoleRadius { get; init; } = 250;
    public string LastUrl { get; init; } = "";
    public int SkipSeconds { get; init; } = 5;
    public HotkeyOptions Hotkeys { get; init; } = new();
    public DanmakuOptions Danmaku { get; init; } = new();
}

internal sealed class SettingsStore
{
    public string DataDirectory { get; }
    public string? LoadWarning { get; private set; }
    private string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    public SettingsStore(string? dataDirectory = null)
    {
        DataDirectory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FloatScreen");
        if (dataDirectory == null) MigratePreviousSettings();
    }

    private void MigratePreviousSettings()
    {
        var legacyDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LanrenLite");
        try
        {
            foreach (var name in new[] { "settings.json", "bookmarks.json" })
            {
                var source = Path.Combine(legacyDirectory, name);
                var destination = Path.Combine(DataDirectory, name);
                if (!File.Exists(source) || File.Exists(destination)) continue;
                Directory.CreateDirectory(DataDirectory);
                File.Copy(source, destination, overwrite: false);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            LoadWarning = "旧版设置未能迁移，请检查用户目录权限。";
        }
    }

    public AppSettings Load()
    {
        try
        {
            var settings = File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new()
                : new();
            if (settings.SkipSeconds is < 1 or > 60 || settings.Hotkeys == null
                || !settings.Hotkeys.TryGetBindings(out _, out _))
            {
                LoadWarning = "快捷键或跳转秒数无效，已恢复默认值。";
                settings = settings with { SkipSeconds = 5, Hotkeys = new() };
            }
            else if (settings.Hotkeys == new HotkeyOptions { TogglePassthrough = "Ctrl+Alt+T" })
            {
                // Only migrate the unchanged defaults from 0.2.0, not custom assignments.
                settings = settings with { Hotkeys = new() };
            }
            if (settings.HoleRadius is < 40 or > 800) settings = settings with { HoleRadius = 250 };
            if (!double.IsFinite(settings.ZoomFactor) || settings.ZoomFactor <= 0)
                settings = settings with { ZoomFactor = 1.0 };
            settings = settings with { Danmaku = (settings.Danmaku ?? new()).Sanitize() };
            return settings;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            LoadWarning = "上次的窗口设置无法读取，已使用默认设置。";
            return new();
        }
    }

    public void Save(AppSettings settings)
    {
        JsonStorage.Write(SettingsPath, settings);
    }
}

internal sealed record SavedWindowBounds(int X, int Y, int Width, int Height)
{
    public static SavedWindowBounds From(Rectangle bounds) => new(bounds.X, bounds.Y, bounds.Width, bounds.Height);
}

internal static class JsonStorage
{
    public static void Write<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        // Replace only after a complete write, so a failed save preserves the previous settings.
        var temporaryPath = Path.Combine(directory, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value,
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
