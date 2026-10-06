using System.Text.Json;

namespace FloatScreen;

internal sealed record HistoryEntry(Guid Id, string Title, string Url, DateTimeOffset VisitedAt);

internal sealed class HistoryStore
{
    public const int Capacity = 10000;
    private readonly string _path;
    private List<HistoryEntry> _items = [];
    public string? LoadWarning { get; private set; }
    public IReadOnlyList<HistoryEntry> Items => _items.AsReadOnly();

    public HistoryStore(string directory)
    {
        _path = Path.Combine(directory, "history.json");
        try
        {
            if (!File.Exists(_path)) return;
            var items = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(_path))
                ?? throw new JsonException();
            if (items.Any(item => item == null || item.Id == Guid.Empty || item.VisitedAt == default
                || string.IsNullOrWhiteSpace(item.Title) || !WebAddress.TryParse(item.Url, out _)))
                throw new JsonException();
            _items = items.DistinctBy(item => item.Id).OrderByDescending(item => item.VisitedAt)
                .Take(Capacity).ToList();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            LoadWarning = "历史文件无法读取，原文件已保留；可备份 history.json 后清空历史重新记录。";
        }
    }

    public Guid RecordVisit(string url, string? title)
    {
        if (!WebAddress.TryParse(url, out var address)) throw new ArgumentException("历史记录需要网页链接。");
        var entry = new HistoryEntry(Guid.NewGuid(), Title(title, address!), address!.AbsoluteUri, DateTimeOffset.UtcNow);
        Commit([entry, .. _items.Take(Capacity - 1)]);
        return entry.Id;
    }

    public void UpdateTitle(Guid id, string? title)
    {
        var index = _items.FindIndex(item => item.Id == id);
        if (index < 0 || !WebAddress.TryParse(_items[index].Url, out var address)) return;
        var name = Title(title, address!);
        if (_items[index].Title == name) return;
        var next = new List<HistoryEntry>(_items);
        next[index] = next[index] with { Title = name };
        Commit(next);
    }

    public void Remove(IEnumerable<Guid> ids)
    {
        var selected = ids.ToHashSet();
        if (selected.Count == 0) return;
        Commit(_items.Where(item => !selected.Contains(item.Id)).ToList());
    }

    // An explicit clear can also reset an unreadable history file. Automatic writes never overwrite it.
    public void Clear() => Commit([], allowReset: true);

    private void Commit(List<HistoryEntry> next, bool allowReset = false)
    {
        if (LoadWarning != null && !allowReset) throw new InvalidDataException(LoadWarning);
        JsonStorage.Write(_path, next);
        _items = next;
        LoadWarning = null;
    }

    private static string Title(string? title, Uri address)
    {
        var value = string.IsNullOrWhiteSpace(title) ? address.Host : title.Trim();
        return value.Length > 512 ? value[..512] : value;
    }
}
