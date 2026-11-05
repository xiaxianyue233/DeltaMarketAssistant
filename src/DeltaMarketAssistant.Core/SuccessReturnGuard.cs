namespace DeltaMarketAssistant.Core;

public sealed class SuccessReturnGuard
{
    public bool SkipSent { get; private set; }
    public bool ContinueSent { get; private set; }

    public bool TryBeginSkip(bool titlePresent, bool skipPresent, bool mainPresent)
    {
        if (SkipSent || mainPresent || !titlePresent || !skipPresent) return false;
        SkipSent = true;
        return true;
    }

    public bool TryBeginContinue(bool pageStable, bool mainPresent, bool dialogPresent, bool successPresent)
    {
        if (!SkipSent || ContinueSent || !pageStable || mainPresent || dialogPresent || successPresent) return false;
        ContinueSent = true;
        return true;
    }
}

public sealed class RewardContinueGate
{
    private int _stableFrames;

    public bool Observe(bool eligible, bool hasDetail, double difference, long elapsedSinceSkipMs)
    {
        _stableFrames = eligible && hasDetail && difference <= 0.025 && elapsedSinceSkipMs >= 700
            ? _stableFrames + 1 : 0;
        return _stableFrames >= 3;
    }
}
