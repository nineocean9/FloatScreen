using System.Runtime.InteropServices;

namespace FloatScreen;

internal sealed partial class MainForm
{
    private readonly HistoryStore _history;
    private readonly System.Windows.Forms.Timer _historyTimer = new() { Interval = 600 };
    private bool _historyNavigationPending, _historyDocumentAvailable;
    private string _historyPageUrl = "";
    private Guid? _historyEntryId;

    private void InitializeHistory()
    {
        _historyTimer.Tick += (_, _) =>
        {
            _historyTimer.Stop();
            RecordHistoryPage(newVisit: false);
        };
    }

    private void ScheduleHistoryUpdate()
    {
        if (_closing || _historyNavigationPending || !_historyDocumentAvailable) return;
        // Save visits immediately, but coalesce the following document-title updates.
        _historyTimer.Stop(); _historyTimer.Start();
    }

    private void RecordHistoryPage(bool newVisit)
    {
        if (_closing || !_ready || _historyNavigationPending || !_historyDocumentAvailable) return;
        try
        {
            var core = Browser.CoreWebView2;
            if (!WebAddress.TryParse(core.Source, out var address)) return;
            var url = address!.AbsoluteUri;
            _settings = _settings with { LastUrl = url };
            if (newVisit || !SameHistoryPage(_historyPageUrl, url))
            {
                _historyEntryId = _history.RecordVisit(url, core.DocumentTitle);
                _historyPageUrl = url;
            }
            else if (_historyEntryId is { } id) _history.UpdateTitle(id, core.DocumentTitle);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or COMException)
        { SetStatus(_history.LoadWarning ?? "历史记录未能保存，请检查用户目录权限。", error: true); }
    }

    private static bool SameHistoryPage(string first, string second)
        => Uri.TryCreate(first, UriKind.Absolute, out var left) && Uri.TryCreate(second, UriKind.Absolute, out var right)
            && left.GetLeftPart(UriPartial.Query) == right.GetLeftPart(UriPartial.Query);

    private void ShowHistory()
    {
        _historyTimer.Stop();
        RecordHistoryPage(newVisit: false);
        ShowPlayerWindow(() => new HistoryDialog(_history), dialog =>
        {
            if (dialog.DialogResult == DialogResult.OK && dialog.SelectedUrl != null && !Navigate(dialog.SelectedUrl))
                SetStatus("播放器尚未准备好，请稍后再打开历史网页。", error: true);
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _historyTimer.Dispose();
        base.Dispose(disposing);
    }
}
