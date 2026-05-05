using AllWorkHRIS.Core.Pipeline;

namespace AllWorkHRIS.Module.Benefits.Steps;

/// <summary>
/// Sequence 800–899. PCT_POST_TAX mode — computes deduction as a percentage of
/// post-deduction gross (IncomeTaxableWages after all pre-tax steps) at execute time.
/// The rate and coverage fraction are fixed at step construction; the base is read
/// from context so the amount reflects whatever pre-tax deductions ran before this step.
/// </summary>
public sealed class PostTaxPctBenefitStep : ICalculationStep
{
    private readonly decimal _rate;
    private readonly decimal _coverageFraction;

    public string        StepCode       { get; }
    public int           SequenceNumber { get; }
    public StepAppliesTo AppliesTo      => StepAppliesTo.Employee;

    public PostTaxPctBenefitStep(
        string  stepCode,
        int     sequenceNumber,
        decimal rate,
        decimal coverageFraction)
    {
        StepCode          = stepCode;
        SequenceNumber    = sequenceNumber;
        _rate             = rate;
        _coverageFraction = coverageFraction;
    }

    public Task<CalculationContext> ExecuteAsync(CalculationContext ctx, CancellationToken ct = default)
    {
        // IncomeTaxableWages has been reduced by pre-tax deductions; this is the
        // post-deduction gross base for Roth and other post-tax percentage contributions.
        var amount = Math.Round(ctx.IncomeTaxableWages * _rate * _coverageFraction, 4);

        ctx = ctx with
        {
            NetPay      = ctx.NetPay - amount,
            StepResults = ctx.StepResults.SetItem(StepCode, amount)
        };

        return Task.FromResult(ctx);
    }
}
