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

    public string        StepCode       { get; }
    public int           SequenceNumber { get; }
    public StepAppliesTo AppliesTo      => StepAppliesTo.Both;

    public PreTaxBenefitStep(
        string  stepCode,
        int     sequenceNumber,
        decimal employeeAmount,
        decimal? employerAmount,
        bool    reducesIncomeTax,
        bool    reducesFica)
    {
        StepCode       = stepCode;
        SequenceNumber = sequenceNumber;
        _employeeAmount   = employeeAmount;
        _employerAmount   = employerAmount;
        _reducesIncomeTax = reducesIncomeTax;
        _reducesFica      = reducesFica;
    }

    public Task<CalculationContext> ExecuteAsync(CalculationContext ctx, CancellationToken ct = default)
    {
        if (_reducesIncomeTax) ctx = ctx.WithReducedIncomeTaxableWages(_employeeAmount);
        if (_reducesFica)      ctx = ctx.WithReducedFicaTaxableWages(_employeeAmount);

        // Record employee deduction as a negative net-pay impact (not via WithStepResult — no ComputedTax)
        ctx = ctx with { NetPay = ctx.NetPay - _employeeAmount };
        ctx = ctx with
        {
            StepResults = ctx.StepResults.SetItem(StepCode, _employeeAmount)
        };

        // Record employer contribution if present
        if (_employerAmount.HasValue && _employerAmount.Value > 0)
            ctx = ctx.WithEmployerStepResult(StepCode + "_ER", _employerAmount.Value);

        return Task.FromResult(ctx);
    }
}
