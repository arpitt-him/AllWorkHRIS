using Autofac;
using AllWorkHRIS.Core.Events;
using AllWorkHRIS.Module.Payroll.Repositories;

namespace AllWorkHRIS.Module.Payroll.Domain.Events;

public sealed class TerminationEventHandler
{
    private readonly ILifetimeScope _rootScope;

    public TerminationEventHandler(ILifetimeScope rootScope) => _rootScope = rootScope;

    public async Task HandleAsync(TerminationEventPayload payload)
    {
        await using var scope = _rootScope.BeginLifetimeScope();
        var repo = scope.Resolve<IPayrollProfileRepository>();

        var profile = await repo.GetByEmploymentIdAsync(payload.EmploymentId);
        if (profile is null)
            return;

        await repo.SetFinalPayFlagAsync(payload.EmploymentId, true, payload.EventId);
    }
}
