using System.Drawing.Drawing2D;
using DeltaMarketAssistant.Core.Models;

namespace DeltaMarketAssistant;

internal sealed record CalibrationStep(string Name, string Instruction);

internal sealed class CalibrationForm : Form
{
    private readonly Bitmap _snapshot;
    private readonly IReadOnlyList<CalibrationStep> _steps;
    private readonly List<NormalizedRect> _selections = [];
    private readonly CalibrationCanvas _canvas;
    private readonly Label _instructionLabel;
    private readonly Label _stepLabel;
    private readonly Button _backButton;
    private readonly Button _nextButton;
    private int _stepIndex;

    public CalibrationForm(Bitmap snapshot, IReadOnlyList<CalibrationStep> steps)
    {
        _snapshot = snapshot;
        _steps = steps;

        Text = "校准识别区域";
        BackColor = Color.FromArgb(13, 19, 25);
        ForeColor = Color.WhiteSmoke;
        Font = new Font("Microsoft YaHei UI", 10f);
        AutoScaleMode = AutoScaleMode.Dpi;
        WindowState = FormWindowState.Maximized;
        FormBorderStyle = FormBorderStyle.None;
        KeyPreview = true;

        _canvas = new CalibrationCanvas(_snapshot) { Dock = DockStyle.Fill };
        Controls.Add(_canvas);

        var commandBar = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 132,
            BackColor = Color.FromArgb(19, 28, 36),
            Padding = new Padding(20, 12, 20, 12),
            ColumnCount = 3,
            RowCount = 1
        };
        commandBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        commandBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        commandBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        commandBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(commandBar);

        _stepLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = new Font(Font, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = false
        };
        commandBar.Controls.Add(_stepLabel, 0, 0);

