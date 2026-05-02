using Autofac;
using AllWorkHRIS.Core.Events;
using AllWorkHRIS.Module.Payroll.Repositories;

namespace AllWorkHRIS.Module.Payroll.Domain.Events;

/// <summary>
/// When the HRIS side creates an onboarding plan with no blocking tasks, immediately
/// clear the payroll gate so the employee is included in their first run without waiting
/// for an OnboardingBlockingTasksCompletePayload that will never arrive.
/// </summary>
public sealed class OnboardingPlanGateHandler
{
    private readonly ILifetimeScope _rootScope;

    public OnboardingPlanGateHandler(ILifetimeScope rootScope)
        => _rootScope = rootScope;

    public async Task HandleAsync(OnboardingPlanCreatedPayload payload)
    {
        if (payload.HasBlockingTasks)
            return;

        await using var scope = _rootScope.BeginLifetimeScope();
        var repo = scope.Resolve<IPayrollProfileRepository>();
        await repo.SetBlockingTasksClearedAsync(payload.EmploymentId, payload.OnboardingPlanId);
    }
}
