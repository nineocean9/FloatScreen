namespace FloatScreen;

internal sealed record DanmakuOptions
{
    public string ScreenDeviceName { get; init; } = "";
    public int AreaPercent { get; init; } = 33;
    public int DisplayRadius { get; init; } = 400;
    public int FontSize { get; init; } = 24;
    public int OpacityPercent { get; init; } = 65;
    public int TravelSeconds { get; init; } = 8;
    public int FixedSeconds { get; init; } = 5;
    public int MaxActive { get; init; } = 60;
    public bool MergeDuplicates { get; init; } = true;
    public int MergeSeconds { get; init; } = 5;
    public bool KeepOriginalColors { get; init; } = true;
    public int ColorRgb { get; init; } = 0xFFFFFF;
    public bool Outline { get; init; } = true;
    public DanmakuAliasRule[] AliasRules { get; init; } = DanmakuMergeKey.DefaultRules();

    public DanmakuOptions Sanitize()
    {
        var rules = AliasRules ?? DanmakuMergeKey.DefaultRules();
        if (!DanmakuRuleMatcher.TryCreate(rules, out _, out _)) rules = DanmakuMergeKey.DefaultRules();
        return this with
        {
            ScreenDeviceName = ScreenDeviceName ?? "", AreaPercent = Math.Clamp(AreaPercent, 10, 100),
            DisplayRadius = Math.Clamp(DisplayRadius, 100, 4000),
            FontSize = Math.Clamp(FontSize, 12, 64), OpacityPercent = Math.Clamp(OpacityPercent, 10, 100),
            TravelSeconds = Math.Clamp(TravelSeconds, 3, 30), FixedSeconds = Math.Clamp(FixedSeconds, 1, 30),
            MaxActive = Math.Clamp(MaxActive, 1, 200), MergeSeconds = Math.Clamp(MergeSeconds, 1, 30),
            ColorRgb = ColorRgb & 0xFFFFFF, AliasRules = rules
        };
    }
}

internal sealed record DanmakuEntry(string Id, double Time, string Text, int Mode, int ColorRgb)
{
    public string MergeKey { get; init; } = "";
}
internal sealed record DanmakuVideo(long Cid, IReadOnlyList<DanmakuEntry> Entries);
internal sealed record VideoClock(bool Available, string Bvid, int Part, long Cid, double Time,
    bool Paused, bool Buffering, bool Seeking, double Rate)
{
    public string Key => Bvid + ":" + Part;
}
