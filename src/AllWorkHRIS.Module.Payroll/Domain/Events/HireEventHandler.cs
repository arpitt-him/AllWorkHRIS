using Autofac;
using AllWorkHRIS.Core.Events;
using AllWorkHRIS.Module.Payroll.Domain.Profile;
using AllWorkHRIS.Module.Payroll.Repositories;

namespace AllWorkHRIS.Module.Payroll.Domain.Events;

public sealed class HireEventHandler
{
    private readonly ILifetimeScope _rootScope;

    public HireEventHandler(ILifetimeScope rootScope) => _rootScope = rootScope;

    public async Task HandleAsync(HireEventPayload payload)
    {
        if (payload.PayrollContextId is null)
            return;

        await using var scope = _rootScope.BeginLifetimeScope();
        var repo = scope.Resolve<IPayrollProfileRepository>();

        var now = DateTimeOffset.UtcNow;
        var profile = new PayrollProfile
        {
            PayrollProfileId    = Guid.NewGuid(),
            EmploymentId        = payload.EmploymentId,
            PersonId            = payload.PersonId,
            PayrollContextId    = payload.PayrollContextId.Value,
            EnrollmentStatus    = "ACTIVE",
            EffectiveStartDate  = payload.EffectiveDate,
            EffectiveEndDate    = null,
            FinalPayFlag        = false,
            EnrollmentSource    = "AUTO_HIRE",
            CreatedBy           = payload.EventId,
            CreationTimestamp   = now,
            LastUpdatedBy       = payload.EventId,
            LastUpdateTimestamp = now
        };

        await repo.InsertAsync(profile);
    }
}