        _instructionLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = false,
            Padding = new Padding(10, 0, 18, 0)
        };
        commandBar.Controls.Add(_instructionLabel, 1, 0);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 22, 0, 0),
            Margin = new Padding(0)
        };
        commandBar.Controls.Add(buttons, 2, 0);

        var cancelButton = CreateButton("取消", Color.FromArgb(55, 67, 76));
        cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(cancelButton);

        _backButton = CreateButton("上一步", Color.FromArgb(55, 67, 76));
        _backButton.Click += (_, _) => MoveBack();
        buttons.Controls.Add(_backButton);

        _nextButton = CreateButton("下一步", Color.FromArgb(20, 137, 105));
        _nextButton.Click += (_, _) => MoveNext();
        buttons.Controls.Add(_nextButton);

        KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
            }
        };

        UpdateStep();
    }

    public IReadOnlyList<NormalizedRect> Selections => _selections;

    private static Button CreateButton(string text, Color color) => new()
    {
        Text = text,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new Size(104, 44),
        Padding = new Padding(8, 2, 8, 2),
        BackColor = color,
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
        Margin = new Padding(8, 0, 0, 0),
        UseVisualStyleBackColor = false
    };

    private void MoveBack()
    {
        if (_stepIndex == 0)
        {
            return;
        }

        if (_selections.Count > _stepIndex - 1)
        {
            _selections.RemoveAt(_selections.Count - 1);
        }

        _stepIndex--;
        UpdateStep();
    }

    private void MoveNext()
    {
        var selection = _canvas.Selection;
        if (!selection.HasValue || selection.Value.Width < 4 || selection.Value.Height < 4)
        {
            MessageBox.Show(this, "请在截图中拖出一个完整区域。", "需要选择区域", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var normalized = NormalizedRect.FromPixels(selection.Value, _snapshot.Size);
        if (_selections.Count > _stepIndex)
        {
            _selections[_stepIndex] = normalized;
        }
        else
        {
            _selections.Add(normalized);
        }

        if (_stepIndex == _steps.Count - 1)
        {
            DialogResult = DialogResult.OK;
            return;
        }

        _stepIndex++;
        UpdateStep();
    }

    private void UpdateStep()
    {
        _stepLabel.Text = $"{_stepIndex + 1} / {_steps.Count}  {_steps[_stepIndex].Name}";
        _instructionLabel.Text = _steps[_stepIndex].Instruction;
        _backButton.Enabled = _stepIndex > 0;
        _nextButton.Text = _stepIndex == _steps.Count - 1 ? "完成" : "下一步";
        _canvas.CompletedRegions = _selections.Take(_stepIndex).ToArray();
        _canvas.Selection = _selections.Count > _stepIndex
            ? _selections[_stepIndex].ToPixels(_snapshot.Size)
            : null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _snapshot.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal sealed class CalibrationCanvas : Control
{
    private readonly Bitmap _snapshot;
    private Point? _dragStart;
    private Rectangle? _selection;

    public CalibrationCanvas(Bitmap snapshot)
    {
        _snapshot = snapshot;
        DoubleBuffered = true;
        Cursor = Cursors.Cross;
        BackColor = Color.Black;
    }

    public IReadOnlyList<NormalizedRect> CompletedRegions { get; set; } = [];

    public Rectangle? Selection
    {
        get => _selection;
        set
        {
            _selection = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        var destination = GetImageDestination();
        eventArgs.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        eventArgs.Graphics.DrawImage(_snapshot, destination);

        using var completedPen = new Pen(Color.FromArgb(220, 245, 183, 67), 2f);
        foreach (var region in CompletedRegions)
        {
            eventArgs.Graphics.DrawRectangle(completedPen, ImageToClient(region.ToPixels(_snapshot.Size), destination));
        }

        if (_selection.HasValue)
        {
            using var selectionPen = new Pen(Color.FromArgb(255, 32, 214, 157), 3f);
            eventArgs.Graphics.DrawRectangle(selectionPen, ImageToClient(_selection.Value, destination));
        }
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        base.OnMouseDown(eventArgs);
        if (eventArgs.Button != MouseButtons.Left)
        {
            return;
        }

        var imagePoint = ClientToImage(eventArgs.Location);
        if (!imagePoint.HasValue)
        {
            return;
        }

        _dragStart = imagePoint;
        _selection = new Rectangle(imagePoint.Value, Size.Empty);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs eventArgs)
    {
        base.OnMouseMove(eventArgs);
        if (!_dragStart.HasValue || eventArgs.Button != MouseButtons.Left)
        {
            return;
        }

        var current = ClientToImage(eventArgs.Location, true);
        if (!current.HasValue)
        {
            return;
        }

        _selection = Rectangle.FromLTRB(
            Math.Min(_dragStart.Value.X, current.Value.X),
            Math.Min(_dragStart.Value.Y, current.Value.Y),
            Math.Max(_dragStart.Value.X, current.Value.X),
            Math.Max(_dragStart.Value.Y, current.Value.Y));
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs eventArgs)
    {
        base.OnMouseUp(eventArgs);
        _dragStart = null;
    }

    private Rectangle GetImageDestination()
    {
        var scale = Math.Min(ClientSize.Width / (double)_snapshot.Width, ClientSize.Height / (double)_snapshot.Height);
        var width = Math.Max(1, (int)Math.Round(_snapshot.Width * scale));
        var height = Math.Max(1, (int)Math.Round(_snapshot.Height * scale));
        return new Rectangle((ClientSize.Width - width) / 2, (ClientSize.Height - height) / 2, width, height);
    }

    private Point? ClientToImage(Point clientPoint, bool clamp = false)
    {
        var destination = GetImageDestination();
        if (!clamp && !destination.Contains(clientPoint))
        {
            return null;
        }

        var x = Math.Clamp(clientPoint.X, destination.Left, destination.Right - 1);
        var y = Math.Clamp(clientPoint.Y, destination.Top, destination.Bottom - 1);
        return new Point(
            Math.Clamp((int)((x - destination.Left) * _snapshot.Width / (double)destination.Width), 0, _snapshot.Width - 1),
            Math.Clamp((int)((y - destination.Top) * _snapshot.Height / (double)destination.Height), 0, _snapshot.Height - 1));
    }

    private Rectangle ImageToClient(Rectangle imageRectangle, Rectangle destination)
    {
        return new Rectangle(
            destination.Left + (int)Math.Round(imageRectangle.Left * destination.Width / (double)_snapshot.Width),
            destination.Top + (int)Math.Round(imageRectangle.Top * destination.Height / (double)_snapshot.Height),
            Math.Max(1, (int)Math.Round(imageRectangle.Width * destination.Width / (double)_snapshot.Width)),
            Math.Max(1, (int)Math.Round(imageRectangle.Height * destination.Height / (double)_snapshot.Height)));
    }
}
