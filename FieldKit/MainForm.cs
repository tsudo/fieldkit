using System.Diagnostics;
using System.Text;
using FieldKit.Services;
using FieldKit.Tasks;

namespace FieldKit;

public class MainForm : Form
{
    private readonly IReadOnlyList<MaintenanceTask> _tasks;
    private readonly Logger _logger;
    private readonly ListView _operationsList;
    private readonly RichTextBox _detailsBox;
    private readonly RichTextBox _logBox;
    private readonly CheckBox _previewOnlyCheckBox;
    private readonly Button _runButton;
    private readonly Button _toggleDetailsButton;
    private readonly Button _openLogButton;
    private readonly Button _systemInfoButton;
    private readonly Label _selectedCountLabel;
    private readonly Label _advancedCountLabel;
    private readonly Label _modeLabel;
    private readonly Label _focusLabel;
    private readonly ToolStripStatusLabel _statusLabel;
    private readonly ToolStripProgressBar _progressBar;
    private readonly Dictionary<MaintenanceTask, ListViewItem> _taskItems = new();
    private readonly Dictionary<MaintenanceTask, string> _taskStatuses = new();
    private CancellationTokenSource? _runCts;
    private bool _syncingChecks;
    private string _currentPresetLabel = "Custom Selection";

    public MainForm(IReadOnlyList<MaintenanceTask> tasks, Logger logger)
    {
        _tasks = tasks;
        _logger = logger;

        Text = "FieldKit";
        Size = new Size(1240, 860);
        MinimumSize = new Size(1120, 760);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Color.FromArgb(18, 21, 29);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = BackColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

        var headerPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(24, 28, 38),
            Padding = new Padding(24, 16, 24, 12)
        };

