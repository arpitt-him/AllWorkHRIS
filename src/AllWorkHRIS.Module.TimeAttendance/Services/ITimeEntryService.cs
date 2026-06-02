using AllWorkHRIS.Module.TimeAttendance.Commands;
using AllWorkHRIS.Module.TimeAttendance.Domain;

namespace AllWorkHRIS.Module.TimeAttendance.Services;

public interface ITimeEntryService
{
    Task<Guid>                   SubmitTimeEntryAsync(SubmitTimeEntryCommand command);
    Task                         ApproveTimeEntryAsync(ApproveTimeEntryCommand command);
    /// <summary>Phase 12.7b — batch approve (per-employee "Approve All", period bulk, approve-clean).
    /// Returns the count actually transitioned (already-approved/ineligible ids are skipped).</summary>
    Task<int>                    ApproveTimeEntriesAsync(IReadOnlyList<Guid> timeEntryIds, Guid approvedBy);
    /// <summary>Phase 12.7b — whether the legal entity auto-approves IMPORT-method time on submit.</summary>
    Task<bool>                   GetAutoApproveImportedTimeAsync(Guid legalEntityId);
    Task                         RejectTimeEntryAsync(RejectTimeEntryCommand command);
    Task                         VoidTimeEntryAsync(Guid timeEntryId, Guid voidedBy, string reason);
    Task<Guid>                   CorrectTimeEntryAsync(CorrectTimeEntryCommand command);
    Task<IEnumerable<TimeEntry>> GetPeriodEntriesAsync(Guid employmentId, Guid payrollPeriodId);
}
