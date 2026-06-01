using AllWorkHRIS.Core.Pipeline;

namespace AllWorkHRIS.Module.Benefits.Steps;

/// <summary>
/// Sequence 100–199. Reduces IncomeTaxableWages and/or FicaTaxableWages before any tax step runs.
/// Does not add to ComputedTax — the deduction is recorded as a separate benefit result line.
/// </summary>
public sealed class PreTaxBenefitStep : ICalculationStep
{
    private readonly decimal _employeeAmount;
    private readonly decimal? _employerAmount;
    private readonly bool   _reducesIncomeTax;
    private readonly bool   _reducesFica;
    private readonly decimal? _combinedDeferralLimit;
    private readonly IReadOnlyList<string>? _deferralMemberCodes;

    public string        StepCode       { get; }
    public int           SequenceNumber { get; }
    public StepAppliesTo AppliesTo      => StepAppliesTo.Both;

    public PreTaxBenefitStep(
        string  stepCode,
        int     sequenceNumber,
        decimal employeeAmount,
        decimal? employerAmount,
        bool    reducesIncomeTax,
        bool    reducesFica,
        decimal? combinedDeferralLimit = null,
        IReadOnlyList<string>? deferralMemberCodes = null)
    {
        StepCode       = stepCode;
        SequenceNumber = sequenceNumber;
        _employeeAmount   = employeeAmount;
        _employerAmount   = employerAmount;
        _reducesIncomeTax = reducesIncomeTax;
        _reducesFica      = reducesFica;
        _combinedDeferralLimit = combinedDeferralLimit;
        _deferralMemberCodes   = deferralMemberCodes;
    }

    public Task<CalculationContext> ExecuteAsync(CalculationContext ctx, CancellationToken ct = default)
    {
        // §402(g) combined limit (ADR-021): clamp the elective deferral to the headroom shared
        // across this group's members. Pre-tax runs first, so it fills the combined room before Roth.
        var employeeAmount = _employeeAmount;
        if (_combinedDeferralLimit.HasValue && _deferralMemberCodes is not null)
            employeeAmount = DeferralLimitClamp.Apply(
                employeeAmount, _combinedDeferralLimit.Value, _deferralMemberCodes, ctx);

        // Wage reductions and net pay must use the CLAMPED amount, not the elected amount.
        if (_reducesIncomeTax) ctx = ctx.WithReducedIncomeTaxableWages(employeeAmount);
        if (_reducesFica)      ctx = ctx.WithReducedFicaTaxableWages(employeeAmount);

        // Record employee deduction as a negative net-pay impact (not via WithStepResult — no ComputedTax)
        ctx = ctx with { NetPay = ctx.NetPay - employeeAmount };
        ctx = ctx with
        {
            StepResults = ctx.StepResults.SetItem(StepCode, employeeAmount)
        };

        // Employer contribution is governed by §415, not §402(g) — left unclamped here.
        if (_employerAmount.HasValue && _employerAmount.Value > 0)
            ctx = ctx.WithEmployerStepResult(StepCode + "_ER", _employerAmount.Value);

        return Task.FromResult(ctx);
    }
}
