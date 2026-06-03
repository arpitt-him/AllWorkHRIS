using Dapper;
using AllWorkHRIS.Core.Composition;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Domain.Time;

namespace AllWorkHRIS.Module.TimeAttendance.Queries;

// ── Query result types ───────────────────────────────────────────────────────

public sealed record TaPeriodOption(
    Guid     PeriodId,
    string   Label,
    DateOnly StartDate,
    DateOnly EndDate,
    DateOnly PayDate);

public sealed record TimecardSummaryRow(
    Guid    EmploymentId,
    string  EmployeeName,
    string  EmployeeNumber,
    Guid    PeriodId,
    decimal TotalHours,
    decimal RegularHours,
    decimal OvertimeHours,
    long    SubmittedCount,
    long    ApprovedCount,
    long    RejectedCount,
    long    LockedCount);

public sealed record TimeEntryRow(
    Guid            TimeEntryId,
    DateOnly        WorkDate,
    string          TimeCategoryCode,
    decimal         Duration,
    TimeOnly?       StartTime,
    TimeOnly?       EndTime,
    string          StatusCode,
    string?         Notes,
    string?         RejectionReason,
    Guid?           OriginalTimeEntryId);

public sealed record HandoffPeriodRow(
    Guid      PeriodId,
    string    PeriodLabel,
    DateOnly  PayDate,
    long      TotalEntries,
    long      ApprovedEntries,
    long      LockedEntries,
    decimal   TotalHours,
    DateTime? FirstLockAt,
    Guid      PayrollContextId,
    string    PayrollContextName);

public sealed record EmploymentOption(Guid EmploymentId, string DisplayName, string EmployeeNumber);

public sealed record HandoffPreviewEntry(
    string   EmployeeName,
    string   EmployeeNumber,
    DateOnly WorkDate,
    string   TimeCategory,
    decimal  Duration,
    string   StatusCode);

// Phase 12.7b — approve-by-exception. A SUBMITTED/CORRECTED entry flagged for human review.
public sealed record TimeExceptionRow(
    Guid     TimeEntryId,
    Guid     EmploymentId,
    string   EmployeeName,
    string   EmployeeNumber,
    DateOnly WorkDate,
    string   TimeCategoryCode,
    decimal  Duration,
    string   Reason);

// An active non-exempt employee with no time entries for the period (missing timecard).
public sealed record MissingTimecardRow(Guid EmploymentId, string EmployeeName, string EmployeeNumber);

// The exception-review breakdown for a period: which SUBMITTED entries are clean (safe to auto-
// approve), which are anomalies to review, and which expected employees filed nothing.
public sealed record ExceptionReviewResult(
    IReadOnlyList<Guid>               CleanEntryIds,
    IReadOnlyList<TimeExceptionRow>   Anomalies,
    IReadOnlyList<MissingTimecardRow> MissingEmployees);

// ── Query service ────────────────────────────────────────────────────────────

public sealed class TimeAttendanceQueryService
{
    private readonly IConnectionFactory    _connectionFactory;
    private readonly IPayrollContextLookup _payrollContextLookup;

    public TimeAttendanceQueryService(
        IConnectionFactory connectionFactory, IPayrollContextLookup payrollContextLookup)
    {
        _connectionFactory    = connectionFactory;
        _payrollContextLookup = payrollContextLookup;
    }

    public async Task<IReadOnlyList<TaPeriodOption>> GetOpenPeriodsForEntityAsync(Guid legalEntityId)
    {
        using var conn = _connectionFactory.CreateConnection();
        var rows = await conn.QueryAsync<dynamic>(
            """
            SELECT pp.period_id,
                   pp.period_start_date,
                   pp.period_end_date,
                   pp.pay_date,
                   pp.period_year,
                   pp.period_number
            FROM   payroll_period pp
            JOIN   payroll_context pc ON pc.payroll_context_id = pp.payroll_context_id
            WHERE  pc.legal_entity_id = @LegalEntityId
              AND  pp.calendar_status NOT IN ('CLOSED','FINALIZED')
            ORDER  BY pp.period_start_date ASC
            """,
            new { LegalEntityId = legalEntityId });

        return rows.Select(r => new TaPeriodOption(
            PeriodId:  (Guid)r.period_id,
            Label:     $"{r.period_year} P{r.period_number} ({((DateOnly)r.period_start_date):MMM d} – {((DateOnly)r.period_end_date):MMM d})",
            StartDate: (DateOnly)r.period_start_date,
            EndDate:   (DateOnly)r.period_end_date,
            PayDate:   (DateOnly)r.pay_date))
            .ToList();
    }

