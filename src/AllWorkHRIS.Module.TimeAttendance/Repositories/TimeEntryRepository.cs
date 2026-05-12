using Dapper;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Lookups;
using AllWorkHRIS.Module.TimeAttendance.Domain;

namespace AllWorkHRIS.Module.TimeAttendance.Repositories;

public sealed class TimeEntryRepository : ITimeEntryRepository
{
    private readonly IConnectionFactory _connectionFactory;
    private readonly ILookupCache       _lookupCache;

    public TimeEntryRepository(IConnectionFactory connectionFactory, ILookupCache lookupCache)
    {
        _connectionFactory = connectionFactory;
        _lookupCache       = lookupCache;
    }

    private const string SelectBase = """
        SELECT
            te.time_entry_id, te.employment_id, te.payroll_period_id,
            te.work_date, te.time_category_id, te.duration,
            te.start_time, te.end_time, te.shift_id,
            te.status_id, te.entry_method_id,
            te.submitted_by, te.submitted_at,
            te.approved_by, te.approved_at, te.rejection_reason,
            te.original_time_entry_id, te.correction_reason, te.retroactive_flag,
            te.payroll_run_id, te.notes, te.project_code, te.task_code,
            te.created_at, te.updated_at,
            s.code  AS status_code,
            c.code  AS time_category_code,
            m.code  AS entry_method_code
        FROM time_entry te
        JOIN lkp_time_entry_status s ON s.id = te.status_id
        JOIN lkp_time_category     c ON c.id = te.time_category_id
        JOIN lkp_entry_method      m ON m.id = te.entry_method_id
        """;

