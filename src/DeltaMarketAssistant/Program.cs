using System.Threading;

namespace DeltaMarketAssistant;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, "DeltaMarketAssistant.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("程序已经在运行。", "三角洲商城助手", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
