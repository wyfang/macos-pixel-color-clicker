using System.Diagnostics;
using System.ComponentModel;
using System.Security.Principal;

namespace PixelColorClicker;

internal sealed class MainForm : Form
{
    private const int StartHotkeyId = 0x5043;
    private static readonly Keys[] AvailableHotkeys =
    [
        Keys.F6, Keys.F7, Keys.F8, Keys.F9, Keys.F10, Keys.F11, Keys.F12
    ];

    private readonly Panel _privilegePanel;
    private readonly Label _privilegeLabel;
    private readonly Button _restartAsAdminButton;
    private readonly Button _selectPositionButton;
    private readonly Label _positionLabel;
    private readonly ComboBox _modeCombo;
    private readonly Panel _targetSection;
    private readonly Panel _changeSection;
    private ComboBox _regionSizeCombo = null!;
    private NumericUpDown _changeDelayInput = null!;
    private NumericUpDown _changeCountInput = null!;
    private NumericUpDown _changeIntervalInput = null!;
    private FlowLayoutPanel _colorRowsPanel = null!;
    private Button _addColorButton = null!;
    private readonly NumericUpDown _toleranceInput;
    private readonly ComboBox _clickLocationCombo;
    private readonly NumericUpDown _countdownInput;
    private readonly ComboBox _hotkeyCombo;
    private readonly Label _hotkeyStatusLabel;
    private readonly Label _currentColorLabel;
    private readonly Button _startButton;
    private readonly Label _statusLabel;
    private readonly List<ColorTargetRowControl> _colorRows = [];
    private readonly List<ColorTarget> _activeTargets = [];
    private readonly System.Windows.Forms.Timer _pollTimer;
    private readonly System.Windows.Forms.Timer _countdownTimer;
    private readonly CancellationTokenSource _lifetimeCancellation = new();

    private NativeMethods.POINT? _selectedPoint;
    private Color? _baselineColor;
    private volatile bool _selectingPosition;
    private volatile ColorTargetRowControl? _pickingColorRow;
    private volatile bool _isCountingDown;
    private volatile bool _isMonitoring;
    private int _countdownRemaining;
    private bool _changeArmed = true;
    private Guid? _lastMatchedTargetId;
    private long _lastChangeTimestamp;
    private long _lastColorDisplayTimestamp;
    private CancellationTokenSource? _pendingClickCancellation;
    private CancellationTokenSource? _clickSequenceCancellation;
    private ClickPlan _activeChangePlan = new(0, 1, 100);
    private bool _hotkeyRegistered;

    internal MainForm()
    {
        Text = "屏幕颜色触发点击器";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(760, 890);
        MinimumSize = new Size(776, 760);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        KeyPreview = true;
        Font = new Font("Microsoft YaHei UI", 9F);

        var root = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(18),
            BackColor = SystemColors.Window
        };

        var title = new Label
        {
            Text = "屏幕颜色触发点击",
            Font = new Font(Font.FontFamily, 16F, FontStyle.Bold),
            Width = 710,
            Height = 38,
            TextAlign = ContentAlignment.MiddleLeft
        };

        _privilegePanel = new Panel
        {
            Width = 710,
            Height = 54,
            Margin = new Padding(0, 0, 0, 6),
            Padding = new Padding(10, 8, 10, 8)
        };
        _privilegeLabel = new Label
        {
            Width = 510,
            Height = 36,
            Font = new Font(Font.FontFamily, 9F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _restartAsAdminButton = new Button
        {
            Text = "以管理员身份重启",
            Width = 160,
            Height = 32,
            Left = 530,
            Top = 10
        };
        _privilegePanel.Controls.Add(_restartAsAdminButton);
        _privilegePanel.Controls.Add(_privilegeLabel);

        var positionRow = CreateRow("监控位置");
        _selectPositionButton = new Button { Text = "选择位置", Width = 105, Height = 30 };
        _positionLabel = new Label
        {
            Text = "尚未选择",
            Width = 260,
            Height = 30,
            ForeColor = SystemColors.GrayText,
            TextAlign = ContentAlignment.MiddleLeft
        };
        positionRow.Controls.Add(_selectPositionButton);
        positionRow.Controls.Add(_positionLabel);

        var modeRow = CreateRow("监控功能");
        _modeCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 190
        };
        _modeCombo.Items.AddRange(["匹配目标颜色", "检测像素变化"]);
        _modeCombo.SelectedIndex = 0;
        modeRow.Controls.Add(_modeCombo);

        _targetSection = BuildTargetSection();
        _changeSection = BuildChangeSection(
            out _regionSizeCombo,
            out _changeDelayInput,
            out _changeCountInput,
            out _changeIntervalInput
        );
        _changeSection.Visible = false;

        var toleranceRow = CreateRow("颜色容差");
        _toleranceInput = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 100,
            Value = 10,
            Width = 70,
            TextAlign = HorizontalAlignment.Right
        };
        var toleranceHint = new Label
        {
            Text = "RGB 每个通道允许 ±10",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(8, 7, 0, 0)
        };
        toleranceRow.Controls.Add(_toleranceInput);
        toleranceRow.Controls.Add(toleranceHint);

