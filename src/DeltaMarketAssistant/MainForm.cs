using DeltaMarketAssistant.Core;
using DeltaMarketAssistant.Core.Models;

namespace DeltaMarketAssistant;

internal sealed class MainForm : Form
{
    private const int StartPauseHotkeyId = 101;
    private const int EmergencyHotkeyId = 102;
    private const int CalibrationHotkeyId = 103;
    private const int WmHotkey = 0x0312;

    private readonly ConfigurationStore _configurationStore;
    private readonly ScreenCapture _screenCapture = new();
    private readonly AppConfig _config;
    private readonly AppLogger _logger;
    private readonly ComboBox _windowSelector = new();
    private readonly TextBox _startHotkey = new();
    private readonly TextBox _stopHotkey = new();
    private readonly Dictionary<string, TimingValueInput> _timingInputs = new();
    private readonly Label _settingsStatus = new() { AutoSize = true };
    private readonly System.Windows.Forms.Timer _settingsSaveTimer = new() { Interval = 500 };
    private readonly HotkeyPairRegistration _hotkeyRegistration;
    private bool _loadingSettings = true;
    private bool _timingDirty;
    private bool _starting;
    private int _startGeneration;
    private readonly RadioButton _detectionOnlyMode = new();
    private readonly RadioButton _automaticMode = new();
    private readonly ComboBox _timingMode = new();
    private readonly Label _stateLabel = new();
    private readonly Label _calibrationLabel = new();
    private readonly Label _failureLabel = new();
    private readonly RichTextBox _logBox = new();
    private readonly PictureBox _preview = new();
    private readonly Button _startButton = new();
    private readonly Button _stopButton = new();
    private readonly NotifyIcon _trayIcon;
    private readonly bool _uiPreviewMode = string.Equals(
        Environment.GetEnvironmentVariable("DMA_UI_PREVIEW"),
        "1",
        StringComparison.Ordinal);
    private AutomationEngine? _engine;
    private CalibrationStage _calibrationStage;
    private CalibrationProfile? _pendingCalibration;
    private bool _processingCalibration;
    private bool _allowExit;

    protected override bool ShowWithoutActivation => _uiPreviewMode || base.ShowWithoutActivation;

    public MainForm() : this(new ConfigurationStore())
    {
    }

