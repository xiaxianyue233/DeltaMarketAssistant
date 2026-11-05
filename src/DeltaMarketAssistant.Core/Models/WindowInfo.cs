namespace DeltaMarketAssistant.Core.Models;

public sealed record WindowInfo(IntPtr Handle, string Title, string ProcessName, int Width, int Height)
{
    public override string ToString() => $"{Title}  [{ProcessName}, {Width}x{Height}]";
}
