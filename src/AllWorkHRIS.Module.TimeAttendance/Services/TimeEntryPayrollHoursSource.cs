using AllWorkHRIS.Core.Pipeline;
using AllWorkHRIS.Module.TimeAttendance.Repositories;

namespace AllWorkHRIS.Module.TimeAttendance.Services;

/// <summary>
/// Adapter that implements the Core-side <see cref="IPayrollHoursSource"/>
/// by delegating to this module's <see cref="ITimeEntryRepository"/>.
///
/// Registered by <c>TimeAttendanceModule.Register</c> so that when the T&amp;A
/// module is composed it overrides the host's <see cref="NullPayrollHoursSource"/>
/// fallback via Autofac last-registration-wins. The Payroll module's
/// CalculationEngine consumes <see cref="IPayrollHoursSource"/>, never this
/// adapter or the T&amp;A repository directly — keeping Payroll's assembly
/// independent of the T&amp;A assembly (ADR-011).
/// </summary>
public sealed class TimeEntryPayrollHoursSource : IPayrollHoursSource
{
    private readonly ITimeEntryRepository _timeEntryRepo;

    public TimeEntryPayrollHoursSource(ITimeEntryRepository timeEntryRepo)
    {
        _timeEntryRepo = timeEntryRepo;
    }

    public Task<IReadOnlyList<(DateOnly WorkDate, decimal Hours)>> GetApprovedHoursByEmploymentAndPeriodAsync(
        Guid employmentId, DateOnly periodStart, DateOnly periodEnd, Guid payrollRunId)
        => _timeEntryRepo.GetApprovedHoursByEmploymentAndPeriodAsync(employmentId, periodStart, periodEnd, payrollRunId);

    public Task<decimal> GetApprovedNonWorkedPayableHoursByEmploymentAndPeriodAsync(
        Guid employmentId, DateOnly periodStart, DateOnly periodEnd, Guid payrollRunId)
        => _timeEntryRepo.GetApprovedNonWorkedPayableHoursByEmploymentAndPeriodAsync(employmentId, periodStart, periodEnd, payrollRunId);

    public Task LockHoursForRunAsync(
        Guid payrollRunId, IReadOnlyList<Guid> employmentIds, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default)
        => _timeEntryRepo.LockHoursForRunAsync(payrollRunId, employmentIds, periodStart, periodEnd, ct);

    public Task UnlockHoursForRunAsync(Guid payrollRunId, CancellationToken ct = default)
        => _timeEntryRepo.UnlockHoursForRunAsync(payrollRunId, ct);
}
