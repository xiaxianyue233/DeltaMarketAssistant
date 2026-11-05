namespace DeltaMarketAssistant.Core;

public enum CountdownDecision { Unavailable, Waiting, Clicking, Finished }

/// <summary>
/// Once the lead phase starts, preserve one continuous click window through the
/// zero-second handoff even if a frame is briefly unreadable.
/// </summary>
public sealed class CountdownClickWindow
{
    public bool IsLatched { get; private set; }
    public long? EndAtMs { get; private set; }

    public bool Observe(CountdownDecision decision, long nowMs, long? deadlineMs, int tailMs)
    {
        if (!IsLatched && decision == CountdownDecision.Clicking && deadlineMs.HasValue)
        {
            IsLatched = true;
            EndAtMs = deadlineMs.Value + Math.Max(0, tailMs);
        }

        return IsLatched && EndAtMs.HasValue && nowMs < EndAtMs.Value;
    }

    public bool HasFinished(long nowMs) =>
        IsLatched && EndAtMs.HasValue && nowMs >= EndAtMs.Value;
}

public sealed class CountdownTracker
{
    public long? DeadlineMs { get; private set; }
    public int? LastSeconds { get; private set; }
    public bool Inconsistent { get; private set; }
    private int? _candidate;
    private long _candidateAt;
    private int _frames;
    private long _lastValidatedAt;

    public bool Observe(int? seconds, long nowMs)
    {
        if (seconds is null || seconds < 0 || seconds > 5999)
        {
            _candidate = null;
            _frames = 0;
            return false;
        }
        if (_candidate != seconds)
        {
            _candidate = seconds;
            _candidateAt = nowMs;
            _frames = 1;
            return false;
        }
        if (++_frames < 2) return false;
        if (DeadlineMs is null)
        {
            // A new zero display gives no evidence of when this listing actually opened.
            if (seconds == 0) return false;
            DeadlineMs = _candidateAt + seconds.Value * 1000L;
        }
        else if (seconds > LastSeconds ||
                 (seconds > 0 && Math.Abs(DeadlineMs.Value - nowMs - seconds.Value * 1000L) > 1400) ||
                 (seconds == 0 && nowMs < DeadlineMs.Value - 1400))
        {
            Inconsistent = true;
            return false;
        }
        else if (seconds < LastSeconds && seconds > 0)
        {
            DeadlineMs = Math.Min(DeadlineMs.Value, _candidateAt + seconds.Value * 1000L);
        }
        LastSeconds = seconds;
        _lastValidatedAt = nowMs;
        return true;
    }

    public CountdownDecision Decide(long nowMs, int leadMs, int tailMs)
    {
        if (DeadlineMs is null || Inconsistent) return CountdownDecision.Unavailable;
        if (nowMs >= DeadlineMs + tailMs) return CountdownDecision.Finished;
        var finalPhase = nowMs >= DeadlineMs && LastSeconds <= 1 &&
                         _lastValidatedAt >= DeadlineMs - 1500;
        if (!finalPhase && nowMs - _lastValidatedAt > 600) return CountdownDecision.Unavailable;
        return nowMs >= DeadlineMs - leadMs ? CountdownDecision.Clicking : CountdownDecision.Waiting;
    }
}
