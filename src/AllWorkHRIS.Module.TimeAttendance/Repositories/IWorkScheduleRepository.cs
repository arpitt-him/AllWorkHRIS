using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.TimeAttendance.Domain.Schedule;

namespace AllWorkHRIS.Module.TimeAttendance.Repositories;

public interface IWorkScheduleRepository
{
    Task<WorkSchedule?>                GetByIdAsync(Guid workScheduleId);
    Task<IEnumerable<WorkSchedule>>    GetByLegalEntityAsync(Guid legalEntityId);
    Task<WorkSchedule?>                GetActiveForEntityAsync(Guid legalEntityId, DateOnly asOf);
    Task<IEnumerable<ShiftDefinition>> GetShiftsAsync(Guid workScheduleId);
    Task<Guid>                         InsertAsync(WorkSchedule schedule, IUnitOfWork uow);

    /// <summary>
    /// Resolves the FLSA workweek anchor (0=Sun … 6=Sat) for a time entry using a three-level cascade:
    /// 1. DayOfWeek(payroll_period.period_start_date) — pay calendar anchor for the employee's pay group
    /// 2. Active work_schedule.workweek_start_day for the legal entity — HRIS-only fallback
    /// 3. 1 (Monday) — safe default
    /// </summary>
    Task<int> ResolveWorkweekAnchorAsync(Guid payrollPeriodId, Guid employmentId);
}
