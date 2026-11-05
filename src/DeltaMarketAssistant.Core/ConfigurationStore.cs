using System.Text.Json;
using DeltaMarketAssistant.Core.Models;

namespace DeltaMarketAssistant.Core;

public sealed class ConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ConfigurationStore(string? baseDirectory = null)
    {
        BaseDirectory = baseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeltaMarketAssistant");
        ConfigPath = Path.Combine(BaseDirectory, "config.json");
    }

    public string BaseDirectory { get; }
    public string ConfigPath { get; }
    public string DiagnosticsDirectory => Path.Combine(BaseDirectory, "diagnostics");

    public AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), JsonOptions) ?? new AppConfig();
                return Upgrade(config);
            }
        }
        catch
        {
            // A damaged config should not prevent the safety defaults from loading.
        }

        return new AppConfig();
    }

    private static AppConfig Upgrade(AppConfig config)
    {
        if (config.SchemaVersion < 3)
        {
            config.Automation.ConfirmBurstStartDelayMs = 4000;
            config.Automation.ConfirmBurstDurationMs = 5000;
            config.Automation.ConfirmClickIntervalMs = 50;
            config.Automation.ConfirmInputDrainMs = 100;
            config.Automation.PostConfirmSettleMs = 300;
            config.Automation.MainScreenStableFramesRequired = 3;
            config.SchemaVersion = 3;
        }

        if (config.SchemaVersion < 4)
        {
            config.Automation.ConfirmBurstDurationMs = 3000;
            config.Automation.AutomaticReturnTimeoutMs = 4000;
            config.SchemaVersion = 4;
        }

        if (config.SchemaVersion < 5)
        {
            config.Automation.PeriodicRefreshIntervalMs = 1000;
            config.SchemaVersion = 5;
        }

        if (config.SchemaVersion < 6)
        {
            config.SchemaVersion = 6;
        }

        if (config.SchemaVersion < 7)
        {
            config.Automation.ConfirmClicksPerPacket = 4;
            config.SchemaVersion = 7;
        }

        if (config.SchemaVersion < 8)
        {
            config.SchemaVersion = 8;
        }

        if (config.SchemaVersion < 9)
        {
            config.Automation.ConfirmClickIntervalMs = Math.Clamp(
                config.Automation.ConfirmClickIntervalMs, 100, 1000);
            config.Automation.ResultWaitTimeoutMs = 15000;
            config.SchemaVersion = 9;
        }

        if (config.SchemaVersion < 10)
        {
            config.Automation.ConfirmExtraDurationMs = 2000;
            if (config.Automation.PostConfirmSettleMs == 300) config.Automation.PostConfirmSettleMs = 75;
            if (config.Automation.RefreshDelayMs == 350) config.Automation.RefreshDelayMs = 150;
            if (config.EmergencyStopHotkey.Equals("F12", StringComparison.OrdinalIgnoreCase))
                config.EmergencyStopHotkey = "Ctrl+Shift+F9";
            config.SchemaVersion = 10;
        }

        if (config.SchemaVersion < 11)
        {
            config.Automation.CountdownDriven = true;
            config.SchemaVersion = 11;
        }
        return config;
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(BaseDirectory);
        var temporaryPath = ConfigPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(config, JsonOptions));
        File.Move(temporaryPath, ConfigPath, true);
    }

    public static AppConfig Snapshot(AppConfig config) =>
        JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(config, JsonOptions), JsonOptions)!;
}
