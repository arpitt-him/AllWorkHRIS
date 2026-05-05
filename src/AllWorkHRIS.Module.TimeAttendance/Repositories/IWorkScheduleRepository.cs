using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.TimeAttendance.Domain.Schedule;

namespace AllWorkHRIS.Module.TimeAttendance.Repositories;

public interface IWorkScheduleRepository
{
    Task<WorkSchedule?>              GetByIdAsync(Guid workScheduleId);
    Task<IEnumerable<WorkSchedule>>  GetByLegalEntityAsync(Guid legalEntityId);
    Task<WorkSchedule?>              GetActiveForEntityAsync(Guid legalEntityId, DateOnly asOf);
    Task<IEnumerable<ShiftDefinition>> GetShiftsAsync(Guid workScheduleId);
    Task<Guid>                        InsertAsync(WorkSchedule schedule, IUnitOfWork uow);
}
