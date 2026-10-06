using System.Drawing.Drawing2D;

namespace FloatScreen;

internal enum ToolbarGlyph { Back, Refresh, History, Bookmarks, AddBookmark }

internal static class UiTheme
{
    public static readonly Color Text = Color.FromArgb(38, 49, 60);
    public static readonly Color Muted = Color.FromArgb(102, 116, 129);
    public static readonly Color Border = Color.FromArgb(221, 227, 232);
    public static readonly Color Accent = Color.FromArgb(36, 120, 93);
    public static readonly Color Selection = Color.FromArgb(226, 244, 235);
    public static Icon AppIcon { get; } = LoadIcon();
    private static readonly Dictionary<ToolbarGlyph, Image> Glyphs = [];

    private static Icon LoadIcon()
    {
        using var stream = typeof(UiTheme).Assembly.GetManifestResourceStream("FloatScreen.app.ico")!;
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }

    public static Image Glyph(ToolbarGlyph type)
    {
        if (Glyphs.TryGetValue(type, out var image)) return image;
        var bitmap = new Bitmap(24, 24);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Color.FromArgb(226, 232, 240), 1.8F)
        { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        switch (type)
        {
            case ToolbarGlyph.Back:
                g.DrawLines(pen, [new(13, 5), new(6, 12), new(13, 19)]); g.DrawLine(pen, 6, 12, 20, 12); break;
            case ToolbarGlyph.Refresh:
                g.DrawArc(pen, 5, 5, 14, 14, 40, 285); g.DrawLines(pen, [new(15, 4), new(20, 5), new(19, 10)]); break;
            case ToolbarGlyph.History:
                g.DrawEllipse(pen, 4, 4, 16, 16); g.DrawLines(pen, [new(12, 7), new(12, 12), new(16, 14)]); break;
            case ToolbarGlyph.Bookmarks:
                g.DrawLines(pen, [new(8, 3), new(19, 3), new(19, 18), new(14, 15), new(8, 18), new(8, 3)]);
                g.DrawLines(pen, [new(5, 7), new(5, 21), new(11, 18)]); break;
            case ToolbarGlyph.AddBookmark:
                g.DrawLines(pen, [new(5, 3), new(16, 3), new(16, 11)]);
                g.DrawLines(pen, [new(5, 3), new(5, 20), new(10, 17)]);
                g.DrawLine(pen, 17, 14, 17, 22); g.DrawLine(pen, 13, 18, 21, 18); break;
        }
        Glyphs[type] = bitmap;
        return bitmap;
    }

    public static void ApplyDialog(Form form)
    {
        form.Icon = AppIcon;
        form.BackColor = Color.FromArgb(245, 247, 248);
        form.ForeColor = Text;
        StyleChildren(form);
    }

    private static void StyleChildren(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            switch (control)
            {
                case Button button when button.Text != "选择统一文字颜色":
                    var primary = button.Text is "保存" or "打开";
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderColor = Border;
                    button.FlatAppearance.BorderSize = primary ? 0 : 1;
                    button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(30, 101, 78) : Selection;
                    button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(25, 87, 66) : Color.FromArgb(208, 232, 221);
                    button.BackColor = primary ? Accent : Color.White;
                    button.ForeColor = primary ? Color.White : button.Text.Contains("删除") || button.Text == "清空全部"
                        ? Color.FromArgb(161, 65, 65) : Text;
                    button.Padding = new Padding(10, 4, 10, 4);
                    button.Margin = new Padding(4);
                    button.UseVisualStyleBackColor = false;
                    break;
                case DataGridView grid:
                    grid.BackgroundColor = Color.White;
                    grid.BorderStyle = BorderStyle.FixedSingle;
                    grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
                    grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
                    grid.EnableHeadersVisualStyles = false;
                    grid.GridColor = Border;
                    grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 245, 243);
                    grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
                    grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(241, 245, 243);
                    grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Text;
                    grid.DefaultCellStyle.BackColor = Color.White;
                    grid.DefaultCellStyle.ForeColor = Text;
                    grid.DefaultCellStyle.SelectionBackColor = Selection;
                    grid.DefaultCellStyle.SelectionForeColor = Text;
                    grid.DefaultCellStyle.Padding = new Padding(4, 2, 4, 2);
                    grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 252, 251);
                    grid.RowTemplate.Height = (int)Math.Round(30 * grid.DeviceDpi / 96D);
                    if (!grid.VirtualMode)
                        foreach (DataGridViewRow row in grid.Rows) row.Height = grid.RowTemplate.Height;
                    grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
                    grid.ColumnHeadersHeight = (int)Math.Round(34 * grid.DeviceDpi / 96D);
                    break;
                case TabControl tabs:
                    tabs.Padding = new Point(14, 7);
                    break;
                case TabPage page:
                    page.UseVisualStyleBackColor = false; page.BackColor = Color.White; break;
                case ListBox list:
                    list.BorderStyle = BorderStyle.FixedSingle; list.BackColor = Color.White; list.ForeColor = Text; break;
                case TextBox box when box is not HotkeyCaptureBox:
                    box.BorderStyle = BorderStyle.FixedSingle; box.BackColor = Color.White; box.ForeColor = Text; break;
                case Label label when label.ForeColor != Color.Firebrick:
                    label.ForeColor = Muted; break;
                case CheckBox or NumericUpDown or ComboBox:
                    control.ForeColor = Text; break;
            }
            if (control is not NumericUpDown and not ComboBox and not DataGridView) StyleChildren(control);
        }
    }
}
