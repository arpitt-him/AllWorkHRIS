using AllWorkHRIS.Core.Events;

namespace AllWorkHRIS.Module.Payroll.Domain.Events;

public sealed class HireEventHandler
{
    public Task HandleAsync(HireEventPayload payload)
    {
        // TODO: create payroll enrollment record for the new employee
        // PayrollContextId may be null if the employee is not yet assigned to a payroll context
        return Task.CompletedTask;
    }
}
