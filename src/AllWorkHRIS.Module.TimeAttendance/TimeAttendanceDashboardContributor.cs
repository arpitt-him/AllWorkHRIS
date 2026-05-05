using Dapper;
using AllWorkHRIS.Core.Dashboard;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Temporal;
using Microsoft.Extensions.Logging;

namespace AllWorkHRIS.Module.TimeAttendance;

public sealed class TimeAttendanceDashboardContributor : IDashboardContributor
{
    private readonly IConnectionFactory                          _connectionFactory;
    private readonly ITemporalContext                            _temporal;
    private readonly ILogger<TimeAttendanceDashboardContributor> _logger;

    public string ModuleName  => "T&A";
    public string AccentColor => "var(--module-ta, #7c3aed)";

    public IReadOnlyList<string> RequiredRoles { get; } = ["TimeAdmin", "Manager", "TimeViewer"];

    public TimeAttendanceDashboardContributor(
        IConnectionFactory                          connectionFactory,
        ITemporalContext                            temporal,
        ILogger<TimeAttendanceDashboardContributor> logger)
    {
        _connectionFactory = connectionFactory;
        _temporal          = temporal;
        _logger            = logger;
    }

    public async Task<IReadOnlyList<DashboardItem>> GetItemsAsync(
        Guid?               entityId,
        IEnumerable<string> roles,
        CancellationToken   ct = default)
    {
        try
        {
            var today = DateOnly.FromDateTime(_temporal.GetOperativeDate());
            using var conn = _connectionFactory.CreateConnection();

            var pendingCount = await conn.ExecuteScalarAsync<long>(
                """
                SELECT COUNT(*)
                FROM   time_entry te
                JOIN   lkp_time_entry_status s ON s.id = te.status_id
                JOIN   payroll_period pp ON pp.period_id = te.payroll_period_id
                WHERE  s.code = 'SUBMITTED'
                  AND  pp.period_end_date >= @Today
                """,
                new { Today = today.ToDateTime(TimeOnly.MinValue) });

            if (pendingCount == 0) return [];

            return
            [
                new DashboardItem(
                    Title:      $"{pendingCount} timecard{(pendingCount == 1 ? "" : "s")} pending approval",
                    Subtitle:   "Current period",
                    EntityId:   entityId ?? Guid.Empty,
                    EntityName: string.Empty,
                    Route:      "/ta/timecards",
                    Urgency:    DashboardItemUrgency.Attention,
                    ModuleName: ModuleName,
                    AccentColor: AccentColor)
            ];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TimeAttendanceDashboardContributor failed");
            return [];
        }
    }
}
