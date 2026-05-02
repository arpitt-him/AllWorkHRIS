using Dapper;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Host.Hris.Domain;

namespace AllWorkHRIS.Host.Hris.Repositories;

public interface IOnboardingRepository
{
    Task<OnboardingPlan?>             GetPlanByIdAsync(Guid planId);
    Task<OnboardingPlan?>             GetPlanByEmploymentIdAsync(Guid employmentId);
    Task<OnboardingTask?>             GetTaskByIdAsync(Guid taskId);
    Task<IEnumerable<OnboardingTask>> GetTasksByPlanIdAsync(Guid planId);
    Task<Guid>                        InsertPlanAsync(OnboardingPlan plan, IUnitOfWork uow);
    Task                              InsertTaskAsync(OnboardingTask task, IUnitOfWork uow);
    Task                              UpdatePlanStatusAsync(Guid planId, int statusId, IUnitOfWork uow);
    Task                              SetPlanCompletionDateAsync(Guid planId, DateOnly completionDate,
                                          IUnitOfWork uow);
    Task                              UpdateTaskStatusAsync(Guid taskId, int statusId,
                                          DateOnly? completionDate, IUnitOfWork uow);
    Task                              SetTaskWaivedAsync(Guid taskId, string reason, Guid waivedBy,
                                          IUnitOfWork uow);
}

public sealed class OnboardingRepository : IOnboardingRepository
{
    private readonly IConnectionFactory _connectionFactory;

    public OnboardingRepository(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public async Task<OnboardingPlan?> GetPlanByIdAsync(Guid planId)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<OnboardingPlan>(
            "SELECT * FROM onboarding_plan WHERE onboarding_plan_id = @Id",
            new { Id = planId });
    }

    public async Task<OnboardingPlan?> GetPlanByEmploymentIdAsync(Guid employmentId)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<OnboardingPlan>(
            "SELECT * FROM onboarding_plan WHERE employment_id = @Id ORDER BY creation_timestamp DESC LIMIT 1",
            new { Id = employmentId });
    }

    public async Task<OnboardingTask?> GetTaskByIdAsync(Guid taskId)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<OnboardingTask>(
            "SELECT * FROM onboarding_task WHERE task_id = @Id",
            new { Id = taskId });
    }

    public async Task<IEnumerable<OnboardingTask>> GetTasksByPlanIdAsync(Guid planId)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<OnboardingTask>(
            "SELECT * FROM onboarding_task WHERE onboarding_plan_id = @Id ORDER BY due_date",
            new { Id = planId });
    }

    public async Task<Guid> InsertPlanAsync(OnboardingPlan plan, IUnitOfWork uow)
    {
        const string sql =
            @"INSERT INTO onboarding_plan (
                onboarding_plan_id, employment_id, plan_template_id, plan_status_id,
                target_start_date, assigned_hr_contact_id,
                created_by, creation_timestamp, last_updated_by, last_update_timestamp)
              VALUES (
                @OnboardingPlanId, @EmploymentId, @PlanTemplateId, @PlanStatusId,
                @TargetStartDate, @AssignedHrContactId,
                @CreatedBy, @CreationTimestamp, @LastUpdatedBy, @LastUpdateTimestamp)
              RETURNING onboarding_plan_id";

        return await uow.Connection.ExecuteScalarAsync<Guid>(sql,
            new
            {
                plan.OnboardingPlanId,
                plan.EmploymentId,
                plan.PlanTemplateId,
                plan.PlanStatusId,
                TargetStartDate      = plan.TargetStartDate.ToDateTime(TimeOnly.MinValue),
                plan.AssignedHrContactId,
                plan.CreatedBy,
                plan.CreationTimestamp,
                plan.LastUpdatedBy,
                plan.LastUpdateTimestamp
            },
            uow.Transaction);
    }

    public async Task InsertTaskAsync(OnboardingTask task, IUnitOfWork uow)
    {
        const string sql =
            @"INSERT INTO onboarding_task (
                task_id, onboarding_plan_id, task_type_id, task_name,
                task_owner_role, task_owner_user_id, due_date,
                task_status_id, blocking_flag, created_by, creation_timestamp)
              VALUES (
                @TaskId, @OnboardingPlanId, @TaskTypeId, @TaskName,
                @TaskOwnerRole, @TaskOwnerUserId, @DueDate,
                @TaskStatusId, @BlockingFlag, @CreatedBy, @CreationTimestamp)";

        await uow.Connection.ExecuteAsync(sql,
            new
            {
                task.TaskId,
                task.OnboardingPlanId,
                task.TaskTypeId,
                task.TaskName,
                task.TaskOwnerRole,
                task.TaskOwnerUserId,
                DueDate           = task.DueDate.ToDateTime(TimeOnly.MinValue),
                task.TaskStatusId,
                task.BlockingFlag,
                task.CreatedBy,
                task.CreationTimestamp
            },
            uow.Transaction);
    }

    public async Task UpdatePlanStatusAsync(Guid planId, int statusId, IUnitOfWork uow)
    {
        await uow.Connection.ExecuteAsync(
            @"UPDATE onboarding_plan SET plan_status_id = @StatusId,
              last_update_timestamp = CURRENT_TIMESTAMP WHERE onboarding_plan_id = @Id",
            new { Id = planId, StatusId = statusId },
            uow.Transaction);
    }

    public async Task SetPlanCompletionDateAsync(Guid planId, DateOnly completionDate,
        IUnitOfWork uow)
    {
        await uow.Connection.ExecuteAsync(
            @"UPDATE onboarding_plan
              SET completion_date = @CompletionDate, last_update_timestamp = CURRENT_TIMESTAMP
              WHERE onboarding_plan_id = @Id",
            new { Id = planId, CompletionDate = completionDate.ToDateTime(TimeOnly.MinValue) },
            uow.Transaction);
    }

    public async Task UpdateTaskStatusAsync(Guid taskId, int statusId,
        DateOnly? completionDate, IUnitOfWork uow)
    {
        await uow.Connection.ExecuteAsync(
            @"UPDATE onboarding_task
              SET task_status_id = @StatusId,
                  completion_date = @CompletionDate
              WHERE task_id = @Id",
            new
            {
                Id             = taskId,
                StatusId       = statusId,
                CompletionDate = completionDate?.ToDateTime(TimeOnly.MinValue)
            },
            uow.Transaction);
    }

    public async Task SetTaskWaivedAsync(Guid taskId, string reason, Guid waivedBy,
        IUnitOfWork uow)
    {
        await uow.Connection.ExecuteAsync(
            @"UPDATE onboarding_task
              SET waiver_reason = @Reason, waived_by = @WaivedBy
              WHERE task_id = @Id",
            new { Id = taskId, Reason = reason, WaivedBy = waivedBy },
            uow.Transaction);
    }
}
