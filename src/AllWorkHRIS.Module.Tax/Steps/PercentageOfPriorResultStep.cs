using AllWorkHRIS.Core.Pipeline;

namespace AllWorkHRIS.Module.Tax.Steps;

public sealed class PercentageOfPriorResultStep : ICalculationStep
{
    public string        StepCode       { get; }
    public int           SequenceNumber { get; }
    public StepAppliesTo AppliesTo      => StepAppliesTo.Employee;

    private readonly string  _dependsOnStepCode;
    private readonly decimal _rate;

    public PercentageOfPriorResultStep(string stepCode, int sequenceNumber,
        string dependsOnStepCode, decimal rate)
    {
        StepCode           = stepCode;
        SequenceNumber     = sequenceNumber;
        _dependsOnStepCode = dependsOnStepCode;
        _rate              = rate;
    }

    public Task<CalculationContext> ExecuteAsync(CalculationContext ctx, CancellationToken ct = default)
    {
        if (ctx.ExemptFlag) return Task.FromResult(ctx);
        if (!ctx.StepResults.TryGetValue(_dependsOnStepCode, out var priorAmount))
            return Task.FromResult(ctx);  // dependency step absent — skip silently
        var amount = priorAmount * _rate;
        return Task.FromResult(ctx.WithStepResult(StepCode, Math.Max(0, amount)));
    }
}
