using System.Text.Json;

namespace FloatScreen;

internal sealed record Bookmark(string Title, string Url)
{
    public override string ToString() => Title;
}

internal sealed class BookmarkStore
{
    private readonly string _path;
    private List<Bookmark> _items = [];
    public event Action? Changed;
    public string? LoadWarning { get; private set; }
    public IReadOnlyList<Bookmark> Items => _items.AsReadOnly();

    public BookmarkStore(string directory)
    {
        _path = Path.Combine(directory, "bookmarks.json");
        try
        {
            if (!File.Exists(_path)) return;
            var items = JsonSerializer.Deserialize<List<Bookmark>>(File.ReadAllText(_path)) ?? [];
            if (items.Any(item => item == null || string.IsNullOrWhiteSpace(item.Title)
                || !WebAddress.TryParse(item.Url, out _))) throw new JsonException();
            _items = items.DistinctBy(item => item.Url).ToList();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            LoadWarning = "收藏文件无法读取，请检查 bookmarks.json；原文件会保留。";
        }
    }

    public bool Add(string url, string title)
    {
        if (!WebAddress.TryParse(url, out var address)) throw new ArgumentException("收藏需要网页链接。");
        var normalized = address!.AbsoluteUri;
        if (_items.Any(item => item.Url == normalized)) return false;
        var name = string.IsNullOrWhiteSpace(title) ? address.Host : title.Trim();
        Commit([new Bookmark(name, normalized), .. _items]);
        return true;
    }

    public void Remove(Bookmark item) => Commit(_items.Where(existing => existing.Url != item.Url).ToList());

    private void Commit(List<Bookmark> next)
    {
        if (LoadWarning != null) throw new InvalidDataException(LoadWarning);
        JsonStorage.Write(_path, next);
        _items = next;
        Changed?.Invoke();
    }
}
