namespace FloatScreen;

internal sealed class BookmarksDialog : Form
{
    private readonly BookmarkStore _store;
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly TextBox _search = new() { Dock = DockStyle.Fill, PlaceholderText = "搜索收藏标题或地址", AccessibleName = "搜索收藏" };
    private readonly Label _detail = new() { AutoSize = true, MaximumSize = new Size(440, 0) };
    public string? SelectedUrl { get; private set; }

    public BookmarksDialog(BookmarkStore store)
    {
        _store = store;
        Text = "收藏列表";
        Font = new Font("Microsoft YaHei UI", 9F);
        ClientSize = new Size(520, 390);
        MinimumSize = new Size(400, 280);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MaximizeBox = MinimizeBox = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 4, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _search.Margin = new Padding(0, 0, 0, 10);
        layout.Controls.Add(_search, 0, 0);
        layout.Controls.Add(_list, 0, 1);
        _detail.Margin = new Padding(0, 10, 0, 10);
        _detail.Text = store.LoadWarning ?? "暂无收藏，点击地址栏右侧“收藏此页”添加。";
        layout.Controls.Add(_detail, 0, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var open = new Button { Text = "打开", AutoSize = true };
        var remove = new Button { Text = "删除", AutoSize = true };
        var close = new Button { Text = "关闭", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([open, remove, close]);
        layout.Controls.Add(buttons, 0, 3);
        Controls.Add(layout);
        CancelButton = close;
        close.Click += (_, _) => Close();
        Activated += (_, _) => RefreshItems();
        _search.TextChanged += (_, _) => RefreshItems();
        _list.DrawMode = DrawMode.OwnerDrawFixed;
        _list.ItemHeight = (int)Math.Round(52 * DeviceDpi / 96D);
        _list.DrawItem += (_, args) => DrawBookmark(args);
        open.Click += (_, _) => OpenSelected();
        _list.DoubleClick += (_, _) => OpenSelected();
        _list.SelectedIndexChanged += (_, _) =>
        {
            var selected = _list.SelectedItem as Bookmark;
            open.Enabled = remove.Enabled = selected != null;
            _detail.Text = selected?.Url ?? store.LoadWarning ?? (store.Items.Count == 0
                ? "暂无收藏，点击地址栏右侧“收藏此页”添加。" : "没有匹配的收藏。");
        };
        remove.Click += (_, _) =>
        {
            if (_list.SelectedItem is not Bookmark item) return;
            try { _store.Remove(item); RefreshItems(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { _detail.Text = "收藏未能删除，请检查用户目录权限。"; }
        };
        RefreshItems();
        open.Enabled = remove.Enabled = _list.SelectedItem != null;
        UiTheme.ApplyDialog(this);
    }

    private void RefreshItems()
    {
        var selected = (_list.SelectedItem as Bookmark)?.Url;
        var query = _search.Text.Trim();
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            _list.Items.AddRange(_store.Items.Where(item => query.Length == 0
                || item.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.Url.Contains(query, StringComparison.OrdinalIgnoreCase)).Cast<object>().ToArray());
            if (_list.Items.Count > 0)
            {
                var index = _list.Items.Cast<Bookmark>().ToList().FindIndex(item => item.Url == selected);
                _list.SelectedIndex = Math.Max(0, index);
            }
        }
        finally { _list.EndUpdate(); }
    }

    private void DrawBookmark(DrawItemEventArgs args)
    {
        if (args.Index < 0 || args.Index >= _list.Items.Count) return;
        var item = (Bookmark)_list.Items[args.Index];
        using var brush = new SolidBrush((args.State & DrawItemState.Selected) != 0 ? UiTheme.Selection : Color.White);
        args.Graphics.FillRectangle(brush, args.Bounds);
        var inset = (int)Math.Round(10 * _list.DeviceDpi / 96D);
        var title = new Rectangle(args.Bounds.Left + inset, args.Bounds.Top + inset / 2,
            Math.Max(1, args.Bounds.Width - inset * 2), _list.Font.Height + 3);
        var url = title with { Y = title.Bottom + 2 };
        TextRenderer.DrawText(args.Graphics, item.Title, _list.Font, title, UiTheme.Text,
            TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(args.Graphics, new Uri(item.Url).Host, _list.Font, url, UiTheme.Muted,
            TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        args.DrawFocusRectangle();
    }

    private void OpenSelected()
    {
        if (_list.SelectedItem is not Bookmark item) return;
        SelectedUrl = item.Url;
        DialogResult = DialogResult.OK;
        Close();
    }
}
