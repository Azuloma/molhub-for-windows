namespace MolHub.Windows;

/// <summary>Where a bridge start attempt stands.</summary>
public enum BridgeStartupPhase
{
    Idle,
    Starting,
    Ready,
    Failed,
    Disposed
}

/// <summary>
/// The answer to <see cref="BridgeStartup.Request"/>: the shared start task, the generation it belongs to and whether
/// the caller must run a new attempt for that generation.
/// </summary>
public readonly record struct BridgeStartRequest(Task<bool> Task, int Generation, bool IsNewAttempt);

/// <summary>
/// Pure coordinator for the data bridge start (no browser-control or UI types). Every attempt has a generation number and
/// one shared task; concurrent callers share one attempt, and only the current generation may change the phase, so a
/// slow or timed-out attempt can never overwrite a newer one. A deadline covers the whole start. Use it on one thread
/// (the UI thread in the app); the deadline await resumes on the captured context.
/// </summary>
public sealed class BridgeStartup : IDisposable
{
    private static readonly Task<bool> False = Task.FromResult(false);

    private readonly TimeSpan _deadline;
    private readonly Func<TimeSpan, Task> _delay;
    private TaskCompletionSource<bool>? _completion;
    private int _generation;
    private BridgeStartupPhase _phase = BridgeStartupPhase.Idle;

    /// <param name="deadline">How long one attempt may stay in <see cref="BridgeStartupPhase.Starting"/>.</param>
    /// <param name="delay">Waits for the deadline; injectable so tests control time.</param>
    public BridgeStartup(TimeSpan deadline, Func<TimeSpan, Task> delay)
    {
        _deadline = deadline;
        _delay = delay;
    }

    public BridgeStartupPhase Phase => _phase;

    public int Generation => _generation;

    /// <summary>Raised on the deadline for a generation that was still starting; that generation is already failed.</summary>
    public event Action<int>? Expired;

    /// <summary>
    /// Disposed: a completed false task and no attempt. Starting or Ready: the shared task. Idle or Failed: a new
    /// generation and task, the deadline watch starts, and <see cref="BridgeStartRequest.IsNewAttempt"/> is true.
    /// </summary>
    public BridgeStartRequest Request()
    {
        if (_phase == BridgeStartupPhase.Disposed) return new BridgeStartRequest(False, _generation, false);
        if ((_phase == BridgeStartupPhase.Starting || _phase == BridgeStartupPhase.Ready) && _completion is not null)
            return new BridgeStartRequest(_completion.Task, _generation, false);

        _generation++;
        _phase = BridgeStartupPhase.Starting;
        _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var generation = _generation;
        _ = WatchDeadlineAsync(generation);
        return new BridgeStartRequest(_completion.Task, generation, true);
    }

    /// <summary>True while this generation is the current one and still starting (use after each startup await).</summary>
    public bool IsCurrent(int generation) => _phase == BridgeStartupPhase.Starting && generation == _generation;

    /// <summary>True while this generation is current and starting or ready (a failure after connecting still applies).</summary>
    public bool IsLive(int generation) =>
        (_phase == BridgeStartupPhase.Starting || _phase == BridgeStartupPhase.Ready) && generation == _generation;

    /// <summary>Marks the current starting generation ready and completes its task true; false when it did not apply.</summary>
    public bool Succeed(int generation)
    {
        if (!IsCurrent(generation)) return false;
        _phase = BridgeStartupPhase.Ready;
        _completion?.TrySetResult(true);
        return true;
    }

    /// <summary>Marks the live generation failed and completes its task false; false when it did not apply.</summary>
    public bool Fail(int generation)
    {
        if (!IsLive(generation)) return false;
        _phase = BridgeStartupPhase.Failed;
        _completion?.TrySetResult(false);
        return true;
    }

    public void Dispose()
    {
        if (_phase == BridgeStartupPhase.Disposed) return;
        _phase = BridgeStartupPhase.Disposed;
        _completion?.TrySetResult(false);
    }

    private async Task WatchDeadlineAsync(int generation)
    {
        try
        {
            await _delay(_deadline);
        }
        catch
        {
            return;
        }
        if (!IsCurrent(generation)) return;
        Fail(generation);
        Expired?.Invoke(generation);
    }
}
