namespace AllWorkHRIS.Core.Temporal;

public sealed class OverridableTemporalContext : ITemporalContext, ITemporalOverrideService
{
    private readonly object  _lock = new();
    private readonly string  _persistPath;
    private DateOnly?        _override;

    public OverridableTemporalContext(string persistPath)
    {
        _persistPath = persistPath;
        if (File.Exists(persistPath))
        {
            var text = File.ReadAllText(persistPath).Trim();
            if (DateOnly.TryParse(text, out var saved))
                _override = saved;
        }
    }

    public bool      IsEnabled        => true;
    public bool      IsOverrideActive { get { lock (_lock) return _override.HasValue; } }
    public DateOnly? OverrideDate     { get { lock (_lock) return _override; } }
    public event Action OnChanged = delegate { };

    public DateTime GetOperativeDate()
    {
        lock (_lock)
        return _override.HasValue
            ? _override.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            : DateTime.UtcNow;
    }

    public void SetOverride(DateOnly date)
    {
        lock (_lock) _override = date;
        File.WriteAllText(_persistPath, date.ToString("yyyy-MM-dd"));
        OnChanged();
    }

    public void ClearOverride()
    {
        lock (_lock) _override = null;
        if (File.Exists(_persistPath)) File.Delete(_persistPath);
        OnChanged();
    }
}
