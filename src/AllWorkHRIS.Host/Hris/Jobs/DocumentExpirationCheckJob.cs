using AllWorkHRIS.Core.Lookups;
using AllWorkHRIS.Core.Temporal;
using AllWorkHRIS.Host.Hris.Repositories;
using AllWorkHRIS.Host.Hris.Services;

namespace AllWorkHRIS.Host.Hris.Jobs;

public sealed class DocumentExpirationCheckJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DocumentExpirationCheckJob> _logger;

    public DocumentExpirationCheckJob(
        IServiceScopeFactory scopeFactory,
        ILogger<DocumentExpirationCheckJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "DocumentExpirationCheckJob cycle failed.");
            }

            await Task.Delay(TimeSpan.FromHours(24), ct);
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var temporalContext  = scope.ServiceProvider.GetRequiredService<ITemporalContext>();
        var lookupCache      = scope.ServiceProvider.GetRequiredService<ILookupCache>();
        var documentService  = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        var documentRepo     = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var workQueueService = scope.ServiceProvider.GetRequiredService<IWorkQueueService>();

        var operativeDate = DateOnly.FromDateTime(temporalContext.GetOperativeDate());

        // Raise / escalate expiration alerts for documents expiring within 90 days
        var expiring = await documentService.GetExpiringDocumentsAsync(withinDays: 90, operativeDate);
        foreach (var doc in expiring)
        {
            try
            {
                var docTypeCode = lookupCache.GetCode(LookupTables.DocumentType, doc.DocumentTypeId);
                await workQueueService.EnsureExpirationAlertAsync(
                    doc, operativeDate, docTypeCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to process expiration alert for document {Id}.", doc.DocumentId);
            }
        }

        // Transition overdue active documents to EXPIRED and close their work queue alerts
        var expired = await documentRepo.GetExpiredAsOfAsync(operativeDate);
        foreach (var doc in expired)
        {
            try
            {
                await documentService.ExpireDocumentAsync(doc.DocumentId);
                await workQueueService.ResolveByReferenceAsync(doc.DocumentId, Guid.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to expire document {Id}.", doc.DocumentId);
            }
        }
    }
}
