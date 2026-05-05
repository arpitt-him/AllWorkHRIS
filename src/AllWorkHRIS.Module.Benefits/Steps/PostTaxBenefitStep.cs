using AllWorkHRIS.Core.Pipeline;

namespace AllWorkHRIS.Module.Benefits.Steps;

/// <summary>
/// Sequence 800–899. Subtracts election amount from NetPay after all tax is computed.
/// Does not affect IncomeTaxableWages or FicaTaxableWages.
/// </summary>
public sealed class PostTaxBenefitStep : ICalculationStep
{
    private readonly decimal  _employeeAmount;
    private readonly decimal? _employerAmount;

    public string        StepCode       { get; }
    public int           SequenceNumber { get; }
    public StepAppliesTo AppliesTo      => StepAppliesTo.Both;

    public PostTaxBenefitStep(
        string   stepCode,
        int      sequenceNumber,
        decimal  employeeAmount,
        decimal? employerAmount)
    {
        StepCode       = stepCode;
        SequenceNumber = sequenceNumber;
        _employeeAmount = employeeAmount;
        _employerAmount = employerAmount;
    }

    public Task<CalculationContext> ExecuteAsync(CalculationContext ctx, CancellationToken ct = default)
    {
        ctx = ctx with
        {
            NetPay      = ctx.NetPay - _employeeAmount,
            StepResults = ctx.StepResults.SetItem(StepCode, _employeeAmount)
        };

        if (_employerAmount.HasValue && _employerAmount.Value > 0)
            ctx = ctx.WithEmployerStepResult(StepCode + "_ER", _employerAmount.Value);

        return Task.FromResult(ctx);
    }
}
