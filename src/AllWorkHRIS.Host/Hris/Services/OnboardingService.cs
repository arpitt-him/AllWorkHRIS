using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Events;
using AllWorkHRIS.Core.Lookups;
using AllWorkHRIS.Host.Hris.Domain;
using AllWorkHRIS.Host.Hris.Repositories;

namespace AllWorkHRIS.Host.Hris.Services;

public interface IOnboardingService
{
    Task<Guid>                        CreatePlanAsync(Guid employmentId, DateOnly startDate,
                                          Guid initiatedBy);
    Task                              CompleteTaskAsync(Guid taskId, DateOnly completionDate,
                                          Guid completedBy);
    Task                              WaiveTaskAsync(Guid taskId, string reason, Guid waivedBy);
    Task<OnboardingPlan?>             GetPlanByEmploymentIdAsync(Guid employmentId);
    Task<IEnumerable<OnboardingTask>> GetTasksByPlanIdAsync(Guid planId);
}

public sealed class OnboardingService : IOnboardingService
{
    private readonly IConnectionFactory      _connectionFactory;
    private readonly IOnboardingRepository   _onboardingRepository;
    private readonly IWorkQueueService       _workQueueService;
    private readonly IEventPublisher         _eventPublisher;
    private readonly ILookupCache            _lookupCache;

    private readonly int _planCreatedStatusId;
    private readonly int _planInProgressStatusId;
    private readonly int _planBlockingCompleteStatusId;
    private readonly int _planCompleteStatusId;
    private readonly int _taskPendingStatusId;
    private readonly int _taskCompletedStatusId;
    private readonly int _taskWaivedStatusId;

    public OnboardingService(
        IConnectionFactory    connectionFactory,
        IOnboardingRepository onboardingRepository,
        IWorkQueueService     workQueueService,
        IEventPublisher       eventPublisher,
        ILookupCache          lookupCache)
    {
        _connectionFactory    = connectionFactory;
        _onboardingRepository = onboardingRepository;
        _workQueueService     = workQueueService;
        _eventPublisher       = eventPublisher;
        _lookupCache          = lookupCache;

        _planCreatedStatusId          = lookupCache.GetId(LookupTables.OnboardingPlanStatus, "NOT_STARTED");
        _planInProgressStatusId       = lookupCache.GetId(LookupTables.OnboardingPlanStatus, "IN_PROGRESS");
        _planBlockingCompleteStatusId = lookupCache.GetId(LookupTables.OnboardingPlanStatus, "BLOCKING_COMPLETE");
        _planCompleteStatusId         = lookupCache.GetId(LookupTables.OnboardingPlanStatus, "COMPLETED");
        _taskPendingStatusId         = lookupCache.GetId(LookupTables.OnboardingTaskStatus, "PENDING");
        _taskCompletedStatusId       = lookupCache.GetId(LookupTables.OnboardingTaskStatus, "COMPLETED");
        _taskWaivedStatusId          = lookupCache.GetId(LookupTables.OnboardingTaskStatus, "WAIVED");
    }

