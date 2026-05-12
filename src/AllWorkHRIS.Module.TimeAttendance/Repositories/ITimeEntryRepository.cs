using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.TimeAttendance.Domain;

namespace AllWorkHRIS.Module.TimeAttendance.Repositories;

public interface ITimeEntryRepository
{
    Task<TimeEntry?>              GetByIdAsync(Guid timeEntryId);
    Task<IEnumerable<TimeEntry>>  GetByEmploymentAndPeriodAsync(Guid employmentId, Guid payrollPeriodId);
    Task<IEnumerable<TimeEntry>>  GetPendingApprovalByManagerAsync(Guid managerEmploymentId, Guid payrollPeriodId);
    Task<IEnumerable<TimeEntry>>  GetApprovedForHandoffAsync(Guid payrollPeriodId);
    Task<IEnumerable<TimeEntry>>  GetWorkweekEntriesAsync(Guid employmentId, DateOnly weekStart);
    Task<IEnumerable<TimeEntry>>  GetOpenByEmploymentAsync(Guid employmentId);
    Task<Guid>                    InsertAsync(TimeEntry entry, IUnitOfWork uow);
    Task                          UpdateStatusAsync(Guid timeEntryId, string status, Guid actorId, IUnitOfWork uow);
    Task                          UpdateStatusWithReasonAsync(Guid timeEntryId, string status, Guid actorId, string reason, IUnitOfWork uow);
    Task                          LockAsync(Guid timeEntryId, Guid payrollRunId, IUnitOfWork uow);
    Task                          ReclassifyAsync(Guid timeEntryId, string timeCategory, IUnitOfWork uow);
    Task<bool>                    EmploymentExistsAsync(Guid employmentId);
    Task<string?>                 GetPeriodStatusAsync(Guid payrollPeriodId);
    Task<string?>                 GetFlsaStatusAsync(Guid employmentId);
    Task<bool>                    IsCategoryWorkedTimeAsync(string categoryCode);

    /// <summary>
    /// Returns total approved/locked worked hours per calendar date for an employment within a
    /// pay period date range. Used by the payroll engine to compute hours-based pay and FLSA
    /// overtime for non-exempt employees.
    /// </summary>
    Task<IReadOnlyList<(DateOnly WorkDate, decimal Hours)>> GetApprovedHoursByEmploymentAndPeriodAsync(
        Guid employmentId, DateOnly periodStart, DateOnly periodEnd);
}
