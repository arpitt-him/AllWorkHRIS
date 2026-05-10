using AllWorkHRIS.Core.Pipeline;
using AllWorkHRIS.Module.Tax.Queries;

namespace AllWorkHRIS.Module.Tax.Steps;

public sealed class TieredFlatStep : ICalculationStep
{
    public string        StepCode       { get; }
    public int           SequenceNumber { get; }
    public StepAppliesTo AppliesTo      { get; }

    private readonly IReadOnlyList<TieredBracketRow> _tiers;

    public TieredFlatStep(string stepCode, int sequenceNumber, StepAppliesTo appliesTo,
        IReadOnlyList<TieredBracketRow> tiers)
    {
        StepCode       = stepCode;
        SequenceNumber = sequenceNumber;
        AppliesTo      = appliesTo;
        _tiers         = tiers;
    }

    public Task<CalculationContext> ExecuteAsync(CalculationContext ctx, CancellationToken ct = default)
    {
        if (ctx.ExemptFlag) return Task.FromResult(ctx);

        // Annualise FICA-taxable wages for tier thresholds (social insurance applies to FICA wages)
        var annualGross = ctx.FicaTaxableWages * ctx.PayPeriodsPerYear;
        var total       = 0m;

        foreach (var tier in _tiers)
        {
            if (annualGross <= tier.LowerLimit) continue;
            var top   = tier.UpperLimit.HasValue ? Math.Min(annualGross, tier.UpperLimit.Value) : annualGross;
            var slice = top - tier.LowerLimit;
            var raw   = slice * tier.Rate;

            if (tier.PeriodCap.HasValue)
                raw = Math.Min(raw, tier.PeriodCap.Value * ctx.PayPeriodsPerYear);

            total += raw;

            if (tier.AnnualCap.HasValue)
                total = Math.Min(total, tier.AnnualCap.Value);
        }

        var periodAmount = total / ctx.PayPeriodsPerYear;

        // Apply annual cap via YTD balance
        var maxAnnualCap = _tiers.Select(t => t.AnnualCap).Where(c => c.HasValue).Max();
        if (maxAnnualCap.HasValue && ctx.YtdBalances.TryGetValue(StepCode, out var ytd))
            periodAmount = Math.Min(periodAmount, Math.Max(0, maxAnnualCap.Value - ytd));

        var amount = Math.Max(0, periodAmount);
        var next = AppliesTo switch
        {
            StepAppliesTo.Employer => ctx.WithEmployerStepResult(StepCode, amount),
            StepAppliesTo.Both     => ctx.WithBothStepResult(StepCode, amount),
            _                      => ctx.WithStepResult(StepCode, amount)
        };

        return Task.FromResult(next);
    }
}