    public async Task<IReadOnlyList<TaPeriodOption>> GetClosedPeriodsForEntityAsync(Guid legalEntityId)
    {
        using var conn = _connectionFactory.CreateConnection();
        var rows = await conn.QueryAsync<dynamic>(
            """
            SELECT pp.period_id,
                   pp.period_start_date,
                   pp.period_end_date,
                   pp.pay_date,
                   pp.period_year,
                   pp.period_number
            FROM   payroll_period pp
            JOIN   payroll_context pc ON pc.payroll_context_id = pp.payroll_context_id
            WHERE  pc.legal_entity_id = @LegalEntityId
              AND  pp.calendar_status IN ('CLOSED','FINALIZED')
            ORDER  BY pp.period_start_date DESC
            """,
            new { LegalEntityId = legalEntityId });

        return rows.Select(r => new TaPeriodOption(
            PeriodId:  (Guid)r.period_id,
            Label:     $"{r.period_year} P{r.period_number} ({((DateOnly)r.period_start_date):MMM d} – {((DateOnly)r.period_end_date):MMM d})",
            StartDate: (DateOnly)r.period_start_date,
            EndDate:   (DateOnly)r.period_end_date,
            PayDate:   (DateOnly)r.pay_date))
            .ToList();
    }

    public async Task<IReadOnlyList<TaPeriodOption>> GetOpenPeriodsForEmploymentAsync(Guid employmentId)
    {
        using var conn = _connectionFactory.CreateConnection();
        var rows = await conn.QueryAsync<dynamic>(
            """
            SELECT pp.period_id,
                   pp.period_start_date,
                   pp.period_end_date,
                   pp.pay_date,
                   pp.period_year,
                   pp.period_number
            FROM   payroll_period pp
            JOIN   payroll_context pc ON pc.payroll_context_id = pp.payroll_context_id
            JOIN   assignment a  ON a.employment_id = @EmploymentId
            JOIN   org_unit   ou ON ou.org_unit_id  = a.department_id
            WHERE  ou.legal_entity_id = pc.legal_entity_id
              AND  pp.calendar_status NOT IN ('CLOSED','FINALIZED')
            ORDER  BY pp.period_start_date DESC
            """,
            new { EmploymentId = employmentId });

        return rows.Select(r => new TaPeriodOption(
            PeriodId:  (Guid)r.period_id,
            Label:     $"{r.period_year} P{r.period_number} ({((DateOnly)r.period_start_date):MMM d} – {((DateOnly)r.period_end_date):MMM d})",
            StartDate: (DateOnly)r.period_start_date,
            EndDate:   (DateOnly)r.period_end_date,
            PayDate:   (DateOnly)r.pay_date))
            .ToList();
    }

