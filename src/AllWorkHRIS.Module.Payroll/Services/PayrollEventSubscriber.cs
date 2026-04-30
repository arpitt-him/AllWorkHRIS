using AllWorkHRIS.Core.Events;
using AllWorkHRIS.Module.Payroll.Domain.Events;

namespace AllWorkHRIS.Module.Payroll.Services;

public sealed class PayrollEventSubscriber : IPayrollEventSubscriber, IEventSubscriber
{
    private readonly HireEventHandler           _hireHandler;
    private readonly TerminationEventHandler    _terminationHandler;
    private readonly CompensationChangeHandler  _compensationHandler;
    private readonly LeaveApprovedHandler       _leaveHandler;

    public PayrollEventSubscriber(
        HireEventHandler          hireHandler,
        TerminationEventHandler   terminationHandler,
        CompensationChangeHandler compensationHandler,
        LeaveApprovedHandler      leaveHandler)
    {
        _hireHandler         = hireHandler;
        _terminationHandler  = terminationHandler;
        _compensationHandler = compensationHandler;
        _leaveHandler        = leaveHandler;
    }

    public void RegisterHandlers(IEventPublisher eventPublisher)
    {
        eventPublisher.RegisterHandler<HireEventPayload>(
            p => _hireHandler.HandleAsync(p));

        eventPublisher.RegisterHandler<TerminationEventPayload>(
            p => _terminationHandler.HandleAsync(p));

        eventPublisher.RegisterHandler<CompensationChangeEventPayload>(
            p => _compensationHandler.HandleAsync(p));

        eventPublisher.RegisterHandler<LeaveApprovedPayload>(
            p => _leaveHandler.HandleAsync(p));
    }
}