    public async Task<Guid> CreatePlanAsync(
        Guid employmentId, DateOnly startDate, Guid initiatedBy)
    {
        var planId = Guid.NewGuid();
        var now    = DateTimeOffset.UtcNow;

        var plan = new OnboardingPlan
        {
            OnboardingPlanId    = planId,
            EmploymentId        = employmentId,
            PlanTemplateId      = null,
            PlanStatusId        = _planCreatedStatusId,
            TargetStartDate     = startDate,
            AssignedHrContactId = null,
            CreatedBy           = initiatedBy,
            CreationTimestamp   = now,
            LastUpdatedBy       = initiatedBy,
            LastUpdateTimestamp = now
        };

        var taskDefs = BuildDefaultTaskDefs(startDate);

        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            await _onboardingRepository.InsertPlanAsync(plan, uow);

            foreach (var def in taskDefs)
            {
                var taskTypeId = _lookupCache.GetId(LookupTables.OnboardingTaskType, def.TypeCode);
                var task = new OnboardingTask
                {
                    TaskId           = def.TaskId,
                    OnboardingPlanId = planId,
                    TaskTypeId       = taskTypeId,
                    TaskName         = def.TaskName,
                    TaskOwnerRole    = def.OwnerRole,
                    DueDate          = def.DueDate,
                    TaskStatusId     = _taskPendingStatusId,
                    BlockingFlag     = def.IsBlocking,
                    CreatedBy        = initiatedBy,
                    CreationTimestamp = now
                };
                await _onboardingRepository.InsertTaskAsync(task, uow);
            }

            uow.Commit();

            foreach (var def in taskDefs)
            {
                await _workQueueService.CreateOnboardingTaskItemAsync(
                    def.TaskId, planId, employmentId, def.TypeCode, def.DueDate);
            }

            await _eventPublisher.PublishAsync(new OnboardingPlanCreatedPayload
            {
                OnboardingPlanId = planId,
                EmploymentId     = employmentId,
                TenantId         = Guid.Empty,
                TargetStartDate  = startDate,
                EventTimestamp   = now
            });

            return planId;
        }
        catch
        {
            uow.Rollback();
            throw;
        }
    }

    public async Task CompleteTaskAsync(
        Guid taskId, DateOnly completionDate, Guid completedBy)
    {
        var task = await _onboardingRepository.GetTaskByIdAsync(taskId)
            ?? throw new NotFoundException(nameof(OnboardingTask), taskId);

        if (task.TaskStatusId == _taskCompletedStatusId)
            return;

        var plan = await _onboardingRepository.GetPlanByIdAsync(task.OnboardingPlanId)
            ?? throw new DomainException($"Onboarding plan {task.OnboardingPlanId} not found.");

        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            await _onboardingRepository.UpdateTaskStatusAsync(
                taskId, _taskCompletedStatusId, completionDate, uow);

            if (plan.PlanStatusId == _planCreatedStatusId)
                await _onboardingRepository.UpdatePlanStatusAsync(
                    plan.OnboardingPlanId, _planInProgressStatusId, uow);

            uow.Commit();
        }
        catch
        {
            uow.Rollback();
            throw;
        }

        await CheckAndAdvancePlanStatusAsync(plan, completedBy);
    }

    public async Task WaiveTaskAsync(Guid taskId, string reason, Guid waivedBy)
    {
        var task = await _onboardingRepository.GetTaskByIdAsync(taskId)
            ?? throw new NotFoundException(nameof(OnboardingTask), taskId);

        if (task.TaskStatusId == _taskWaivedStatusId)
            return;

        var plan = await _onboardingRepository.GetPlanByIdAsync(task.OnboardingPlanId)
            ?? throw new DomainException($"Onboarding plan {task.OnboardingPlanId} not found.");

        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            await _onboardingRepository.UpdateTaskStatusAsync(
                taskId, _taskWaivedStatusId, null, uow);
            await _onboardingRepository.SetTaskWaivedAsync(taskId, reason, waivedBy, uow);

            if (plan.PlanStatusId == _planCreatedStatusId)
                await _onboardingRepository.UpdatePlanStatusAsync(
                    plan.OnboardingPlanId, _planInProgressStatusId, uow);

            uow.Commit();
        }
        catch
        {
            uow.Rollback();
            throw;
        }

        await CheckAndAdvancePlanStatusAsync(plan, waivedBy);
    }

    public Task<OnboardingPlan?> GetPlanByEmploymentIdAsync(Guid employmentId)
        => _onboardingRepository.GetPlanByEmploymentIdAsync(employmentId);

    public Task<IEnumerable<OnboardingTask>> GetTasksByPlanIdAsync(Guid planId)
        => _onboardingRepository.GetTasksByPlanIdAsync(planId);

    // -----------------------------------------------------------------------

    private async Task CheckAndAdvancePlanStatusAsync(OnboardingPlan plan, Guid actor)
    {
        var allTasks = (await _onboardingRepository.GetTasksByPlanIdAsync(
            plan.OnboardingPlanId)).ToList();

        var blockingDone = allTasks
            .Where(t => t.BlockingFlag)
            .All(t => t.TaskStatusId == _taskCompletedStatusId
                   || t.TaskStatusId == _taskWaivedStatusId);

        var allDone = allTasks
            .All(t => t.TaskStatusId == _taskCompletedStatusId
                   || t.TaskStatusId == _taskWaivedStatusId);

        if (!blockingDone) return;

        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            if (allDone)
            {
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                await _onboardingRepository.UpdatePlanStatusAsync(
                    plan.OnboardingPlanId, _planCompleteStatusId, uow);
                await _onboardingRepository.SetPlanCompletionDateAsync(
                    plan.OnboardingPlanId, today, uow);
            }
            else if (plan.PlanStatusId != _planBlockingCompleteStatusId
                  && plan.PlanStatusId != _planCompleteStatusId)
            {
                await _onboardingRepository.UpdatePlanStatusAsync(
                    plan.OnboardingPlanId, _planBlockingCompleteStatusId, uow);
            }

            uow.Commit();
        }
        catch
        {
            uow.Rollback();
            throw;
        }

        if (blockingDone && plan.PlanStatusId != _planBlockingCompleteStatusId
                         && plan.PlanStatusId != _planCompleteStatusId)
        {
            await _eventPublisher.PublishAsync(new OnboardingBlockingTasksCompletePayload
            {
                OnboardingPlanId = plan.OnboardingPlanId,
                EmploymentId     = plan.EmploymentId,
                TenantId         = Guid.Empty,
                EventTimestamp   = DateTimeOffset.UtcNow
            });
        }
    }

    private static List<TaskDef> BuildDefaultTaskDefs(DateOnly startDate)
    {
        return
        [
            new("DOCUMENT_COMPLETION", "Complete I-9 (Employment Eligibility Verification)",
                "Employee",    AddBusinessDays(startDate, 3),  IsBlocking: true),
            new("DOCUMENT_COMPLETION", "Complete W-4 (Federal Tax Withholding)",
                "Employee",    startDate,                       IsBlocking: true),
            new("PAYROLL_PROFILE_SETUP", "Set Up Payroll Profile",
                "HrisAdmin",   AddBusinessDays(startDate, -5), IsBlocking: true),
            new("IT_PROVISIONING", "Provision IT Access",
                "HrisAdmin",   AddBusinessDays(startDate, -2), IsBlocking: false),
            new("EQUIPMENT_REQUEST", "Request Equipment",
                "HrisAdmin",   AddBusinessDays(startDate, -2), IsBlocking: false),
            new("MANAGER_INTRODUCTION", "Manager Introduction",
                "Manager",     startDate,                       IsBlocking: false),
            new("BENEFITS_ENROLLMENT", "Complete Benefits Enrollment",
                "Employee",    startDate.AddDays(30),           IsBlocking: false),
            new("TRAINING_ASSIGNMENT", "Complete Required Training",
                "Employee",    startDate.AddDays(14),           IsBlocking: false),
        ];
    }

    private static DateOnly AddBusinessDays(DateOnly date, int days)
    {
        if (days == 0) return date;
        var direction = days < 0 ? -1 : 1;
        var remaining = Math.Abs(days);
        var current   = date;
        while (remaining > 0)
        {
            current = current.AddDays(direction);
            if (current.DayOfWeek != DayOfWeek.Saturday
             && current.DayOfWeek != DayOfWeek.Sunday)
                remaining--;
        }
        return current;
    }

    private sealed record TaskDef(
        string   TypeCode,
        string   TaskName,
        string   OwnerRole,
        DateOnly DueDate,
        bool     IsBlocking)
    {
        public Guid TaskId { get; } = Guid.NewGuid();
    }
}