    public async Task<IReadOnlyList<TimecardSummaryRow>> GetTimecardSummariesAsync(
        Guid legalEntityId, Guid periodId)
    {
        // ADR-023: reg/OT is no longer stored — it is derived here from worked-time daily totals via
        // the shared Core OvertimeSplitCalculator. ADR-024 / 12.13.1: the weekly threshold + anchor
        // come from the shared resolver (period's context, as-of period start), the same one payroll
        // uses — so display matches pay. TotalHours is unchanged (all non-rejected hours). Worked-time
        // = lkp_time_category.is_worked_time (REGULAR/OVERTIME) — the FLSA OT basis.
        var otConfig = await GetOtConfigForPeriodAsync(periodId);

        using var conn = _connectionFactory.CreateConnection();
        var rows = (await conn.QueryAsync<TimecardEntryRaw>(
            """
            SELECT
                te.employment_id,
                p.legal_first_name || ' ' || p.legal_last_name AS employee_name,
                e.employee_number,
                te.work_date,
                te.duration,
                s.code  AS status_code,
                c.is_worked_time
            FROM   time_entry te
            JOIN   lkp_time_entry_status s ON s.id = te.status_id
            JOIN   lkp_time_category     c ON c.id = te.time_category_id
            JOIN   employment            e ON e.employment_id = te.employment_id
            JOIN   person                p ON p.person_id     = e.person_id
            WHERE  te.payroll_period_id = @PeriodId
              AND  s.code NOT IN ('VOID','DRAFT')
            ORDER  BY p.legal_last_name, p.legal_first_name
            """,
            new { PeriodId = periodId })).ToList();

        // Group by employee (rows arrive in last/first-name order, which GroupBy preserves) and
        // derive each employee's reg/OT split from their worked-time daily totals.
        return rows
            .GroupBy(r => r.EmploymentId)
            .Select(g =>
            {
                var first = g.First();
                var workedDaily = g
                    .Where(r => r.IsWorkedTime && r.StatusCode != "REJECTED")
                    .GroupBy(r => r.WorkDate)
                    .Select(d => (d.Key, d.Sum(x => x.Duration)));
                var split = OvertimeSplitCalculator.Compute(
                    workedDaily, otConfig.WeeklyThreshold, otConfig.WorkweekStartDay);

                return new TimecardSummaryRow(
                    EmploymentId:   first.EmploymentId,
                    EmployeeName:   first.EmployeeName,
                    EmployeeNumber: first.EmployeeNumber,
                    PeriodId:       periodId,
                    TotalHours:     g.Where(r => r.StatusCode != "REJECTED").Sum(r => r.Duration),
                    RegularHours:   split.RegularHours,
                    OvertimeHours:  split.OvertimeHours,
                    SubmittedCount: g.Count(r => r.StatusCode == "SUBMITTED"),
                    ApprovedCount:  g.Count(r => r.StatusCode == "APPROVED"),
                    RejectedCount:  g.Count(r => r.StatusCode == "REJECTED"),
                    LockedCount:    g.Count(r => r.StatusCode == "LOCKED"));
            })
            .ToList();
    }

    // Row-grain projection backing GetTimecardSummariesAsync — aggregated per employee in C#.
    private sealed record TimecardEntryRaw(
        Guid     EmploymentId,
        string   EmployeeName,
        string   EmployeeNumber,
        DateOnly WorkDate,
        decimal  Duration,
        string   StatusCode,
        bool     IsWorkedTime);

    public async Task<IReadOnlyList<TimeEntryRow>> GetEntriesForEmploymentAsync(
        Guid employmentId, Guid periodId)
    {
        using var conn = _connectionFactory.CreateConnection();
        return (await conn.QueryAsync<TimeEntryRow>(
            """
            SELECT
                te.time_entry_id,
                te.work_date,
                c.code  AS time_category_code,
                te.duration,
                te.start_time,
                te.end_time,
                s.code  AS status_code,
                te.notes,
                te.rejection_reason,
                te.original_time_entry_id
            FROM   time_entry te
            JOIN   lkp_time_entry_status s ON s.id = te.status_id
            JOIN   lkp_time_category     c ON c.id = te.time_category_id
            WHERE  te.employment_id     = @EmploymentId
              AND  te.payroll_period_id = @PeriodId
              AND  s.code NOT IN ('VOID')
            ORDER  BY te.work_date, te.created_at
            """,
            new { EmploymentId = employmentId, PeriodId = periodId })).ToList();
    }