        var clickRow = CreateRow("点击位置");
        _clickLocationCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 190
        };
        _clickLocationCombo.Items.AddRange(["选择的监控位置", "当前鼠标位置"]);
        _clickLocationCombo.SelectedIndex = 0;
        clickRow.Controls.Add(_clickLocationCombo);

        var countdownRow = CreateRow("启动倒计时");
        _countdownInput = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 60,
            Value = AppSettings.DefaultCountdownSeconds,
            Width = 70,
            TextAlign = HorizontalAlignment.Right
        };
        countdownRow.Controls.Add(_countdownInput);
        countdownRow.Controls.Add(new Label
        {
            Text = "秒（0 表示立即开始）",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(8, 7, 0, 0)
        });

        var hotkeyRow = CreateRow("启动快捷键");
        _hotkeyCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 100
        };
        _hotkeyCombo.Items.AddRange(AvailableHotkeys.Select(key => key.ToString()).ToArray());
        _hotkeyCombo.SelectedItem = Keys.F8.ToString();
        _hotkeyStatusLabel = new Label
        {
            Text = "全局快捷键，可开始或停止监控",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(8, 7, 0, 0)
        };
        hotkeyRow.Controls.Add(_hotkeyCombo);
        hotkeyRow.Controls.Add(_hotkeyStatusLabel);

        _currentColorLabel = new Label
        {
            Text = "当前位置颜色：—",
            Width = 710,
            Height = 26,
            Font = new Font("Consolas", 9.5F),
            ForeColor = SystemColors.GrayText,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _startButton = new Button
        {
            Text = "开始监控",
            Width = 150,
            Height = 36
        };
        _statusLabel = new Label
        {
            Text = "点击“选择位置”，移动鼠标后按 Enter 确认",
            Width = 710,
            Height = 52,
            ForeColor = SystemColors.GrayText
        };

        root.Controls.AddRange([
            title, _privilegePanel, positionRow, modeRow, _targetSection, _changeSection, toleranceRow,
            clickRow, countdownRow, hotkeyRow, _currentColorLabel, _startButton, _statusLabel
        ]);
        Controls.Add(root);

        _pollTimer = new System.Windows.Forms.Timer { Interval = 8 };
        _countdownTimer = new System.Windows.Forms.Timer { Interval = 1000 };

        _selectPositionButton.Click += (_, _) => BeginSelectingPosition();
        _restartAsAdminButton.Click += (_, _) => RestartAsAdministrator();
        _modeCombo.SelectedIndexChanged += (_, _) => UpdateMode();
        _hotkeyCombo.SelectedIndexChanged += (_, _) => UpdateHotkeyRegistration();
        _toleranceInput.ValueChanged += (_, _) => toleranceHint.Text = $"RGB 每个通道允许 ±{Tolerance}";
        _startButton.Click += (_, _) => ToggleMonitoring();
        _pollTimer.Tick += (_, _) => PollPixel();
        _countdownTimer.Tick += (_, _) => AdvanceCountdown();
        KeyDown += HandleFormKeyDown;

        LoadSettings();
        UpdateMode();
        UpdatePrivilegeBanner();
        UpdateStartButtonIdleText();
        _ = RunKeyWatcherAsync(_lifetimeCancellation.Token);
    }

    private int Tolerance => decimal.ToInt32(_toleranceInput.Value);
    private bool UsesTargetColors => _modeCombo.SelectedIndex == 0;
    private int RegionSize => _regionSizeCombo.SelectedIndex + 1;
    private Keys SelectedHotkey => AvailableHotkeys[Math.Clamp(_hotkeyCombo.SelectedIndex, 0, AvailableHotkeys.Length - 1)];

    private static FlowLayoutPanel CreateRow(string title)
    {
        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Width = 710,
            Height = 40,
            Margin = new Padding(0, 3, 0, 3)
        };
        row.Controls.Add(new Label
        {
            Text = title,
            Width = 85,
            Height = 30,
            TextAlign = ContentAlignment.MiddleLeft
        });
        return row;
    }

    private Panel BuildTargetSection()
    {
        var panel = new Panel
        {
            Width = 710,
            Height = 242,
            Margin = new Padding(0, 2, 0, 4)
        };
        var header = new Label
        {
            Text = "目标颜色                            延时（ms）    次数      点击间隔（ms）",
            Dock = DockStyle.Top,
            Height = 24,
            ForeColor = SystemColors.GrayText
        };
        _colorRowsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 172,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(5)
        };
        _addColorButton = new Button
        {
            Text = "＋ 添加颜色",
            Width = 110,
            Height = 30,
            Top = 204,
            Left = 0
        };
        _addColorButton.Click += (_, _) => AddColorRow("#FFFFFF");
        panel.Controls.Add(_addColorButton);
        panel.Controls.Add(_colorRowsPanel);
        panel.Controls.Add(header);
        return panel;
    }

    private Panel BuildChangeSection(
        out ComboBox regionSize,
        out NumericUpDown delay,
        out NumericUpDown count,
        out NumericUpDown interval)
    {
        var panel = new Panel { Width = 710, Height = 95, Margin = new Padding(0, 2, 0, 4) };
        var sizeRow = CreateRow("采样区域");
        regionSize = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
        for (int size = 1; size <= 10; size++)
        {
            regionSize.Items.Add($"{size} × {size}");
        }
        regionSize.SelectedIndex = 0;
        sizeRow.Controls.Add(regionSize);
        sizeRow.Controls.Add(new Label
        {
            Text = "对区域内所有像素取平均颜色",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(8, 7, 0, 0)
        });

        var actionRow = CreateRow("变化后操作");
        actionRow.Controls.Add(new Label { Text = "延时", AutoSize = true, Margin = new Padding(0, 7, 3, 0) });
        delay = MakeNumberInput(0, 0, 60_000, 76);
        actionRow.Controls.Add(delay);
        actionRow.Controls.Add(new Label { Text = "ms   点击", AutoSize = true, Margin = new Padding(3, 7, 3, 0) });
        count = MakeNumberInput(1, 1, 100, 58);
        actionRow.Controls.Add(count);
        actionRow.Controls.Add(new Label { Text = "次   间隔", AutoSize = true, Margin = new Padding(3, 7, 3, 0) });
        interval = MakeNumberInput(100, 0, 60_000, 76);
        actionRow.Controls.Add(interval);
        actionRow.Controls.Add(new Label { Text = "ms", AutoSize = true, Margin = new Padding(3, 7, 0, 0) });

        panel.Controls.Add(actionRow);
        panel.Controls.Add(sizeRow);
        actionRow.Top = 42;
        return panel;
    }

    private static NumericUpDown MakeNumberInput(int value, int minimum, int maximum, int width) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
        Value = value,
        Width = width,
        ThousandsSeparator = true,
        TextAlign = HorizontalAlignment.Right
    };

    private void AddColorRow(
        string initialColor,
        int delayMilliseconds = 0,
        int clickCount = 1,
        int intervalMilliseconds = 100,
        bool announce = true)
    {
        var row = new ColorTargetRowControl(
            initialColor,
            delayMilliseconds,
            clickCount,
            intervalMilliseconds
        );
        row.DeleteRequested += (_, _) => DeleteColorRow(row);
        row.PickRequested += (_, _) => BeginPickingColor(row);
        _colorRows.Add(row);
        _colorRowsPanel.Controls.Add(row);
        RefreshColorRows();
        _colorRowsPanel.ScrollControlIntoView(row);
        if (announce && _statusLabel is not null && _colorRows.Count > 1)
        {
            _statusLabel.Text = $"已添加颜色 {_colorRows.Count}";
        }
    }

    private void DeleteColorRow(ColorTargetRowControl row)
    {
        if (_colorRows.Count <= 1)
        {
            System.Media.SystemSounds.Beep.Play();
            _statusLabel.Text = "至少需要保留一个目标颜色";
            return;
        }

        _colorRows.Remove(row);
        _colorRowsPanel.Controls.Remove(row);
        row.Dispose();
        RefreshColorRows();
        _statusLabel.Text = $"已删除，当前共有 {_colorRows.Count} 个目标颜色";
    }

    private void RefreshColorRows()
    {
        for (int index = 0; index < _colorRows.Count; index++)
        {
            _colorRows[index].SetIndex(index + 1);
            _colorRows[index].SetEditorEnabled(!_isMonitoring && !_isCountingDown, _colorRows.Count > 1);
        }
    }

    private void UpdateMode()
    {
        _targetSection.Visible = UsesTargetColors;
        _changeSection.Visible = !UsesTargetColors;
        _statusLabel.Text = UsesTargetColors
            ? "每个颜色可分别设置延时、点击次数和点击间隔"
            : "可设置采样区域、延时、点击次数和点击间隔";
    }

    private void BeginSelectingPosition()
    {
        StopAll(string.Empty);
        CancelInteraction();
        _selectingPosition = true;
        _selectPositionButton.Text = "等待 Enter…";
        _statusLabel.Text = "请移动鼠标到目标位置，然后按 Enter；按 Esc 取消";
    }

    private void BeginPickingColor(ColorTargetRowControl row)
    {
        StopAll(string.Empty);
        CancelInteraction();
        _pickingColorRow = row;
        row.PickButton.Text = "等待…";
        _statusLabel.Text = "请把鼠标移到要吸取的颜色上，然后按 Enter；按 Esc 取消";
    }

    private void ConfirmInteraction()
    {
        if (!NativeMethods.GetCursorPos(out NativeMethods.POINT point))
        {
            _statusLabel.Text = "无法读取鼠标位置";
            return;
        }

        if (_pickingColorRow is { } row)
        {
            Color? color = NativeMethods.ReadScreenPixel(point);
            if (color is null)
            {
                _statusLabel.Text = "无法读取屏幕颜色";
                CancelInteraction();
                return;
            }

            row.SetPickedColor(color.Value);
            CancelInteraction();
            _statusLabel.Text = $"已吸取颜色 {ColorUtilities.ToHex(color.Value)}";
            Activate();
            return;
        }

        if (_selectingPosition)
        {
            _selectedPoint = point;
            _positionLabel.Text = $"x: {point.X}, y: {point.Y}";
            Color? color = NativeMethods.ReadScreenPixel(point);
            _currentColorLabel.Text = color is null
                ? "当前位置颜色：读取失败"
                : $"当前位置颜色：{ColorUtilities.ToHex(color.Value)}";
            CancelInteraction();
            _statusLabel.Text = "位置已确认";
            Activate();
        }
    }

    private void CancelInteraction()
    {
        _selectingPosition = false;
        _selectPositionButton.Text = _selectedPoint is null ? "选择位置" : "重新选择";
        if (_pickingColorRow is { } row)
        {
            row.PickButton.Text = "吸取";
        }
        _pickingColorRow = null;
    }

    private void ToggleMonitoring()
    {
        if (_selectingPosition || _pickingColorRow is not null)
        {
            System.Media.SystemSounds.Beep.Play();
            _statusLabel.Text = "请先按 Enter 确认，或按 Esc 取消当前选择";
            return;
        }

        if (_isMonitoring || _isCountingDown)
        {
            StopAll("已取消");
        }
        else
        {
            PrepareCountdown();
        }
    }

    private void PrepareCountdown()
    {
        if (_selectedPoint is null)
        {
            System.Media.SystemSounds.Beep.Play();
            _statusLabel.Text = "请先选择监控位置";
            return;
        }

        _activeTargets.Clear();
        if (UsesTargetColors)
        {
            for (int index = 0; index < _colorRows.Count; index++)
            {
                if (!_colorRows[index].TryCreateTarget(out ColorTarget target))
                {
                    System.Media.SystemSounds.Beep.Play();
                    _statusLabel.Text = $"颜色 {index + 1} 格式不正确，请输入例如 #66D169";
                    return;
                }
                _activeTargets.Add(target);
            }
        }
        else
        {
            _activeChangePlan = new ClickPlan(
                decimal.ToInt32(_changeDelayInput.Value),
                decimal.ToInt32(_changeCountInput.Value),
                decimal.ToInt32(_changeIntervalInput.Value)
            );
        }

        int regionSize = UsesTargetColors ? 1 : RegionSize;
        if (NativeMethods.ReadScreenRegion(_selectedPoint.Value, regionSize) is null)
        {
            _statusLabel.Text = "无法读取监控位置的屏幕颜色";
            return;
        }

        CancelInteraction();
        int countdownSeconds = decimal.ToInt32(_countdownInput.Value);
        if (countdownSeconds == 0)
        {
            ActivateMonitoring();
            return;
        }

        _isCountingDown = true;
        _countdownRemaining = countdownSeconds;
        SetControlsEnabled(false);
        _startButton.Enabled = true;
        _startButton.Text = $"取消倒计时（{_countdownRemaining}）";
        _statusLabel.Text = $"{_countdownRemaining} 秒后开始监控… 按 Esc 取消";
        _countdownTimer.Start();
    }

    private void AdvanceCountdown()
    {
        if (!_isCountingDown)
        {
            return;
        }

        _countdownRemaining--;
        if (_countdownRemaining > 0)
        {
            _startButton.Text = $"取消倒计时（{_countdownRemaining}）";
            _statusLabel.Text = $"{_countdownRemaining} 秒后开始监控… 按 Esc 取消";
        }
        else
        {
            ActivateMonitoring();
        }
    }

    private void ActivateMonitoring()
    {
        int regionSize = UsesTargetColors ? 1 : RegionSize;
        if (_selectedPoint is null || NativeMethods.ReadScreenRegion(_selectedPoint.Value, regionSize) is not { } current)
        {
            StopAll("无法读取屏幕颜色");
            return;
        }

        _countdownTimer.Stop();
        _isCountingDown = false;
        _baselineColor = current;
        _changeArmed = true;
        _lastMatchedTargetId = null;
        _lastChangeTimestamp = 0;
        _isMonitoring = true;
        SetControlsEnabled(false);
        _startButton.Enabled = true;
        _startButton.Text = "停止监控";
        _statusLabel.Text = UsesTargetColors
            ? $"正在监控 {_activeTargets.Count} 个目标颜色… 按 Esc 停止"
            : $"正在检测 {regionSize}×{regionSize} 区域变化，容差 ±{Tolerance}… 按 Esc 停止";
        _pollTimer.Start();
    }

    private void PollPixel()
    {
        int regionSize = UsesTargetColors ? 1 : RegionSize;
        if (!_isMonitoring || _selectedPoint is null ||
            NativeMethods.ReadScreenRegion(_selectedPoint.Value, regionSize) is not { } current)
        {
            return;
        }

        long now = Stopwatch.GetTimestamp();
        if (Stopwatch.GetElapsedTime(_lastColorDisplayTimestamp, now) >= TimeSpan.FromMilliseconds(100))
        {
            _currentColorLabel.Text = $"当前位置颜色：{ColorUtilities.ToHex(current)}";
            _lastColorDisplayTimestamp = now;
        }

        if (UsesTargetColors)
        {
            PollTargetColors(current);
        }
        else
        {
            PollPixelChange(current, now);
        }
    }

    private void PollTargetColors(Color current)
    {
        ColorTarget? matched = null;
        foreach (ColorTarget candidate in _activeTargets)
        {
            if (ColorUtilities.IsWithin(current, candidate.Color, Tolerance))
            {
                matched = candidate;
                break;
            }
        }

        if (matched is null)
        {
            if (_lastMatchedTargetId is not null)
            {
                CancelPendingClick();
                _statusLabel.Text = "继续等待目标颜色… 按 Esc 停止";
            }
            _lastMatchedTargetId = null;
            return;
        }

        ColorTarget target = matched.Value;
        if (_lastMatchedTargetId == target.Id)
        {
            return;
        }

        CancelPendingClick();
        CancelClickSequence();
        _lastMatchedTargetId = target.Id;
        ScheduleTargetClick(target);
    }

    private async void ScheduleTargetClick(ColorTarget target)
    {
        CancellationTokenSource cancellation = new();
        _pendingClickCancellation = cancellation;
        CancellationToken token = cancellation.Token;
        bool shouldStart = false;
        try
        {
            if (target.Plan.DelayMilliseconds > 0)
            {
                _statusLabel.Text = $"匹配 {ColorUtilities.ToHex(target.Color)}，保持 {target.Plan.DelayMilliseconds} ms 后点击";
                await Task.Delay(target.Plan.DelayMilliseconds, token);
            }

            shouldStart = !token.IsCancellationRequested && _isMonitoring && _selectedPoint is not null &&
                _lastMatchedTargetId == target.Id &&
                NativeMethods.ReadScreenPixel(_selectedPoint.Value) is { } current &&
                ColorUtilities.IsWithin(current, target.Color, Tolerance);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (ReferenceEquals(_pendingClickCancellation, cancellation))
            {
                _pendingClickCancellation = null;
            }
            cancellation.Dispose();
        }

        if (!shouldStart)
        {
            return;
        }
        _ = RunClickSequenceAsync(
            target.Plan,
            $"已匹配 {ColorUtilities.ToHex(target.Color)}，完成 {target.Plan.ClickCount} 次点击"
        );
    }

    private void PollPixelChange(Color current, long now)
    {
        if (_baselineColor is null || _selectedPoint is null)
        {
            return;
        }

        bool changed = !ColorUtilities.IsWithin(current, _baselineColor.Value, Tolerance);
        if (changed)
        {
            if (_changeArmed)
            {
                _changeArmed = false;
                ScheduleChangeClick();
            }
            _baselineColor = current;
            _lastChangeTimestamp = now;
        }
        else if (!_changeArmed && _pendingClickCancellation is null && _clickSequenceCancellation is null &&
                 Stopwatch.GetElapsedTime(_lastChangeTimestamp, now) >= TimeSpan.FromMilliseconds(250))
        {
            _changeArmed = true;
            _statusLabel.Text = "颜色已稳定，继续监控… 按 Esc 停止";
        }
    }

    private async void ScheduleChangeClick()
    {
        CancelPendingClick();
        CancellationTokenSource cancellation = new();
        _pendingClickCancellation = cancellation;
        CancellationToken token = cancellation.Token;
        bool shouldStart = false;
        try
        {
            if (_activeChangePlan.DelayMilliseconds > 0)
            {
                _statusLabel.Text = $"检测到像素变化，{_activeChangePlan.DelayMilliseconds} ms 后点击";
                await Task.Delay(_activeChangePlan.DelayMilliseconds, token);
            }

            shouldStart = !token.IsCancellationRequested && _isMonitoring && !UsesTargetColors;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (ReferenceEquals(_pendingClickCancellation, cancellation))
            {
                _pendingClickCancellation = null;
            }
            cancellation.Dispose();
        }

        if (!shouldStart)
        {
            return;
        }
        _lastChangeTimestamp = Stopwatch.GetTimestamp();
        _ = RunClickSequenceAsync(
            _activeChangePlan,
            $"像素变化触发，完成 {_activeChangePlan.ClickCount} 次点击"
        );
    }

    private async Task RunClickSequenceAsync(ClickPlan plan, string completionMessage)
    {
        CancelClickSequence();
        CancellationTokenSource cancellation = new();
        _clickSequenceCancellation = cancellation;
        CancellationToken token = cancellation.Token;
        try
        {
            for (int index = 1; index <= plan.ClickCount; index++)
            {
                if (index > 1 && plan.IntervalMilliseconds > 0)
                {
                    await Task.Delay(plan.IntervalMilliseconds, token);
                }
                token.ThrowIfCancellationRequested();
                if (!_isMonitoring)
                {
                    return;
                }
                PerformClick();
            }
            if (!token.IsCancellationRequested)
            {
                _statusLabel.Text = completionMessage;
            }
        }
        catch (OperationCanceledException)
        {
            // Esc, stop, or a newer trigger cancels the remaining clicks.
        }
        finally
        {
            if (ReferenceEquals(_clickSequenceCancellation, cancellation))
            {
                _clickSequenceCancellation = null;
            }
            cancellation.Dispose();
        }
    }

    private void PerformClick()
    {
        if (_selectedPoint is null)
        {
            return;
        }

        NativeMethods.POINT clickPoint = _selectedPoint.Value;
        bool moveCursor = _clickLocationCombo.SelectedIndex == 0;
        if (!moveCursor && !NativeMethods.GetCursorPos(out clickPoint))
        {
            _statusLabel.Text = "无法读取当前鼠标位置";
            return;
        }

        if (!NativeMethods.LeftClick(clickPoint, moveCursor))
        {
            _statusLabel.Text = "点击失败；如果目标程序以管理员身份运行，请让本程序也以管理员身份运行";
        }
    }

    private void StopAll(string message)
    {
        CancelPendingClick();
        CancelClickSequence();
        _countdownTimer.Stop();
        _pollTimer.Stop();
        _isCountingDown = false;
        _isMonitoring = false;
        _countdownRemaining = 0;
        _lastMatchedTargetId = null;
        SetControlsEnabled(true);
        UpdateStartButtonIdleText();
        if (!string.IsNullOrEmpty(message))
        {
            _statusLabel.Text = message;
        }
    }

    private void CancelPendingClick()
    {
        CancellationTokenSource? cancellation = _pendingClickCancellation;
        _pendingClickCancellation = null;
        cancellation?.Cancel();
    }

    private void CancelClickSequence()
    {
        _clickSequenceCancellation?.Cancel();
        _clickSequenceCancellation = null;
    }

    private void SetControlsEnabled(bool enabled)
    {
        _selectPositionButton.Enabled = enabled;
        _modeCombo.Enabled = enabled;
        _toleranceInput.Enabled = enabled;
        _clickLocationCombo.Enabled = enabled;
        _regionSizeCombo.Enabled = enabled;
        _changeDelayInput.Enabled = enabled;
        _changeCountInput.Enabled = enabled;
        _changeIntervalInput.Enabled = enabled;
        _countdownInput.Enabled = enabled;
        _hotkeyCombo.Enabled = enabled;
        _addColorButton.Enabled = enabled;
        foreach (ColorTargetRowControl row in _colorRows)
        {
            row.SetEditorEnabled(enabled, _colorRows.Count > 1);
        }
    }

    private void LoadSettings()
    {
        AppSettings settings = SettingsStore.Load();

        _modeCombo.SelectedIndex = Math.Clamp(settings.SelectedMode, 0, 1);
        _toleranceInput.Value = Math.Clamp(settings.Tolerance, 0, 100);
        _clickLocationCombo.SelectedIndex = Math.Clamp(settings.ClickLocation, 0, 1);
        _regionSizeCombo.SelectedIndex = Math.Clamp(settings.RegionSize, 1, 10) - 1;
        _changeDelayInput.Value = Math.Clamp(settings.ChangeDelayMilliseconds, 0, 60_000);
        _changeCountInput.Value = Math.Clamp(settings.ChangeClickCount, 1, 100);
        _changeIntervalInput.Value = Math.Clamp(settings.ChangeIntervalMilliseconds, 0, 60_000);
        _countdownInput.Value = Math.Clamp(settings.CountdownSeconds, 0, 60);

        Keys savedHotkey = (Keys)settings.StartHotkey;
        int hotkeyIndex = Array.IndexOf(AvailableHotkeys, savedHotkey);
        _hotkeyCombo.SelectedIndex = hotkeyIndex >= 0
            ? hotkeyIndex
            : Array.IndexOf(AvailableHotkeys, Keys.F8);

        if (settings.MonitorX is { } x && settings.MonitorY is { } y)
        {
            _selectedPoint = new NativeMethods.POINT { X = x, Y = y };
            _positionLabel.Text = $"x: {x}, y: {y}";
            _selectPositionButton.Text = "重新选择";
        }

        IEnumerable<ColorTargetSetting> targetSettings = settings.TargetColors ?? [];
        foreach (ColorTargetSetting target in targetSettings.Take(100))
        {
            AddColorRow(
                string.IsNullOrWhiteSpace(target.Color) ? "#66D169" : target.Color,
                target.DelayMilliseconds,
                target.ClickCount,
                target.IntervalMilliseconds,
                announce: false
            );
        }
        if (_colorRows.Count == 0)
        {
            AddColorRow("#66D169", announce: false);
        }

        _statusLabel.Text = _selectedPoint is null
            ? "设置已恢复；请先选择监控位置"
            : "已恢复上次关闭前的全部设置";
    }

    private bool SaveSettings()
    {
        var settings = new AppSettings
        {
            MonitorX = _selectedPoint?.X,
            MonitorY = _selectedPoint?.Y,
            SelectedMode = _modeCombo.SelectedIndex,
            Tolerance = Tolerance,
            ClickLocation = _clickLocationCombo.SelectedIndex,
            RegionSize = RegionSize,
            ChangeDelayMilliseconds = decimal.ToInt32(_changeDelayInput.Value),
            ChangeClickCount = decimal.ToInt32(_changeCountInput.Value),
            ChangeIntervalMilliseconds = decimal.ToInt32(_changeIntervalInput.Value),
            CountdownSeconds = decimal.ToInt32(_countdownInput.Value),
            StartHotkey = (int)SelectedHotkey,
            TargetColors = _colorRows.Select(row => row.CreateSetting()).ToList()
        };
        return SettingsStore.Save(settings);
    }

    private static bool IsRunningAsAdministrator()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private void UpdatePrivilegeBanner()
    {
        if (IsRunningAsAdministrator())
        {
            _privilegePanel.BackColor = Color.FromArgb(222, 245, 226);
            _privilegeLabel.ForeColor = Color.FromArgb(20, 105, 45);
            _privilegeLabel.Text = "✓ 当前已以管理员权限运行\n可以向管理员权限的软件发送点击";
            _restartAsAdminButton.Visible = false;
        }
        else
        {
            _privilegePanel.BackColor = Color.FromArgb(255, 232, 224);
            _privilegeLabel.ForeColor = Color.FromArgb(170, 45, 20);
            _privilegeLabel.Text = "⚠ 当前为普通权限\n若目标软件是管理员权限，点击可能无效";
            _restartAsAdminButton.Visible = true;
        }
    }

    private void RestartAsAdministrator()
    {
        string? executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            _statusLabel.Text = "无法确定程序路径，请右键 EXE 选择“以管理员身份运行”";
            return;
        }

        _ = SaveSettings();
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = true,
                Verb = "runas"
            });
            Application.Exit();
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            _statusLabel.Text = "已取消管理员权限请求，程序继续以普通权限运行";
        }
        catch (Exception exception)
        {
            _statusLabel.Text = $"无法以管理员身份重启：{exception.Message}";
        }
    }

    private void UpdateStartButtonIdleText()
    {
        if (!_isMonitoring && !_isCountingDown)
        {
            _startButton.Text = $"开始监控（{SelectedHotkey}）";
        }
    }

    private void UpdateHotkeyRegistration()
    {
        if (!IsHandleCreated || _hotkeyCombo.SelectedIndex < 0)
        {
            return;
        }

        if (_hotkeyRegistered)
        {
            _ = NativeMethods.UnregisterHotKey(Handle, StartHotkeyId);
            _hotkeyRegistered = false;
        }

        _hotkeyRegistered = NativeMethods.RegisterHotKey(
            Handle,
            StartHotkeyId,
            0,
            (uint)SelectedHotkey
        );
        _hotkeyStatusLabel.Text = _hotkeyRegistered
            ? "全局快捷键，可开始或停止监控"
            : "快捷键被其他软件占用，请换一个";
        _hotkeyStatusLabel.ForeColor = _hotkeyRegistered
            ? SystemColors.GrayText
            : Color.Firebrick;
        UpdateStartButtonIdleText();
    }

    private void HandleFormKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyCode == Keys.Escape)
        {
            HandleEscape();
            eventArgs.Handled = true;
        }
        else if (eventArgs.KeyCode == Keys.Enter && (_selectingPosition || _pickingColorRow is not null))
        {
            ConfirmInteraction();
            eventArgs.Handled = true;
        }
    }

    private void HandleEscape()
    {
        CancelInteraction();
        StopAll("已停止（Esc）");
    }

    private async Task RunKeyWatcherAsync(CancellationToken token)
    {
        bool enterWasDown = false;
        bool escapeWasDown = false;
        while (!token.IsCancellationRequested)
        {
            bool enterDown = NativeMethods.IsKeyDown(NativeMethods.VK_RETURN);
            bool escapeDown = NativeMethods.IsKeyDown(NativeMethods.VK_ESCAPE);

            if (escapeDown && !escapeWasDown &&
                (_isMonitoring || _isCountingDown || _selectingPosition || _pickingColorRow is not null))
            {
                SafeBeginInvoke(HandleEscape);
            }
            if (enterDown && !enterWasDown && (_selectingPosition || _pickingColorRow is not null))
            {
                SafeBeginInvoke(ConfirmInteraction);
            }

            enterWasDown = enterDown;
            escapeWasDown = escapeDown;
            try
            {
                await Task.Delay(5, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void SafeBeginInvoke(Action action)
    {
        if (!IsDisposed && IsHandleCreated)
        {
            BeginInvoke(action);
        }
    }

    protected override void OnHandleCreated(EventArgs eventArgs)
    {
        base.OnHandleCreated(eventArgs);
        UpdateHotkeyRegistration();
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == NativeMethods.WM_HOTKEY && message.WParam.ToInt32() == StartHotkeyId)
        {
            ToggleMonitoring();
            return;
        }
        base.WndProc(ref message);
    }

    protected override void OnFormClosing(FormClosingEventArgs eventArgs)
    {
        if (!SaveSettings())
        {
            _statusLabel.Text = "设置保存失败";
        }
        base.OnFormClosing(eventArgs);
    }

    protected override void OnFormClosed(FormClosedEventArgs eventArgs)
    {
        if (_hotkeyRegistered && IsHandleCreated)
        {
            _ = NativeMethods.UnregisterHotKey(Handle, StartHotkeyId);
            _hotkeyRegistered = false;
        }
        _lifetimeCancellation.Cancel();
        CancelPendingClick();
        CancelClickSequence();
        _pollTimer.Dispose();
        _countdownTimer.Dispose();
        _lifetimeCancellation.Dispose();
        base.OnFormClosed(eventArgs);
    }
}
