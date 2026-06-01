using AllWorkHRIS.Core;
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
    private readonly decimal? _combinedDeferralLimit;
    private readonly IReadOnlyList<string>? _deferralMemberCodes;

    public string        StepCode       { get; }
    public int           SequenceNumber { get; }
    public StepAppliesTo AppliesTo      => StepAppliesTo.Employee;

    public PostTaxPctBenefitStep(
        string  stepCode,
        int     sequenceNumber,
        decimal rate,
        decimal coverageFraction,
        decimal? combinedDeferralLimit = null,
        IReadOnlyList<string>? deferralMemberCodes = null)
    {
        StepCode          = stepCode;
        SequenceNumber    = sequenceNumber;
        _rate             = rate;
        _coverageFraction = coverageFraction;
        _combinedDeferralLimit = combinedDeferralLimit;
        _deferralMemberCodes   = deferralMemberCodes;
    }

    public Task<CalculationContext> ExecuteAsync(CalculationContext ctx, CancellationToken ct = default)
    {
        // IncomeTaxableWages has been reduced by pre-tax deductions; this is the
        // post-deduction gross base for Roth and other post-tax percentage contributions.
        var amount = Money.Round(ctx.IncomeTaxableWages * _rate * _coverageFraction);

        // §402(g) combined limit (ADR-021): Roth takes only the headroom left after this period's
        // pre-tax deferral (already posted to StepResults) and prior-run member YTD.
        if (_combinedDeferralLimit.HasValue && _deferralMemberCodes is not null)
            amount = DeferralLimitClamp.Apply(
                amount, _combinedDeferralLimit.Value, _deferralMemberCodes, ctx);

        ctx = ctx with
        {
            NetPay      = ctx.NetPay - amount,
            StepResults = ctx.StepResults.SetItem(StepCode, amount)
        };

        return Task.FromResult(ctx);
    }
}
