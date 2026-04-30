using AllWorkHRIS.Core.Events;

namespace AllWorkHRIS.Module.Payroll.Domain.Events;

public sealed class LeaveApprovedHandler
{
    public Task HandleAsync(LeaveApprovedPayload payload)
    {
        // TODO: record leave period against payroll context for pay impact (UNPAID, REDUCED, FULL)
        return Task.CompletedTask;
    }
}
