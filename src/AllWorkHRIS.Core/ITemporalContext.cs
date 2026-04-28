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
}

/// <summary>
/// Default implementation — returns system UTC date.
/// Replaced by TemporalOverrideContext when Temporal Override is active.
/// </summary>
public sealed class SystemTemporalContext : ITemporalContext
{
    public DateTime GetOperativeDate() => DateTime.UtcNow;
}
