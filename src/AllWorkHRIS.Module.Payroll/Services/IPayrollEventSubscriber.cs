using AllWorkHRIS.Core.Events;

namespace AllWorkHRIS.Module.Payroll.Services;

/// <summary>
/// Registers all payroll-side HRIS event handlers on the InProcessEventBus.
/// Called once from PayrollModule.Register after the bus is resolved.
/// </summary>
public interface IPayrollEventSubscriber
{
    void RegisterHandlers(IEventPublisher eventPublisher);
}
