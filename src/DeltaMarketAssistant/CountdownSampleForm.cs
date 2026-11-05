using DeltaMarketAssistant.Core;

namespace DeltaMarketAssistant;

internal sealed class CountdownSampleForm : Form
{
    private readonly NumericUpDown _minutes = new() { Minimum = -1, Maximum = 99, Width = 75, Value = -1 };
    private readonly NumericUpDown _seconds = new() { Minimum = -1, Maximum = 59, Width = 75, Value = -1 };
    public int TotalSeconds => (int)_minutes.Value * 60 + (int)_seconds.Value;
    protected override bool ShowWithoutActivation =>
        Environment.GetEnvironmentVariable("DMA_UI_PREVIEW") == "1" || base.ShowWithoutActivation;

    public CountdownSampleForm(Bitmap image, CountdownReading? reading)
    {
        Text = "确认倒计时数字样本";
        Size = new Size(660, 300);
        MinimumSize = Size;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(16) };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);
        var preview = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Black, Image = new Bitmap(image) };
        root.Controls.Add(preview);
        var row = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        row.Controls.Add(new Label { Text = "截图中的倒计时：", AutoSize = true });
        row.Controls.Add(_minutes);
        row.Controls.Add(new Label { Text = "分", AutoSize = true });
        row.Controls.Add(_seconds);
        row.Controls.Add(new Label { Text = "秒", AutoSize = true });
        root.Controls.Add(row);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var save = new Button { Text = "确认样本", AutoSize = true };
        save.Click += (_, _) =>
        {
            if (_minutes.Value < 0 || _seconds.Value < 0)
            {
                MessageBox.Show(this, "请填写截图中的实际分秒，不能把未识别结果默认为零。",
                    "需要核对样本", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
        };
        var skip = new Button { Text = "不录入样本", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(save);
        buttons.Controls.Add(skip);
        root.Controls.Add(buttons);
        if (reading is not null) { _minutes.Value = reading.Seconds / 60; _seconds.Value = reading.Seconds % 60; }
        FormClosed += (_, _) => preview.Image?.Dispose();
    }
}
