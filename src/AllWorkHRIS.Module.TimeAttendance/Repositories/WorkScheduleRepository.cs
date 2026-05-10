using Dapper;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.TimeAttendance.Domain.Schedule;

namespace AllWorkHRIS.Module.TimeAttendance.Repositories;

public sealed class WorkScheduleRepository : IWorkScheduleRepository
{
    private readonly IConnectionFactory _connectionFactory;

    public WorkScheduleRepository(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public async Task<WorkSchedule?> GetByIdAsync(Guid workScheduleId)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<WorkSchedule>(
            "SELECT * FROM work_schedule WHERE work_schedule_id = @Id",
            new { Id = workScheduleId });
    }

    public async Task<IEnumerable<WorkSchedule>> GetByLegalEntityAsync(Guid legalEntityId)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<WorkSchedule>(
            "SELECT * FROM work_schedule WHERE legal_entity_id = @LegalEntityId ORDER BY effective_date DESC",
            new { LegalEntityId = legalEntityId });
    }

    public async Task<WorkSchedule?> GetActiveForEntityAsync(Guid legalEntityId, DateOnly asOf)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<WorkSchedule>(
            """
            SELECT * FROM work_schedule
            WHERE  legal_entity_id = @LegalEntityId
              AND  effective_date  <= @AsOf
              AND  (end_date IS NULL OR end_date >= @AsOf)
            ORDER  BY effective_date DESC
            """,
            new { LegalEntityId = legalEntityId, AsOf = asOf.ToDateTime(TimeOnly.MinValue) });
    }

    public async Task<IEnumerable<ShiftDefinition>> GetShiftsAsync(Guid workScheduleId)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<ShiftDefinition>(
            "SELECT * FROM shift_definition WHERE work_schedule_id = @WorkScheduleId ORDER BY day_of_week, start_time",
            new { WorkScheduleId = workScheduleId });
    }

    public async Task<int> ResolveWorkweekAnchorAsync(Guid payrollPeriodId, Guid employmentId)
    {
        using var conn = _connectionFactory.CreateConnection();

        // Level 1 — pay calendar anchor: derive from payroll period start date
        var periodStart = await conn.ExecuteScalarAsync<DateOnly?>(
            "SELECT period_start_date FROM payroll_period WHERE period_id = @PeriodId",
            new { PeriodId = payrollPeriodId });

        if (periodStart.HasValue)
            return (int)periodStart.Value.DayOfWeek;

        // Level 2 — entity work_schedule fallback (HRIS-only deployments)
        var legalEntityId = await conn.ExecuteScalarAsync<Guid?>(
            "SELECT legal_entity_id FROM employment WHERE employment_id = @EmploymentId",
            new { EmploymentId = employmentId });

        if (legalEntityId.HasValue)
        {
            var schedule = await GetActiveForEntityAsync(legalEntityId.Value, DateOnly.FromDateTime(DateTime.UtcNow));
            if (schedule is not null)
                return schedule.WorkweekStartDay;
        }

        // Level 3 — Monday safe default
        return 1;
    }

    public async Task<Guid> InsertAsync(WorkSchedule schedule, IUnitOfWork uow)
    {
        const string sql = """
            INSERT INTO work_schedule (
                work_schedule_id, legal_entity_id, name,
                standard_hours_per_week, workweek_start_day,
                effective_date, end_date, created_at, updated_at)
            VALUES (
                @WorkScheduleId, @LegalEntityId, @Name,
                @StandardHoursPerWeek, @WorkweekStartDay,
                @EffectiveDate, @EndDate, @CreatedAt, @UpdatedAt)
            """;
        await uow.Connection.ExecuteAsync(sql, new
        {
            schedule.WorkScheduleId,
            schedule.LegalEntityId,
            schedule.Name,
            schedule.StandardHoursPerWeek,
            schedule.WorkweekStartDay,
            EffectiveDate = schedule.EffectiveDate.ToDateTime(TimeOnly.MinValue),
            EndDate       = schedule.EndDate.HasValue ? (object)schedule.EndDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value,
            CreatedAt     = DateTimeOffset.UtcNow,
            UpdatedAt     = DateTimeOffset.UtcNow
        }, uow.Transaction);
        return schedule.WorkScheduleId;
    }
}
