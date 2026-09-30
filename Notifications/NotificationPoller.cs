using System.Text.Json.Nodes;
using Microsoft.UI.Dispatching;

namespace MolHub.Windows;

public enum NotificationPollerState
{
    Idle,
    Loading,
    Ready,
    Paused,
    Error,
    Stopped
}

/// <summary>
/// Polls the `notifications` bridge read on the UI thread: one request in flight, a generation number so results that arrive
/// after Stop (sign-out, disconnect) are ignored, and the delay after each result decided by <see cref="NotificationFeed.Next"/>.
/// Holds no WebView2 types; the bridge is reached only through the request delegate.
/// </summary>
public sealed class NotificationPoller
{
    private const int PausedRetrySeconds = 60;

    private readonly Func<string, JsonObject?, Task<BridgeResult>> _request;
    private readonly DispatcherQueueTimer _timer;
    private string? _userId;
    private int _generation;
    private bool _inFlight;
    private bool _firstLoad = true;
    private bool _loaded;

    public NotificationPoller(DispatcherQueue dispatcher, Func<string, JsonObject?, Task<BridgeResult>> request)
    {
        _request = request;
        _timer = dispatcher.CreateTimer();
        _timer.IsRepeating = false;
        _timer.Tick += Timer_Tick;
    }

    public NotificationFeed Feed { get; } = new();
    public NotificationPollerState State { get; private set; } = NotificationPollerState.Idle;

    /// <summary>State, items or unread count changed.</summary>
    public event EventHandler? Changed;
    /// <summary>New items that arrived by polling (never the first load or a reset).</summary>
    public event Action<IReadOnlyList<NotificationItem>>? ItemsArrived;
    /// <summary>The server reset the cursor: visible pages should refresh.</summary>
    public event EventHandler? PageRefreshRequested;

    public void Start(string? userId)
    {
        Halt();
        _userId = userId;
        _firstLoad = true;
        _loaded = false;
        Feed.Initialize(NotificationLastSeenStore.Read(userId));
        State = NotificationPollerState.Loading;
        Changed?.Invoke(this, EventArgs.Empty);
        _ = RequestAsync(_generation);
    }

    public void Stop()
    {
        Halt();
        Feed.Initialize(null);
        State = NotificationPollerState.Idle;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Marks everything shown as seen, persists the new last-seen id and raises <see cref="Changed"/>.</summary>
    public void MarkAllSeen()
    {
        if (Feed.MarkAllSeen() is { } lastSeen) NotificationLastSeenStore.Write(_userId, lastSeen);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Halt()
    {
        _generation++;
        _timer.Stop();
        _inFlight = false;
    }

    private void Timer_Tick(DispatcherQueueTimer sender, object args)
    {
        _timer.Stop();
        _ = RequestAsync(_generation);
    }

    private async Task RequestAsync(int generation)
    {
        if (_inFlight || generation != _generation) return;
        _inFlight = true;
        BridgeResult result;
        try
        {
            result = await _request("notifications", Feed.BuildPayload());
        }
        catch
        {
            result = BridgeResult.HostFailure(string.Empty, "HOST_ERROR", true, BridgeOutcome.None);
        }
        // Stop (or a newer Start) already cleared the in-flight flag for this request.
        if (generation != _generation) return;
        _inFlight = false;
        Handle(result, generation);
    }

    private void Handle(BridgeResult result, int generation)
    {
        NotificationPage? page = result.Ok ? NotificationPayloadParser.Parse(result.Data, result.Meta) : null;
        var next = NotificationFeed.Next(result, page?.Meta);
        IReadOnlyList<NotificationItem> arrived = [];
        var reset = false;

        if (page is { } applied)
        {
            var first = _firstLoad;
            var outcome = first ? Feed.ApplyFirstLoad(applied) : Feed.ApplyPoll(applied);
            _firstLoad = false;
            _loaded = true;
            if (outcome.LastSeenChanged && Feed.LastSeen is { } lastSeen) NotificationLastSeenStore.Write(_userId, lastSeen);
            if (outcome.Reset)
            {
                reset = true;
                Feed.Initialize(Feed.LastSeen);
                _firstLoad = true;
            }
            else if (!first)
            {
                arrived = outcome.NewItems;
            }
            State = NotificationPollerState.Ready;
        }
        else
        {
            switch (next.Kind)
            {
                case NotificationNextKind.Pause:
                    State = NotificationPollerState.Paused;
                    break;
                case NotificationNextKind.Stop:
                    State = NotificationPollerState.Stopped;
                    break;
                default:
                    // A transient failure keeps the list once it has loaded; before that the flyout shows the error.
                    State = _loaded ? NotificationPollerState.Ready : NotificationPollerState.Error;
                    break;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        if (generation != _generation) return;
        if (arrived.Count > 0) ItemsArrived?.Invoke(arrived);
        if (reset) PageRefreshRequested?.Invoke(this, EventArgs.Empty);
        if (generation != _generation) return;
        Schedule(reset ? new NotificationNextStep(NotificationNextKind.Immediate, 0) : next, generation);
    }

    private void Schedule(NotificationNextStep next, int generation)
    {
        switch (next.Kind)
        {
            case NotificationNextKind.Stop:
                return;
            case NotificationNextKind.Immediate:
                _ = RequestAsync(generation);
                return;
            case NotificationNextKind.Pause:
                StartTimer(PausedRetrySeconds);
                return;
            default:
                StartTimer(Math.Max(1, next.Seconds));
                return;
        }
    }

    private void StartTimer(int seconds)
    {
        _timer.Interval = TimeSpan.FromSeconds(seconds);
        _timer.Start();
    }
}