    public async Task<IReadOnlyList<HandoffPeriodRow>> GetHandoffSummariesAsync(Guid legalEntityId)
    {
        using var conn = _connectionFactory.CreateConnection();
        // Exclude SALARY payroll contexts — exempt/salaried pay groups don't submit time
        // entries, so their periods would always show 0 entries and pad the handoff list
        // with permanent "No approved entries" rows. NULL rate_type is left visible
        // (unclassified context — admin still needs visibility). Other non-time-entry
        // rate types (COMMISSION, STIPEND) are not filtered yet — add them if/when they
        // show as noise.
        return (await conn.QueryAsync<HandoffPeriodRow>(
            """
            SELECT
                pp.period_id,
                pp.period_year || ' P' || pp.period_number AS period_label,
                pp.pay_date,
                COUNT(te.time_entry_id)                                              AS total_entries,
                COUNT(CASE WHEN s.code = 'APPROVED' THEN 1 END)                     AS approved_entries,
                COUNT(CASE WHEN s.code = 'LOCKED'   THEN 1 END)                     AS locked_entries,
                COALESCE(SUM(CASE WHEN s.code = 'LOCKED' THEN te.duration END), 0)  AS total_hours,
                MIN(CASE WHEN s.code = 'LOCKED' THEN te.updated_at END)             AS first_lock_at,
                pc.payroll_context_id,
                pc.payroll_context_name
            FROM   payroll_period pp
            JOIN   payroll_context pc ON pc.payroll_context_id = pp.payroll_context_id
            LEFT   JOIN time_entry te ON te.payroll_period_id = pp.period_id
            LEFT   JOIN lkp_time_entry_status s ON s.id = te.status_id
            WHERE  pc.legal_entity_id = @LegalEntityId
              AND  (pc.compensation_rate_type_id IS NULL
                    OR pc.compensation_rate_type_id NOT IN
                       (SELECT id FROM lkp_compensation_rate_type WHERE code = 'SALARY'))
            GROUP  BY pp.period_id, pp.period_year, pp.period_number, pp.pay_date, pp.period_start_date,
                     pc.payroll_context_id, pc.payroll_context_name
            ORDER  BY pp.period_start_date ASC
            """,
            new { LegalEntityId = legalEntityId })).ToList();
    }

    public async Task<IReadOnlyList<EmploymentOption>> GetEmploymentsForEntityAsync(Guid legalEntityId)
    {
        using var conn = _connectionFactory.CreateConnection();
        return (await conn.QueryAsync<EmploymentOption>(
            """
            SELECT e.employment_id,
                   p.legal_first_name || ' ' || p.legal_last_name AS display_name,
                   e.employee_number
            FROM   employment e
            JOIN   person     p  ON p.person_id    = e.person_id
            JOIN   assignment a  ON a.employment_id = e.employment_id
            JOIN   org_unit   ou ON ou.org_unit_id  = a.department_id
            WHERE  ou.legal_entity_id = @LegalEntityId
              AND  e.employment_status_id IN (
                       SELECT id FROM lkp_employment_status WHERE code = 'ACTIVE'
                   )
            ORDER  BY p.legal_last_name, p.legal_first_name
            """,
            new { LegalEntityId = legalEntityId })).ToList();
    }

    // Phase 12.7b — classify a period's SUBMITTED/CORRECTED entries into clean (safe to auto-approve)
    // vs anomalies needing review, and find active non-exempt employees who filed nothing. Anomalies:
    // zero/negative hours, an INCOMPLETE punch (exactly one of start/end present — a clock-in with no
    // clock-out or vice versa; a duration-only entry with NEITHER is legitimate, e.g. imported time),
    // and overtime — but routine OT
    // auto-approves; OT is flagged only when an employee's TOTAL period overtime REACHES OR EXCEEDS the
    // threshold (the saved value is the trigger: OT >= it forces review). NULL = never flag OT.
    // "Outside the employee's schedule" is deferred until per-employee scheduled hours are populated.
    // Phase 12.7b — the OT review threshold operative for a period: its payroll context's
    // ot_review_threshold_hours (pre-populated from the legal-entity default at context creation,
    // set on the pay-calendar/context page). NULL = overtime never flagged on hours for that group.
    public async Task<decimal?> GetOtReviewThresholdAsync(Guid periodId)
        // ADR-024 / 12.13.1: via the shared resolver (consistent with the rest of the OT config).
        => (await GetOtConfigForPeriodAsync(periodId)).ReviewThreshold;

