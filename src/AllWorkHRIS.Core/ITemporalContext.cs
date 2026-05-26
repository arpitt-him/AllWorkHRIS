// AllWorkHRIS.Core/Temporal/ITemporalContext.cs
namespace AllWorkHRIS.Core.Temporal;

/// <summary>
/// Provides the governed operative date for all point-in-time queries.
/// In production, returns the current system date.
/// In non-production tenants with Temporal Override active, returns the
/// configured override date instead of the system clock.
/// All services and repositories that perform effective-date resolution
/// must route through this interface — never DateTime.UtcNow directly.
/// </summary>
public interface ITemporalContext
{
    /// <summary>
    /// Returns the current operative date for effective-date resolution.
    /// </summary>
    DateTime GetOperativeDate();

    /// <summary>
    /// Operative "now" as a UTC <see cref="DateTimeOffset"/>: the operative (TDO) date
    /// combined with the real current time-of-day. Use for audit-style "now" timestamps
    /// (created / updated / submitted / locked) that should reflect the simulated date in
    /// dev while still advancing realistically within a session. In production (no override)
    /// this equals the real UtcNow. Default-implemented so every <see cref="ITemporalContext"/>
    /// — including test fakes — gets it for free.
    /// </summary>
    DateTimeOffset GetOperativeNow()
    {
#pragma warning disable RS0030 // Sanctioned: real time-of-day is intentional; the DATE comes from the TDO.
        var timeOfDay = DateTime.UtcNow.TimeOfDay;
#pragma warning restore RS0030
        return new DateTimeOffset(GetOperativeDate().Date + timeOfDay, TimeSpan.Zero);
    }
}

/// <summary>
/// Default implementation — returns system UTC date.
/// Replaced by TemporalOverrideContext when Temporal Override is active.
/// </summary>
public sealed class SystemTemporalContext : ITemporalContext
{
#pragma warning disable RS0030 // Sanctioned wrapper: production operative date IS the real UTC clock.
    public DateTime GetOperativeDate() => DateTime.UtcNow;
#pragma warning restore RS0030
}