        var appTitleLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 34,
            Text = "FieldKit",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 21f, FontStyle.Bold)
        };

        var appSubtitleLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Use one app for cleanup, updates, and system health. Quick presets make focused runs easy.",
            ForeColor = Color.FromArgb(177, 186, 205),
            Font = new Font("Segoe UI", 10.5f)
        };

        headerPanel.Controls.Add(appSubtitleLabel);
        headerPanel.Controls.Add(appTitleLabel);

        var controlsPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 10, 18, 10),
            BackColor = BackColor
        };

        var controlsLayout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = controlsPanel.BackColor
        };

        _runButton = CreateToolbarButton("Run Selected", 132, Color.FromArgb(47, 126, 221), Color.White, borderless: true);
        _runButton.Margin = new Padding(0, 0, 10, 0);
        _runButton.Click += async (_, _) => await RunSelectedTasksAsync();

        _previewOnlyCheckBox = new CheckBox
        {
            Text = "Preview Only",
            AutoSize = true,
            Margin = new Padding(4, 7, 18, 0),
            ForeColor = Color.FromArgb(230, 235, 243)
        };
        _previewOnlyCheckBox.CheckedChanged += (_, _) => UpdateSelectionSummary();

        _toggleDetailsButton = CreateToolbarButton("Hide Details", 108);
        _toggleDetailsButton.Margin = new Padding(0, 0, 8, 0);
        _toggleDetailsButton.Click += (_, _) => ToggleDetails();

        _openLogButton = CreateToolbarButton("Open Log", 96);
        _openLogButton.Margin = new Padding(0, 0, 8, 0);
        _openLogButton.Click += (_, _) => OpenLog();

        _systemInfoButton = CreateToolbarButton("System Info", 104);
        _systemInfoButton.Click += (_, _) =>
        {
            using var form = new SystemInfoForm();
            form.ShowDialog(this);
        };

        controlsLayout.Controls.AddRange([_runButton, _previewOnlyCheckBox, _toggleDetailsButton, _openLogButton, _systemInfoButton]);
        controlsPanel.Controls.Add(controlsLayout);

        var horizontalSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(64, 71, 86)
        };

        var verticalSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(64, 71, 86)
        };

        var operationsPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(31, 35, 46),
            Padding = new Padding(18, 16, 14, 14)
        };

        var operationsHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 132,
            ColumnCount = 3,
            BackColor = operationsPanel.BackColor
        };
        operationsHeader.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        operationsHeader.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        operationsHeader.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
        operationsHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        operationsHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        operationsHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));

        var operationsTitlePanel = new Panel { Dock = DockStyle.Fill, BackColor = operationsPanel.BackColor };
        var operationsTitle = new Label
        {
            Dock = DockStyle.Top,
            Height = 32,
            Text = "Operations",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 16f, FontStyle.Bold)
        };
        var operationsSubtitle = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Text = "Pick a preset for the kind of work you want to do, or build a custom run.",
            ForeColor = Color.FromArgb(170, 180, 198),
            Font = new Font("Segoe UI", 9.5f)
        };
        operationsTitlePanel.Controls.Add(operationsSubtitle);
        operationsTitlePanel.Controls.Add(operationsTitle);

        _selectedCountLabel = CreateStatCard("Selected");
        _advancedCountLabel = CreateStatCard("Advanced");

        operationsHeader.Controls.Add(operationsTitlePanel, 0, 0);
        operationsHeader.Controls.Add(_selectedCountLabel, 1, 0);
        operationsHeader.Controls.Add(_advancedCountLabel, 2, 0);

        var presetsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = operationsPanel.BackColor,
            Margin = new Padding(0)
        };
        presetsPanel.Controls.Add(CreatePresetButton("All Tasks", task => true));
        presetsPanel.Controls.Add(CreatePresetButton("Cleanup", task => task.Category == "Preparation" || task.Category == "Cleanup"));
        presetsPanel.Controls.Add(CreatePresetButton("Updates", task => task.Category == "Updates"));
        presetsPanel.Controls.Add(CreatePresetButton("System Health", task => task.Category == "Preparation" || task.Category == "Repair & Optimize"));
        presetsPanel.Controls.Add(CreatePresetButton("Custom Reset", task => task.SelectedByDefault));

        operationsHeader.Controls.Add(presetsPanel, 0, 1);
        operationsHeader.SetColumnSpan(presetsPanel, 3);

        _operationsList = new ListView
        {
            Dock = DockStyle.Fill,
            CheckBoxes = true,
            FullRowSelect = true,
            MultiSelect = false,
            View = View.Details,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            HideSelection = false,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(31, 35, 46),
            ForeColor = Color.FromArgb(240, 243, 247)
        };
        _operationsList.Columns.Add("Operation", 300);
        _operationsList.Columns.Add("Area", 130);
        _operationsList.Columns.Add("Time", 90);
        _operationsList.Columns.Add("Safety", 120);
        _operationsList.Columns.Add("Status", 100);
        _operationsList.ItemChecked += OperationsListOnItemChecked;
        _operationsList.SelectedIndexChanged += (_, _) => RefreshDetails();

        operationsPanel.Controls.Add(_operationsList);
        operationsPanel.Controls.Add(operationsHeader);

        var sidebar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = BackColor,
            Padding = new Padding(8, 0, 0, 0)
        };
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 180));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));

        var summaryPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(31, 35, 46),
            Padding = new Padding(18, 16, 18, 14),
            Margin = new Padding(0)
        };

        var summaryTitle = new Label
        {
            Dock = DockStyle.Top,
            Height = 30,
            Text = "Run Summary",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold)
        };

        _focusLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Text = "Focus: Custom Selection",
            ForeColor = Color.FromArgb(143, 174, 245),
            Font = new Font("Segoe UI", 10f)
        };

        _modeLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Text = "Mode: Live run",
            ForeColor = Color.FromArgb(134, 223, 188),
            Font = new Font("Segoe UI", 10f)
        };

        var summaryHint = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Use presets to work by intent: cleanup only, updates only, or system health. You can still fine-tune the list before running.",
            ForeColor = Color.FromArgb(170, 180, 198),
            Font = new Font("Segoe UI", 9.5f)
        };

        summaryPanel.Controls.Add(summaryHint);
        summaryPanel.Controls.Add(_modeLabel);
        summaryPanel.Controls.Add(_focusLabel);
        summaryPanel.Controls.Add(summaryTitle);

        var detailsPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(31, 35, 46),
            Padding = new Padding(18, 14, 18, 16),
            Margin = new Padding(0, 10, 0, 10)
        };

        var detailsTitle = new Label
        {
            Dock = DockStyle.Top,
            Height = 30,
            Text = "Operation Details",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold)
        };

        _detailsBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(31, 35, 46),
            ForeColor = Color.FromArgb(224, 228, 235),
            Font = new Font("Segoe UI", 10f),
            ScrollBars = RichTextBoxScrollBars.Vertical
        };

        detailsPanel.Controls.Add(_detailsBox);
        detailsPanel.Controls.Add(detailsTitle);

        var roadmapPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(31, 35, 46),
            Padding = new Padding(18, 16, 18, 14),
            Margin = new Padding(0)
        };

        var roadmapTitle = new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            Text = "Next Modules",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold)
        };

        var roadmapText = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Good future fits: connectivity checks, external IP lookup, ping/tracert, DNS tools, and app bundles via Ninite or winget.",
            ForeColor = Color.FromArgb(170, 180, 198),
            Font = new Font("Segoe UI", 9.5f)
        };

        roadmapPanel.Controls.Add(roadmapText);
        roadmapPanel.Controls.Add(roadmapTitle);

        sidebar.Controls.Add(summaryPanel, 0, 0);
        sidebar.Controls.Add(detailsPanel, 0, 1);
        sidebar.Controls.Add(roadmapPanel, 0, 2);

        _logBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(16, 19, 27),
            ForeColor = Color.FromArgb(229, 235, 241),
            Font = new Font("Consolas", 9f),
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Vertical
        };

        var logPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(16, 19, 27)
        };

        var logTitle = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Text = "Live Log",
            ForeColor = Color.FromArgb(200, 208, 223),
            Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
            BackColor = Color.FromArgb(16, 19, 27),
            Padding = new Padding(8, 4, 0, 0)
        };

        logPanel.Controls.Add(_logBox);
        logPanel.Controls.Add(logTitle);

        horizontalSplit.Panel1.Padding = new Padding(18, 14, 18, 10);
        horizontalSplit.Panel2.Padding = new Padding(18, 4, 18, 18);

        verticalSplit.Panel1.Controls.Add(operationsPanel);
        verticalSplit.Panel2.Controls.Add(sidebar);
        horizontalSplit.Panel1.Controls.Add(verticalSplit);
        horizontalSplit.Panel2.Controls.Add(logPanel);

        var statusStrip = new StatusStrip();
        _statusLabel = new ToolStripStatusLabel("Ready");
        _progressBar = new ToolStripProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Size = new Size(180, 18)
        };
        statusStrip.Items.Add(_statusLabel);
        statusStrip.Items.Add(new ToolStripStatusLabel { Spring = true });
        statusStrip.Items.Add(_progressBar);

        root.Controls.Add(headerPanel, 0, 0);
        root.Controls.Add(controlsPanel, 0, 1);
        root.Controls.Add(horizontalSplit, 0, 2);
        root.Controls.Add(statusStrip, 0, 3);

        Controls.Add(root);

        BuildOperationsList();
        UpdateSelectionSummary();
        _logger.LogWritten += OnLogWritten;
        Load += async (_, _) => await LoadContextAsync();
        FormClosed += (_, _) =>
        {
            _logger.LogWritten -= OnLogWritten;
            _runCts?.Dispose();
        };
    }

    private async Task LoadContextAsync()
    {
        foreach (var task in _tasks)
        {
            try
            {
                await task.GatherContextAsync();
            }
            catch (Exception ex)
            {
                _logger.Log($"[{task.Name}] Context probe failed: {ex.Message}", "WARN");
            }
        }

        UpdateSelectionSummary();
        RefreshDetails();
    }

    private void BuildOperationsList()
    {
        _operationsList.BeginUpdate();
        _operationsList.Items.Clear();
        _operationsList.Groups.Clear();
        _taskItems.Clear();

        foreach (var category in _tasks.Select(t => t.Category).Distinct())
            _operationsList.Groups.Add(new ListViewGroup(category, HorizontalAlignment.Left) { Name = category });

        foreach (var task in _tasks)
        {
            var item = new ListViewItem(task.Name)
            {
                Tag = task,
                Checked = task.SelectedByDefault,
                Group = _operationsList.Groups[task.Category]
            };
            item.SubItems.Add(task.Category);
            item.SubItems.Add(task.EstimatedTime);
            item.SubItems.Add(task.IsAdvanced ? "Advanced" : "Routine");
            item.SubItems.Add("Ready");

            _operationsList.Items.Add(item);
            _taskItems[task] = item;
            _taskStatuses[task] = "Ready";
            UpdateListItemVisual(task);
        }

        if (_operationsList.Items.Count > 0)
            _operationsList.Items[0].Selected = true;

        _operationsList.EndUpdate();
    }

    private void OperationsListOnItemChecked(object? sender, ItemCheckedEventArgs e)
    {
        if (_syncingChecks || e.Item.Tag is not MaintenanceTask)
            return;

        _currentPresetLabel = "Custom Selection";
        UpdateSelectionSummary();
        RefreshDetails();
    }

    private void RefreshDetails()
    {
        var task = _operationsList.SelectedItems.Count > 0
            ? _operationsList.SelectedItems[0].Tag as MaintenanceTask
            : null;

        var sb = new StringBuilder();

        if (task is not null)
        {
            sb.AppendLine(task.Name);
            sb.AppendLine();
            sb.AppendLine(task.Description);
            sb.AppendLine();
            sb.AppendLine($"Area: {task.Category}");
            sb.AppendLine($"Estimated time: {task.EstimatedTime}");
            sb.AppendLine($"Current status: {_taskStatuses.GetValueOrDefault(task, "Ready")}");
            sb.AppendLine($"Default in presets: {(task.SelectedByDefault ? "Yes" : "No")}");
            sb.AppendLine($"Operation type: {(task.IsAdvanced ? "Advanced / repair" : "Routine")}");
            if (task.RequiresInternet)
                sb.AppendLine("Requires internet: Yes");
            if (task.MayRequireReboot)
                sb.AppendLine("May require reboot: Yes");
            if (!string.IsNullOrWhiteSpace(task.ContextInfo))
            {
                sb.AppendLine();
                sb.AppendLine("Current system context");
                sb.AppendLine(task.ContextInfo);
            }
        }
        else
        {
            sb.AppendLine("Select an operation from the list.");
            sb.AppendLine();
            sb.AppendLine("Use the quick presets to switch between common workflows:");
            sb.AppendLine("- Cleanup for storage and cache work");
            sb.AppendLine("- Updates for app and Windows patching");
            sb.AppendLine("- System Health for deeper repair-focused checks");
        }

        _detailsBox.Text = sb.ToString();
    }

    private async Task RunSelectedTasksAsync()
    {
        if (_runCts is not null)
            return;

        var selectedTasks = _taskItems.Where(kvp => kvp.Value.Checked).Select(kvp => kvp.Key).ToList();
        if (selectedTasks.Count == 0)
        {
            MessageBox.Show(this, "Select at least one operation before running.", "Nothing Selected",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _runCts = new CancellationTokenSource();
        var dryRun = _previewOnlyCheckBox.Checked;
        var startFree = TryGetSystemDriveFreeSpace();
        var rebootRecommended = false;
        int success = 0, warning = 0, error = 0, skipped = 0;

        SetRunningState(true);
        _logger.Log($"Run started. Preview mode: {(dryRun ? "ON" : "OFF")}. Log: {_logger.LogFilePath}", "INFO");

        try
        {
            for (int i = 0; i < selectedTasks.Count; i++)
            {
                var task = selectedTasks[i];
                _statusLabel.Text = $"Running {task.Name} ({i + 1}/{selectedTasks.Count})";
                _progressBar.Value = (int)Math.Round((double)i / selectedTasks.Count * 100);
                _taskStatuses[task] = "Running";
                UpdateListItemVisual(task);
                RefreshDetails();

                TaskResult result;
                try
                {
                    result = await task.ExecuteAsync(dryRun, _runCts.Token);
                }
                catch (OperationCanceledException)
                {
                    result = TaskResult.Skipped("Cancelled");
                }
                catch (Exception ex)
                {
                    _logger.Log($"[{task.Name}] Unhandled failure: {ex.Message}", "ERROR");
                    result = TaskResult.Fail(ex.Message);
                }

                rebootRecommended |= result.RebootRecommended;

                switch (result.State)
                {
                    case TaskResultState.Success:
                        success++;
                        _taskStatuses[task] = "Success";
                        break;
                    case TaskResultState.Warning:
                        warning++;
                        _taskStatuses[task] = "Warning";
                        break;
                    case TaskResultState.Skipped:
                        skipped++;
                        _taskStatuses[task] = "Skipped";
                        break;
                    default:
                        error++;
                        _taskStatuses[task] = "Error";
                        break;
                }

                if (!string.IsNullOrWhiteSpace(result.Summary))
                {
                    _logger.Log($"[{task.Name}] {result.Summary}", result.State switch
                    {
                        TaskResultState.Success => "OK",
                        TaskResultState.Warning => "WARN",
                        TaskResultState.Skipped => "WARN",
                        _ => "ERROR"
                    });
                }

                UpdateListItemVisual(task);
                RefreshDetails();
            }
        }
        finally
        {
            _progressBar.Value = 100;
            _statusLabel.Text = "Run complete";
            SetRunningState(false);
            _runCts.Dispose();
            _runCts = null;
        }

        var endFree = TryGetSystemDriveFreeSpace();
        var summary = new StringBuilder();
        summary.AppendLine(dryRun ? "Preview complete." : "Run complete.");
        summary.AppendLine();
        summary.AppendLine($"Successful: {success}");
        summary.AppendLine($"Warnings: {warning}");
        summary.AppendLine($"Errors: {error}");
        if (skipped > 0)
            summary.AppendLine($"Skipped: {skipped}");

        if (startFree.HasValue && endFree.HasValue)
        {
            var delta = Math.Round(endFree.Value - startFree.Value, 2);
            summary.AppendLine();
            summary.AppendLine($"System drive free space: {startFree.Value:F2} GB -> {endFree.Value:F2} GB ({delta:+0.00;-0.00;0.00} GB)");
        }

        if (rebootRecommended)
        {
            summary.AppendLine();
            summary.AppendLine("A reboot is recommended before relying on repair results.");
        }

        summary.AppendLine();
        summary.AppendLine($"Log file: {_logger.LogFilePath}");

        MessageBox.Show(this, summary.ToString(), "FieldKit",
            MessageBoxButtons.OK, warning > 0 || error > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
    }

    private void SetRunningState(bool isRunning)
    {
        _runButton.Enabled = !isRunning;
        _previewOnlyCheckBox.Enabled = !isRunning;
        _operationsList.Enabled = !isRunning;
        _toggleDetailsButton.Enabled = !isRunning;
        _openLogButton.Enabled = !isRunning;
        _systemInfoButton.Enabled = !isRunning;
    }

    private void ApplyPreset(string label, Func<MaintenanceTask, bool> predicate)
    {
        try
        {
            _syncingChecks = true;
            foreach (var task in _tasks)
                _taskItems[task].Checked = predicate(task);
        }
        finally
        {
            _syncingChecks = false;
        }

        _currentPresetLabel = label;
        UpdateSelectionSummary();
        RefreshDetails();
    }

    private void UpdateSelectionSummary()
    {
        var selected = _taskItems.Count(kvp => kvp.Value.Checked);
        var advanced = _taskItems.Count(kvp => kvp.Value.Checked && kvp.Key.IsAdvanced);

        _selectedCountLabel.Text = $"Selected{Environment.NewLine}{selected}";
        _advancedCountLabel.Text = $"Advanced{Environment.NewLine}{advanced}";
        _focusLabel.Text = $"Focus: {_currentPresetLabel}";
        _modeLabel.Text = _previewOnlyCheckBox.Checked ? "Mode: Preview only" : "Mode: Live run";
        _modeLabel.ForeColor = _previewOnlyCheckBox.Checked
            ? Color.FromArgb(255, 211, 122)
            : Color.FromArgb(134, 223, 188);
    }

    private void UpdateListItemVisual(MaintenanceTask task)
    {
        if (!_taskItems.TryGetValue(task, out var item))
            return;

        var status = _taskStatuses.GetValueOrDefault(task, "Ready");
        item.SubItems[4].Text = status;
        item.UseItemStyleForSubItems = false;

        var rowColor = status switch
        {
            "Success" => Color.FromArgb(132, 234, 178),
            "Warning" => Color.FromArgb(255, 208, 122),
            "Error" => Color.FromArgb(255, 135, 135),
            "Running" => Color.FromArgb(143, 174, 245),
            "Skipped" => Color.FromArgb(163, 170, 183),
            _ => Color.FromArgb(240, 243, 247)
        };

        item.ForeColor = Color.FromArgb(240, 243, 247);
        item.SubItems[4].ForeColor = rowColor;
        item.SubItems[3].ForeColor = task.IsAdvanced
            ? Color.FromArgb(255, 208, 122)
            : Color.FromArgb(132, 234, 178);
    }

    private void ToggleDetails()
    {
        _logBox.Visible = !_logBox.Visible;
        _toggleDetailsButton.Text = _logBox.Visible ? "Hide Details" : "Show Details";
    }

    private void OpenLog()
    {
        try
        {
            if (!File.Exists(_logger.LogFilePath))
            {
                MessageBox.Show(this, "No log file has been created yet.", "Open Log",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = _logger.LogFilePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open the log file: {ex.Message}", "Open Log",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnLogWritten(string message, string level)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => OnLogWritten(message, level)));
            return;
        }

        _logBox.AppendText(message + Environment.NewLine);
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.ScrollToCaret();
    }

    private Button CreateToolbarButton(string text, int width, Color? backColor = null, Color? foreColor = null, bool borderless = false)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = 34,
            BackColor = backColor ?? Color.FromArgb(38, 43, 57),
            ForeColor = foreColor ?? Color.FromArgb(235, 238, 244),
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0)
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(82, 91, 112);
        button.FlatAppearance.BorderSize = borderless ? 0 : 1;
        return button;
    }

    private Button CreatePresetButton(string label, Func<MaintenanceTask, bool> predicate)
    {
        var button = CreateToolbarButton(label, label == "System Health" ? 120 : 96);
        button.Height = 32;
        button.Margin = new Padding(0, 4, 8, 0);
        button.Click += (_, _) => ApplyPreset(label, predicate);
        return button;
    }

    private Label CreateStatCard(string title)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(8, 2, 0, 2),
            Padding = new Padding(14, 10, 14, 10),
            BackColor = Color.FromArgb(41, 46, 60),
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = $"{title}{Environment.NewLine}0"
        };
    }

    private static double? TryGetSystemDriveFreeSpace()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory);
            if (string.IsNullOrWhiteSpace(root))
                return null;

            var drive = new DriveInfo(root);
            return drive.AvailableFreeSpace / 1024d / 1024d / 1024d;
        }
        catch
        {
            return null;
        }
    }
}
