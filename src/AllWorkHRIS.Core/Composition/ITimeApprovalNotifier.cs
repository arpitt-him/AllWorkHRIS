namespace AllWorkHRIS.Core.Composition;

public interface ITimeApprovalNotifier
{
    Task NotifyTimeApprovalAsync(Guid timeEntryId, Guid employmentId);
    Task NotifyOvertimeWarningAsync(Guid employmentId, DateOnly weekStart, decimal overtimeHours);
    Task NotifyRetroCalculationReviewAsync(Guid correctionId, Guid employmentId, Guid periodId);
}