    internal MainForm(ConfigurationStore configurationStore)
    {
        _configurationStore = configurationStore;
        _config = _configurationStore.Load();
        _logger = new AppLogger(_configurationStore.BaseDirectory);
        _logger.MessageWritten += AppendLog;
        _hotkeyRegistration = new HotkeyPairRegistration(
            (id, chord) => GlobalHotkeys.Register(Handle, id, chord),
            id => GlobalHotkeys.Unregister(Handle, id));

        Text = $"三角洲商城助手 v{Application.ProductVersion}";
        MinimumSize = new Size(920, 650);
        Size = new Size(1080, 760);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(13, 19, 25);
        ForeColor = Color.FromArgb(226, 232, 236);
        Font = new Font("Microsoft YaHei UI", 9.5f);
        Icon = LoadApplicationIcon();

        _trayIcon = BuildTrayIcon();
        BuildLayout();
        LoadHotkeyChoices();
        _loadingSettings = false;
        _settingsSaveTimer.Tick += (_, _) =>
        {
            _settingsSaveTimer.Stop();
            SaveTimingInputs(false);
        };

        Shown += (_, _) =>
        {
            if (_uiPreviewMode)
            {
                return;
            }

            RefreshWindowList();
            RegisterConfiguredHotkeys(true);
            UpdateCalibrationStatus();
            _logger.Write($"助手版本 {Application.ProductVersion}，本地数字模板与双计时模式");
        };
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized)
            {
                Hide();
            }
        };
        FormClosing += OnFormClosing;
    }

    private enum CalibrationStage
    {
        None,
        Main,
        Dialog
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(20),
            BackColor = BackColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var header = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 10)
        };
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.Controls.Add(new Label
        {
            Text = "三角洲商城助手",
            AutoSize = true,
            Margin = new Padding(0),
            Font = new Font(Font.FontFamily, 18f, FontStyle.Bold),
            ForeColor = Color.White
        }, 0, 0);
        header.Controls.Add(new Label
        {
            Text = "前台窗口识别与队首购买",
            AutoSize = true,
            Margin = new Padding(2, 4, 0, 0),
            ForeColor = Color.FromArgb(143, 160, 171)
        }, 0, 1);
        root.Controls.Add(header, 0, 0);

        var controls = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.FromArgb(18, 27, 34),
            Padding = new Padding(12)
        };
        controls.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        controls.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        controls.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(controls, 0, 1);

        var targetRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        targetRow.Controls.Add(CreateInlineLabel("目标窗口"));
        _windowSelector.DropDownStyle = ComboBoxStyle.DropDownList;
        _windowSelector.Width = 580;
        _windowSelector.Height = 34;
        targetRow.Controls.Add(_windowSelector);
        var refreshWindowsButton = CreateButton("刷新窗口", Color.FromArgb(49, 65, 75), 112);
        refreshWindowsButton.Click += (_, _) => RefreshWindowList();
        targetRow.Controls.Add(refreshWindowsButton);
        var calibrationButton = CreateButton("开始校准", Color.FromArgb(31, 112, 131), 112);
        calibrationButton.Click += async (_, _) => await BeginCalibrationAsync();
        targetRow.Controls.Add(calibrationButton);
        controls.Controls.Add(targetRow, 0, 0);

        var optionsRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 6, 0, 0)
        };
        optionsRow.Controls.Add(CreateInlineLabel("运行模式"));

        _detectionOnlyMode.Text = "仅识别（不点击）";
        _detectionOnlyMode.AutoSize = true;
        _detectionOnlyMode.Checked = true;
        _detectionOnlyMode.Margin = new Padding(0, 8, 14, 0);
        _detectionOnlyMode.CheckedChanged += (_, _) => UpdateStartButtonText();
        optionsRow.Controls.Add(_detectionOnlyMode);

        _automaticMode.Text = "自动购买（会点击）";
        _automaticMode.AutoSize = true;
        _automaticMode.ForeColor = Color.FromArgb(32, 214, 157);
        _automaticMode.Margin = new Padding(0, 8, 14, 0);
        _automaticMode.CheckedChanged += (_, _) => UpdateStartButtonText();
        optionsRow.Controls.Add(_automaticMode);
        _timingMode.DropDownStyle = ComboBoxStyle.DropDownList;
        _timingMode.Width = 150;
        _timingMode.Items.AddRange(["倒计时驱动", "固定延迟"]);
        _timingMode.SelectedIndex = _config.Automation.CountdownDriven ? 0 : 1;
        _timingMode.SelectedIndexChanged += (_, _) =>
        {
            UpdateTimingModeInputs();
            if (_loadingSettings) return;
            UpdateCalibrationStatus();
            _timingDirty = true;
            _settingsSaveTimer.Stop();
            _settingsSaveTimer.Start();
        };
        optionsRow.Controls.Add(_timingMode);

        var startHotkeyGroup = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty
        };
        startHotkeyGroup.Controls.Add(CreateInlineLabel("开始/暂停"));
        ConfigureHotkeyInput(_startHotkey);
        startHotkeyGroup.Controls.Add(_startHotkey);
        optionsRow.Controls.Add(startHotkeyGroup);
        var stopHotkeyGroup = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty
        };
        stopHotkeyGroup.Controls.Add(CreateInlineLabel("紧急停止"));
        ConfigureHotkeyInput(_stopHotkey);
        stopHotkeyGroup.Controls.Add(_stopHotkey);
        optionsRow.Controls.Add(stopHotkeyGroup);
        var saveHotkeysButton = CreateButton("保存并应用", Color.FromArgb(49, 65, 75), 118);
        saveHotkeysButton.Click += async (_, _) => await ApplySettingsAsync();
        optionsRow.Controls.Add(saveHotkeysButton);
        controls.Controls.Add(optionsRow, 0, 1);

        var timingRow = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top, WrapContents = true
        };
        foreach (var option in TimingOptions.All)
        {
            var group = new FlowLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
                Margin = new Padding(0, 0, 12, 0)
            };
            var input = new TimingValueInput
            {
                Name = option.Id, Minimum = option.Minimum, Maximum = option.Maximum,
                Increment = option.Increment, Width = option == TimingOptions.BurstDuration ? 140 : 90,
                Value = option.Clamp(option.Read(_config.Automation))
            };
            input.TextChanged += (_, _) =>
            {
                if (_loadingSettings) return;
                _timingDirty = true;
                _settingsStatus.Text = "参数未保存";
                _settingsSaveTimer.Stop();
                _settingsSaveTimer.Start();
            };
            input.Leave += (_, _) => SaveTimingInputs(false);
            group.Controls.Add(CreateInlineLabel(option.Label));
            group.Controls.Add(input);
            timingRow.Controls.Add(group);
            _timingInputs.Add(option.Id, input);
        }
        _settingsStatus.Text = "参数已加载";
        _settingsStatus.ForeColor = Color.FromArgb(143, 160, 171);
        _settingsStatus.Margin = new Padding(0, 8, 0, 0);
        timingRow.Controls.Add(_settingsStatus);
        controls.Controls.Add(timingRow, 0, 2);
        UpdateTimingModeInputs();

        var statusBand = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            BackColor = Color.FromArgb(9, 14, 18),
            Padding = new Padding(14, 10, 14, 10),
            MinimumSize = new Size(0, 68)
        };
        statusBand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        statusBand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        statusBand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15));
        _stateLabel.Text = "状态：未启动";
        _stateLabel.Dock = DockStyle.Fill;
        _stateLabel.Font = new Font(Font, FontStyle.Bold);
        _stateLabel.TextAlign = ContentAlignment.MiddleLeft;
        _stateLabel.AutoEllipsis = true;
        statusBand.Controls.Add(_stateLabel, 0, 0);
        _calibrationLabel.Dock = DockStyle.Fill;
        _calibrationLabel.TextAlign = ContentAlignment.MiddleLeft;
        _calibrationLabel.AutoEllipsis = true;
        statusBand.Controls.Add(_calibrationLabel, 1, 0);
        _failureLabel.Text = "异常：0 / 3";
        _failureLabel.Dock = DockStyle.Fill;
        _failureLabel.TextAlign = ContentAlignment.MiddleRight;
        _failureLabel.AutoEllipsis = true;
        statusBand.Controls.Add(_failureLabel, 2, 0);
        root.Controls.Add(statusBand, 0, 2);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor = Color.FromArgb(31, 43, 52)
        };
        split.HandleCreated += (_, _) => BeginInvoke(() =>
        {
            if (split.Width < 700)
            {
                return;
            }

            split.SplitterDistance = Math.Clamp((int)(split.Width * 0.62), 360, split.Width - 260);
            split.Panel1MinSize = 360;
            split.Panel2MinSize = 260;
        });
        _preview.Dock = DockStyle.Fill;
        _preview.SizeMode = PictureBoxSizeMode.Zoom;
        _preview.BackColor = Color.Black;
        split.Panel1.Controls.Add(_preview);
        _logBox.Dock = DockStyle.Fill;
        _logBox.ReadOnly = true;
        _logBox.BackColor = Color.FromArgb(16, 23, 29);
        _logBox.ForeColor = Color.FromArgb(193, 205, 212);
        _logBox.BorderStyle = BorderStyle.None;
        _logBox.Font = new Font("Consolas", 9f);
        split.Panel2.Controls.Add(_logBox);
        root.Controls.Add(split, 0, 3);

        var commands = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 8, 0, 0)
        };
        _stopButton.Text = "紧急停止  F12";
        _stopButton.AutoSize = true;
        _stopButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _stopButton.MinimumSize = new Size(160, 40);
        _stopButton.Padding = new Padding(10, 2, 10, 2);
        _stopButton.BackColor = Color.FromArgb(163, 56, 65);
        _stopButton.ForeColor = Color.White;
        _stopButton.FlatStyle = FlatStyle.Flat;
        _stopButton.UseVisualStyleBackColor = false;
        _stopButton.Click += async (_, _) => await EmergencyStopAsync();
        commands.Controls.Add(_stopButton);
        _startButton.Text = "开始仅识别  F8";
        _startButton.AutoSize = true;
        _startButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _startButton.MinimumSize = new Size(180, 40);
        _startButton.Padding = new Padding(10, 2, 10, 2);
        _startButton.BackColor = Color.FromArgb(20, 137, 105);
        _startButton.ForeColor = Color.White;
        _startButton.FlatStyle = FlatStyle.Flat;
        _startButton.UseVisualStyleBackColor = false;
        _startButton.Margin = new Padding(0, 0, 10, 0);
        _startButton.Click += async (_, _) => await ToggleAutomationAsync();
        commands.Controls.Add(_startButton);
        root.Controls.Add(commands, 0, 4);
    }

    private static Label CreateInlineLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MinimumSize = new Size(0, 38),
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(0, 0, 6, 0)
    };

    private static Button CreateButton(string text, Color color, int width) => new()
    {
        Text = text,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new Size(width, 38),
        Padding = new Padding(8, 2, 8, 2),
        BackColor = color,
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
        UseVisualStyleBackColor = false,
        Margin = new Padding(8, 0, 0, 0)
    };

    private void UpdateStartButtonText()
    {
        var hotkey = _config.StartPauseHotkey;
        _startButton.Text = _automaticMode.Checked
            ? $"开始自动购买  {hotkey}"
            : $"开始仅识别  {hotkey}";
    }

    private NotifyIcon BuildTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("显示控制窗", null, (_, _) => ShowControlWindow());
        menu.Items.Add("紧急停止", null, async (_, _) => await EmergencyStopAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) =>
        {
            _allowExit = true;
            Close();
        });

        var tray = new NotifyIcon
        {
            Text = $"三角洲商城助手 v{Application.ProductVersion}",
            Visible = !_uiPreviewMode,
            ContextMenuStrip = menu
        };
        var formIcon = Icon ?? SystemIcons.Application;
        tray.Icon = (Icon)formIcon.Clone();
        tray.DoubleClick += (_, _) => ShowControlWindow();
        return tray;
    }

    private static Icon LoadApplicationIcon()
    {
        try
        {
            var executablePath = Environment.ProcessPath ?? Application.ExecutablePath;
            return Icon.ExtractAssociatedIcon(executablePath) ?? SystemIcons.Application;
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    private void ShowControlWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void LoadHotkeyChoices()
    {
        _startHotkey.Text = _config.StartPauseHotkey;
        _stopHotkey.Text = _config.EmergencyStopHotkey;
        UpdateStartButtonText();
        _stopButton.Text = $"紧急停止  {_config.EmergencyStopHotkey}";
    }

    private static void ConfigureHotkeyInput(TextBox input)
    {
        input.Width = 150;
        input.ReadOnly = true;
        input.ShortcutsEnabled = false;
        input.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Tab) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return;
            input.Text = HotkeyChord.FormatKeyData(e.KeyData);
        };
    }

    private bool SaveTimingInputs(bool showErrors)
    {
        if (_loadingSettings) return true;
        var candidate = ConfigurationStore.Snapshot(_config);
        candidate.Automation.CountdownDriven = _timingMode.SelectedIndex == 0;
        var values = _timingInputs.ToDictionary(pair => pair.Key, pair => pair.Value.Text);
        if (!TimingOptions.TryApply(candidate.Automation, values, out var error))
        {
            _settingsStatus.Text = error;
            if (showErrors) MessageBox.Show(this, error, "参数无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        if (!_timingDirty && !showErrors) return true;
        try
        {
            _configurationStore.Save(candidate);
            _config.Automation = candidate.Automation;
            _timingDirty = false;
            _settingsStatus.Text = _engine?.IsRunning == true ? "已保存，待应用" : "已保存，下次启动生效";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _settingsStatus.Text = "参数保存失败";
            if (showErrors) MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    private void UpdateTimingModeInputs()
    {
        var dynamic = _timingMode.SelectedIndex == 0;
        foreach (var option in new[] { TimingOptions.StartDelay, TimingOptions.BurstDuration, TimingOptions.ExtraDuration })
            if (_timingInputs.TryGetValue(option.Id, out var input)) input.Parent!.Visible = !dynamic;
        foreach (var option in new[] { TimingOptions.CountdownLead, TimingOptions.CountdownTail })
            if (_timingInputs.TryGetValue(option.Id, out var input)) input.Parent!.Visible = dynamic;
    }

    private async Task ApplySettingsAsync()
    {
        if (!SaveTimingInputs(true)) return;
        _startGeneration++;
        await StopEngineAsync("应用设置，已暂停");
        if (!RegisterConfiguredHotkeys(true)) return;
        _settingsStatus.Text = "参数已应用，当前已暂停";
        _logger.Write("界面参数已保存并应用，下次启动使用完整新配置");
    }

    private void RefreshWindowList()
    {
        var priorHandle = (_windowSelector.SelectedItem as WindowInfo)?.Handle ?? IntPtr.Zero;
        var windows = WindowService.EnumerateVisibleWindows()
            .Where(window => window.Handle != Handle)
            .ToArray();
        _windowSelector.BeginUpdate();
        _windowSelector.Items.Clear();
        _windowSelector.Items.AddRange(windows);
        _windowSelector.EndUpdate();

        var preferred = windows.FirstOrDefault(window => window.Handle == priorHandle) ??
                        windows.FirstOrDefault(window =>
                            window.ProcessName.Equals(_config.TargetProcessName, StringComparison.OrdinalIgnoreCase)) ??
                        windows.FirstOrDefault(window =>
                            window.Title.Contains(_config.TargetWindowTitle, StringComparison.OrdinalIgnoreCase));
        if (preferred is not null)
        {
            _windowSelector.SelectedItem = preferred;
        }
        else if (windows.Length > 0)
        {
            _windowSelector.SelectedIndex = 0;
        }
    }

    private bool RegisterConfiguredHotkeys(bool showErrors)
    {
        if (!IsHandleCreated)
        {
            return false;
        }

        var startName = _startHotkey.Text;
        var stopName = _stopHotkey.Text;
        if (!_hotkeyRegistration.TryApply(startName, stopName, out var error))
        {
            _settingsStatus.Text = error;
            if (showErrors)
            {
                MessageBox.Show(this, error, "热键注册失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return false;
        }

        HotkeyChord.TryParse(startName, out var start, out _);
        HotkeyChord.TryParse(stopName, out var stop, out _);
        _config.StartPauseHotkey = start.Name;
        _config.EmergencyStopHotkey = stop.Name;
        _startHotkey.Text = start.Name;
        _stopHotkey.Text = stop.Name;
        try
        {
            _configurationStore.Save(_config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _settingsStatus.Text = "热键已注册，但未能写入配置";
            if (showErrors) MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        UpdateStartButtonText();
        _stopButton.Text = $"紧急停止  {stop.Name}";
        return true;
    }

    private async Task BeginCalibrationAsync()
    {
        if (_windowSelector.SelectedItem is not WindowInfo target)
        {
            MessageBox.Show(this, "请先选择游戏窗口。", "无法校准", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await StopEngineAsync("开始校准");
        if (!WindowService.HasExpectedAspectRatio(target.Handle, _config.Automation.AspectRatioTolerance))
        {
            MessageBox.Show(this, "目标窗口客户区必须保持 16:9。", "无法校准", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _config.TargetProcessName = target.ProcessName;
        _config.TargetWindowTitle = target.Title;
        _pendingCalibration = null;
        _calibrationStage = CalibrationStage.Main;
        if (!GlobalHotkeys.Register(Handle, CalibrationHotkeyId, Keys.F10))
        {
            _calibrationStage = CalibrationStage.None;
            MessageBox.Show(this, "F10 已被其他程序占用，无法进入校准。", "校准热键失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        MessageBox.Show(
            this,
            "请让收藏第一件商品保持选中，并确认一级按钮显示“公示中”。关闭此提示后，在游戏主商城界面按 F10 抓取校准画面。",
            "第一阶段：主界面",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        PrepareTargetForCalibration(target);
    }

    private void PrepareTargetForCalibration(WindowInfo target)
    {
        Hide();
        WindowService.BringToForeground(target.Handle);
        _trayIcon.ShowBalloonTip(2500, "校准等待中", "保持游戏在前台，画面准备好后按 F10。", ToolTipIcon.Info);
    }

    private void CaptureCalibrationStage()
    {
        if (_calibrationStage == CalibrationStage.None || _processingCalibration ||
            _windowSelector.SelectedItem is not WindowInfo target)
        {
            return;
        }

        if (!WindowService.IsForeground(target.Handle))
        {
            _trayIcon.ShowBalloonTip(2000, "未捕捉", "F10 按下时游戏不是前台窗口。", ToolTipIcon.Warning);
            return;
        }

        var snapshot = _screenCapture.CaptureClient(target.Handle);
        if (snapshot is null || VisualAnalyzer.Analyze(snapshot).IsBlank)
        {
            snapshot?.Dispose();
            _trayIcon.ShowBalloonTip(2000, "未捕捉", "游戏画面为空，请保持窗口可见后重试。", ToolTipIcon.Warning);
            return;
        }

        _processingCalibration = true;
        BeginInvoke(() => ProcessCalibrationSnapshot(snapshot, target));
    }

    private void ProcessCalibrationSnapshot(Bitmap snapshot, WindowInfo target)
    {
        try
        {
            if (_calibrationStage == CalibrationStage.Main)
            {
                var steps = new[]
                {
                    new CalibrationStep("第一件收藏", "拖框覆盖左上角第一件商品的可点击主体区域。"),
                    new CalibrationStep("一级购买按钮", "拖框覆盖右侧详情栏底部完整的“公示中”按钮。"),
                    new CalibrationStep("刷新按钮", "拖框覆盖排序栏右侧的双箭头刷新图标。"),
                    new CalibrationStep("一级倒计时", "拖框覆盖按钮上方整行倒计时，包含数字、分、秒和后解锁购买；左右留空以适应文字移动，不包含按钮。")
                };
                using var form = new CalibrationForm(snapshot, steps);
                if (form.ShowDialog() != DialogResult.OK)
                {
                    EndCalibration("校准已取消", false);
                    return;
                }

                _pendingCalibration = new CalibrationProfile
                {
                    FirstItemRegion = form.Selections[0],
                    PrimaryButtonRegion = form.Selections[1],
                    RefreshButtonRegion = form.Selections[2],
                    MainCountdownRegion = form.Selections[3],
                    PrimaryInactiveFingerprint = VisualAnalyzer.CreateFingerprint(snapshot, form.Selections[1]),
                    RefreshFingerprint = VisualAnalyzer.CreateFingerprint(snapshot, form.Selections[2])
                };
                _pendingCalibration.MainAnchorRegion = CalibrationProfile.DeriveMainAnchorRegion(
                    _pendingCalibration.RefreshButtonRegion);
                _pendingCalibration.MainAnchorFingerprint = VisualAnalyzer.CreateFingerprint(
                    snapshot,
                    _pendingCalibration.MainAnchorRegion);
                LearnCountdownSample(snapshot, _pendingCalibration.MainCountdownRegion, _pendingCalibration);
                _calibrationStage = CalibrationStage.Dialog;
                ShowControlWindow();
                MessageBox.Show(
                    this,
                    "回到游戏，等待一级按钮出现价格并手动点击一次以打开二层购买弹窗。不要点击最终价格；弹窗稳定后按 F10 抓取画面。",
                    "第二阶段：购买弹窗",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                PrepareTargetForCalibration(target);
                return;
            }

            var dialogSteps = new[]
            {
                new CalibrationStep("弹窗标题", "拖框覆盖“外观购买”标题文字和其下方横向亮线，不要包含大面积空白。"),
                new CalibrationStep("二层确认价格", "拖框覆盖弹窗右下角完整的绿色价格确认区域。"),
                new CalibrationStep("二层倒计时", "拖框覆盖确认按钮上方整行倒计时，包含数字、分、秒和后解锁购买；不要框到价格。")
            };
            using (var form = new CalibrationForm(snapshot, dialogSteps))
            {
                if (form.ShowDialog() != DialogResult.OK || _pendingCalibration is null)
                {
                    EndCalibration("校准已取消", false);
                    return;
                }

                _pendingCalibration.DialogHeaderRegion = form.Selections[0];
                _pendingCalibration.ConfirmButtonRegion = form.Selections[1];
                _pendingCalibration.DialogCountdownRegion = form.Selections[2];
                _pendingCalibration.DialogHeaderFingerprint = VisualAnalyzer.CreateFingerprint(
                    snapshot,
                    _pendingCalibration.DialogHeaderRegion);
                _pendingCalibration.CalibratedAtUtc = DateTime.UtcNow;
                LearnCountdownSample(snapshot, _pendingCalibration.DialogCountdownRegion, _pendingCalibration);
                _config.SchemaVersion = 11;
                _config.Calibration = _pendingCalibration;
                _configurationStore.Save(_config);
            }

            EndCalibration("校准完成，建议先运行“仅识别”模式", true);
        }
        finally
        {
            _processingCalibration = false;
        }
    }

    private void LearnCountdownSample(Bitmap snapshot, NormalizedRect region, CalibrationProfile profile)
    {
        using var crop = VisualAnalyzer.Crop(snapshot, region);
        var reader = new CountdownReader(profile.CountdownTemplates);
        using var prompt = new CountdownSampleForm(crop, reader.Read(crop));
        if (prompt.ShowDialog(this) != DialogResult.OK) return;
        var learned = reader.LearnSample(crop, prompt.TotalSeconds);
        if (learned.Count == 0)
        {
            MessageBox.Show(this, "未能分离“分/秒”及数字，未录入样本。可重新校准更清晰的倒计时区域。",
                "样本未录入", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        profile.CountdownTemplates.AddRange(learned);
        _logger.Write($"已本地录入倒计时数字样本：{string.Join("", learned.Select(g => g.Value))}");
    }

    private void EndCalibration(string message, bool completed)
    {
        GlobalHotkeys.Unregister(Handle, CalibrationHotkeyId);
        _calibrationStage = CalibrationStage.None;
        _pendingCalibration = null;
        ShowControlWindow();
        UpdateCalibrationStatus();
        _logger.Write(message);
        if (completed)
        {
            MessageBox.Show(this, message, "校准完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async Task ToggleAutomationAsync()
    {
        if (_starting) return;
        if (_engine?.IsRunning == true)
        {
            await StopEngineAsync("用户暂停");
            return;
        }

        if (_windowSelector.SelectedItem is not WindowInfo target)
        {
            MessageBox.Show(this, "请先选择游戏窗口。", "无法启动", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_config.Calibration is not { IsComplete: true })
        {
            MessageBox.Show(this, "首次运行必须完成两阶段校准。", "无法启动", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!SaveTimingInputs(true)) return;
        if (_config.Automation.CountdownDriven && !_config.Calibration.HasCountdownRegions)
        {
            MessageBox.Show(this, "请重新校准，补充一级和二层倒计时区域；或手动选择固定延迟模式。",
                "需要倒计时校准", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!RegisterConfiguredHotkeys(true)) return;
        _starting = true;
        var generation = ++_startGeneration;
        try
        {
            var detectionOnly = _detectionOnlyMode.Checked;
            await StopEngineAsync("重新启动");
            _config.TargetProcessName = target.ProcessName;
            _config.TargetWindowTitle = target.Title;
            _configurationStore.Save(_config);

            Hide();
            WindowService.BringToForeground(target.Handle);
            await Task.Delay(320);
            if (generation != _startGeneration) return;
            if (!WindowService.IsForeground(target.Handle))
            {
                ShowControlWindow();
                MessageBox.Show(this, "无法将目标游戏切换到前台。", "无法启动", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _engine = new AutomationEngine(
                ConfigurationStore.Snapshot(_config),
                target.Handle,
                detectionOnly,
                _configurationStore.DiagnosticsDirectory,
                _logger);
            _engine.StateChanged += OnEngineStateChanged;
            _engine.FailureCountChanged += OnFailureCountChanged;
            _engine.PreviewReady += OnPreviewReady;
            _engine.Start();
            _trayIcon.ShowBalloonTip(1500, "自动化已启动", detectionOnly ? "当前为仅识别模式。" : "自动点击已启用。", ToolTipIcon.Info);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowControlWindow();
            MessageBox.Show(this, ex.Message, "启动配置保存失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _starting = false;
        }
    }

    private async Task EmergencyStopAsync()
    {
        _startGeneration++;
        await StopEngineAsync("紧急停止");
        _trayIcon.ShowBalloonTip(1800, "已停止", "自动购买已紧急停止。", ToolTipIcon.Warning);
    }

    private async Task StopEngineAsync(string reason)
    {
        var engine = _engine;
        _engine = null;
        if (engine is null)
        {
            return;
        }

        await engine.StopAsync(reason);
        engine.Dispose();
    }

    private void OnEngineStateChanged(AutomationState state, string message)
    {
        RunOnUi(() =>
        {
            _stateLabel.Text = $"状态：{message}";
            _stateLabel.ForeColor = state switch
            {
                AutomationState.Paused => Color.FromArgb(238, 103, 112),
                AutomationState.Opening or AutomationState.Confirming or AutomationState.Confirmed => Color.FromArgb(32, 214, 157),
                AutomationState.Recovery => Color.FromArgb(245, 183, 67),
                _ => Color.FromArgb(226, 232, 236)
            };
        });
    }

    private void OnFailureCountChanged(int count) =>
        RunOnUi(() => _failureLabel.Text = $"异常：{count} / {_config.Automation.FailureLimit}");

    private void OnPreviewReady(Bitmap bitmap)
    {
        RunOnUi(() =>
        {
            var old = _preview.Image;
            _preview.Image = bitmap;
            old?.Dispose();
        }, bitmap.Dispose);
    }

    private void AppendLog(string message)
    {
        RunOnUi(() =>
        {
            _logBox.AppendText(message + Environment.NewLine);
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.ScrollToCaret();
        });
    }

    private void RunOnUi(Action action, Action? whenUnavailable = null)
    {
        if (IsDisposed || Disposing)
        {
            whenUnavailable?.Invoke();
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
                whenUnavailable?.Invoke();
            }
        }
        else
        {
            action();
        }
    }

    private void UpdateCalibrationStatus()
    {
        var complete = _config.SchemaVersion >= 2 && _config.Calibration is { IsComplete: true };
        if (complete && _timingMode.SelectedIndex == 0 && !_config.Calibration!.HasCountdownRegions)
        {
            _calibrationLabel.Text = "校准：待补充倒计时区域";
            _calibrationLabel.ForeColor = Color.FromArgb(245, 183, 67);
            return;
        }
        _calibrationLabel.Text = complete
            ? $"校准：完成  {_config.Calibration!.CalibratedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}"
            : _config.Calibration is null ? "校准：未完成" : "校准：旧版配置，请重新校准";
        _calibrationLabel.ForeColor = complete
            ? Color.FromArgb(32, 214, 157)
            : Color.FromArgb(245, 183, 67);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmHotkey)
        {
            if (_startHotkey.ContainsFocus || _stopHotkey.ContainsFocus)
            {
                base.WndProc(ref message);
                return;
            }
            switch (message.WParam.ToInt32())
            {
                case StartPauseHotkeyId:
                    _ = ToggleAutomationAsync();
                    break;
                case EmergencyHotkeyId:
                    _ = EmergencyStopAsync();
                    break;
                case CalibrationHotkeyId:
                    CaptureCalibrationStage();
                    break;
            }
        }

        base.WndProc(ref message);
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        _settingsSaveTimer.Stop();
        SaveTimingInputs(false);
        if (!_allowExit && eventArgs.CloseReason == CloseReason.UserClosing)
        {
            eventArgs.Cancel = true;
            Hide();
            return;
        }

        _engine?.Dispose();
        _startGeneration++;
        _hotkeyRegistration.Unregister();
        _settingsSaveTimer.Dispose();

        if (_calibrationStage != CalibrationStage.None)
        {
            GlobalHotkeys.Unregister(Handle, CalibrationHotkeyId);
        }

        _trayIcon.Visible = false;
        _trayIcon.Icon?.Dispose();
        _trayIcon.Dispose();
        _preview.Image?.Dispose();
    }
}
