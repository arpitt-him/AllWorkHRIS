namespace AllWorkHRIS.Core.Pipeline;

// Implemented by AllWorkHRIS.Module.Benefits.
// PayrollPipelineService calls all registered implementations when assembling
// the step list, merging employee-specific benefit steps into the jurisdiction steps.
public interface IBenefitStepProvider
{
    Task<IReadOnlyList<ICalculationStep>> GetStepsForEmployeeAsync(
        PipelineRequest   request,
        CancellationToken ct = default);
}
