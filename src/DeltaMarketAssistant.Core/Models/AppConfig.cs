namespace DeltaMarketAssistant.Core.Models;

public sealed class AppConfig
{
    public int SchemaVersion { get; set; } = 11;
    public string TargetProcessName { get; set; } = string.Empty;
    public string TargetWindowTitle { get; set; } = string.Empty;
    public string StartPauseHotkey { get; set; } = "F8";
    public string EmergencyStopHotkey { get; set; } = "Ctrl+Shift+F9";
    public AutomationSettings Automation { get; set; } = new();
    public CalibrationProfile? Calibration { get; set; }
}

public sealed class AutomationSettings
{
    public bool CountdownDriven { get; set; } = true;
    public int CountdownLeadMs { get; set; } = 3000;
    public int CountdownTailMs { get; set; } = 2000;
    public int PollIntervalMs { get; set; } = 20;
    public int DialogTimeoutMs { get; set; } = 1000;
    public int DialogExitTimeoutMs { get; set; } = 2000;
    public int ConfirmBurstStartDelayMs { get; set; } = 4000;
    public int ConfirmBurstDurationMs { get; set; } = 3000;
    public int ConfirmExtraDurationMs { get; set; } = 2000;
    public int ConfirmClickIntervalMs { get; set; } = 100;
    public int ResultWaitTimeoutMs { get; set; } = 15000;
    public int ConfirmClicksPerPacket { get; set; } = 4;
    public int AutomaticReturnTimeoutMs { get; set; } = 4000;
    public int ConfirmInputDrainMs { get; set; } = 100;
    public int PostConfirmSettleMs { get; set; } = 75;
    public int MainScreenStableFramesRequired { get; set; } = 3;
    public int PeriodicRefreshIntervalMs { get; set; } = 1000;
    public int RefreshDelayMs { get; set; } = 150;
    public int ConsecutiveFramesRequired { get; set; } = 2;
    public int FailureLimit { get; set; } = 3;
    public double AspectRatioTolerance { get; set; } = 0.02;
    public double PrimaryGreenRatioMin { get; set; } = 0.006;
    public double ConfirmGreenRatioMin { get; set; } = 0.006;
    public double PrimaryDifferenceMin { get; set; } = 0.045;
    public double DialogHeaderDifferenceMax { get; set; } = 0.30;
    public double MainSignatureDifferenceMax { get; set; } = 0.04;
}

public sealed class CalibrationProfile
{
    public NormalizedRect FirstItemRegion { get; set; }
    public NormalizedRect PrimaryButtonRegion { get; set; }
    public NormalizedRect RefreshButtonRegion { get; set; }
    public NormalizedRect ConfirmButtonRegion { get; set; }
    public NormalizedRect DialogHeaderRegion { get; set; }
    public NormalizedRect MainAnchorRegion { get; set; }
    public NormalizedRect MainCountdownRegion { get; set; }
    public NormalizedRect DialogCountdownRegion { get; set; }
    public List<CountdownGlyphTemplate> CountdownTemplates { get; set; } = [];
    public bool HasCountdownRegions => MainCountdownRegion.IsValid && DialogCountdownRegion.IsValid;
    public string PrimaryInactiveFingerprint { get; set; } = string.Empty;
    public string RefreshFingerprint { get; set; } = string.Empty;
    public string MainAnchorFingerprint { get; set; } = string.Empty;
    public string DialogHeaderFingerprint { get; set; } = string.Empty;
    public DateTime CalibratedAtUtc { get; set; } = DateTime.UtcNow;

    public bool IsComplete => FirstItemRegion.IsValid && PrimaryButtonRegion.IsValid &&
                              RefreshButtonRegion.IsValid && ConfirmButtonRegion.IsValid &&
                              DialogHeaderRegion.IsValid && MainAnchorRegion.IsValid &&
                              !string.IsNullOrWhiteSpace(PrimaryInactiveFingerprint) &&
                              !string.IsNullOrWhiteSpace(RefreshFingerprint) &&
                              !string.IsNullOrWhiteSpace(MainAnchorFingerprint) &&
                              !string.IsNullOrWhiteSpace(DialogHeaderFingerprint);

    public static NormalizedRect DeriveMainAnchorRegion(NormalizedRect refreshButton)
    {
        var left = Math.Max(0, refreshButton.X - 0.30);
        var top = Math.Max(0, refreshButton.Y - 0.015);
        var right = Math.Min(1, refreshButton.X + refreshButton.Width + 0.01);
        var bottom = Math.Min(1, refreshButton.Y + refreshButton.Height + 0.02);
        return new NormalizedRect(left, top, right - left, bottom - top);
    }
}

public enum AutomationState
{
    Paused,
    Idle,
    Waiting,
    Opening,
    Confirming,
    Confirmed,
    Refreshing,
    Refreshed,
    Recovery
}
