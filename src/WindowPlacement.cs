namespace FloatScreen;

internal static class WindowPlacement
{
    public static Rectangle Restore(AppSettings settings, IReadOnlyList<Rectangle> workAreas, Size? minimum = null)
    {
        var primary = workAreas.Count > 0 ? workAreas[0] : new Rectangle(0, 0, 1920, 1080);
        var minSize = minimum ?? new Size(520, 340);
        var saved = new Rectangle(settings.X, settings.Y,
            Math.Clamp(settings.Width, minSize.Width, 7680), Math.Clamp(settings.Height, minSize.Height, 4320));
        // Keep the user's actual position, including a window partly outside a monitor.
        // Use wide arithmetic so corrupt coordinates cannot overflow the visibility check.
        if (settings.HasPosition && workAreas.Any(area =>
            Math.Min((long)saved.X + saved.Width, (long)area.X + area.Width) > Math.Max((long)saved.X, area.X)
            && Math.Min((long)saved.Y + saved.Height, (long)area.Y + area.Height) > Math.Max((long)saved.Y, area.Y)))
            return saved;

        var width = Math.Min(saved.Width, primary.Width);
        var height = Math.Min(saved.Height, primary.Height);
        var x = primary.Left + (primary.Width - width) / 2;
        var y = primary.Top + (primary.Height - height) / 2;
        return new Rectangle(x, y, width, height);
    }
}
