namespace DeltaMarketAssistant.Core;

public sealed class AppLogger
{
    private readonly object _sync = new();
    private readonly string _logPath;

    public AppLogger(string baseDirectory)
    {
        Directory.CreateDirectory(baseDirectory);
        _logPath = Path.Combine(baseDirectory, "assistant.log");
    }

    public event Action<string>? MessageWritten;

    public void Write(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}";
        lock (_sync)
        {
            File.AppendAllText(_logPath, line + Environment.NewLine);
        }

        MessageWritten?.Invoke(line);
    }
}
