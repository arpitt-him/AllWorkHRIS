using AllWorkHRIS.Core.Composition;
using AllWorkHRIS.Host.Hris.Services;

namespace AllWorkHRIS.Host.TimeAttendance;

public sealed class WorkQueueTimeApprovalNotifier : ITimeApprovalNotifier
{
    private readonly IWorkQueueService _workQueueService;

    public WorkQueueTimeApprovalNotifier(IWorkQueueService workQueueService)
        => _workQueueService = workQueueService;

    public Task NotifyTimeApprovalAsync(Guid timeEntryId, Guid employmentId)
        => _workQueueService.CreateTimeApprovalTaskAsync(timeEntryId, employmentId);

    public Task NotifyOvertimeWarningAsync(Guid employmentId, DateOnly weekStart, decimal overtimeHours)
        => _workQueueService.CreateOvertimeWarningAsync(employmentId, weekStart, overtimeHours);

    public Task NotifyRetroCalculationReviewAsync(Guid correctionId, Guid employmentId, Guid periodId)
        => _workQueueService.CreateRetroCalculationReviewAsync(correctionId, employmentId, periodId);
}
