namespace FloatScreen;

internal sealed class SettingsDialog : Form
{
    private readonly Dictionary<PlayerAction, HotkeyCaptureBox> _keys = [];
    private readonly NumericUpDown _seconds = Number(1, 60);
    private readonly NumericUpDown _holeRadius = Number(40, 800, 10);
    private readonly Label _message = new() { AutoSize = true, ForeColor = Color.Firebrick, MaximumSize = new Size(560, 0) };
    private readonly ComboBox _screen = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 290 };
    private readonly NumericUpDown _area = Number(10, 100), _fontSize = Number(12, 64), _opacity = Number(10, 100);
    private readonly NumericUpDown _displayRadius = Number(100, 4000, 20);
    private readonly NumericUpDown _travel = Number(3, 30), _fixed = Number(1, 30), _density = Number(1, 200), _mergeSeconds = Number(1, 30);
    private readonly CheckBox _merge = new() { Text = "合并相同文字，显示 ×数量", AutoSize = true };
    private readonly CheckBox _originalColors = new() { Text = "保留弹幕原色", AutoSize = true };
    private readonly CheckBox _outline = new() { Text = "显示黑色描边", AutoSize = true };
    private readonly Button _color = new() { Text = "选择统一文字颜色", AutoSize = true };
    private readonly DataGridView _rules = new()
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = true, AllowUserToDeleteRows = true,
        RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true
    };
    private int _colorRgb;
    public HotkeyOptions Options { get; private set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<HotkeyOptions, int, int, DanmakuOptions, string?>? ApplySettings { get; set; }

    public SettingsDialog(AppSettings settings, string? hotkeyWarning = null)
    {
        Options = settings.Hotkeys;
        Text = "设置";
        Font = new Font("Microsoft YaHei UI", 9F);
        ClientSize = new Size(620, 560);
        MinimumSize = new Size(620, 500);
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(10) };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var playerTab = new TabPage("播放器与快捷键");
        var danmakuTab = new TabPage("弹幕投影");
        var rulesTab = new TabPage("去重规则表");
        tabs.TabPages.AddRange([playerTab, danmakuTab, rulesTab]);
        root.Controls.Add(tabs, 0, 0);
        BuildPlayerTab(playerTab, settings);
        BuildDanmakuTab(danmakuTab, settings.Danmaku);
        BuildRulesTab(rulesTab, settings.Danmaku.AliasRules);
        _message.Text = hotkeyWarning ?? "";
        _message.Margin = new Padding(8, 8, 8, 8);
        root.Controls.Add(_message, 0, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var save = new Button { Text = "保存", AutoSize = true };
        var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        var reset = new Button { Text = "恢复默认", AutoSize = true };
        save.Click += (_, _) => Save();
        cancel.Click += (_, _) => Close();
        reset.Click += (_, _) =>
        {
            var defaults = new HotkeyOptions();
            foreach (var (action, box) in _keys) box.Text = defaults.Get(action);
            _seconds.Value = 5; _holeRadius.Value = 250;
            FillDanmaku(new()); FillRules(DanmakuMergeKey.DefaultRules()); _message.Text = "";
        };
        buttons.Controls.AddRange([save, cancel, reset]);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
        AcceptButton = save; CancelButton = cancel;
        UiTheme.ApplyDialog(this);
    }

    private static NumericUpDown Number(int minimum, int maximum, int increment = 1)
        => new() { Minimum = minimum, Maximum = maximum, Increment = increment, Width = 100 };

    private static TableLayoutPanel Body(TabPage page)
    {
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(16) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        scroll.Controls.Add(table); page.Controls.Add(scroll);
        return table;
    }

    private static void Row(TableLayoutPanel table, string label, Control control)
    {
        var row = table.RowCount++;
        table.Controls.Add(new Label { Text = label, AutoSize = true, MaximumSize = new Size(210, 0),
            Margin = new Padding(0, 4, 12, 12) }, 0, row);
        control.Margin = new Padding(0, 0, 0, 12);
        table.Controls.Add(control, 1, row);
    }

    private void BuildPlayerTab(TabPage page, AppSettings settings)
    {
        var table = Body(page);
        var hint = new Label { Text = "点击快捷键框，再按键或操作鼠标。Esc 取消读取。\n单键会在其他程序中触发，建议使用组合键。", AutoSize = true, Margin = new Padding(0, 0, 0, 16) };
        table.Controls.Add(hint, 0, 0); table.SetColumnSpan(hint, 2); table.RowCount = 1;
        foreach (var action in Enum.GetValues<PlayerAction>())
        {
            var box = new HotkeyCaptureBox { Text = settings.Hotkeys.Get(action), Width = 260, AccessibleName = HotkeyService.Describe(action) };
            box.CaptureMessage += message => { _message.ForeColor = SystemColors.GrayText; _message.Text = message; };
            _keys[action] = box;
            Row(table, HotkeyService.Describe(action), box);
        }
        _seconds.Value = Math.Clamp(settings.SkipSeconds, 1, 60);
        _holeRadius.Value = Math.Clamp(settings.HoleRadius, 40, 800);
        Row(table, "每次跳转（秒）", _seconds);
        Row(table, "透明圆洞半径（随系统缩放）", _holeRadius);
    }

    private void BuildDanmakuTab(TabPage page, DanmakuOptions options)
    {
        var table = Body(page);
        _screen.Items.Add(new DisplayChoice("", "跟随播放器所在屏幕"));
        var index = 1;
        foreach (var screen in Screen.AllScreens)
            _screen.Items.Add(new DisplayChoice(screen.DeviceName, $"显示器 {index++} · {screen.Bounds.Width}×{screen.Bounds.Height}" + (screen.Primary ? "（主屏）" : "")));
        Row(table, "投影显示器", _screen);
        Row(table, "上方显示区域（屏幕百分比）", _area);
        Row(table, "显示半径（屏幕像素，左右各展开）", _displayRadius);
        Row(table, "字体大小（随系统缩放）", _fontSize);
        Row(table, "不透明度（越小越透明）", _opacity);
        Row(table, "滚动耗时（视频秒，越小越快）", _travel);
        Row(table, "固定弹幕停留（视频秒）", _fixed);
        Row(table, "同时显示上限", _density);
        Row(table, "重复弹幕", _merge);
        Row(table, "合并时间窗口（视频秒）", _mergeSeconds);
        Row(table, "文字颜色", _originalColors);
        Row(table, "统一颜色", _color);
        Row(table, "描边", _outline);
        _color.Click += (_, _) =>
        {
            var pinned = TopMost;
            TopMost = false;
            try
            {
                using var picker = new ColorDialog { Color = Color.FromArgb(255, (_colorRgb >> 16) & 255, (_colorRgb >> 8) & 255, _colorRgb & 255), FullOpen = true };
                if (picker.ShowDialog(this) == DialogResult.OK)
                {
                    _colorRgb = picker.Color.ToArgb() & 0xFFFFFF;
                    UpdateColorButton();
                }
            }
            finally { TopMost = pinned; }
        };
        _originalColors.CheckedChanged += (_, _) => _color.Enabled = !_originalColors.Checked;
        _merge.CheckedChanged += (_, _) => _mergeSeconds.Enabled = _merge.Checked;
        FillDanmaku(options);
    }

    private void FillDanmaku(DanmakuOptions options)
    {
        options = options.Sanitize();
        _screen.SelectedIndex = 0;
        for (var index = 0; index < _screen.Items.Count; index++)
            if (((DisplayChoice)_screen.Items[index]!).Device == options.ScreenDeviceName) _screen.SelectedIndex = index;
        _area.Value = options.AreaPercent; _fontSize.Value = options.FontSize; _opacity.Value = options.OpacityPercent;
        _displayRadius.Value = options.DisplayRadius;
        _travel.Value = options.TravelSeconds; _fixed.Value = options.FixedSeconds; _density.Value = options.MaxActive;
        _merge.Checked = options.MergeDuplicates; _mergeSeconds.Value = options.MergeSeconds;
        _originalColors.Checked = options.KeepOriginalColors; _outline.Checked = options.Outline;
        _colorRgb = options.ColorRgb; UpdateColorButton();
        _color.Enabled = !_originalColors.Checked; _mergeSeconds.Enabled = _merge.Checked;
    }

    private void UpdateColorButton()
    {
        _color.BackColor = Color.FromArgb(255, (_colorRgb >> 16) & 255, (_colorRgb >> 8) & 255, _colorRgb & 255);
        _color.ForeColor = _color.BackColor.GetBrightness() > 0.5 ? Color.Black : Color.White;
    }

    private void BuildRulesTab(TabPage page, IReadOnlyList<DanmakuAliasRule> rules)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(540, 0), Margin = new Padding(0, 0, 0, 10),
            Text = "添加“原写法 → 统一词”，例如 biaoji → 标记。\n匹配前自动统一大小写、空格、标点和整句重复；只合并计数，保留首条原文。\n在末尾空白行添加规则，双击单元格修改，最多 200 条。"
        }, 0, 0);
        _rules.Columns.Add(new DataGridViewTextBoxColumn { Name = "Source", HeaderText = "原写法", MaxInputLength = 256, SortMode = DataGridViewColumnSortMode.NotSortable });
        _rules.Columns.Add(new DataGridViewTextBoxColumn { Name = "Target", HeaderText = "统一词（合并为）", MaxInputLength = 256, SortMode = DataGridViewColumnSortMode.NotSortable });
        layout.Controls.Add(_rules, 0, 1);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
        var add = new Button { Text = "添加规则", AutoSize = true };
        var delete = new Button { Text = "删除选中", AutoSize = true };
        var defaults = new Button { Text = "恢复默认规则", AutoSize = true };
        add.Click += (_, _) =>
        {
            if (_rules.Rows.Count - 1 >= 200) { _message.Text = "最多支持 200 条规则。"; return; }
            var row = _rules.Rows.Add("", "");
            _rules.CurrentCell = _rules.Rows[row].Cells[0]; _rules.BeginEdit(true);
        };
        delete.Click += (_, _) =>
        {
            foreach (var row in _rules.SelectedRows.Cast<DataGridViewRow>().Where(row => !row.IsNewRow).ToArray()) _rules.Rows.Remove(row);
        };
        defaults.Click += (_, _) => FillRules(DanmakuMergeKey.DefaultRules());
        buttons.Controls.AddRange([add, delete, defaults]);
        layout.Controls.Add(buttons, 0, 2); page.Controls.Add(layout);
        FillRules(rules);
    }

    private void FillRules(IReadOnlyList<DanmakuAliasRule> rules)
    {
        _rules.EndEdit(); _rules.Rows.Clear();
        foreach (var rule in rules) _rules.Rows.Add(rule.Source, rule.Target);
    }

    private void Save()
    {
        var keys = new HotkeyOptions
        {
            TogglePlay = _keys[PlayerAction.TogglePlay].Text, Backward = _keys[PlayerAction.Backward].Text,
            Forward = _keys[PlayerAction.Forward].Text, ToggleWindow = _keys[PlayerAction.ToggleWindow].Text,
            TogglePassthrough = _keys[PlayerAction.TogglePassthrough].Text, ToggleDanmaku = _keys[PlayerAction.ToggleDanmaku].Text
        };
        _message.ForeColor = Color.Firebrick;
        if (!keys.TryGetBindings(out _, out var error)) { _message.Text = error; return; }
        _rules.EndEdit();
        var rules = _rules.Rows.Cast<DataGridViewRow>().Where(row => !row.IsNewRow)
            .Select(row => new DanmakuAliasRule(Convert.ToString(row.Cells[0].Value)?.Trim() ?? "", Convert.ToString(row.Cells[1].Value)?.Trim() ?? ""))
            .Where(rule => rule.Source.Length > 0 || rule.Target.Length > 0).ToArray();
        if (!DanmakuRuleMatcher.TryCreate(rules, out _, out error)) { _message.Text = error; return; }
        var danmaku = new DanmakuOptions
        {
            ScreenDeviceName = (_screen.SelectedItem as DisplayChoice)?.Device ?? "",
            AreaPercent = (int)_area.Value, FontSize = (int)_fontSize.Value, OpacityPercent = (int)_opacity.Value,
            DisplayRadius = (int)_displayRadius.Value,
            TravelSeconds = (int)_travel.Value, FixedSeconds = (int)_fixed.Value, MaxActive = (int)_density.Value,
            MergeDuplicates = _merge.Checked, MergeSeconds = (int)_mergeSeconds.Value,
            KeepOriginalColors = _originalColors.Checked, ColorRgb = _colorRgb, Outline = _outline.Checked, AliasRules = rules
        };
        var failure = ApplySettings?.Invoke(keys, (int)_seconds.Value, (int)_holeRadius.Value, danmaku);
        if (failure != null) { _message.Text = failure; return; }
        Options = keys; DialogResult = DialogResult.OK; Close();
    }

    private sealed record DisplayChoice(string Device, string Label) { public override string ToString() => Label; }
}
