namespace PixelColorClicker;

internal sealed class ColorTargetRowControl : UserControl
{
    private readonly Label _numberLabel;
    private readonly TextBox _colorTextBox;
    private readonly Panel _previewPanel;
    private readonly NumericUpDown _delayInput;
    private readonly NumericUpDown _clickCountInput;
    private readonly NumericUpDown _intervalInput;
    internal readonly Button PickButton;
    internal readonly Button DeleteButton;

    internal Guid Id { get; } = Guid.NewGuid();
    internal string ColorText => _colorTextBox.Text.Trim();
    internal int DelayMilliseconds => decimal.ToInt32(_delayInput.Value);
    internal int ClickCount => decimal.ToInt32(_clickCountInput.Value);
    internal int IntervalMilliseconds => decimal.ToInt32(_intervalInput.Value);

    internal event EventHandler? DeleteRequested;
    internal event EventHandler? PickRequested;

    internal ColorTargetRowControl(string initialColor = "#66D169")
    {
        Height = 38;
        Width = 680;
        Margin = new Padding(0, 2, 0, 2);

        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        _numberLabel = new Label
        {
            Text = "颜色 1",
            Width = 55,
            Height = 28,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 3, 4, 0)
        };
        _colorTextBox = new TextBox
        {
            Text = initialColor,
            Width = 92,
            CharacterCasing = CharacterCasing.Upper,
            Font = new Font("Consolas", 10F),
            Margin = new Padding(0, 3, 5, 0)
        };
        _previewPanel = new Panel
        {
            Width = 28,
            Height = 25,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 3, 5, 0)
        };
        PickButton = new Button
        {
            Text = "吸取",
            Width = 52,
            Height = 28,
            Margin = new Padding(0, 1, 8, 0)
        };
        _delayInput = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 60_000,
            Value = 0,
            Width = 78,
            ThousandsSeparator = true,
            TextAlign = HorizontalAlignment.Right,
            Margin = new Padding(0, 3, 2, 0)
        };
        var millisecondsLabel = new Label
        {
            Text = "ms",
            Width = 28,
            Height = 28,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 3, 5, 0)
        };
        _clickCountInput = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 100,
            Value = 1,
            Width = 54,
            TextAlign = HorizontalAlignment.Right,
            Margin = new Padding(0, 3, 2, 0)
        };
        var countLabel = new Label
        {
            Text = "次",
            Width = 25,
            Height = 28,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 3, 5, 0)
        };
        _intervalInput = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 60_000,
            Value = 100,
            Width = 72,
            ThousandsSeparator = true,
            TextAlign = HorizontalAlignment.Right,
            Margin = new Padding(0, 3, 2, 0)
        };
        var intervalLabel = new Label
        {
            Text = "ms",
            Width = 28,
            Height = 28,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 3, 5, 0)
        };
        DeleteButton = new Button
        {
            Text = "−",
            Width = 32,
            Height = 28,
            Margin = new Padding(0, 1, 0, 0)
        };

        row.Controls.AddRange([
            _numberLabel, _colorTextBox, _previewPanel, PickButton,
            _delayInput, millisecondsLabel, _clickCountInput, countLabel,
            _intervalInput, intervalLabel, DeleteButton
        ]);
        Controls.Add(row);

        _colorTextBox.TextChanged += (_, _) => UpdatePreview();
        PickButton.Click += (_, _) => PickRequested?.Invoke(this, EventArgs.Empty);
        DeleteButton.Click += (_, _) => DeleteRequested?.Invoke(this, EventArgs.Empty);
        UpdatePreview();
    }

    internal void SetIndex(int index) => _numberLabel.Text = $"颜色 {index}";

    internal void SetEditorEnabled(bool enabled, bool canDelete)
    {
        _colorTextBox.Enabled = enabled;
        _delayInput.Enabled = enabled;
        _clickCountInput.Enabled = enabled;
        _intervalInput.Enabled = enabled;
        PickButton.Enabled = enabled;
        DeleteButton.Enabled = enabled && canDelete;
    }

    internal void SetPickedColor(Color color)
    {
        _colorTextBox.Text = ColorUtilities.ToHex(color);
        _previewPanel.BackColor = color;
    }

    internal bool TryCreateTarget(out ColorTarget target)
    {
        if (ColorUtilities.TryParseHex(ColorText, out Color color))
        {
            target = new ColorTarget(
                Id,
                color,
                new ClickPlan(DelayMilliseconds, ClickCount, IntervalMilliseconds)
            );
            return true;
        }

        target = default;
        return false;
    }

    private void UpdatePreview()
    {
        _previewPanel.BackColor = ColorUtilities.TryParseHex(ColorText, out Color color)
            ? color
            : SystemColors.Control;
    }
}

internal readonly record struct ColorTarget(Guid Id, Color Color, ClickPlan Plan);
internal readonly record struct ClickPlan(int DelayMilliseconds, int ClickCount, int IntervalMilliseconds);

internal static class ColorUtilities
{
    internal static bool TryParseHex(string input, out Color color)
    {
        string value = input.Trim().TrimStart('#');
        if (value.Length == 6 && int.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out int rgb))
        {
            color = Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            return true;
        }

        color = Color.Empty;
        return false;
    }

    internal static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    internal static bool IsWithin(Color current, Color target, int tolerance) =>
        Math.Abs(current.R - target.R) <= tolerance &&
        Math.Abs(current.G - target.G) <= tolerance &&
        Math.Abs(current.B - target.B) <= tolerance;
}
