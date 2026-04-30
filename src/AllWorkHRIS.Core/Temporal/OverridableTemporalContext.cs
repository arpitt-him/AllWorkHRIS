namespace AllWorkHRIS.Core.Temporal;

public sealed class OverridableTemporalContext : ITemporalContext, ITemporalOverrideService
{
    private readonly object _lock = new();
    private DateOnly? _override;

    public bool      IsEnabled        => true;
    public bool      IsOverrideActive { get { lock (_lock) return _override.HasValue; } }
    public DateOnly? OverrideDate     { get { lock (_lock) return _override; } }

    public DateTime GetOperativeDate()
    {
        lock (_lock)
        return _override.HasValue
            ? _override.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            : DateTime.UtcNow;
    }

    public void SetOverride(DateOnly date) { lock (_lock) _override = date; }
    public void ClearOverride()            { lock (_lock) _override = null; }
}
