using Microsoft.Web.WebView2.Core;

namespace FloatScreen;

internal sealed partial class MainForm
{
    private DanmakuProjection? _projection;
    private readonly ToolStripMenuItem _danmakuItem = new("弹幕投影：已关闭") { Enabled = false };
    private readonly ToolStripMenuItem _danmakuStatusItem = new("投影状态：已关闭") { Enabled = false };
    private ToolStripMenuItem? _trayDanmakuItem;
    private ToolStripMenuItem? _trayDanmakuStatusItem;

    private void InitializeProjection(CoreWebView2 browser)
    {
        _projection?.Dispose();
        _projection = new DanmakuProjection(this, browser, () => _settings.Danmaku,
            (message, error) =>
            {
                if (_closing) return;
                SetStatus(message, error);
                _danmakuStatusItem.Text = "投影状态：" + message;
                if (_trayDanmakuStatusItem != null) _trayDanmakuStatusItem.Text = _danmakuStatusItem.Text;
                if (error && _immersiveMode && _tray != null)
                    _tray.ShowBalloonTip(5000, "弹幕投影", message, ToolTipIcon.Warning);
            },
            enabled =>
            {
                if (_closing) return;
                _danmakuItem.Checked = enabled;
                _danmakuItem.Text = enabled ? "弹幕投影：已开启" : "弹幕投影：已关闭";
                if (_trayDanmakuItem != null)
                {
                    _trayDanmakuItem.Checked = enabled;
                    _trayDanmakuItem.Text = _danmakuItem.Text;
                }
                if (_tray != null) _tray.Text = enabled ? "浮幕 · 弹幕投影已开" : "浮幕";
            });
        _danmakuItem.Enabled = true;
        if (_trayDanmakuItem != null) _trayDanmakuItem.Enabled = true;
    }

    private void ToggleDanmaku()
    {
        if (!_ready || _projection == null) { SetStatus("播放器尚未准备好。", error: true); return; }
        _projection.SetEnabled(!_projection.Enabled);
    }
}
