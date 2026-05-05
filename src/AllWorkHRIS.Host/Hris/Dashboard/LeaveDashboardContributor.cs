// AllWorkHRIS.Host/Hris/Dashboard/LeaveDashboardContributor.cs
using AllWorkHRIS.Core.Dashboard;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Temporal;
using Dapper;
using Microsoft.Extensions.Logging;

namespace AllWorkHRIS.Host.Hris.Dashboard;

public sealed class LeaveDashboardContributor : IDashboardContributor
{
    private readonly IConnectionFactory             _connectionFactory;
    private readonly ITemporalContext               _temporal;
    private readonly ILogger<LeaveDashboardContributor> _logger;

    public string                 ModuleName    => "People";
    public string                 AccentColor   => "var(--module-hris, #1d4ed8)";
    public IReadOnlyList<string>  RequiredRoles => ["HrisViewer", "HrisAdmin", "Manager"];

    public LeaveDashboardContributor(
        IConnectionFactory                  connectionFactory,
        ITemporalContext                    temporal,
        ILogger<LeaveDashboardContributor>  logger)
    {
        _connectionFactory = connectionFactory;
        _temporal          = temporal;
        _logger            = logger;
    }

    public async Task<IReadOnlyList<DashboardItem>> GetItemsAsync(
        Guid?               entityId,
        IEnumerable<string> userRoles,
        CancellationToken   ct = default)
    {
        var items = new List<DashboardItem>();

        try
        {
            var today = DateOnly.FromDateTime(_temporal.GetOperativeDate());

            // Pending leave requests — entity scoped via employment → assignment join
            const string pendingSql =
                """
                SELECT
                    lr.leave_request_id,
                    p.legal_first_name || ' ' || p.legal_last_name AS employee_name,
                    lr.leave_start_date,
                    lr.leave_end_date,
                    ou.org_unit_id   AS entity_id,
                    ou.org_unit_name AS entity_name
                FROM leave_request lr
                JOIN lkp_leave_status ls ON ls.id = lr.leave_status_id
                JOIN employment e  ON e.employment_id = lr.employment_id
                JOIN person    p  ON p.person_id = e.person_id
                JOIN org_unit  ou ON ou.org_unit_id = e.legal_entity_id
                WHERE ls.code = 'PENDING_MANAGER_APPROVAL'
                  AND (@EntityId IS NULL OR e.legal_entity_id = @EntityId)
                ORDER BY lr.leave_start_date
                LIMIT 10
                """;

            using var conn    = _connectionFactory.CreateConnection();
            var pending       = await conn.QueryAsync(pendingSql,
                new { EntityId = entityId });

            foreach (var row in pending)
            {
                items.Add(new DashboardItem(
                    Title:       $"Leave pending approval: {row.employee_name}",
                    Subtitle:    $"{(DateOnly)row.leave_start_date:MMM d} – {(DateOnly)row.leave_end_date:MMM d, yyyy}",
                    EntityId:    (Guid)row.entity_id,
                    EntityName:  (string)row.entity_name,
                    Route:       "/hris/employees",
                    Urgency:     DashboardItemUrgency.Attention,
                    ModuleName:  ModuleName,
                    AccentColor: AccentColor));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LeaveDashboardContributor query failed");
        }

        return items;
    }
}
