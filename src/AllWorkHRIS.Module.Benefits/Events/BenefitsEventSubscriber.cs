using AllWorkHRIS.Core.Events;
using AllWorkHRIS.Module.Benefits.Services;
using Microsoft.Extensions.Logging;

namespace AllWorkHRIS.Module.Benefits.Events;

public sealed class BenefitsEventSubscriber : IEventSubscriber
{
    private readonly IBenefitElectionService             _electionService;
    private readonly ILogger<BenefitsEventSubscriber>    _logger;

    public BenefitsEventSubscriber(
        IBenefitElectionService          electionService,
        ILogger<BenefitsEventSubscriber> logger)
    {
        _electionService = electionService;
        _logger          = logger;
    }

    public void RegisterHandlers(IEventPublisher eventPublisher)
    {
        eventPublisher.RegisterHandler<TerminationEventPayload>(HandleTerminationAsync);
        eventPublisher.RegisterHandler<LeaveApprovedPayload>(HandleLeaveApprovedAsync);
        eventPublisher.RegisterHandler<ReturnToWorkPayload>(HandleReturnToWorkAsync);
    }

    private async Task HandleTerminationAsync(TerminationEventPayload payload)
    {
        _logger.LogInformation(
            "Benefits — terminating elections for employment={EmploymentId} event={EventId}",
            payload.EmploymentId, payload.EventId);

        await _electionService.TerminateAllActiveAsync(payload.EmploymentId, payload.EventId);
    }

    private async Task HandleLeaveApprovedAsync(LeaveApprovedPayload payload)
    {
        // Only suspend elections for unpaid leave — paid leave does not interrupt deductions
        if (!string.Equals(payload.PayrollImpactType, "UNPAID", StringComparison.OrdinalIgnoreCase))
            return;

        var eventId = payload.LeaveRequestId;
        _logger.LogInformation(
            "Benefits — suspending elections for employment={EmploymentId} leave={LeaveRequestId}",
            payload.EmploymentId, payload.LeaveRequestId);

        await _electionService.SuspendAllActiveAsync(payload.EmploymentId, eventId);
    }

    private async Task HandleReturnToWorkAsync(ReturnToWorkPayload payload)
    {
        _logger.LogInformation(
            "Benefits — reinstating elections for employment={EmploymentId} leave={LeaveRequestId}",
            payload.EmploymentId, payload.LeaveRequestId);

        await _electionService.ReinstateAllSuspendedAsync(payload.EmploymentId, payload.LeaveRequestId);
    }
}
