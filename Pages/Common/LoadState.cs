namespace MolHub.Windows;

/// <summary>Where a page's data is in its load cycle.</summary>
public enum LoadPhase
{
    /// <summary>Nothing was requested yet (or the state was reset).</summary>
    NotLoaded,
    /// <summary>The newest load was sent and has not answered.</summary>
    Loading,
    /// <summary>The newest load answered and its data is shown.</summary>
    Showing,
    /// <summary>The newest load failed; the page keeps what it showed before and offers a retry.</summary>
    Retryable
}

/// <summary>What a load recorded when it started: its own number and the data version at that moment.</summary>
public readonly record struct LoadTicket(long Number, long Version);

/// <summary>
/// The shared answer to "may this response still be shown or stored?", for one set of data on one page. Pure and used
/// from the UI thread only. Each load takes a new <see cref="LoadTicket"/>; only the newest ticket is accepted. Every
/// write that may have changed the data calls <see cref="Invalidate"/>: a response to a load that started before that is
/// still shown, but never counts as fresh, so the page reloads the next time it is shown. The page keeps its own JSON
/// parsing and rendering; this class only decides acceptance and holds the phase.
/// </summary>
public class LoadState
{
    private long _number;
    private long _version;
    private long _activeVersion;
    private DateTimeOffset? _loadedAt;

    public LoadPhase Phase { get; private set; } = LoadPhase.NotLoaded;

    /// <summary>True once a load was shown (a later failure keeps it).</summary>
    public bool HasContent { get; private set; }

    /// <summary>True while the newest load has not answered.</summary>
    public bool IsLoading => Phase == LoadPhase.Loading;

    /// <summary>Starts a load: a new ticket number that makes every older ticket stale, and the current data version.</summary>
    public LoadTicket Begin()
    {
        _number++;
        _activeVersion = _version;
        Phase = LoadPhase.Loading;
        return new LoadTicket(_number, _version);
    }

    /// <summary>Whether this ticket is the newest one (a response to any other ticket is dropped).</summary>
    public bool IsCurrent(LoadTicket ticket) => ticket.Number != 0 && ticket.Number == _number;

    /// <summary>
    /// Records the answer of the newest load: success shows the data (fresh only when no <see cref="Invalidate"/> came in
    /// between), failure keeps the content and allows a retry. Returns false, and changes nothing, for an older ticket.
    /// </summary>
    public bool Complete(LoadTicket ticket, bool success, DateTimeOffset now)
    {
        if (!IsCurrent(ticket)) return false;
        if (success)
        {
            Phase = LoadPhase.Showing;
            HasContent = true;
            _loadedAt = ticket.Version == _version ? now : null;
        }
        else
        {
            Phase = LoadPhase.Retryable;
        }
        return true;
    }

    /// <summary>The data may have changed (a write anywhere): raises the data version and forgets the loaded time.</summary>
    public void Invalidate()
    {
        _version++;
        _loadedAt = null;
    }

    /// <summary>
    /// Whether the page should load when it is shown: there is no content, it was invalidated, or it is older than
    /// <paramref name="staleAfter"/>. A load that started after the last invalidation is already on its way and counts as not needed.
    /// </summary>
    public bool NeedsLoad(DateTimeOffset now, TimeSpan staleAfter)
    {
        if (Phase == LoadPhase.Loading && _activeVersion == _version) return false;
        if (!HasContent || _loadedAt is not { } loaded) return true;
        return now - loaded > staleAfter;
    }

    /// <summary>Back to the initial state; a response to any earlier ticket is dropped.</summary>
    public virtual void Reset()
    {
        _number++;
        _version++;
        _loadedAt = null;
        HasContent = false;
        Phase = LoadPhase.NotLoaded;
    }
}

/// <summary>What a list load recorded when it started: the load ticket plus the list generation, condition and offset.</summary>
public sealed record PagedTicket<TCondition>(LoadTicket Load, long Generation, TCondition Condition, int Offset, bool IsMore);

/// <summary>
/// A <see cref="LoadState"/> for a list with a search/filter condition and "Load more". The condition and the offset are
/// recorded when a request starts; a "Load more" answer is applied only when the list generation, the condition and the
/// offset are still the current ones, so it cannot be appended to a refreshed or re-filtered list.
/// </summary>
public sealed class PagedLoadState<TCondition> : LoadState
{
    private long _generation;

    /// <summary>The condition of the list as it is now (the last <see cref="BeginFirst"/>).</summary>
    public TCondition? Condition { get; private set; }

    /// <summary>The offset of the next page, or null when the list has no more.</summary>
    public int? NextOffset { get; private set; }

    /// <summary>Load more is offered only with a next page and while no load of this list runs.</summary>
    public bool CanLoadMore => NextOffset is not null && !IsLoading;

    /// <summary>Starts a first page: a new list generation with this condition; the old next offset no longer applies.</summary>
    public PagedTicket<TCondition> BeginFirst(TCondition condition)
    {
        _generation++;
        Condition = condition;
        NextOffset = null;
        return new PagedTicket<TCondition>(Begin(), _generation, condition, 0, false);
    }

    /// <summary>Starts the next page, or returns null when there is none or any load of this list is still running.</summary>
    public PagedTicket<TCondition>? BeginMore()
    {
        if (NextOffset is not { } offset || IsLoading) return null;
        return new PagedTicket<TCondition>(Begin(), _generation, Condition!, offset, true);
    }

    /// <summary>Whether a first-page answer is still the newest and belongs to the current list.</summary>
    public bool AcceptFirst(PagedTicket<TCondition> ticket) =>
        !ticket.IsMore && IsCurrent(ticket.Load) && ticket.Generation == _generation && SameCondition(ticket.Condition);

    /// <summary>Whether a "Load more" answer still matches the current list: generation, condition and offset.</summary>
    public bool AcceptMore(PagedTicket<TCondition> ticket) =>
        ticket.IsMore && IsCurrent(ticket.Load) && ticket.Generation == _generation && SameCondition(ticket.Condition)
        && NextOffset == ticket.Offset;

    /// <summary>Records an accepted page (its next offset, null at the end). Returns false, changing nothing, when it is not accepted.</summary>
    public bool CompletePage(PagedTicket<TCondition> ticket, int? nextOffset, DateTimeOffset now)
    {
        if (ticket.IsMore ? !AcceptMore(ticket) : !AcceptFirst(ticket)) return false;
        NextOffset = nextOffset;
        return Complete(ticket.Load, true, now);
    }

    /// <summary>Records a failed page of an accepted ticket (the list and the next offset stay as they were).</summary>
    public bool FailPage(PagedTicket<TCondition> ticket, DateTimeOffset now)
    {
        if (ticket.IsMore ? !AcceptMore(ticket) : !AcceptFirst(ticket)) return false;
        return Complete(ticket.Load, false, now);
    }

    /// <summary>Back to the initial state: the list, its condition and its offset are forgotten, and a reply to any earlier ticket is dropped.</summary>
    public override void Reset()
    {
        base.Reset();
        _generation++;
        Condition = default;
        NextOffset = null;
    }

    private bool SameCondition(TCondition other) => EqualityComparer<TCondition>.Default.Equals(Condition!, other);
}
