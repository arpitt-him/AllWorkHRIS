using AllWorkHRIS.Host.Hris.Domain;
using AllWorkHRIS.Host.Hris.Repositories;

namespace AllWorkHRIS.Host.Hris.Services;

public interface IWorkQueueService
{
    Task<Guid> CreateLeaveApprovalTaskAsync(Guid leaveRequestId, Guid employmentId);
    Task       EnsureExpirationAlertAsync(HrDocument doc, DateOnly operativeDate, string docTypeCode);
    Task       CreateOnboardingTaskItemAsync(Guid taskId, Guid planId, Guid employmentId,
                   string taskType, DateOnly dueDate);
    Task       ResolveByReferenceAsync(Guid referenceId, Guid resolvedBy);
    Task<IEnumerable<WorkQueueItem>> GetOpenByRoleAsync(string role, Guid? legalEntityId = null, Guid? employmentId = null);
}

public sealed class WorkQueueService : IWorkQueueService
{
    private readonly IWorkQueueRepository _repository;

    public WorkQueueService(IWorkQueueRepository repository)
        => _repository = repository;

    public async Task<Guid> CreateLeaveApprovalTaskAsync(Guid leaveRequestId, Guid employmentId)
    {
        var item = new WorkQueueItem
        {
            WorkQueueItemId = Guid.NewGuid(),
            ItemType        = WorkQueueItemTypes.LeaveApproval,
            ReferenceId     = leaveRequestId,
            ReferenceType   = "LEAVE_REQUEST",
            EmploymentId    = employmentId,
            AssignedRole    = "Manager",
            Status          = "OPEN",
            Priority        = WorkQueuePriority.Normal,
            Title           = "Leave Request Pending Approval",
            Description     = $"Leave request {leaveRequestId} requires your approval.",
            CreatedAt       = DateTimeOffset.UtcNow
        };
        return await _repository.InsertAsync(item);
    }

    public async Task EnsureExpirationAlertAsync(HrDocument doc, DateOnly operativeDate,
        string docTypeCode)
    {
        if (doc.ExpirationDate is null) return;

        var daysUntil = doc.ExpirationDate.Value.DayNumber - operativeDate.DayNumber;

        string itemType;
        string priority;
        if (daysUntil <= 0)
        {
            itemType = WorkQueueItemTypes.DocExpired;
            priority = WorkQueuePriority.Hold;
        }
        else if (daysUntil <= 30)
        {
            itemType = WorkQueueItemTypes.DocExpiring30;
            priority = WorkQueuePriority.High;
        }
        else
        {
            itemType = WorkQueueItemTypes.DocExpiring90;
            priority = WorkQueuePriority.Normal;
        }

        var existing = await _repository.GetAnyOpenByReferenceAsync(doc.DocumentId);
        if (existing is not null)
        {
            if (existing.ItemType == itemType)
            {
                if (existing.Priority != priority)
                    await _repository.UpdatePriorityAsync(existing.WorkQueueItemId, priority);
                return;
            }
            // Escalated to a new alert stage — close the superseded item
            await _repository.ResolveAsync(existing.WorkQueueItemId, Guid.Empty);
        }

        var item = new WorkQueueItem
        {
            WorkQueueItemId = Guid.NewGuid(),
            ItemType        = itemType,
            ReferenceId     = doc.DocumentId,
            ReferenceType   = "DOCUMENT",
            EmploymentId    = doc.EmploymentId,
            AssignedRole    = "HrisAdmin",
            Status          = "OPEN",
            Priority        = priority,
            Title           = $"{docTypeCode} document expiring in {daysUntil} days",
            Description     = $"Document '{doc.DocumentName}' expires on {doc.ExpirationDate}.",
            DueDate         = doc.ExpirationDate,
            CreatedAt       = DateTimeOffset.UtcNow
        };
        await _repository.InsertAsync(item);
    }

    public async Task CreateOnboardingTaskItemAsync(Guid taskId, Guid planId, Guid employmentId,
        string taskType, DateOnly dueDate)
    {
        var item = new WorkQueueItem
        {
            WorkQueueItemId = Guid.NewGuid(),
            ItemType        = WorkQueueItemTypes.OnboardingTask,
            ReferenceId     = taskId,
            ReferenceType   = "ONBOARDING_TASK",
            EmploymentId    = employmentId,
            AssignedRole    = "HrisAdmin",
            Status          = "OPEN",
            Priority        = WorkQueuePriority.Normal,
            Title           = $"Onboarding task: {taskType}",
            DueDate         = dueDate,
            CreatedAt       = DateTimeOffset.UtcNow
        };
        await _repository.InsertAsync(item);
    }

    public Task ResolveByReferenceAsync(Guid referenceId, Guid resolvedBy)
        => _repository.ResolveByReferenceAsync(referenceId, resolvedBy);

    public Task<IEnumerable<WorkQueueItem>> GetOpenByRoleAsync(string role, Guid? legalEntityId = null, Guid? employmentId = null)
        => _repository.GetOpenByRoleAsync(role, legalEntityId, employmentId);
}
