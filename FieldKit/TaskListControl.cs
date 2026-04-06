using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace FieldKit;

/// <summary>
/// Owner-drawn task list with category groups, custom checkboxes, and status display.
/// </summary>
public class TaskListControl : Control
{
    // --- Layout constants ---
    private const int GroupHeaderHeight = 36;
    private const int TaskRowHeight = 30;
    private const int GroupGap = 2;
    private const int TopPadding = 2;
    private const int BottomPadding = 4;
    private const int AccentWidth = 3;
    private const int CheckboxSize = 16;
    private const int CbLeftGroup = 14;
    private const int CbLeftTask = 28;
    private const int TextLeftGroup = 38;
    private const int TextLeftTask = 54;
    private const int DurationColWidth = 70;
    private const int StatusColWidth = 200;
    private const int RightPad = 14;

    // --- Colors ---
    private static readonly Color BgColor = Color.White;
    private static readonly Color HeaderBg = Color.FromArgb(250, 250, 252);
    private static readonly Color HeaderBorder = Color.FromArgb(228, 228, 228);
    private static readonly Color HoverBg = Color.FromArgb(242, 245, 249);
    private static readonly Color RunningBg = Color.FromArgb(232, 240, 254);
    private static readonly Color ErrorBg = Color.FromArgb(254, 240, 240);
    private static readonly Color TextPrimary = Color.FromArgb(48, 48, 48);
    private static readonly Color TextSecondary = Color.FromArgb(120, 120, 120);
    private static readonly Color CheckedFill = Color.FromArgb(0, 120, 215);
    private static readonly Color UncheckedBorder = Color.FromArgb(170, 170, 170);
    private static readonly Color DisabledGray = Color.FromArgb(190, 190, 190);

    private static readonly Color[] GroupAccents =
    [
        Color.FromArgb(84, 110, 122),   // Preparation — blue-gray
        Color.FromArgb(46, 125, 50),    // Cleanup — green
        Color.FromArgb(21, 101, 192),   // Updates — blue
        Color.FromArgb(230, 81, 0)      // Repair — orange
    ];

    private static readonly (string Name, string Desc, TaskCategory Cat)[] GroupDefs =
    [
        ("PREPARATION", "Safety checkpoint before changes", TaskCategory.Preparation),
        ("CLEANUP", "Free up disk space and clear caches", TaskCategory.Cleanup),
        ("UPDATES", "Install security patches and app updates", TaskCategory.Updates),
        ("REPAIR & OPTIMIZE", "Scan for issues and tune performance", TaskCategory.Repair)
    ];

    // --- Layout items ---
    private enum ItemType { GroupHeader, TaskRow }
    private record struct LayoutItem(ItemType Type, int Y, int Height, int GroupIndex, int TaskIndex);

    // --- Data ---
    private List<MaintenanceTask> _tasks = [];
    private bool[] _checked = [];
    private int[][] _groupTaskIndices = [];
    private LayoutItem[] _layout = [];
    private int _totalHeight;

    // --- Interaction ---
    private int _hoverIndex = -1;
    private readonly VScrollBar _vScroll;
    private readonly ToolTip _tooltip;
    private int _tooltipTaskIndex = -1;

    // --- Fonts ---
    private readonly Font _groupNameFont;
    private readonly Font _groupDescFont;
    private readonly Font _taskNameFont;
    private readonly Font _durationFont;
    private readonly Font _statusFont;

    // --- Events ---
    public event Action? CheckChanged;

    public TaskListControl()
    {
        SetStyle(
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable, true);

        BackColor = BgColor;

        _groupNameFont = new Font("Segoe UI", 9f, FontStyle.Bold);
        _groupDescFont = new Font("Segoe UI", 8.25f);
        _taskNameFont = new Font("Segoe UI", 9.5f);
        _durationFont = new Font("Segoe UI", 8.25f);
        _statusFont = new Font("Segoe UI", 8.25f);

        _vScroll = new VScrollBar { Dock = DockStyle.Right, Visible = false };
        _vScroll.Scroll += (_, _) => Invalidate();
        Controls.Add(_vScroll);

        _tooltip = new ToolTip
        {
            AutoPopDelay = 5000,
            InitialDelay = 400,
            ReshowDelay = 200
        };
    }

    // --- Public API ---

    public void SetTasks(List<MaintenanceTask> tasks)
    {
        _tasks = tasks;
        _checked = new bool[tasks.Count];
        Array.Fill(_checked, true);
        ComputeLayout();
        Invalidate();
    }