    public async Task<ExceptionReviewResult> GetExceptionReviewAsync(Guid legalEntityId, Guid periodId)
    {
        // A period belongs to one payroll context, so the weekly OT threshold, workweek anchor, and
        // OT review trigger are single values for the whole call.
        var otConfig = await GetOtConfigForPeriodAsync(periodId);

        using var conn = _connectionFactory.CreateConnection();

        // Per-entry base anomalies (zero/negative hours, incomplete punch) are computed in SQL. The
        // OT-review trigger is applied in C# below (ADR-023): OT is no longer a stored category to
        // SUM — it is derived per employee from worked-time daily totals via the shared calculator.
        const string entriesSql = """
            SELECT te.time_entry_id,
                   te.employment_id,
                   p.legal_first_name || ' ' || p.legal_last_name AS employee_name,
                   e.employee_number,
                   te.work_date,
                   c.code      AS time_category_code,
                   te.duration,
                   c.is_worked_time,
                   CASE
                       WHEN te.duration <= 0 THEN 'Zero or negative hours'
                       WHEN (te.start_time IS NULL AND te.end_time IS NOT NULL)
                         OR (te.start_time IS NOT NULL AND te.end_time IS NULL)
                            THEN 'Incomplete punch (only one of start/end)'
                       ELSE NULL
                   END AS reason
            FROM   time_entry te
            JOIN   lkp_time_entry_status s ON s.id = te.status_id
            JOIN   lkp_time_category     c ON c.id = te.time_category_id
            JOIN   employment            e ON e.employment_id = te.employment_id
            JOIN   person                p ON p.person_id     = e.person_id
            JOIN   assignment            a ON a.employment_id = e.employment_id
            JOIN   org_unit             ou ON ou.org_unit_id  = a.department_id
            WHERE  te.payroll_period_id = @PeriodId
              AND  ou.legal_entity_id   = @LegalEntityId
              AND  s.code IN ('SUBMITTED','CORRECTED')
            ORDER  BY p.legal_last_name, p.legal_first_name, te.work_date
            """;
        var rows = (await conn.QueryAsync<ExceptionEntryRaw>(
            entriesSql, new { PeriodId = periodId, LegalEntityId = legalEntityId })).ToList();

        // OT-review trigger: an employee whose derived period OT hours reach/exceed the trigger has
        // their (otherwise-clean) worked-time entries held out of the auto-approve set for review.
        var otFlagged = new HashSet<Guid>();
        if (otConfig.ReviewThreshold is { } reviewTrigger)
        {
            foreach (var emp in rows.Where(r => r.IsWorkedTime).GroupBy(r => r.EmploymentId))
            {
                var daily = emp.GroupBy(r => r.WorkDate).Select(d => (d.Key, d.Sum(x => x.Duration)));
                var ot    = OvertimeSplitCalculator.Compute(daily, otConfig.WeeklyThreshold, otConfig.WorkweekStartDay);
                if (ot.OvertimeHours >= reviewTrigger)
                    otFlagged.Add(emp.Key);
            }
        }

        string? Reason(ExceptionEntryRaw r) =>
            !string.IsNullOrEmpty(r.Reason)                        ? r.Reason
            : r.IsWorkedTime && otFlagged.Contains(r.EmploymentId) ? "Overtime at/over trigger"
            : null;

        var cleanIds  = rows.Where(r => string.IsNullOrEmpty(Reason(r))).Select(r => r.TimeEntryId).ToList();
        var anomalies = rows.Where(r => !string.IsNullOrEmpty(Reason(r)))
            .Select(r => new TimeExceptionRow(
                r.TimeEntryId, r.EmploymentId, r.EmployeeName, r.EmployeeNumber,
                r.WorkDate, r.TimeCategoryCode, r.Duration, Reason(r)!))
            .ToList();

        // Active non-exempt employees in the entity with no (non-void) entry for the period.
        const string missingSql = """
            SELECT e.employment_id,
                   p.legal_first_name || ' ' || p.legal_last_name AS employee_name,
                   e.employee_number
            FROM   employment e
            JOIN   person      p  ON p.person_id     = e.person_id
            JOIN   assignment  a  ON a.employment_id = e.employment_id
            JOIN   org_unit    ou ON ou.org_unit_id  = a.department_id
            JOIN   lkp_flsa_status f ON f.id = e.flsa_status_id
            WHERE  ou.legal_entity_id = @LegalEntityId
              AND  f.code             = 'NON_EXEMPT'
              AND  e.employment_status_id IN (SELECT id FROM lkp_employment_status WHERE code = 'ACTIVE')
              AND  NOT EXISTS (
                       SELECT 1 FROM time_entry te
                       JOIN   lkp_time_entry_status s ON s.id = te.status_id
                       WHERE  te.employment_id     = e.employment_id
                         AND  te.payroll_period_id = @PeriodId
                         AND  s.code <> 'VOID'
                   )
            ORDER  BY p.legal_last_name, p.legal_first_name
            """;
        var missing = (await conn.QueryAsync<MissingTimecardRow>(
            missingSql, new { PeriodId = periodId, LegalEntityId = legalEntityId })).ToList();

        return new ExceptionReviewResult(cleanIds, anomalies, missing);
    }

