using AllWorkHRIS.Core.Events;

namespace AllWorkHRIS.Module.Payroll.Domain.Events;

public sealed class CompensationChangeHandler
{
    public Task HandleAsync(CompensationChangeEventPayload payload)
    {
        // TODO: if IsRetroactive, flag affected closed periods for retro recalculation
        return Task.CompletedTask;
    }
}
