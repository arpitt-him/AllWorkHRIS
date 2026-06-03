using Dapper;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Lookups;
using AllWorkHRIS.Core.Temporal;
using AllWorkHRIS.Module.TimeAttendance.Domain;

namespace AllWorkHRIS.Module.TimeAttendance.Repositories;

public sealed class TimeEntryRepository : ITimeEntryRepository
{
    private readonly IConnectionFactory _connectionFactory;
    private readonly ILookupCache       _lookupCache;
    private readonly ITemporalContext   _temporal;

    public TimeEntryRepository(IConnectionFactory connectionFactory, ILookupCache lookupCache, ITemporalContext temporal)
    {
        _connectionFactory = connectionFactory;
        _lookupCache       = lookupCache;
        _temporal          = temporal;
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
            Now         = _temporal.GetOperativeNow(),
            TimeEntryId = timeEntryId
        }, uow.Transaction);
    }

    public async Task<int> ApproveEntriesAsync(IReadOnlyList<Guid> timeEntryIds, Guid approvedBy, IUnitOfWork uow)
    {
        // Phase 12.7b — batch approve: SUBMITTED/CORRECTED → APPROVED for the given ids in one
        // statement. Ids not in an approvable state are silently skipped (count reflects actual
        // transitions). Backs the per-employee "Approve All", period bulk, and approve-by-exception.
        if (timeEntryIds.Count == 0) return 0;

        var approvedId  = _lookupCache.GetId(TimeAttendanceLookupTables.TimeEntryStatus, "APPROVED");
        var submittedId = _lookupCache.GetId(TimeAttendanceLookupTables.TimeEntryStatus, "SUBMITTED");
        var correctedId = _lookupCache.GetId(TimeAttendanceLookupTables.TimeEntryStatus, "CORRECTED");

        var p = new DynamicParameters();
        p.Add("ApprovedId",  approvedId);
        p.Add("ApprovedBy",  approvedBy);
        p.Add("Now",         _temporal.GetOperativeNow());
        p.Add("SubmittedId", submittedId);
        p.Add("CorrectedId", correctedId);
        var inIds = BuildInClause(p, "Id", timeEntryIds);

        var sql = $"""
            UPDATE time_entry
            SET    status_id   = @ApprovedId,
                   approved_by = @ApprovedBy,
                   approved_at = @Now,
                   updated_at  = @Now
            WHERE  time_entry_id IN {inIds}
              AND  status_id     IN (@SubmittedId, @CorrectedId)
            """;
        return await uow.Connection.ExecuteAsync(sql, p, uow.Transaction);
    }

    public async Task<bool> GetAutoApproveImportedTimeAsync(Guid legalEntityId)
    {
        // Phase 12.7b — per-LE config: does this legal entity auto-approve IMPORT-method time?
        using var conn = _connectionFactory.CreateConnection();
        return await conn.ExecuteScalarAsync<bool>(
            "SELECT COALESCE(auto_approve_imported_time, FALSE) FROM org_unit WHERE org_unit_id = @LegalEntityId",
            new { LegalEntityId = legalEntityId });
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
            Now         = _temporal.GetOperativeNow(),
            TimeEntryId = timeEntryId
        }, uow.Transaction);
    }

    public async Task LockAsync(Guid timeEntryId, Guid payrollRunId, DateTimeOffset lockedAt, IUnitOfWork uow)
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
            Now           = lockedAt,
            TimeEntryId   = timeEntryId
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
        Guid employmentId, DateOnly periodStart, DateOnly periodEnd, Guid payrollRunId,
        IReadOnlyCollection<int> otEligibleCategoryIds)
    {
        // Phase 12.7: a run consumes hours that are APPROVED (not yet committed to any run) or
        // already LOCKED to *this* run — but never hours LOCKED to a *different* run, so a second
        // run over the same date range can't re-sum hours an earlier approved run already took.
        // Phase 12.12 / ADR-023: only OT-ELIGIBLE hours count toward the FLSA OT threshold; paid
        // leave is summed separately (see below) and paid at straight time.
        // Phase 12.13.4 / ADR-024: the OT basis is membership in the resolved per-context eligible
        // set; an empty set falls back to the is_worked_time flag (today's behavior).
        var p = new DynamicParameters();
        p.Add("EmploymentId", employmentId);
        p.Add("PeriodStart",  periodStart.ToDateTime(TimeOnly.MinValue));
        p.Add("PeriodEnd",    periodEnd.ToDateTime(TimeOnly.MinValue));
        p.Add("PayrollRunId", payrollRunId);
        var eligiblePredicate = otEligibleCategoryIds.Count > 0
            ? $"c.id IN {SqlInList.BuildInts(p, otEligibleCategoryIds, "elig")}"
            : "c.is_worked_time = true";
        var sql = $"""
            SELECT te.work_date, SUM(te.duration) AS hours
            FROM   time_entry          te
            JOIN   lkp_time_entry_status s ON s.id = te.status_id
            JOIN   lkp_time_category     c ON c.id = te.time_category_id
            WHERE  te.employment_id = @EmploymentId
              AND  te.work_date >= @PeriodStart
              AND  te.work_date <= @PeriodEnd
              AND  {eligiblePredicate}
              AND  (s.code = 'APPROVED'
                    OR (s.code = 'LOCKED' AND te.payroll_run_id = @PayrollRunId))
            GROUP BY te.work_date
            ORDER BY te.work_date
            """;

        using var conn = _connectionFactory.CreateConnection();
        var rows = await conn.QueryAsync<WorkedDayRow>(sql, p);

        return rows.Select(r => (r.WorkDate, r.Hours)).ToList();
    }

    public async Task<decimal> GetApprovedNonWorkedPayableHoursByEmploymentAndPeriodAsync(
        Guid employmentId, DateOnly periodStart, DateOnly periodEnd, Guid payrollRunId,
        IReadOnlyCollection<int> otEligibleCategoryIds)
    {
        // Phase 12.12: paid leave (payable categories that are NOT OT-eligible — PTO/holiday/sick)
        // is still paid (at straight time) but does not count toward the OT threshold. Same
        // APPROVED / LOCKED-to-this-run rule as the worked-hours query; UNPAID excluded.
        // Phase 12.13.4: the non-eligible bucket is the complement of the resolved set over payable
        // categories; an empty set falls back to "payable and not is_worked_time" (today's behavior).
        var p = new DynamicParameters();
        p.Add("EmploymentId", employmentId);
        p.Add("PeriodStart",  periodStart.ToDateTime(TimeOnly.MinValue));
        p.Add("PeriodEnd",    periodEnd.ToDateTime(TimeOnly.MinValue));
        p.Add("PayrollRunId", payrollRunId);
        var nonEligiblePredicate = otEligibleCategoryIds.Count > 0
            ? $"c.id NOT IN {SqlInList.BuildInts(p, otEligibleCategoryIds, "elig")}"
            : "c.is_worked_time = false";
        var sql = $"""
            SELECT COALESCE(SUM(te.duration), 0)
            FROM   time_entry          te
            JOIN   lkp_time_entry_status s ON s.id = te.status_id
            JOIN   lkp_time_category     c ON c.id = te.time_category_id
            WHERE  te.employment_id = @EmploymentId
              AND  te.work_date >= @PeriodStart
              AND  te.work_date <= @PeriodEnd
              AND  {nonEligiblePredicate}
              AND  c.payable        = true
              AND  (s.code = 'APPROVED'
                    OR (s.code = 'LOCKED' AND te.payroll_run_id = @PayrollRunId))
            """;

        using var conn = _connectionFactory.CreateConnection();
        return await conn.ExecuteScalarAsync<decimal>(sql, p);
    }

    // ── Phase 12.7 — lock-on-approve / unlock-on-cancel ─────────────────────────
    public async Task<int> LockHoursForRunAsync(
        Guid payrollRunId, IReadOnlyList<Guid> employmentIds, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default)
    {
        if (employmentIds.Count == 0) return 0;

        // APPROVED → LOCKED for the run's employees within the period range, stamped with the run.
        // Entries already LOCKED to this run are untouched (status filter), so re-firing is a no-op.
        var p = new DynamicParameters();
        p.Add("PayrollRunId", payrollRunId);
        p.Add("Now",          _temporal.GetOperativeNow());
        p.Add("PeriodStart",  periodStart.ToDateTime(TimeOnly.MinValue));
        p.Add("PeriodEnd",    periodEnd.ToDateTime(TimeOnly.MinValue));
        var inEmp = BuildInClause(p, "Emp", employmentIds);

        var sql = $"""
            UPDATE time_entry
            SET    status_id      = (SELECT id FROM lkp_time_entry_status WHERE code = 'LOCKED'),
                   payroll_run_id = @PayrollRunId,
                   updated_at     = @Now
            WHERE  status_id      = (SELECT id FROM lkp_time_entry_status WHERE code = 'APPROVED')
              AND  work_date     >= @PeriodStart
              AND  work_date     <= @PeriodEnd
              AND  employment_id IN {inEmp}
            """;
        using var conn = _connectionFactory.CreateConnection();
        return await conn.ExecuteAsync(sql, p);
    }

    // ANSI-compliant parameterized IN list — avoids Postgres-specific ANY(@array) and Dapper's
    // list-expansion (not wired in this stack; it leaves a bare placeholder → 42601 syntax error).
    private static string BuildInClause<T>(DynamicParameters p, string prefix, IEnumerable<T> values)
    {
        var names = new List<string>();
        var i = 0;
        foreach (var v in values) { var n = $"{prefix}{i++}"; p.Add(n, v); names.Add($"@{n}"); }
        return names.Count == 0 ? "(NULL)" : "(" + string.Join(",", names) + ")";
    }

    public async Task<int> UnlockHoursForRunAsync(Guid payrollRunId, CancellationToken ct = default)
    {
        // LOCKED → APPROVED, clear the run id, for every entry locked to this run.
        const string sql = """
            UPDATE time_entry
            SET    status_id      = (SELECT id FROM lkp_time_entry_status WHERE code = 'APPROVED'),
                   payroll_run_id = NULL,
                   updated_at     = @Now
            WHERE  status_id      = (SELECT id FROM lkp_time_entry_status WHERE code = 'LOCKED')
              AND  payroll_run_id = @PayrollRunId
            """;
        using var conn = _connectionFactory.CreateConnection();
        return await conn.ExecuteAsync(sql, new
        {
            PayrollRunId = payrollRunId,
            Now          = _temporal.GetOperativeNow()
        });
    }

    private sealed record WorkedDayRow
    {
        public DateOnly WorkDate { get; init; }
        public decimal  Hours    { get; init; }
    }
}
