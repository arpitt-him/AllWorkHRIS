using AllWorkHRIS.Core.Pipeline;

namespace AllWorkHRIS.Host.Tests.TestSupport;

/// <summary>
/// Test double for <see cref="IPayrollHoursSource"/>. The real adapter lives in
/// the T&amp;A module and reads approved time entries; the Core fallback
/// (<c>NullPayrollHoursSource</c>) returns an empty list, which would leave a
/// NON_EXEMPT hourly employee with zero hours and therefore no REG earnings line.
/// This double returns a standard 40-hour week at the period start — a single
/// workweek at exactly the FLSA threshold, so it yields a clean REG line with no
/// overtime. The gate tests assert the run calculates and a REG line exists, not
/// the amount. (ToDo #36)
/// </summary>
internal sealed class StandardHoursPayrollHoursSource : IPayrollHoursSource
{
    public Task<IReadOnlyList<(DateOnly WorkDate, decimal Hours)>> GetApprovedHoursByEmploymentAndPeriodAsync(
        Guid employmentId, DateOnly periodStart, DateOnly periodEnd, Guid payrollRunId,
        IReadOnlyCollection<int> otEligibleCategoryIds)
        => Task.FromResult<IReadOnlyList<(DateOnly WorkDate, decimal Hours)>>([(periodStart, 40m)]);

    // No paid-leave hours in the gate fixture — worked hours only (Phase 12.12).
    public Task<decimal> GetApprovedNonWorkedPayableHoursByEmploymentAndPeriodAsync(
        Guid employmentId, DateOnly periodStart, DateOnly periodEnd, Guid payrollRunId,
        IReadOnlyCollection<int> otEligibleCategoryIds)
        => Task.FromResult(0m);

    public Task LockHoursForRunAsync(
        Guid payrollRunId, IReadOnlyList<Guid> employmentIds, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task UnlockHoursForRunAsync(Guid payrollRunId, CancellationToken ct = default)
        => Task.CompletedTask;
}
