namespace AllWorkHRIS.Module.TimeAttendance.Services;

/// <summary>
/// Decouples T&A module from Host's IWorkQueueService.
/// Implemented by the Host; a no-op NullTimeApprovalNotifier is registered by default.
/// Pattern: same as IPayrollContextLookup / NullPayrollContextLookup.
/// </summary>
public interface ITimeApprovalNotifier
{
    Task NotifyTimeApprovalAsync(Guid timeEntryId, Guid employmentId);
    Task NotifyOvertimeWarningAsync(Guid employmentId, DateOnly weekStart, decimal overtimeHours);
    Task NotifyRetroCalculationReviewAsync(Guid correctionId, Guid employmentId, Guid periodId);
}

public sealed class NullTimeApprovalNotifier : ITimeApprovalNotifier
{
    public Task NotifyTimeApprovalAsync(Guid timeEntryId, Guid employmentId)
        => Task.CompletedTask;

    public Task NotifyOvertimeWarningAsync(Guid employmentId, DateOnly weekStart, decimal overtimeHours)
        => Task.CompletedTask;

    public Task NotifyRetroCalculationReviewAsync(Guid correctionId, Guid employmentId, Guid periodId)
        => Task.CompletedTask;
}
