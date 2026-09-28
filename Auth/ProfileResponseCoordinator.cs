namespace MolHub.Windows;

/// <summary>
/// Tracks asynchronous profile responses so only the newest response can update
/// the native title-bar profile indicator.
/// </summary>
public sealed class ProfileResponseCoordinator
{
    private long _generation;

    public long BeginResponse()
    {
        return Interlocked.Increment(ref _generation);
    }

    public long Invalidate()
    {
        return Interlocked.Increment(ref _generation);
    }

    public bool IsCurrent(long generation)
    {
        return generation == Volatile.Read(ref _generation);
    }
}