    private sealed record ExceptionEntryRaw(
        Guid TimeEntryId, Guid EmploymentId, string EmployeeName, string EmployeeNumber,
        DateOnly WorkDate, string TimeCategoryCode, decimal Duration, bool IsWorkedTime, string? Reason);

    // The OT config operative for a period — read from its payroll context (the layer the engine
    // reads; effective-dating is tracked as ToDo #45). Weekly threshold + anchor feed the OT split;
    // the review threshold (nullable) is the approve-by-exception trigger.
    private async Task<OtPeriodConfig> GetOtConfigForPeriodAsync(Guid periodId)
    {
        // ADR-024 / 12.13.1: resolve OT config via the shared effective-dated resolver (the same one
        // payroll uses), not by reading payroll_context directly. Period-level (as-of period start)
        // for now; 12.13.2 moves this to per-workweek.
        using var conn = _connectionFactory.CreateConnection();
        var pc = await conn.QueryFirstOrDefaultAsync<PeriodContextRow>(
            "SELECT pp.payroll_context_id, pp.period_start_date FROM payroll_period pp WHERE pp.period_id = @PeriodId",
            new { PeriodId = periodId });
        if (pc is null) return new OtPeriodConfig(40m, 1, null);

        var cfg = await _payrollContextLookup.ResolveOtConfigAsync(pc.PayrollContextId, pc.PeriodStartDate);
        return new OtPeriodConfig(cfg.WeeklyThresholdHours, cfg.WorkweekStartDay, cfg.ReviewThresholdHours);
    }

    private sealed record OtPeriodConfig(decimal WeeklyThreshold, int WorkweekStartDay, decimal? ReviewThreshold);
    private sealed record PeriodContextRow(Guid PayrollContextId, DateOnly PeriodStartDate);

    public async Task<(int PendingApproval, int OverTimeAlerts, int CutoffRisk)>
        GetStatCardsAsync(Guid legalEntityId, Guid periodId)
    {
        using var conn = _connectionFactory.CreateConnection();

        // Status counts — category-independent.
        var row = await conn.QueryFirstOrDefaultAsync<dynamic>(
            """
            SELECT
                COUNT(CASE WHEN s.code = 'SUBMITTED' THEN 1 END) AS pending_approval,
                COUNT(CASE WHEN s.code IN ('DRAFT','SUBMITTED') THEN 1 END) AS cutoff_risk
            FROM   time_entry te
            JOIN   lkp_time_entry_status s ON s.id = te.status_id
            JOIN   employment            e ON e.employment_id = te.employment_id
            JOIN   assignment            a ON a.employment_id = e.employment_id
            JOIN   org_unit             ou ON ou.org_unit_id  = a.department_id
            WHERE  te.payroll_period_id = @PeriodId
              AND  ou.legal_entity_id   = @LegalEntityId
            """,
            new { PeriodId = periodId, LegalEntityId = legalEntityId });

        var pendingApproval = row is null ? 0 : (int)(long)row.pending_approval;
        var cutoffRisk      = row is null ? 0 : (int)(long)row.cutoff_risk;

        // ADR-023: "Overtime Alerts" = employees with derived OT > 0 on APPROVED worked hours
        // (previously a count of stored OVERTIME-category rows). Derived via the shared calculator.
        var otConfig   = await GetOtConfigForPeriodAsync(periodId);
        var workedRows = await conn.QueryAsync<WorkedDailyRow>(
            """
            SELECT te.employment_id, te.work_date, SUM(te.duration) AS worked_hours
            FROM   time_entry te
            JOIN   lkp_time_entry_status s ON s.id = te.status_id
            JOIN   lkp_time_category     c ON c.id = te.time_category_id
            JOIN   employment            e ON e.employment_id = te.employment_id
            JOIN   assignment            a ON a.employment_id = e.employment_id
            JOIN   org_unit             ou ON ou.org_unit_id  = a.department_id
            WHERE  te.payroll_period_id = @PeriodId
              AND  ou.legal_entity_id   = @LegalEntityId
              AND  s.code = 'APPROVED'
              AND  c.is_worked_time = true
            GROUP  BY te.employment_id, te.work_date
            """,
            new { PeriodId = periodId, LegalEntityId = legalEntityId });

        var overtimeAlerts = workedRows
            .GroupBy(r => r.EmploymentId)
            .Count(g => OvertimeSplitCalculator.Compute(
                g.Select(r => (r.WorkDate, r.WorkedHours)),
                otConfig.WeeklyThreshold, otConfig.WorkweekStartDay).OvertimeHours > 0m);

        return (pendingApproval, overtimeAlerts, cutoffRisk);
    }

