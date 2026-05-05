namespace AllWorkHRIS.Core.Pipeline;

public interface IPayrollPipelineService
{
    Task<PipelineResult> RunAsync(PipelineRequest request, CancellationToken ct = default);
}
