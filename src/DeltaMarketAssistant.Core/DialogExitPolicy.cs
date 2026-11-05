namespace DeltaMarketAssistant.Core;

public static class DialogExitPolicy
{
    public static bool IsExitCandidate(
        bool mainScreenPresent,
        bool dialogSurfacePresent,
        long dialogSurfaceMissingMs,
        int minimumMissingMs = 300)
    {
        return mainScreenPresent &&
               !dialogSurfacePresent &&
               dialogSurfaceMissingMs >= Math.Max(0, minimumMissingMs);
    }
}
