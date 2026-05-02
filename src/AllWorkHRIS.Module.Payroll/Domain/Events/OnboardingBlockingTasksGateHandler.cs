using Autofac;
using AllWorkHRIS.Core.Events;
using AllWorkHRIS.Module.Payroll.Repositories;

namespace AllWorkHRIS.Module.Payroll.Domain.Events;

public sealed class OnboardingBlockingTasksGateHandler
{
    private readonly ILifetimeScope _rootScope;

    public OnboardingBlockingTasksGateHandler(ILifetimeScope rootScope)
        => _rootScope = rootScope;

    public async Task HandleAsync(OnboardingBlockingTasksCompletePayload payload)
    {
        await using var scope = _rootScope.BeginLifetimeScope();
        var repo = scope.Resolve<IPayrollProfileRepository>();
        await repo.SetBlockingTasksClearedAsync(payload.EmploymentId, payload.OnboardingPlanId);
    }
}
