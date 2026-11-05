using System.Globalization;
using System.Windows.Forms;

namespace DeltaMarketAssistant.Core;

public sealed class TimingValueInput : NumericUpDown
{
    public bool TryCommit()
    {
        if (!decimal.TryParse(Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) ||
            value < Minimum || value > Maximum)
        {
            return false;
        }
        UserEdit = false;
        Value = value;
        return true;
    }

    protected override void ValidateEditText()
    {
        // Keep invalid edits visible. Applying settings reports the range instead of silently clamping.
        if (TryCommit()) base.ValidateEditText();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (e is HandledMouseEventArgs handled) handled.Handled = true;
    }
}
