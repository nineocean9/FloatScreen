using System.Runtime.InteropServices;

namespace FloatScreen;

internal sealed class HistoryDialog : Form
{
    private readonly HistoryStore _store;
    private readonly TextBox _search = new() { Dock = DockStyle.Fill, PlaceholderText = "搜索网页标题或地址", AccessibleName = "搜索历史记录" };
    private readonly DataGridView _list = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, VirtualMode = true, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = SystemColors.Window
    };
    private readonly Label _detail = new() { AutoSize = true, MaximumSize = new Size(760, 0) };
    private readonly Button _open = new() { Text = "打开", AutoSize = true };
    private readonly Button _remove = new() { Text = "删除选中", AutoSize = true };
    private readonly Button _copy = new() { Text = "复制链接", AutoSize = true };
    private readonly Button _clear = new() { Text = "清空全部", AutoSize = true };
    private readonly System.Windows.Forms.Timer _filterTimer = new() { Interval = 200 };
    private List<HistoryEntry> _visible = [];
    public string? SelectedUrl { get; private set; }

    public HistoryDialog(HistoryStore store)
    {
        _store = store;
        Text = "历史记录";
        Font = new Font("Microsoft YaHei UI", 9F);
        ClientSize = new Size(800, 480);
        MinimumSize = new Size(600, 360);
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
        _list.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "网页标题", FillWeight = 55, SortMode = DataGridViewColumnSortMode.NotSortable });
        _list.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "地址", FillWeight = 45, SortMode = DataGridViewColumnSortMode.NotSortable });
        _list.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "访问时间", AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            Width = 155, SortMode = DataGridViewColumnSortMode.NotSortable });
        layout.Controls.Add(_list, 0, 1);
        _detail.Margin = new Padding(0, 10, 0, 10);
        layout.Controls.Add(_detail, 0, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var close = new Button { Text = "关闭", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([close, _open, _copy, _remove, _clear]);
        layout.Controls.Add(buttons, 0, 3);
        Controls.Add(layout);
        CancelButton = close;
        close.Click += (_, _) => Close();
        Activated += (_, _) => RefreshItems();
        _list.CellValueNeeded += (_, args) =>
        {
            if (args.RowIndex < 0 || args.RowIndex >= _visible.Count) return;
            var item = _visible[args.RowIndex];
            args.Value = args.ColumnIndex switch { 0 => item.Title, 1 => item.Url, 2 => item.VisitedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), _ => null };
        };
        _list.SelectionChanged += (_, _) => UpdateSelection();
        _list.CellDoubleClick += (_, args) => { if (args.RowIndex >= 0) OpenSelected(); };
        _list.KeyDown += (_, args) =>
        {
            if (args.KeyCode == Keys.Enter) { args.SuppressKeyPress = true; OpenSelected(); }
            else if (args.KeyCode == Keys.Delete) { args.SuppressKeyPress = true; RemoveSelected(); }
        };
        _search.TextChanged += (_, _) => { _filterTimer.Stop(); _filterTimer.Start(); };
        _filterTimer.Tick += (_, _) => { _filterTimer.Stop(); RefreshItems(); };
        _open.Click += (_, _) => OpenSelected();
        _remove.Click += (_, _) => RemoveSelected();
        _copy.Click += (_, _) =>
        {
            if (CurrentItem() is not { } item) return;
            try { Clipboard.SetText(item.Url); _detail.Text = "链接已复制。"; }
            catch (ExternalException) { _detail.Text = "剪贴板暂不可用，请稍后重试。"; }
        };
        _clear.Click += (_, _) =>
        {
            var message = store.LoadWarning == null ? "清空全部历史记录？此操作无法撤销。"
                : "历史文件无法读取。清空会覆盖原文件，请先自行备份。继续清空？";
            if (MessageBox.Show(this, message, "清空历史", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            ChangeHistory(store.Clear);
        };
        UiTheme.ApplyDialog(this);
        RefreshItems();
    }

    private HistoryEntry? CurrentItem()
        => _list.CurrentRow is { Index: >= 0 } row && row.Index < _visible.Count ? _visible[row.Index] : null;

    private void RefreshItems()
    {
        _list.RowCount = 0;
        var query = _search.Text.Trim();
        _visible = _store.Items.Where(item => query.Length == 0
            || item.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || item.Url.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        _list.RowCount = _visible.Count;
        if (_visible.Count > 0) _list.CurrentCell = _list.Rows[0].Cells[0];
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        var item = CurrentItem();
        _open.Enabled = _copy.Enabled = item != null;
        _remove.Enabled = _list.SelectedRows.Count > 0;
        _clear.Enabled = _store.Items.Count > 0 || _store.LoadWarning != null;
        _detail.Text = _store.LoadWarning ?? (item == null
            ? (_store.Items.Count == 0 ? "暂无历史记录，成功打开网页后会自动保存。" : "没有匹配的记录。")
            : $"匹配 {_visible.Count} 条 / 共 {_store.Items.Count} 条 · 最近访问在前 · 最多保留 {HistoryStore.Capacity:N0} 条");
    }

    private void OpenSelected()
    {
        if (CurrentItem() is not { } item) return;
        SelectedUrl = item.Url;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void RemoveSelected()
    {
        var ids = _list.SelectedRows.Cast<DataGridViewRow>().Where(row => row.Index >= 0 && row.Index < _visible.Count)
            .Select(row => _visible[row.Index].Id).ToArray();
        if (ids.Length > 0) ChangeHistory(() => _store.Remove(ids));
    }

    private void ChangeHistory(Action change)
    {
        try { change(); RefreshItems(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { _detail.Text = _store.LoadWarning ?? "历史记录未能修改，请检查用户目录权限。"; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _filterTimer.Dispose();
        base.Dispose(disposing);
    }
}
