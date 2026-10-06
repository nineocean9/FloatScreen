using System.Runtime.InteropServices;

namespace FloatScreen;

internal sealed partial class MainForm
{
    private Rectangle[] WorkingAreas() => Screen.AllScreens.OrderByDescending(screen => screen.Primary)
        .Select(screen => screen.WorkingArea).ToArray();

    private void RestoreSavedWindowPlacement()
    {
        // Apply after the first DPI/font layout instead of allowing startup scaling
        // to change the saved pixel coordinates and dimensions.
        Bounds = WindowPlacement.Restore(_settings, WorkingAreas());
        if (_settings.WindowMaximized) WindowState = FormWindowState.Maximized;
    }

    private Rectangle RestoreImmersiveBounds(SavedWindowBounds bounds) => WindowPlacement.Restore(
        new AppSettings { X = bounds.X, Y = bounds.Y, Width = bounds.Width, Height = bounds.Height, HasPosition = true },
        WorkingAreas(), new Size(160, 90));

    private Rectangle CurrentImmersiveBounds() => WindowState == FormWindowState.Minimized ? RestoreBounds : Bounds;

    private Rectangle AdjustedNormalBounds(Rectangle current)
    {
        if (_normalWindowBounds.IsEmpty || _immersiveInitialBounds.IsEmpty) return _normalWindowBounds;
        return new Rectangle(
            _normalWindowBounds.X + current.X - _immersiveInitialBounds.X,
            _normalWindowBounds.Y + current.Y - _immersiveInitialBounds.Y,
            Math.Max(_normalMinimumSize.Width, _normalWindowBounds.Width + current.Width - _immersiveInitialBounds.Width),
            Math.Max(_normalMinimumSize.Height, _normalWindowBounds.Height + current.Height - _immersiveInitialBounds.Height));
    }

    private AppSettings CaptureWindowPreferences()
    {
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        var maximized = WindowState == FormWindowState.Maximized;
        SavedWindowBounds? immersiveBounds = null;
        if (_immersiveMode && !_normalWindowBounds.IsEmpty)
        {
            var current = CurrentImmersiveBounds();
            immersiveBounds = SavedWindowBounds.From(current);
            bounds = AdjustedNormalBounds(current);
            maximized = _normalWindowState == FormWindowState.Maximized;
        }
        var zoom = _settings.ZoomFactor;
        try
        {
            if (_ready && Browser.CoreWebView2 != null) zoom = Browser.ZoomFactor;
        }
        catch (Exception error) when (error is InvalidOperationException or COMException) { }
        return _settings with
        {
            X = bounds.X, Y = bounds.Y, Width = bounds.Width, Height = bounds.Height, HasPosition = true,
            WindowMaximized = maximized, ImmersiveBounds = immersiveBounds, ZoomFactor = zoom,
            AlwaysOnTop = TopMost, MousePassthrough = _immersiveMode
        };
    }
}