    public async Task<TimeEntry?> GetByIdAsync(Guid timeEntryId)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<TimeEntry>(
            $"{SelectBase} WHERE te.time_entry_id = @TimeEntryId",
            new { TimeEntryId = timeEntryId });
    }

    public async Task<IEnumerable<TimeEntry>> GetByEmploymentAndPeriodAsync(
        Guid employmentId, Guid payrollPeriodId)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<TimeEntry>(
            $"{SelectBase} WHERE te.employment_id = @EmploymentId AND te.payroll_period_id = @PeriodId ORDER BY te.work_date",
            new { EmploymentId = employmentId, PeriodId = payrollPeriodId });
    }

    public async Task<IEnumerable<TimeEntry>> GetPendingApprovalByManagerAsync(
        Guid managerEmploymentId, Guid payrollPeriodId)
    {
        using var conn = _connectionFactory.CreateConnection();
        const string sql = """
            SELECT DISTINCT
                te.time_entry_id, te.employment_id, te.payroll_period_id,
                te.work_date, te.time_category_id, te.duration,
                te.start_time, te.end_time, te.shift_id,
                te.status_id, te.entry_method_id,
                te.submitted_by, te.submitted_at,
                te.approved_by, te.approved_at, te.rejection_reason,
                te.original_time_entry_id, te.correction_reason, te.retroactive_flag,
                te.payroll_run_id, te.notes, te.project_code, te.task_code,
            te.created_at, te.updated_at,
                s.code AS status_code,
                c.code AS time_category_code,
                m.code AS entry_method_code
            FROM time_entry te
            JOIN lkp_time_entry_status s ON s.id = te.status_id
            JOIN lkp_time_category     c ON c.id = te.time_category_id
            JOIN lkp_entry_method      m ON m.id = te.entry_method_id
            JOIN assignment a ON a.employment_id = te.employment_id
                              AND a.manager_employment_id = @ManagerId
            WHERE te.payroll_period_id = @PeriodId
              AND s.code IN ('SUBMITTED', 'CORRECTED')
            ORDER BY te.work_date
            """;
        return await conn.QueryAsync<TimeEntry>(sql,
            new { ManagerId = managerEmploymentId, PeriodId = payrollPeriodId });
    }

    public async Task<IEnumerable<TimeEntry>> GetApprovedForHandoffAsync(Guid payrollPeriodId)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<TimeEntry>(
            $"{SelectBase} WHERE te.payroll_period_id = @PeriodId AND s.code = 'APPROVED' ORDER BY te.employment_id, te.work_date",
            new { PeriodId = payrollPeriodId });
    }

    public async Task<IEnumerable<TimeEntry>> GetWorkweekEntriesAsync(
        Guid employmentId, DateOnly weekStart)
    {
        var weekEnd = weekStart.AddDays(6);
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<TimeEntry>(
            $"{SelectBase} WHERE te.employment_id = @EmploymentId AND te.work_date >= @WeekStart AND te.work_date <= @WeekEnd ORDER BY te.work_date",
            new
            {
                EmploymentId = employmentId,
                WeekStart    = weekStart.ToDateTime(TimeOnly.MinValue),
                WeekEnd      = weekEnd.ToDateTime(TimeOnly.MinValue)
            });
    }

    public async Task<IEnumerable<TimeEntry>> GetOpenByEmploymentAsync(Guid employmentId)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<TimeEntry>(
            $"{SelectBase} WHERE te.employment_id = @EmploymentId AND s.code IN ('DRAFT','SUBMITTED') ORDER BY te.work_date",
            new { EmploymentId = employmentId });
    }

    public async Task<Guid> InsertAsync(TimeEntry entry, IUnitOfWork uow)
    {
        const string sql = """
            INSERT INTO time_entry (
                time_entry_id, employment_id, payroll_period_id, work_date,
                time_category_id, duration, start_time, end_time, shift_id,
                status_id, entry_method_id, submitted_by, submitted_at,
                original_time_entry_id, correction_reason, retroactive_flag, notes,
                project_code, task_code,
                created_at, updated_at)
            VALUES (
                @TimeEntryId, @EmploymentId, @PayrollPeriodId, @WorkDate,
                @TimeCategoryId, @Duration, @StartTime, @EndTime, @ShiftId,
                @StatusId, @EntryMethodId, @SubmittedBy, @SubmittedAt,
                @OriginalTimeEntryId, @CorrectionReason, @RetroactiveFlag, @Notes,
                @ProjectCode, @TaskCode,
                @CreatedAt, @UpdatedAt)
            """;

        await uow.Connection.ExecuteAsync(sql, new
        {
            entry.TimeEntryId,
            entry.EmploymentId,
            entry.PayrollPeriodId,
            WorkDate              = entry.WorkDate.ToDateTime(TimeOnly.MinValue),
            entry.TimeCategoryId,
            entry.Duration,
            StartTime             = entry.StartTime.HasValue ? (object)entry.StartTime.Value.ToTimeSpan() : DBNull.Value,
            EndTime               = entry.EndTime.HasValue   ? (object)entry.EndTime.Value.ToTimeSpan()   : DBNull.Value,
            ShiftId               = (object?)entry.ShiftId ?? DBNull.Value,
            entry.StatusId,
            entry.EntryMethodId,
            entry.SubmittedBy,
            entry.SubmittedAt,
            OriginalTimeEntryId   = (object?)entry.OriginalTimeEntryId ?? DBNull.Value,
            CorrectionReason      = (object?)entry.CorrectionReason    ?? DBNull.Value,
            entry.RetroactiveFlag,
            Notes                 = (object?)entry.Notes       ?? DBNull.Value,
            ProjectCode           = (object?)entry.ProjectCode ?? DBNull.Value,
            TaskCode              = (object?)entry.TaskCode    ?? DBNull.Value,
            entry.CreatedAt,
            UpdatedAt             = entry.CreatedAt
        }, uow.Transaction);

        return entry.TimeEntryId;
    }

    public async Task UpdateStatusAsync(
        Guid timeEntryId, string status, Guid actorId, IUnitOfWork uow)
    {
        var statusId = _lookupCache.GetId(TimeAttendanceLookupTables.TimeEntryStatus, status);
        const string sql = """
            UPDATE time_entry
            SET    status_id  = @StatusId,
                   approved_by = CASE WHEN @Status = 'APPROVED' THEN @ActorId ELSE approved_by END,
                   approved_at = CASE WHEN @Status = 'APPROVED' THEN @Now      ELSE approved_at END,
                   rejection_reason = CASE WHEN @Status = 'REJECTED' THEN @Reason ELSE rejection_reason END,
                   updated_at = @Now
            WHERE  time_entry_id = @TimeEntryId
            """;
        await uow.Connection.ExecuteAsync(sql, new
        {
            StatusId    = statusId,
            Status      = status,
            ActorId     = actorId,
            Reason      = (object?)null,
            Now         = DateTimeOffset.UtcNow,
            TimeEntryId = timeEntryId
        }, uow.Transaction);
    }

    public async Task UpdateStatusWithReasonAsync(
        Guid timeEntryId, string status, Guid actorId, string reason, IUnitOfWork uow)
    {
        var statusId = _lookupCache.GetId(TimeAttendanceLookupTables.TimeEntryStatus, status);
        const string sql = """
            UPDATE time_entry
            SET    status_id        = @StatusId,
                   rejection_reason = @Reason,
                   updated_at       = @Now
            WHERE  time_entry_id    = @TimeEntryId
            """;
        await uow.Connection.ExecuteAsync(sql, new
        {
            StatusId    = statusId,
            Reason      = reason,
            Now         = DateTimeOffset.UtcNow,
            TimeEntryId = timeEntryId
        }, uow.Transaction);
    }

    public async Task LockAsync(Guid timeEntryId, Guid payrollRunId, IUnitOfWork uow)
    {
        var lockedId = _lookupCache.GetId(TimeAttendanceLookupTables.TimeEntryStatus, "LOCKED");
        const string sql = """
            UPDATE time_entry
            SET    status_id      = @StatusId,
                   payroll_run_id = @PayrollRunId,
                   updated_at     = @Now
            WHERE  time_entry_id  = @TimeEntryId
            """;
        await uow.Connection.ExecuteAsync(sql, new
        {
            StatusId      = lockedId,
            PayrollRunId  = payrollRunId,
            Now           = DateTimeOffset.UtcNow,
            TimeEntryId   = timeEntryId
        }, uow.Transaction);
    }

    public async Task ReclassifyAsync(Guid timeEntryId, string timeCategory, IUnitOfWork uow)
    {
        var categoryId = _lookupCache.GetId(TimeAttendanceLookupTables.TimeCategory, timeCategory);
        const string sql = """
            UPDATE time_entry
            SET    time_category_id = @CategoryId,
                   updated_at       = @Now
            WHERE  time_entry_id    = @TimeEntryId
            """;
        await uow.Connection.ExecuteAsync(sql, new
        {
            CategoryId  = categoryId,
            Now         = DateTimeOffset.UtcNow,
            TimeEntryId = timeEntryId
        }, uow.Transaction);
    }

    public async Task<bool> EmploymentExistsAsync(Guid employmentId)
    {
        using var conn = _connectionFactory.CreateConnection();
        var count = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM employment WHERE employment_id = @EmploymentId",
            new { EmploymentId = employmentId });
        return count > 0;
    }

    public async Task<string?> GetPeriodStatusAsync(Guid payrollPeriodId)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.ExecuteScalarAsync<string?>(
            "SELECT pp.calendar_status FROM payroll_period pp WHERE pp.period_id = @PeriodId",
            new { PeriodId = payrollPeriodId });
    }

    public async Task<string?> GetFlsaStatusAsync(Guid employmentId)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.ExecuteScalarAsync<string?>(
            """
            SELECT lfs.code
            FROM   employment    emp
            JOIN   lkp_flsa_status lfs ON lfs.id = emp.flsa_status_id
            WHERE  emp.employment_id = @EmploymentId
            """,
            new { EmploymentId = employmentId });
    }

    public async Task<bool> IsCategoryWorkedTimeAsync(string categoryCode)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.ExecuteScalarAsync<bool>(
            "SELECT is_worked_time FROM lkp_time_category WHERE code = @Code",
            new { Code = categoryCode });
    }

    public async Task<IReadOnlyList<(DateOnly WorkDate, decimal Hours)>> GetApprovedHoursByEmploymentAndPeriodAsync(
        Guid employmentId, DateOnly periodStart, DateOnly periodEnd)
    {
        const string sql = """
            SELECT te.work_date, SUM(te.duration) AS hours
            FROM   time_entry          te
            JOIN   lkp_time_entry_status s ON s.id = te.status_id
            WHERE  te.employment_id = @EmploymentId
              AND  te.work_date >= @PeriodStart
              AND  te.work_date <= @PeriodEnd
              AND  s.code IN ('APPROVED', 'LOCKED')
            GROUP BY te.work_date
            ORDER BY te.work_date
            """;

        using var conn = _connectionFactory.CreateConnection();
        var rows = await conn.QueryAsync<WorkedDayRow>(sql, new
        {
            EmploymentId = employmentId,
            PeriodStart  = periodStart.ToDateTime(TimeOnly.MinValue),
            PeriodEnd    = periodEnd.ToDateTime(TimeOnly.MinValue)
        });

        return rows.Select(r => (r.WorkDate, r.Hours)).ToList();
    }

    private sealed record WorkedDayRow
    {
        public DateOnly WorkDate { get; init; }
        public decimal  Hours    { get; init; }
    }
}
