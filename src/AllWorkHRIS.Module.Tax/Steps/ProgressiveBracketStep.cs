using AllWorkHRIS.Core.Pipeline;
using AllWorkHRIS.Module.Tax.Queries;

namespace AllWorkHRIS.Module.Tax.Steps;

public sealed class ProgressiveBracketStep : ICalculationStep
{
    public string        StepCode       { get; }
    public int           SequenceNumber { get; }
    public StepAppliesTo AppliesTo      => StepAppliesTo.Employee;

    private readonly IReadOnlyList<BracketRow> _brackets;  // ordered by lower_limit ascending

    public ProgressiveBracketStep(string stepCode, int sequenceNumber, IReadOnlyList<BracketRow> brackets)
    {
        StepCode       = stepCode;
        SequenceNumber = sequenceNumber;
        _brackets      = brackets;
    }

    public Task<CalculationContext> ExecuteAsync(CalculationContext ctx, CancellationToken ct = default)
    {
        if (ctx.ExemptFlag) return Task.FromResult(ctx);

        // Annualise income-taxable wages; adjust with form-specific other income and deductions
        var annualTaxable = ctx.IncomeTaxableWages * ctx.PayPeriodsPerYear
                            + ctx.OtherIncomeAmount
                            - ctx.DeductionsAmount;
        annualTaxable = Math.Max(0, annualTaxable);

        var annualTax = 0m;
        foreach (var bracket in _brackets)
        {
            if (annualTaxable <= bracket.LowerLimit) break;
            var top    = bracket.UpperLimit.HasValue
                ? Math.Min(annualTaxable, bracket.UpperLimit.Value)
                : annualTaxable;
            var slice  = top - bracket.LowerLimit;
            annualTax += slice * bracket.Rate;
        }

        // Subtract form-declared credits from annual tax (non-refundable: floor at 0)
        annualTax = Math.Max(0, annualTax - ctx.CreditsAmount);

        var periodTax = annualTax / ctx.PayPeriodsPerYear + ctx.AdditionalWithholding;
        return Task.FromResult(ctx.WithStepResult(StepCode, Math.Max(0, periodTax)));
    }
}
