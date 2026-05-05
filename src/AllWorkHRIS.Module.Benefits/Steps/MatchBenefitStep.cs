using AllWorkHRIS.Core.Pipeline;

namespace AllWorkHRIS.Module.Benefits.Steps;

/// <summary>
/// Sequence 110–199 (immediately after the employee PCT contribution step).
/// Computes the employer match on the employee's per-period contribution.
/// The employee step result is read from StepResults at execute time so the match
/// applies to whatever the employee actually contributed this period (after any cap).
/// periodCap is the per-period ceiling on the matched contribution amount — derived
/// from the match rule's MatchCapPctOfGross or MatchCapAnnualAmount / pay_periods.
/// </summary>
public sealed class MatchBenefitStep : ICalculationStep
{
    private readonly string  _eeStepCode;
    private readonly decimal _matchRate;
    private readonly decimal _periodCap;

    public string        StepCode       { get; }
    public int           SequenceNumber { get; }
    public StepAppliesTo AppliesTo      => StepAppliesTo.Employer;

    public MatchBenefitStep(
        string  stepCode,
        int     sequenceNumber,
        string  eeStepCode,
        decimal matchRate,
        decimal periodCap)
    {
        StepCode       = stepCode;
        SequenceNumber = sequenceNumber;
        _eeStepCode    = eeStepCode;
        _matchRate     = matchRate;
        _periodCap     = periodCap;
    }

    public Task<CalculationContext> ExecuteAsync(CalculationContext ctx, CancellationToken ct = default)
    {
        ctx.StepResults.TryGetValue(_eeStepCode, out var eeContribution);
        var matchable   = Math.Min(eeContribution, _periodCap);
        var matchAmount = Math.Round(matchable * _matchRate, 4);
        ctx = ctx.WithEmployerStepResult(StepCode, matchAmount);
        return Task.FromResult(ctx);
    }
}
