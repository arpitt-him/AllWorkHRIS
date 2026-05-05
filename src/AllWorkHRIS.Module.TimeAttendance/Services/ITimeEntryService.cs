using AllWorkHRIS.Module.TimeAttendance.Commands;
using AllWorkHRIS.Module.TimeAttendance.Domain;

namespace AllWorkHRIS.Module.TimeAttendance.Services;

public interface ITimeEntryService
{
    Task<Guid>                   SubmitTimeEntryAsync(SubmitTimeEntryCommand command);
    Task                         ApproveTimeEntryAsync(ApproveTimeEntryCommand command);
    Task                         RejectTimeEntryAsync(RejectTimeEntryCommand command);
    Task                         VoidTimeEntryAsync(Guid timeEntryId, Guid voidedBy, string reason);
    Task<Guid>                   CorrectTimeEntryAsync(CorrectTimeEntryCommand command);
    Task<IEnumerable<TimeEntry>> GetPeriodEntriesAsync(Guid employmentId, Guid payrollPeriodId);
}
