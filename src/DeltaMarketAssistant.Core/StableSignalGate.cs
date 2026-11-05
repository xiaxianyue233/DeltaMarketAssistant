namespace DeltaMarketAssistant.Core;

public sealed class StableSignalGate
{
    private readonly int _requiredFrames;
    private int _matchingFrames;
    private bool _latched;

    public StableSignalGate(int requiredFrames)
    {
        if (requiredFrames < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredFrames));
        }

        _requiredFrames = requiredFrames;
    }

    public bool Observe(bool matches)
    {
        if (!matches)
        {
            Reset();
            return false;
        }

        _matchingFrames++;
        if (_matchingFrames < _requiredFrames || _latched)
        {
            return false;
        }

        _latched = true;
        return true;
    }

    public void Reset()
    {
        _matchingFrames = 0;
        _latched = false;
    }
}
