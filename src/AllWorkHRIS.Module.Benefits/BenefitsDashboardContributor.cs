// AllWorkHRIS.Module.Benefits/BenefitsDashboardContributor.cs
using AllWorkHRIS.Core.Dashboard;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Temporal;
using Dapper;
using Microsoft.Extensions.Logging;

namespace AllWorkHRIS.Module.Benefits;

public sealed class BenefitsDashboardContributor : IDashboardContributor
{
    private readonly IConnectionFactory                   _connectionFactory;
    private readonly ITemporalContext                     _temporal;
    private readonly ILogger<BenefitsDashboardContributor> _logger;

    public string                 ModuleName    => "Benefits";
    public string                 AccentColor   => "var(--module-benefits, #047857)";
    public IReadOnlyList<string>  RequiredRoles => ["BenefitsAdmin", "HrisAdmin"];

    public BenefitsDashboardContributor(
        IConnectionFactory                    connectionFactory,
        ITemporalContext                      temporal,
        ILogger<BenefitsDashboardContributor> logger)
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
            using var conn = _connectionFactory.CreateConnection();

            // Elections with a future effective date not yet in effect — items needing awareness
            var today = DateOnly.FromDateTime(_temporal.GetOperativeDate());

            // Nullable Guid filter split into two branches to avoid Npgsql type-inference
            // failure when passing a null Guid? parameter in an IS NULL OR col = @param predicate.
            var entityFilter = entityId.HasValue
                ? "AND e.legal_entity_id = @EntityId"
                : string.Empty;

            var upcomingSql =
                $"""
                SELECT
                    COUNT(*)                  AS election_count,
                    d.description             AS deduction_code_name,
                    bde.effective_start_date,
                    ou.org_unit_id            AS entity_id,
                    ou.org_unit_name          AS entity_name
                FROM benefit_deduction_election bde
                JOIN deduction  d  ON d.deduction_id  = bde.deduction_id
                JOIN employment e  ON e.employment_id = bde.employment_id
                JOIN org_unit   ou ON ou.org_unit_id  = e.legal_entity_id
                WHERE bde.status = 'ACTIVE'
                  AND bde.effective_start_date > @Today
                  {entityFilter}
                GROUP BY d.description, bde.effective_start_date, ou.org_unit_id, ou.org_unit_name
                ORDER BY bde.effective_start_date
                LIMIT 5
                """;

            var upcoming = await conn.QueryAsync(upcomingSql,
                new { Today = today.ToDateTime(TimeOnly.MinValue), EntityId = entityId });

            foreach (var row in upcoming)
            {
                items.Add(new DashboardItem(
                    Title:       $"{(int)row.election_count} upcoming {row.deduction_code_name} elections",
                    Subtitle:    $"Effective {(DateOnly)row.effective_start_date:MMM d, yyyy}",
                    EntityId:    (Guid)row.entity_id,
                    EntityName:  (string)row.entity_name,
                    Route:       "/benefits/elections",
                    Urgency:     DashboardItemUrgency.Upcoming,
                    ModuleName:  ModuleName,
                    AccentColor: AccentColor));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "BenefitsDashboardContributor query failed");
        }

        return items;
    }
}
