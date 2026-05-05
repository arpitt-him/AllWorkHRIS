using AllWorkHRIS.Core.Pipeline;

namespace AllWorkHRIS.Module.Tax.Steps;

public sealed class StandardDeductionStep : ICalculationStep
{
    public string        StepCode       { get; }
    public int           SequenceNumber { get; }
    public StepAppliesTo AppliesTo      => StepAppliesTo.Employee;

    private readonly decimal _annualAmount;

    public StandardDeductionStep(string stepCode, int sequenceNumber, decimal annualAmount)
    {
        StepCode       = stepCode;
        SequenceNumber = sequenceNumber;
        _annualAmount  = annualAmount;
    }

    public Task<CalculationContext> ExecuteAsync(CalculationContext ctx, CancellationToken ct = default)
    {
        if (ctx.ExemptFlag) return Task.FromResult(ctx);
        var periodDeduction = _annualAmount / ctx.PayPeriodsPerYear;
        return Task.FromResult(ctx.WithReducedIncomeTaxableWages(periodDeduction));
    }
}
