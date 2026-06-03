using AllWorkHRIS.Core.Pipeline;

namespace AllWorkHRIS.Host.Tests.TestSupport;

/// <summary>
/// Test double for <see cref="IPayrollHoursSource"/> returning a fixed set of <b>worked</b> daily
/// totals plus a fixed <b>non-worked-but-payable</b> (paid-leave) total, regardless of employment or
/// period. Used to exercise the Phase 12.12 / ADR-023 split: overtime is computed on the worked
/// hours only, while paid leave is still paid at straight time (folded into REG).
/// </summary>
internal sealed class FixedHoursPayrollHoursSource : IPayrollHoursSource
{
    private readonly IReadOnlyList<(DateOnly WorkDate, decimal Hours)> _worked;
    private readonly decimal _nonWorkedPayableHours;

    public FixedHoursPayrollHoursSource(
        IReadOnlyList<(DateOnly WorkDate, decimal Hours)> worked, decimal nonWorkedPayableHours)
    {
        _worked                = worked;
        _nonWorkedPayableHours = nonWorkedPayableHours;
    }

    public Task<IReadOnlyList<(DateOnly WorkDate, decimal Hours)>> GetApprovedHoursByEmploymentAndPeriodAsync(
        Guid employmentId, DateOnly periodStart, DateOnly periodEnd, Guid payrollRunId,
        IReadOnlyCollection<int> otEligibleCategoryIds)
        => Task.FromResult(_worked);

    public Task<decimal> GetApprovedNonWorkedPayableHoursByEmploymentAndPeriodAsync(
        Guid employmentId, DateOnly periodStart, DateOnly periodEnd, Guid payrollRunId,
        IReadOnlyCollection<int> otEligibleCategoryIds)
        => Task.FromResult(_nonWorkedPayableHours);

    public Task LockHoursForRunAsync(
        Guid payrollRunId, IReadOnlyList<Guid> employmentIds, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task UnlockHoursForRunAsync(Guid payrollRunId, CancellationToken ct = default)
        => Task.CompletedTask;
}
