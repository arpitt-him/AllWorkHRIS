using AllWorkHRIS.Core.Composition;

namespace AllWorkHRIS.Module.TimeAttendance.Services;

public sealed class NullTimeApprovalNotifier : ITimeApprovalNotifier
{
    public Task NotifyTimeApprovalAsync(Guid timeEntryId, Guid employmentId)
        => Task.CompletedTask;

    public Task NotifyOvertimeWarningAsync(Guid employmentId, DateOnly weekStart, decimal overtimeHours)
        => Task.CompletedTask;

    public Task NotifyRetroCalculationReviewAsync(Guid correctionId, Guid employmentId, Guid periodId)
        => Task.CompletedTask;
}
