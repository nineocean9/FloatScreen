using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace FloatScreen;

internal sealed class DanmakuSprite : IDisposable
{
    private readonly DanmakuOptions _options;
    private readonly float _scale;
    private readonly int _rgb;
    private readonly string _text;
    public string MergeKey { get; }
    private int _count = 1;
    public Bitmap Image { get; private set; }
    public double Start { get; }
    public int Lane { get; private set; }
    public int Y { get; private set; }
    public bool Fixed { get; }
    public double Lifetime => Fixed ? _options.FixedSeconds : _options.TravelSeconds;
    public string Text => _text;

    public DanmakuSprite(DanmakuEntry entry, int lane, int y, DanmakuOptions options, float scale, int widthLimit, int count = 1)
    {
        _options = options; _scale = scale; _text = entry.Text;
        MergeKey = entry.MergeKey.Length == 0 ? DanmakuMergeKey.Create(entry.Text) : entry.MergeKey;
        _count = count;
        _rgb = options.KeepOriginalColors ? entry.ColorRgb : options.ColorRgb;
        Start = entry.Time; Lane = lane; Y = y; Fixed = entry.Mode is 4 or 5;
        using var font = new Font("Microsoft YaHei UI", options.FontSize * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        var reserve = _text + (options.MergeDuplicates ? " ×99999" : "");
        var measured = TextRenderer.MeasureText(reserve, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        Image = new Bitmap(Math.Clamp(measured.Width + 8, 8, widthLimit * 3), Math.Max(8, measured.Height + 8), PixelFormat.Format32bppPArgb);
        Repaint();
    }

    public double X(double time, int width) => Fixed ? (width - Image.Width) / 2d
        : width - (time - Start) * (width + Image.Width) / Lifetime;
    public double Velocity(int width) => (width + Image.Width) / Lifetime;
    public bool Expired(double time) => time >= Start + Lifetime;
    public void SetCount(int count) { _count = count; Repaint(); }
    public void Place(int lane, int y) { Lane = lane; Y = y; }

    private void Repaint()
    {
        using (var graphics = Graphics.FromImage(Image))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = new GraphicsPath();
            using var family = new FontFamily("Microsoft YaHei UI");
            path.AddString(_text + (_count > 1 ? " ×" + _count : ""), family, (int)FontStyle.Regular,
                _options.FontSize * _scale, new PointF(3, 2), StringFormat.GenericTypographic);
            if (_options.Outline)
            {
                using var pen = new Pen(Color.Black, Math.Max(1.5f, 2 * _scale)) { LineJoin = LineJoin.Round };
                graphics.DrawPath(pen, path);
            }
            using var brush = new SolidBrush(Color.FromArgb(255, (_rgb >> 16) & 255, (_rgb >> 8) & 255, _rgb & 255));
            graphics.FillPath(brush, path);
        }
        if (_options.OpacityPercent < 100)
        {
            // Fade the completed glyph, including its outline, once. PArgb RGB and alpha
            // are scaled together so overlapping fill/stroke does not increase opacity.
            var data = Image.LockBits(new Rectangle(Point.Empty, Image.Size), ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
            try
            {
                unsafe
                {
                    for (var row = 0; row < Image.Height; row++)
                    {
                        var pixels = (byte*)data.Scan0 + row * data.Stride;
                        for (var column = 0; column < Image.Width * 4; column++)
                            pixels[column] = (byte)((pixels[column] * _options.OpacityPercent + 50) / 100);
                    }
                }
            }
            finally { Image.UnlockBits(data); }
        }
    }

    public void Dispose() => Image.Dispose();
}

internal sealed class DanmakuTimeline : IDisposable
{
    private IReadOnlyList<DanmakuEntry> _entries = [];
    private readonly List<DanmakuSprite> _active = [];
    private readonly Dictionary<string, DuplicateGroup> _groups = new(StringComparer.Ordinal);
    private int _index, _nextLane;
    public IReadOnlyList<DanmakuSprite> Active => _active;

    public void SetEntries(IReadOnlyList<DanmakuEntry> entries, double time)
    {
        _entries = entries.Any(entry => entry.MergeKey.Length == 0)
            ? entries.Select(entry => entry.MergeKey.Length == 0 ? entry with { MergeKey = DanmakuMergeKey.Create(entry.Text) } : entry).ToArray()
            : entries;
        Reset(time);
    }

    public void Reset(double time)
    {
        ClearSprites();
        var low = 0; var high = _entries.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (_entries[middle].Time < Math.Max(0, time - 0.1)) low = middle + 1; else high = middle;
        }
        _index = low;
    }

    public void Advance(double time, Size area, DanmakuOptions options, float scale)
    {
        foreach (var sprite in _active.Where(item => item.Expired(time)).ToArray()) Remove(sprite);
        var rowHeight = Math.Max(18, (int)Math.Ceiling(options.FontSize * scale * 1.55f) + 6);
        var lanes = Math.Max(1, (area.Height - 8) / rowHeight);
        while (_index < _entries.Count && _entries[_index].Time <= time)
        {
            var entry = _entries[_index++];
            if (time - entry.Time > 0.75) continue;
            DuplicateGroup? group = null;
            if (options.MergeDuplicates && _groups.TryGetValue(entry.MergeKey, out var previous))
            {
                if (entry.Time - previous.First.Time <= options.MergeSeconds)
                {
                    group = previous;
                    group.Count++;
                    if (group.Sprite != null)
                    {
                        group.Sprite.SetCount(group.Count);
                        continue;
                    }
                    // Keep counts even when a short-lived fixed comment has already left.
                    entry = entry with { Text = group.First.Text, Mode = group.First.Mode, ColorRgb = group.First.ColorRgb };
                }
                else if (previous.Sprite != null) Remove(previous.Sprite);
            }
            if (options.MergeDuplicates && group == null)
                _groups[entry.MergeKey] = group = new DuplicateGroup(entry);
            if (_active.Count >= options.MaxActive) continue;
            var candidates = Enumerable.Range(0, lanes);
            if (entry.Mode == 4) candidates = candidates.Reverse();
            else if (entry.Mode != 5) candidates = candidates.OrderBy(lane => (lane - _nextLane + lanes) % lanes);
            DanmakuSprite? chosen = null;
            // Rasterize once per incoming comment, not once for every rejected lane.
            var sprite = new DanmakuSprite(entry, 0, 4, options, scale, area.Width, group?.Count ?? 1);
            foreach (var lane in candidates)
            {
                sprite.Place(lane, 4 + lane * rowHeight);
                var blocked = _active.Where(item => item.Lane == lane).Any(old =>
                {
                    if (old.Fixed || sprite.Fixed) return true;
                    var gap = area.Width - (old.X(time, area.Width) + old.Image.Width) - 32 * scale;
                    return gap < 0 || (sprite.Velocity(area.Width) - old.Velocity(area.Width))
                        * Math.Max(0, old.Start + old.Lifetime - time) > gap;
                });
                if (!blocked) { chosen = sprite; _nextLane = (lane + 1) % lanes; break; }
            }
            if (chosen == null) { sprite.Dispose(); continue; }
            _active.Add(chosen);
            if (group != null) group.Sprite = chosen;
        }
        foreach (var key in _groups.Where(item => item.Value.Sprite == null
            && time > item.Value.First.Time + options.MergeSeconds).Select(item => item.Key).ToArray()) _groups.Remove(key);
    }

    private void Remove(DanmakuSprite sprite)
    {
        _active.Remove(sprite);
        if (_groups.TryGetValue(sprite.MergeKey, out var group) && ReferenceEquals(group.Sprite, sprite)) group.Sprite = null;
        sprite.Dispose();
    }

    public void ClearSprites()
    {
        foreach (var sprite in _active) sprite.Dispose();
        _active.Clear(); _groups.Clear(); _nextLane = 0;
    }

    public void Dispose() => ClearSprites();

    private sealed class DuplicateGroup(DanmakuEntry first)
    {
        public DanmakuEntry First { get; } = first;
        public int Count { get; set; } = 1;
        public DanmakuSprite? Sprite { get; set; }
    }
}
