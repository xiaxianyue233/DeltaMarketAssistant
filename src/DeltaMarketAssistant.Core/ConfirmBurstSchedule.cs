namespace DeltaMarketAssistant.Core;

public static class ConfirmBurstSchedule
{
    public static long TotalDurationMs(int configuredDurationMs, int extraDurationMs) =>
        (long)TimingOptions.BurstDuration.Clamp(configuredDurationMs) + TimingOptions.ExtraDuration.Clamp(extraDurationMs);

    public static bool HasReachedClickLimit(long elapsedMs, int configuredDurationMs, int extraDurationMs = 0) =>
        elapsedMs >= TotalDurationMs(configuredDurationMs, extraDurationMs);

    public static int GetRemainingDelayMs(long elapsedSinceDialogVerifiedMs, int configuredDelayMs)
    {
        var elapsed = Math.Max(0, elapsedSinceDialogVerifiedMs);
        return (int)Math.Max(0, TimingOptions.StartDelay.Clamp(configuredDelayMs) - elapsed);
    }
}
