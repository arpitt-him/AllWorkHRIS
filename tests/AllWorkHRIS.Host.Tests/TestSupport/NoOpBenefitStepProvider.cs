using AllWorkHRIS.Core.Pipeline;

namespace AllWorkHRIS.Host.Tests.TestSupport;

/// <summary>
/// Test double for <see cref="IBenefitStepProvider"/>. The real implementation
/// lives in the Benefits module, which payroll-only engine tests do not load —
/// and unlike the hours / jurisdiction collaborators there is no Core "Null"
/// fallback for it. Returns no benefit steps; the payroll gate tests exercise
/// base earnings + the run lifecycle, not benefit deductions. (ToDo #36)
/// </summary>
internal sealed class NoOpBenefitStepProvider : IBenefitStepProvider
{
    public Task<IReadOnlyList<ICalculationStep>> GetStepsForEmployeeAsync(
        PipelineRequest request, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ICalculationStep>>([]);
}
