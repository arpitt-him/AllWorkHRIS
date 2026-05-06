using Dapper;
using AllWorkHRIS.Core.Composition;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Lookups;
using AllWorkHRIS.Module.TimeAttendance.Domain;
using AllWorkHRIS.Module.TimeAttendance.Repositories;

namespace AllWorkHRIS.Module.TimeAttendance.Services;

public sealed class OvertimeDetectionService : IOvertimeDetectionService
{
    private const decimal FlsaWeeklyThreshold = 40m;

    private readonly ITimeEntryRepository  _repository;
    private readonly IConnectionFactory    _connectionFactory;
    private readonly ILookupCache          _lookupCache;
    private readonly ITimeApprovalNotifier _notifier;

    public OvertimeDetectionService(
        ITimeEntryRepository  repository,
        IConnectionFactory    connectionFactory,
        ILookupCache          lookupCache,
        ITimeApprovalNotifier notifier)
    {
        _repository        = repository;
        _connectionFactory = connectionFactory;
        _lookupCache       = lookupCache;
        _notifier          = notifier;
    }

    public async Task<OvertimeDetectionResult> DetectAndReclassifyAsync(
        Guid employmentId, DateOnly workweekStart, IUnitOfWork uow)
    {
        var flsaCode = await GetFlsaStatusCodeAsync(employmentId);

        // Exempt employees — no overtime evaluation
        if (flsaCode is not null && flsaCode.StartsWith("EXEMPT", StringComparison.OrdinalIgnoreCase))
            return OvertimeDetectionResult.NotApplicable(employmentId);

        var weekEntries = await _repository.GetWorkweekEntriesAsync(employmentId, workweekStart);

        var approved = weekEntries
            .Where(e => e.Status == TimeEntryStatus.Approved
                     && e.TimeCategoryCode == "REGULAR")
            .OrderBy(e => e.WorkDate)
            .ToList();

        var totalRegularHours = approved.Sum(e => e.Duration);

        if (totalRegularHours <= FlsaWeeklyThreshold)
            return OvertimeDetectionResult.NoOvertime(employmentId, totalRegularHours);

        var overtimeHours = totalRegularHours - FlsaWeeklyThreshold;
        var reclassified  = new List<Guid>();

        decimal remaining = overtimeHours;
        foreach (var entry in approved.OrderByDescending(e => e.WorkDate))
        {
            if (remaining <= 0) break;

            var hoursToReclassify = Math.Min(entry.Duration, remaining);

            if (hoursToReclassify == entry.Duration)
            {
                await _repository.ReclassifyAsync(entry.TimeEntryId, "OVERTIME", uow);
                reclassified.Add(entry.TimeEntryId);
            }
            else
            {
                // Split: update original to remaining regular hours, insert new OT entry
                await SplitAndReclassifyAsync(entry, hoursToReclassify, uow);
                reclassified.Add(entry.TimeEntryId);
            }

            remaining -= hoursToReclassify;
        }

        await _notifier.NotifyOvertimeWarningAsync(employmentId, workweekStart, overtimeHours);

        return OvertimeDetectionResult.WithOvertime(
            employmentId, totalRegularHours, overtimeHours, reclassified);
    }

    private async Task SplitAndReclassifyAsync(
        TimeEntry entry, decimal overtimeHours, IUnitOfWork uow)
    {
        var regularHours = entry.Duration - overtimeHours;

        // Shrink original entry to regular portion
        const string shrinkSql = """
            UPDATE time_entry
            SET    duration   = @Duration,
                   updated_at = @Now
            WHERE  time_entry_id = @TimeEntryId
            """;
        await uow.Connection.ExecuteAsync(shrinkSql, new
        {
            Duration    = regularHours,
            Now         = DateTimeOffset.UtcNow,
            TimeEntryId = entry.TimeEntryId
        }, uow.Transaction);

        // Insert new OVERTIME entry for the split hours
        var overtimeCategoryId = _lookupCache.GetId(TimeAttendanceLookupTables.TimeCategory, "OVERTIME");
        var approvedStatusId   = _lookupCache.GetId(TimeAttendanceLookupTables.TimeEntryStatus, "APPROVED");

        const string insertSql = """
            INSERT INTO time_entry (
                time_entry_id, employment_id, payroll_period_id, work_date,
                time_category_id, duration, status_id, entry_method_id,
                submitted_by, submitted_at, approved_by, approved_at,
                original_time_entry_id, created_at, updated_at)
            VALUES (
                @TimeEntryId, @EmploymentId, @PayrollPeriodId, @WorkDate,
                @TimeCategoryId, @Duration, @StatusId, @EntryMethodId,
                @SubmittedBy, @SubmittedAt, @ApprovedBy, @ApprovedAt,
                @OriginalId, @Now, @Now)
            """;
        await uow.Connection.ExecuteAsync(insertSql, new
        {
            TimeEntryId     = Guid.NewGuid(),
            entry.EmploymentId,
            entry.PayrollPeriodId,
            WorkDate        = entry.WorkDate.ToDateTime(TimeOnly.MinValue),
            TimeCategoryId  = overtimeCategoryId,
            Duration        = overtimeHours,
            StatusId        = approvedStatusId,
            entry.EntryMethodId,
            entry.SubmittedBy,
            entry.SubmittedAt,
            ApprovedBy      = (object?)entry.ApprovedBy ?? DBNull.Value,
            ApprovedAt      = (object?)entry.ApprovedAt ?? DBNull.Value,
            OriginalId      = entry.TimeEntryId,
            Now             = DateTimeOffset.UtcNow
        }, uow.Transaction);
    }

    private async Task<string?> GetFlsaStatusCodeAsync(Guid employmentId)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.ExecuteScalarAsync<string?>(
            """
            SELECT s.code
            FROM   employment e
            JOIN   lkp_flsa_status s ON s.id = e.flsa_status_id
            WHERE  e.employment_id = @EmploymentId
            """,
            new { EmploymentId = employmentId });
    }
}
