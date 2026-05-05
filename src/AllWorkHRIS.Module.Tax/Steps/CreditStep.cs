using AllWorkHRIS.Core.Pipeline;

namespace AllWorkHRIS.Module.Tax.Steps;

public sealed class CreditStep : ICalculationStep
{
    public string        StepCode       { get; }
    public int           SequenceNumber { get; }
    public StepAppliesTo AppliesTo      => StepAppliesTo.Employee;

    private readonly decimal _annualCredit;
    private readonly decimal _creditRate;  // credit value = annualCredit × creditRate

    public CreditStep(string stepCode, int sequenceNumber, decimal annualCredit, decimal creditRate)
    {
        StepCode       = stepCode;
        SequenceNumber = sequenceNumber;
        _annualCredit  = annualCredit;
        _creditRate    = creditRate;
    }

    public Task<CalculationContext> ExecuteAsync(CalculationContext ctx, CancellationToken ct = default)
    {
        if (ctx.ExemptFlag) return Task.FromResult(ctx);
        var periodCreditValue = _annualCredit * _creditRate / ctx.PayPeriodsPerYear;
        // Non-refundable: credit cannot reduce computed tax below zero
        var effectiveCredit = Math.Min(periodCreditValue, ctx.ComputedTax);
        return Task.FromResult(ctx.WithStepResult(StepCode, -effectiveCredit));
    }
}
