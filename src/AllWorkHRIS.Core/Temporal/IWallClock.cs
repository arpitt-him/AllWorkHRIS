namespace AllWorkHRIS.Core.Temporal;

/// <summary>
/// Real, physical wall-clock time — independent of any Temporal Date Override.
///
/// Use this ONLY for genuine audit / infrastructure timestamps that must record
/// when something physically happened in the real world (e.g. created_at /
/// updated_at audit columns, log timestamps, cache TTLs).
///
/// For anything a user reads as "when X happened" in the operating/simulated world,
/// or for effective-date / "as of" resolution, use
/// <see cref="ITemporalContext.GetOperativeDate"/> instead — that one honors the TDO.
///
/// Raw <c>DateTime.UtcNow</c> / <c>DateTimeOffset.UtcNow</c> / <c>.Now</c> / <c>.Today</c>
/// are banned solution-wide (BannedSymbols.txt → RS0030). Route every physical-time
/// need through this interface so the intent — "this is real time, on purpose" — is
/// explicit at the call site and visible in review, rather than indistinguishable
/// from a bug that forgot the TDO.
///
/// (Named WallClock rather than SystemClock to avoid colliding with the obsolete
/// Microsoft.AspNetCore.Authentication.ISystemClock that is globally imported in the
/// web host project.)
/// </summary>
public interface IWallClock
{
    /// <summary>Current physical time in UTC.</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>Current physical date in UTC.</summary>
    DateOnly Today { get; }
}

/// <summary>
/// Default <see cref="IWallClock"/> — the one sanctioned place that reads the
/// real clock. Registered as a singleton. Tests may substitute a fake.
/// </summary>
public sealed class WallClock : IWallClock
{
#pragma warning disable RS0030 // Sanctioned wrapper: this is THE allowed real-clock access point.
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public DateOnly       Today  => DateOnly.FromDateTime(DateTime.UtcNow);
#pragma warning restore RS0030
}
