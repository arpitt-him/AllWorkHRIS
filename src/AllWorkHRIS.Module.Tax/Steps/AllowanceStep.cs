using AllWorkHRIS.Core.Pipeline;

namespace AllWorkHRIS.Module.Tax.Steps;

public sealed class AllowanceStep : ICalculationStep
{
    public string        StepCode       { get; }
    public int           SequenceNumber { get; }
    public StepAppliesTo AppliesTo      => StepAppliesTo.Employee;

    private readonly decimal _annualAmountPerAllowance;

    public AllowanceStep(string stepCode, int sequenceNumber, decimal annualAmountPerAllowance)
    {
        StepCode                  = stepCode;
        SequenceNumber            = sequenceNumber;
        _annualAmountPerAllowance = annualAmountPerAllowance;
    }

    public Task<CalculationContext> ExecuteAsync(CalculationContext ctx, CancellationToken ct = default)
    {
        if (ctx.ExemptFlag || ctx.AllowanceCount == 0) return Task.FromResult(ctx);
        var periodDeduction = ctx.AllowanceCount * _annualAmountPerAllowance / ctx.PayPeriodsPerYear;
        return Task.FromResult(ctx.WithReducedIncomeTaxableWages(periodDeduction));
    }
}
