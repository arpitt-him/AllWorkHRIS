// AllWorkHRIS.Host/Hris/Dashboard/HrisDashboardContributor.cs
using AllWorkHRIS.Core.Dashboard;
using AllWorkHRIS.Core.Temporal;
using AllWorkHRIS.Host.Hris.Repositories;
using AllWorkHRIS.Host.Hris.Services;
using Microsoft.Extensions.Logging;

namespace AllWorkHRIS.Host.Hris.Dashboard;

public sealed class HrisDashboardContributor : IDashboardContributor
{
    private readonly IDocumentRepository          _documents;
    private readonly IOrgStructureService         _org;
    private readonly ITemporalContext             _temporal;
    private readonly ILogger<HrisDashboardContributor> _logger;

    public string                 ModuleName    => "People";
    public string                 AccentColor   => "var(--module-hris, #1d4ed8)";
    public IReadOnlyList<string>  RequiredRoles => ["HrisViewer", "HrisAdmin", "HrisOperator", "Manager"];

    public HrisDashboardContributor(
        IDocumentRepository               documents,
        IOrgStructureService              org,
        ITemporalContext                  temporal,
        ILogger<HrisDashboardContributor> logger)
    {
        _documents = documents;
        _org       = org;
        _temporal  = temporal;
        _logger    = logger;
    }

    public async Task<IReadOnlyList<DashboardItem>> GetItemsAsync(
        Guid?               entityId,
        IEnumerable<string> userRoles,
        CancellationToken   ct = default)
    {
        var items = new List<DashboardItem>();

        try
        {
            var today    = DateOnly.FromDateTime(_temporal.GetOperativeDate());
            var expiring = await _documents.GetExpiringWithinAsync(
                days: 30, asOf: today, legalEntityId: entityId);

            var entities    = await _org.GetLegalEntitiesAsync();
            var entityNames = entities.ToDictionary(e => e.OrgUnitId, e => e.OrgUnitName);

            foreach (var doc in expiring)
            {
                if (doc.ExpirationDate is null) continue;

                // When entity-scoped, every result belongs to entityId.
                // When cross-entity (null), we can't reliably resolve per-doc without
                // an additional join — route to the employee list for the tenant.
                var resolvedEntityId = entityId ?? Guid.Empty;
                entityNames.TryGetValue(resolvedEntityId, out var entityName);

                items.Add(new DashboardItem(
                    Title:       $"Document expiring: {doc.DocumentName}",
                    Subtitle:    $"Expires {doc.ExpirationDate.Value:MMM d, yyyy}",
                    EntityId:    resolvedEntityId,
                    EntityName:  entityName ?? "Unknown entity",
                    Route:       "/hris/employees",
                    Urgency:     doc.ExpirationDate.Value <= today.AddDays(7)
                                     ? DashboardItemUrgency.Urgent
                                     : DashboardItemUrgency.Attention,
                    ModuleName:  ModuleName,
                    AccentColor: AccentColor));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HrisDashboardContributor query failed");
        }

        return items;
    }
}
