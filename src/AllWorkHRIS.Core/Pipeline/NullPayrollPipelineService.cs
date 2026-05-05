using System.Collections.Immutable;

namespace AllWorkHRIS.Core.Pipeline;

// Host fallback — registered when AllWorkHRIS.Module.Tax is absent.
// Returns gross = net with zero withholding so the payroll engine degrades
// gracefully rather than failing.
public sealed class NullPayrollPipelineService : IPayrollPipelineService
{
    public Task<PipelineResult> RunAsync(PipelineRequest request, CancellationToken ct = default)
        => Task.FromResult(new PipelineResult
        {
            Succeeded      = true,
            GrossPayPeriod = request.GrossPayPeriod,
            NetPay         = request.GrossPayPeriod,
            ComputedTax    = 0m,
            EmployerCost   = 0m,
            StepResults    = ImmutableDictionary<string, decimal>.Empty
        });
}
