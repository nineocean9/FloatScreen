using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Xml;

namespace FloatScreen;

internal sealed class BilibiliDanmakuSource : IDisposable
{
    private readonly HttpClient _http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.All
    }) { Timeout = TimeSpan.FromSeconds(15) };
    private readonly Dictionary<string, DanmakuVideo> _cache = [];
    private DanmakuRuleMatcher _matcher;

    public BilibiliDanmakuSource()
    {
        DanmakuRuleMatcher.TryCreate(DanmakuMergeKey.DefaultRules(), out _matcher, out _);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        _http.DefaultRequestHeaders.Referrer = new Uri("https://www.bilibili.com/");
    }

    public void SetRules(DanmakuRuleMatcher matcher)
    {
        _matcher = matcher;
        foreach (var key in _cache.Keys.ToArray())
            _cache[key] = _cache[key] with { Entries = RebuildKeys(_cache[key].Entries, matcher) };
    }

    private static IReadOnlyList<DanmakuEntry> RebuildKeys(IReadOnlyList<DanmakuEntry> entries, DanmakuRuleMatcher matcher)
        => entries.Select(entry => entry with { MergeKey = matcher.Create(entry.Text) }).ToArray();

    public async Task<DanmakuVideo> Load(VideoClock clock, CancellationToken cancel)
    {
        if (_cache.TryGetValue(clock.Key, out var cached)) return cached;
        var cid = clock.Cid;
        if (cid <= 0)
        {
            using var response = await _http.GetAsync(
                "https://api.bilibili.com/x/web-interface/view?bvid=" + Uri.EscapeDataString(clock.Bvid), cancel);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
            var root = json.RootElement;
            if (!root.TryGetProperty("code", out var code) || code.GetInt32() != 0)
                throw new InvalidDataException("B站暂时未返回可用的视频信息。" );
            var data = root.GetProperty("data");
            foreach (var page in data.GetProperty("pages").EnumerateArray())
                if (page.GetProperty("page").GetInt32() == clock.Part) cid = page.GetProperty("cid").GetInt64();
        }
        if (cid <= 0) throw new InvalidDataException("未找到当前分集的弹幕编号。" );
        using var xmlResponse = await _http.GetAsync($"https://comment.bilibili.com/{cid}.xml",
            HttpCompletionOption.ResponseHeadersRead, cancel);
        xmlResponse.EnsureSuccessStatusCode();
        if (xmlResponse.Content.Headers.ContentLength > 10 * 1024 * 1024)
            throw new InvalidDataException("弹幕数据过大，本次投影已停止。" );
        await using var stream = await xmlResponse.Content.ReadAsStreamAsync(cancel);
        using var bodyCancel = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        bodyCancel.CancelAfter(TimeSpan.FromSeconds(15));
        using var abortRead = bodyCancel.Token.Register(() =>
        {
            try { stream.Dispose(); } catch (IOException) { }
        });
        var matcher = _matcher;
        IReadOnlyList<DanmakuEntry> entries;
        try { entries = await Task.Run(() => Parse(stream, bodyCancel.Token, matcher), bodyCancel.Token); }
        catch (Exception error) when (bodyCancel.IsCancellationRequested && error is IOException or ObjectDisposedException)
        {
            throw new OperationCanceledException(bodyCancel.Token);
        }
        cancel.ThrowIfCancellationRequested();
        if (!ReferenceEquals(matcher, _matcher)) entries = RebuildKeys(entries, _matcher);
        var result = new DanmakuVideo(cid, entries);
        if (_cache.Count >= 3) _cache.Remove(_cache.Keys.First());
        _cache[clock.Key] = result;
        return result;
    }

    private static IReadOnlyList<DanmakuEntry> Parse(Stream stream, CancellationToken cancel, DanmakuRuleMatcher matcher)
    {
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 10 * 1024 * 1024,
            IgnoreWhitespace = true
        });
        var entries = new List<DanmakuEntry>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (!reader.Read()) return entries;
        // ReadElementContentAsString already advances to the next node.
        while (!reader.EOF && entries.Count < 20000)
        {
            cancel.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element || reader.Name != "d") { reader.Read(); continue; }
            var fields = (reader.GetAttribute("p") ?? "").Split(',');
            var rawText = reader.ReadElementContentAsString().Trim();
            if (fields.Length < 4 || rawText.Length == 0
                || !double.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var time)
                || !double.IsFinite(time) || time < 0
                || !int.TryParse(fields[1], out var mode) || mode is < 1 or > 5
                || !int.TryParse(fields[3], out var color)) continue;
            var id = fields.Length > 7 ? fields[7] : "row-" + entries.Count;
            if (!ids.Add(id)) continue;
            var text = new StringBuilder();
            foreach (var rune in rawText.EnumerateRunes().Take(120)) text.Append(rune.ToString());
            var displayed = text.ToString();
            entries.Add(new DanmakuEntry(id, time, displayed, mode, color & 0xFFFFFF) { MergeKey = matcher.Create(displayed) });
        }
        return entries.OrderBy(entry => entry.Time).ToArray();
    }

    public void Dispose() => _http.Dispose();
}
