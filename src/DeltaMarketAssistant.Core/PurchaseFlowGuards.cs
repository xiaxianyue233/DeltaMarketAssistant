namespace DeltaMarketAssistant.Core;

public sealed class LatePurchaseWindow
{
    public const int DurationMs = 2000;
    private int _frames;
    private bool _stopped;
    public long? EndAtMs { get; private set; }
    public bool IsStarted => EndAtMs.HasValue;

    public bool TryStart(bool listingDurationVisible, bool originalDialogVerified,
        bool buttonActive, bool trustedCountdownPending, bool normalBurstStarted,
        int purchaseClicks, long nowMs)
    {
        if (IsStarted || _stopped) return false;
        // A normal burst changing to listing duration must not receive a second click window.
        var candidate = listingDurationVisible && originalDialogVerified && buttonActive &&
                        !trustedCountdownPending && !normalBurstStarted && purchaseClicks == 0;
        _frames = candidate ? _frames + 1 : 0;
        if (_frames < 2) return false;
        EndAtMs = nowMs + DurationMs;
        return true;
    }

    public bool CanClick(long nowMs) => !_stopped && EndAtMs is { } end && nowMs < end;
    public bool HasExpired(long nowMs) => EndAtMs is { } end && nowMs >= end;
    public void Stop() => _stopped = true;
}

public sealed class ConfirmClickCadence
{
    private readonly int _intervalMs;
    private long _nextClickAt;

    public ConfirmClickCadence(int intervalMs)
    {
        _intervalMs = TimingOptions.ClickInterval.Clamp(intervalMs);
    }

    public bool IsDue(long now) => now >= _nextClickAt;

    public void RecordSent(long now) => _nextClickAt = now + _intervalMs;
}

public sealed class PurchaseReturnGuard
{
    public bool EscapeSent { get; private set; }
    public bool RefreshSent { get; private set; }

    public bool TryBeginRefresh()
    {
        if (RefreshSent) return false;
        RefreshSent = true;
        return true;
    }

    public bool TryBeginEscape(bool mainScreenPresent, bool originalDialogVerified)
    {
        if (EscapeSent || mainScreenPresent || !originalDialogVerified)
        {
            return false;
        }

        EscapeSent = true;
        return true;
    }
}
