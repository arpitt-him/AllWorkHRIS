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
#pragma warning disable RS0030 // Sanctioned wrapper: real UTC clock is the no-override fallback.
        return _override.HasValue
            ? _override.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            : DateTime.UtcNow;
#pragma warning restore RS0030
    }

    /// <summary>
    /// Operative "now" as a UTC <see cref="DateTimeOffset"/>. Overrides the default
    /// (UTC-time-of-day) implementation: when an override date is active we combine the
    /// TDO date with the real <b>local</b> time-of-day (and local offset), then normalise
    /// to UTC. Anchoring to local time-of-day keeps the TDO date intact through the
    /// <c>.ToLocalTime()</c> round-trip every display performs — the UTC-time-of-day default
    /// rolls the displayed date back a day for negative-offset zones (e.g. US) in the evening.
    /// </summary>
    public DateTimeOffset GetOperativeNow()
    {
        DateOnly? ov;
        lock (_lock) ov = _override;

#pragma warning disable RS0030 // Sanctioned: real LOCAL wall clock is intentional here; only the DATE comes from the TDO.
        var realLocal = DateTimeOffset.Now;
#pragma warning restore RS0030

        // No override active → behave like production: real "now" in UTC.
        if (ov is null) return realLocal.ToUniversalTime();

        // TDO date + real local time-of-day, tagged with the local offset (Unspecified-kind
        // DateTime so the offset ctor is valid), then converted to UTC for storage.
        var localMoment = ov.Value.ToDateTime(TimeOnly.FromTimeSpan(realLocal.TimeOfDay));
        return new DateTimeOffset(localMoment, realLocal.Offset).ToUniversalTime();
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
