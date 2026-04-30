using AllWorkHRIS.Core.Events;

namespace AllWorkHRIS.Module.Payroll.Domain.Events;

public sealed class TerminationEventHandler
{
    public Task HandleAsync(TerminationEventPayload payload)
    {
        // TODO: flag open payroll results for final pay processing
        return Task.CompletedTask;
    }
}
