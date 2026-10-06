using System.Globalization;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace FloatScreen;

internal sealed class VideoController(CoreWebView2 browser)
{
    private static readonly string Script = ReadScript();

    private static string ReadScript()
    {
        using var stream = typeof(VideoController).Assembly.GetManifestResourceStream("FloatScreen.video-control.js")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public async Task<string> Execute(PlayerAction action, int seconds)
    {
        var command = action switch { PlayerAction.TogglePlay => "toggle", PlayerAction.Backward => "backward", _ => "forward" };
        var result = await browser.ExecuteScriptAsync($"({Script})({JsonSerializer.Serialize(command)}, {seconds.ToString(CultureInfo.InvariantCulture)})");
        using var json = JsonDocument.Parse(result);
        return json.RootElement.TryGetProperty("message", out var message)
            ? message.GetString() ?? "视频操作未能完成。" : "当前网页不支持快捷控制，请使用网页播放器。";
    }
}