    private sealed record WorkedDailyRow(Guid EmploymentId, DateOnly WorkDate, decimal WorkedHours);

    // ADR-023: one employee's derived reg/OT split for a period (MyTimecard self-service view).
    // Same basis as the timecard summary: worked-time, non-rejected daily totals through the shared
    // calculator, using the period context's weekly threshold + workweek anchor.
    public async Task<OvertimeSplit> GetPeriodOvertimeSplitForEmploymentAsync(Guid employmentId, Guid periodId)
    {
        var otConfig = await GetOtConfigForPeriodAsync(periodId);

        using var conn = _connectionFactory.CreateConnection();
        var daily = await conn.QueryAsync<WorkedDailyRow>(
            """
            SELECT te.employment_id, te.work_date, SUM(te.duration) AS worked_hours
            FROM   time_entry te
            JOIN   lkp_time_entry_status s ON s.id = te.status_id
            JOIN   lkp_time_category     c ON c.id = te.time_category_id
            WHERE  te.employment_id     = @EmploymentId
              AND  te.payroll_period_id = @PeriodId
              AND  s.code NOT IN ('VOID','DRAFT','REJECTED')
              AND  c.is_worked_time = true
            GROUP  BY te.employment_id, te.work_date
            """,
            new { EmploymentId = employmentId, PeriodId = periodId });

        return OvertimeSplitCalculator.Compute(
            daily.Select(r => (r.WorkDate, r.WorkedHours)),
            otConfig.WeeklyThreshold, otConfig.WorkweekStartDay);
    }

    public async Task<IReadOnlyList<HandoffPreviewEntry>> GetEntriesForPeriodAsync(Guid periodId)
    {
        using var conn = _connectionFactory.CreateConnection();
        return (await conn.QueryAsync<HandoffPreviewEntry>(
            """
            SELECT p.legal_first_name || ' ' || p.legal_last_name AS employee_name,
                   e.employee_number,
                   te.work_date,
                   c.code  AS time_category,
                   te.duration,
                   s.code  AS status_code
            FROM   time_entry te
            JOIN   lkp_time_entry_status s ON s.id = te.status_id
            JOIN   lkp_time_category     c ON c.id = te.time_category_id
            JOIN   employment            e ON e.employment_id = te.employment_id
            JOIN   person                p ON p.person_id     = e.person_id
            WHERE  te.payroll_period_id = @PeriodId
            ORDER  BY p.legal_last_name, p.legal_first_name, te.work_date
            """,
            new { PeriodId = periodId })).ToList();
    }

    public async Task<IReadOnlyList<TaPeriodOption>> GetPeriodsWithEntriesForEmploymentAsync(Guid employmentId)
    {
        using var conn = _connectionFactory.CreateConnection();
        var rows = await conn.QueryAsync<dynamic>(
            """
            SELECT DISTINCT
                   pp.period_id,
                   pp.period_start_date,
                   pp.period_end_date,
                   pp.pay_date,
                   pp.period_year,
                   pp.period_number
            FROM   time_entry    te
            JOIN   payroll_period pp ON pp.period_id = te.payroll_period_id
            WHERE  te.employment_id = @EmploymentId
            ORDER  BY pp.period_start_date DESC
            """,
            new { EmploymentId = employmentId });

        return rows.Select(r => new TaPeriodOption(
            PeriodId:  (Guid)r.period_id,
            Label:     $"{r.period_year} P{r.period_number} ({((DateOnly)r.period_start_date):MMM d} – {((DateOnly)r.period_end_date):MMM d})",
            StartDate: (DateOnly)r.period_start_date,
            EndDate:   (DateOnly)r.period_end_date,
            PayDate:   (DateOnly)r.pay_date))
            .ToList();
    }
}
