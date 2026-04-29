using Dapper;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Host.Hris.Domain;

namespace AllWorkHRIS.Host.Hris.Repositories;

public interface IWorkQueueRepository
{
    Task<WorkQueueItem?>              GetByIdAsync(Guid itemId);
    Task<WorkQueueItem?>              GetOpenByReferenceAsync(Guid referenceId, string itemType);
    Task<IEnumerable<WorkQueueItem>>  GetOpenByRoleAsync(string role, Guid? employmentId = null);
    Task<Guid>                        InsertAsync(WorkQueueItem item);
    Task                              UpdatePriorityAsync(Guid itemId, string priority);
    Task                              ResolveAsync(Guid itemId, Guid resolvedBy);
    Task                              ResolveByReferenceAsync(Guid referenceId, Guid resolvedBy);
}

public sealed class WorkQueueRepository : IWorkQueueRepository
{
    private readonly IConnectionFactory _connectionFactory;

    public WorkQueueRepository(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public async Task<WorkQueueItem?> GetByIdAsync(Guid itemId)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<WorkQueueItem>(
            "SELECT * FROM work_queue_item WHERE work_queue_item_id = @Id",
            new { Id = itemId });
    }

    public async Task<WorkQueueItem?> GetOpenByReferenceAsync(Guid referenceId, string itemType)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<WorkQueueItem>(
            @"SELECT * FROM work_queue_item
              WHERE reference_id = @RefId AND item_type = @Type AND status = 'OPEN'
              LIMIT 1",
            new { RefId = referenceId, Type = itemType });
    }

    public async Task<IEnumerable<WorkQueueItem>> GetOpenByRoleAsync(string role,
        Guid? employmentId = null)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<WorkQueueItem>(
            @"SELECT * FROM work_queue_item
              WHERE assigned_role = @Role AND status = 'OPEN'
                AND (@EmploymentId IS NULL OR employment_id = @EmploymentId)
              ORDER BY
                CASE priority WHEN 'HOLD' THEN 1 WHEN 'HIGH' THEN 2 ELSE 3 END,
                created_at",
            new { Role = role, EmploymentId = employmentId });
    }

    public async Task<Guid> InsertAsync(WorkQueueItem item)
    {
        using var conn = _connectionFactory.CreateConnection();
        const string sql =
            @"INSERT INTO work_queue_item (
                work_queue_item_id, item_type, reference_id, reference_type,
                employment_id, assigned_role, status, priority, title,
                description, due_date, created_at)
              VALUES (
                @WorkQueueItemId, @ItemType, @ReferenceId, @ReferenceType,
                @EmploymentId, @AssignedRole, @Status, @Priority, @Title,
                @Description, @DueDate, @CreatedAt)
              RETURNING work_queue_item_id";

        return await conn.ExecuteScalarAsync<Guid>(sql,
            new
            {
                item.WorkQueueItemId,
                item.ItemType,
                item.ReferenceId,
                item.ReferenceType,
                item.EmploymentId,
                item.AssignedRole,
                item.Status,
                item.Priority,
                item.Title,
                item.Description,
                DueDate    = item.DueDate?.ToDateTime(TimeOnly.MinValue),
                CreatedAt  = item.CreatedAt
            });
    }

    public async Task UpdatePriorityAsync(Guid itemId, string priority)
    {
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE work_queue_item SET priority = @Priority WHERE work_queue_item_id = @Id",
            new { Id = itemId, Priority = priority });
    }

    public async Task ResolveAsync(Guid itemId, Guid resolvedBy)
    {
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            @"UPDATE work_queue_item
              SET status = 'RESOLVED', resolved_at = now(), resolved_by = @ResolvedBy
              WHERE work_queue_item_id = @Id",
            new { Id = itemId, ResolvedBy = resolvedBy });
    }

    public async Task ResolveByReferenceAsync(Guid referenceId, Guid resolvedBy)
    {
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            @"UPDATE work_queue_item
              SET status = 'RESOLVED', resolved_at = now(), resolved_by = @ResolvedBy
              WHERE reference_id = @RefId AND status = 'OPEN'",
            new { RefId = referenceId, ResolvedBy = resolvedBy });
    }
}
