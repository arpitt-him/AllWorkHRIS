using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.Reporting.Domain;
using Dapper;

namespace AllWorkHRIS.Module.Reporting.Repositories;

public sealed class ReportHistoryRepository : IReportHistoryRepository
{
    private readonly IConnectionFactory _connectionFactory;

    public ReportHistoryRepository(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public async Task<Guid> InsertAsync(ReportExecutionHistory e, IUnitOfWork uow)
    {
        const string sql = """
            INSERT INTO report_execution_history (
                execution_id, report_id, report_title, requested_by,
                execution_status, parameters_json,
                row_count, export_format, storage_reference, async_job_id,
                started_at, completed_at, error_message,
                created_timestamp, last_update_timestamp)
            VALUES (
                @ExecutionId, @ReportId, @ReportTitle, @RequestedBy,
                @ExecutionStatus, @ParametersJson,
                @RowCount, @ExportFormat, @StorageReference, @AsyncJobId,
                @StartedAt, @CompletedAt, @ErrorMessage,
                @CreatedTimestamp, @LastUpdateTimestamp)
            """;

        await uow.Connection.ExecuteAsync(sql, new
        {
            e.ExecutionId,
            e.ReportId,
            e.ReportTitle,
            e.RequestedBy,
            e.ExecutionStatus,
            e.ParametersJson,
            RowCount         = (object?)e.RowCount         ?? DBNull.Value,
            ExportFormat     = (object?)e.ExportFormat     ?? DBNull.Value,
            StorageReference = (object?)e.StorageReference ?? DBNull.Value,
            AsyncJobId       = (object?)e.AsyncJobId       ?? DBNull.Value,
            e.StartedAt,
            CompletedAt      = (object?)e.CompletedAt      ?? DBNull.Value,
            ErrorMessage     = (object?)e.ErrorMessage     ?? DBNull.Value,
            e.CreatedTimestamp,
            e.LastUpdateTimestamp
        }, uow.Transaction);

        return e.ExecutionId;
    }

    public async Task UpdateCompletedAsync(Guid executionId, int rowCount, string? storageReference, DateTime completedAt, IUnitOfWork uow)
    {
        const string sql = """
            UPDATE report_execution_history
            SET    execution_status      = @Status,
                   row_count             = @RowCount,
                   storage_reference     = @StorageReference,
                   completed_at          = @CompletedAt,
                   last_update_timestamp = @CompletedAt
            WHERE  execution_id          = @ExecutionId
            """;

        await uow.Connection.ExecuteAsync(sql, new
        {
            ExecutionId      = executionId,
            Status           = ReportExecutionStatus.Completed,
            RowCount         = rowCount,
            StorageReference = (object?)storageReference ?? DBNull.Value,
            CompletedAt      = completedAt
        }, uow.Transaction);
    }

    public async Task UpdateFailedAsync(Guid executionId, string errorMessage, DateTime completedAt, IUnitOfWork uow)
    {
        const string sql = """
            UPDATE report_execution_history
            SET    execution_status      = @Status,
                   error_message         = @ErrorMessage,
                   completed_at          = @CompletedAt,
                   last_update_timestamp = @CompletedAt
            WHERE  execution_id          = @ExecutionId
            """;

        await uow.Connection.ExecuteAsync(sql, new
        {
            ExecutionId  = executionId,
            Status       = ReportExecutionStatus.Failed,
            ErrorMessage = errorMessage,
            CompletedAt  = completedAt
        }, uow.Transaction);
    }

    public async Task UpdateAsyncCompletedAsync(Guid executionId, Guid jobId, int rowCount, string? storageReference, DateTime completedAt, IUnitOfWork uow)
    {
        const string sql = """
            UPDATE report_execution_history
            SET    execution_status      = @Status,
                   async_job_id          = @JobId,
                   row_count             = @RowCount,
                   storage_reference     = @StorageReference,
                   completed_at          = @CompletedAt,
                   last_update_timestamp = @CompletedAt
            WHERE  execution_id          = @ExecutionId
            """;

        await uow.Connection.ExecuteAsync(sql, new
        {
            ExecutionId      = executionId,
            Status           = ReportExecutionStatus.Completed,
            JobId            = jobId,
            RowCount         = rowCount,
            StorageReference = (object?)storageReference ?? DBNull.Value,
            CompletedAt      = completedAt
        }, uow.Transaction);
    }

    public async Task<ReportExecutionHistory?> GetByIdAsync(Guid executionId)
    {
        const string sql = $"""
            {SelectBase}
            WHERE  execution_id = @ExecutionId
            """;

        using var conn = _connectionFactory.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<ReportExecutionHistory>(sql,
            new { ExecutionId = executionId });
    }

    public async Task<IEnumerable<ReportExecutionHistory>> GetRecentByUserAsync(Guid requestedBy, int count = 20)
    {
        const string sql = $"""
            {SelectBase}
            WHERE  requested_by = @RequestedBy
            ORDER BY started_at DESC
            FETCH FIRST @Count ROWS ONLY
            """;

        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<ReportExecutionHistory>(sql,
            new { RequestedBy = requestedBy, Count = count });
    }

    public async Task<IEnumerable<ReportExecutionHistory>> GetRecentByUserAndReportAsync(Guid requestedBy, string reportId, int count = 5)
    {
        const string sql = $"""
            {SelectBase}
            WHERE  requested_by = @RequestedBy
              AND  report_id    = @ReportId
            ORDER BY started_at DESC
            FETCH FIRST @Count ROWS ONLY
            """;

        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<ReportExecutionHistory>(sql,
            new { RequestedBy = requestedBy, ReportId = reportId, Count = count });
    }

    public async Task<IEnumerable<ReportExecutionHistory>> GetByReportAndDateRangeAsync(string reportId, DateOnly from, DateOnly to)
    {
        const string sql = $"""
            {SelectBase}
            WHERE  report_id  = @ReportId
              AND  started_at >= @From
              AND  started_at <  @ToExclusive
            ORDER BY started_at DESC
            """;

        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<ReportExecutionHistory>(sql, new
        {
            ReportId    = reportId,
            From        = from.ToDateTime(TimeOnly.MinValue),
            ToExclusive = to.AddDays(1).ToDateTime(TimeOnly.MinValue)
        });
    }

    public async Task<IEnumerable<ReportExecutionHistory>> GetByUserAndDateRangeAsync(Guid requestedBy, DateOnly from, DateOnly to)
    {
        const string sql = $"""
            {SelectBase}
            WHERE  requested_by = @RequestedBy
              AND  started_at  >= @From
              AND  started_at  <  @ToExclusive
            ORDER BY started_at DESC
            """;

        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<ReportExecutionHistory>(sql, new
        {
            RequestedBy = requestedBy,
            From        = from.ToDateTime(TimeOnly.MinValue),
            ToExclusive = to.AddDays(1).ToDateTime(TimeOnly.MinValue)
        });
    }

    private const string SelectBase = """
        SELECT execution_id           AS ExecutionId,
               report_id              AS ReportId,
               report_title           AS ReportTitle,
               requested_by           AS RequestedBy,
               execution_status       AS ExecutionStatus,
               parameters_json        AS ParametersJson,
               row_count              AS RowCount,
               export_format          AS ExportFormat,
               storage_reference      AS StorageReference,
               async_job_id           AS AsyncJobId,
               started_at             AS StartedAt,
               completed_at           AS CompletedAt,
               error_message          AS ErrorMessage,
               created_timestamp      AS CreatedTimestamp,
               last_update_timestamp  AS LastUpdateTimestamp
        FROM   report_execution_history
        """;
}
