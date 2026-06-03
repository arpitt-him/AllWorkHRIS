using AllWorkHRIS.Module.Payroll.Services;

namespace AllWorkHRIS.Host.Tests.TestSupport;

/// <summary>
/// No-op <see cref="IPayrollCorrectionService"/> for tests that construct <c>PayrollRunService</c>
/// directly but never exercise the reverse/correction path (ADR-027).
/// </summary>
internal sealed class NullPayrollCorrectionService : IPayrollCorrectionService
{
    public Task<ReversalOutcome> ReverseRunAsync(ReverseRunRequest request, CancellationToken ct = default)
        => Task.FromResult(new ReversalOutcome
        {
            RunId = request.RunId,
            AlreadyReversed = false,
            ReversedResultIds = []
        });
}