    public int CheckedCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < _checked.Length; i++)
                if (_checked[i]) n++;
            return n;
        }
    }

    public List<int> GetCheckedIndices()
    {
        var list = new List<int>();
        for (int i = 0; i < _checked.Length; i++)
            if (_checked[i]) list.Add(i);
        return list;
    }

    public void SetAllChecked(bool value)
    {
        Array.Fill(_checked, value);
        CheckChanged?.Invoke();
        Invalidate();
    }

    public void RefreshTask(int taskIndex)
    {
        if (InvokeRequired) { Invoke(() => RefreshTask(taskIndex)); return; }
        Invalidate();
    }

    // --- Layout ---

    private void ComputeLayout()
    {
        var items = new List<LayoutItem>();
        var groups = new List<int[]>();

        int y = TopPadding;

        for (int g = 0; g < GroupDefs.Length; g++)
        {
            var cat = GroupDefs[g].Cat;
            var indices = new List<int>();
            for (int t = 0; t < _tasks.Count; t++)
                if (_tasks[t].Category == cat) indices.Add(t);

            groups.Add(indices.ToArray());
            if (indices.Count == 0) continue;

            items.Add(new LayoutItem(ItemType.GroupHeader, y, GroupHeaderHeight, g, -1));
            y += GroupHeaderHeight;

            foreach (int t in indices)
            {
                items.Add(new LayoutItem(ItemType.TaskRow, y, TaskRowHeight, g, t));
                y += TaskRowHeight;
            }

            y += GroupGap;
        }

        _groupTaskIndices = groups.ToArray();
        _layout = items.ToArray();
        _totalHeight = y + BottomPadding;
        UpdateScrollBar();
    }

    private void UpdateScrollBar()
    {
        int clientH = ClientSize.Height;
        if (_totalHeight <= clientH)
        {
            _vScroll.Visible = false;
            _vScroll.Value = 0;
        }
        else
        {
            _vScroll.Visible = true;
            _vScroll.Minimum = 0;
            _vScroll.Maximum = _totalHeight;
            _vScroll.LargeChange = Math.Max(1, clientH);
            _vScroll.SmallChange = TaskRowHeight;
            if (_vScroll.Value > Math.Max(0, _vScroll.Maximum - _vScroll.LargeChange))
                _vScroll.Value = Math.Max(0, _vScroll.Maximum - _vScroll.LargeChange);
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateScrollBar();
    }

    // --- Painting ---

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(BgColor);

        int scrollY = _vScroll.Visible ? _vScroll.Value : 0;
        int clientH = ClientSize.Height;
        int clientW = ClientSize.Width - (_vScroll.Visible ? _vScroll.Width : 0);

        for (int i = 0; i < _layout.Length; i++)
        {
            ref readonly var item = ref _layout[i];
            int paintY = item.Y - scrollY;

            if (paintY + item.Height < 0) continue;
            if (paintY > clientH) break;

            bool hovered = Enabled && i == _hoverIndex;

            if (item.Type == ItemType.GroupHeader)
                PaintGroupHeader(g, paintY, clientW, item.GroupIndex, hovered);
            else
                PaintTaskRow(g, paintY, clientW, item.TaskIndex, item.GroupIndex, hovered);
        }

        // Subtle top/bottom border
        using var borderPen = new Pen(HeaderBorder);
        g.DrawLine(borderPen, 0, 0, clientW, 0);
        g.DrawLine(borderPen, 0, ClientSize.Height - 1, ClientSize.Width, ClientSize.Height - 1);
    }

    private void PaintGroupHeader(Graphics g, int y, int w, int gi, bool hovered)
    {
        var (name, desc, _) = GroupDefs[gi];
        var accent = GroupAccents[gi];

        // Background
        using (var brush = new SolidBrush(hovered ? HoverBg : HeaderBg))
            g.FillRectangle(brush, 0, y, w, GroupHeaderHeight);

        // Accent bar
        using (var brush = new SolidBrush(accent))
            g.FillRectangle(brush, 0, y, AccentWidth, GroupHeaderHeight);

        // Bottom border
        using (var pen = new Pen(HeaderBorder))
            g.DrawLine(pen, AccentWidth, y + GroupHeaderHeight - 1, w, y + GroupHeaderHeight - 1);

        // Group checkbox
        var state = GetGroupCheckState(gi);
        PaintCheckbox(g, CbLeftGroup, y + (GroupHeaderHeight - CheckboxSize) / 2,
            state == GrpCheck.Checked, state == GrpCheck.Indeterminate);

        // Name in accent color
        var nameColor = Enabled ? accent : DisabledGray;
        var nameSize = TextRenderer.MeasureText(name, _groupNameFont, Size.Empty, TextFormatFlags.NoPrefix);
        var nameRect = new Rectangle(TextLeftGroup, y, nameSize.Width, GroupHeaderHeight);
        TextRenderer.DrawText(g, name, _groupNameFont, nameRect,
            nameColor, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        // Description
        int descX = TextLeftGroup + nameSize.Width;
        var descRect = new Rectangle(descX, y, w - descX - RightPad, GroupHeaderHeight);
        TextRenderer.DrawText(g, $"\u2014  {desc}", _groupDescFont, descRect,
            TextSecondary, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }

    private void PaintTaskRow(Graphics g, int y, int w, int ti, int gi, bool hovered)
    {
        var task = _tasks[ti];

        // Row background based on state
        Color rowBg = task.State switch
        {
            TaskState.Running => RunningBg,
            TaskState.Error => ErrorBg,
            _ when hovered => HoverBg,
            _ => Color.Empty
        };
        if (rowBg != Color.Empty)
        {
            using var brush = new SolidBrush(rowBg);
            g.FillRectangle(brush, AccentWidth, y, w - AccentWidth, TaskRowHeight);
        }

        // Faint accent continuation
        using (var brush = new SolidBrush(Color.FromArgb(35, GroupAccents[gi])))
            g.FillRectangle(brush, 0, y, AccentWidth, TaskRowHeight);

        // Checkbox
        PaintCheckbox(g, CbLeftTask, y + (TaskRowHeight - CheckboxSize) / 2, _checked[ti]);

        // Task name
        int rightReserved = StatusColWidth + DurationColWidth + RightPad + 8;
        var nameRect = new Rectangle(TextLeftTask, y, w - TextLeftTask - rightReserved, TaskRowHeight);
        var nameColor = Enabled ? TextPrimary : DisabledGray;
        TextRenderer.DrawText(g, task.Name, _taskNameFont, nameRect,
            nameColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

        // Duration badge
        int durX = w - StatusColWidth - DurationColWidth - RightPad;
        var durRect = new Rectangle(durX, y, DurationColWidth, TaskRowHeight);
        TextRenderer.DrawText(g, task.DurationLabel, _durationFont, durRect,
            TextSecondary, TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.SingleLine);

        // Status or context info
        if (task.State != TaskState.Pending)
        {
            var (text, color) = StatusDisplay(task);
            int statX = w - StatusColWidth - RightPad;
            var statRect = new Rectangle(statX, y, StatusColWidth, TaskRowHeight);
            TextRenderer.DrawText(g, text, _statusFont, statRect,
                color, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }
        else if (!string.IsNullOrEmpty(task.ContextInfo))
        {
            int ctxX = w - StatusColWidth - RightPad;
            var ctxRect = new Rectangle(ctxX, y, StatusColWidth, TaskRowHeight);
            TextRenderer.DrawText(g, task.ContextInfo, _statusFont, ctxRect,
                TextSecondary, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }
    }

    private void PaintCheckbox(Graphics g, int x, int y, bool isChecked, bool indeterminate = false)
    {
        var rect = new Rectangle(x, y, CheckboxSize, CheckboxSize);
        const int r = 3;

        if (!Enabled)
        {
            using var path = RoundedRect(rect, r);
            if (isChecked || indeterminate)
            {
                using var brush = new SolidBrush(DisabledGray);
                g.FillPath(brush, path);
                DrawCheckmark(g, x, y, Color.White);
            }
            else
            {
                using var pen = new Pen(DisabledGray, 1.5f);
                g.DrawPath(pen, path);
            }
            return;
        }

        if (isChecked || indeterminate)
        {
            using var path = RoundedRect(rect, r);
            using var brush = new SolidBrush(CheckedFill);
            g.FillPath(brush, path);

            if (indeterminate)
            {
                using var pen = new Pen(Color.White, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                int mid = y + CheckboxSize / 2;
                g.DrawLine(pen, x + 4, mid, x + CheckboxSize - 4, mid);
            }
            else
            {
                DrawCheckmark(g, x, y, Color.White);
            }
        }
        else
        {
            using var path = RoundedRect(rect, r);
            using var pen = new Pen(UncheckedBorder, 1.5f);
            g.DrawPath(pen, path);
        }
    }

    private static void DrawCheckmark(Graphics g, int x, int y, Color color)
    {
        using var pen = new Pen(color, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, x + 4, y + 8, x + 7, y + 11);
        g.DrawLine(pen, x + 7, y + 11, x + 12, y + 5);
    }

    private static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static (string Text, Color Color) StatusDisplay(MaintenanceTask task) => task.State switch
    {
        TaskState.Running => ("\u25CF Running\u2026", Color.FromArgb(0, 120, 215)),
        TaskState.Success => string.IsNullOrEmpty(task.ResultNote)
            ? ("\u2713 Done", Color.FromArgb(46, 125, 50))
            : ($"\u2713 {task.ResultNote}", Color.FromArgb(46, 125, 50)),
        TaskState.Warning => ($"\u26A0 {task.ResultNote}", Color.FromArgb(195, 135, 0)),
        TaskState.Error => ($"\u2715 {task.ResultNote}", Color.FromArgb(198, 40, 40)),
        TaskState.Skipped => ($"\u2014 {task.ResultNote}", Color.FromArgb(120, 120, 120)),
        _ => ("", SystemColors.WindowText)
    };

    private enum GrpCheck { Unchecked, Checked, Indeterminate }

    private GrpCheck GetGroupCheckState(int gi)
    {
        var indices = _groupTaskIndices[gi];
        if (indices.Length == 0) return GrpCheck.Unchecked;
        int n = 0;
        foreach (int i in indices)
            if (_checked[i]) n++;
        if (n == 0) return GrpCheck.Unchecked;
        if (n == indices.Length) return GrpCheck.Checked;
        return GrpCheck.Indeterminate;
    }

    // --- Mouse input ---

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!Enabled) return;

        int idx = HitTest(e.Y);
        if (idx != _hoverIndex)
        {
            _hoverIndex = idx;
            Invalidate();
        }

        // Tooltip
        int taskIdx = idx >= 0 && idx < _layout.Length && _layout[idx].Type == ItemType.TaskRow
            ? _layout[idx].TaskIndex : -1;
        if (taskIdx != _tooltipTaskIndex)
        {
            _tooltipTaskIndex = taskIdx;
            _tooltip.SetToolTip(this, taskIdx >= 0 ? _tasks[taskIdx].Description : "");
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverIndex != -1)
        {
            _hoverIndex = -1;
            _tooltipTaskIndex = -1;
            _tooltip.SetToolTip(this, "");
            Invalidate();
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (!Enabled || e.Button != MouseButtons.Left) return;
        Focus();

        int idx = HitTest(e.Y);
        if (idx < 0 || idx >= _layout.Length) return;

        ref readonly var item = ref _layout[idx];
        if (item.Type == ItemType.GroupHeader)
        {
            var state = GetGroupCheckState(item.GroupIndex);
            bool newVal = state != GrpCheck.Checked;
            foreach (int t in _groupTaskIndices[item.GroupIndex])
                _checked[t] = newVal;
        }
        else
        {
            _checked[item.TaskIndex] = !_checked[item.TaskIndex];
        }

        CheckChanged?.Invoke();
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!_vScroll.Visible) return;

        int delta = e.Delta > 0 ? -TaskRowHeight * 3 : TaskRowHeight * 3;
        int maxVal = Math.Max(0, _vScroll.Maximum - _vScroll.LargeChange + 1);
        _vScroll.Value = Math.Clamp(_vScroll.Value + delta, 0, maxVal);
        Invalidate();
    }

    private int HitTest(int mouseY)
    {
        int scrollY = _vScroll.Visible ? _vScroll.Value : 0;
        int absY = mouseY + scrollY;

        for (int i = 0; i < _layout.Length; i++)
        {
            ref readonly var item = ref _layout[i];
            if (absY >= item.Y && absY < item.Y + item.Height)
                return i;
        }
        return -1;
    }

    // --- Cleanup ---

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _groupNameFont.Dispose();
            _groupDescFont.Dispose();
            _taskNameFont.Dispose();
            _durationFont.Dispose();
            _statusFont.Dispose();
            _tooltip.Dispose();
        }
        base.Dispose(disposing);
    }
}
