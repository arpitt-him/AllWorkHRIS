using AllWorkHRIS.Core.Pipeline;

namespace AllWorkHRIS.Module.Tax.Steps;

public sealed class FlatRateStep : ICalculationStep
{
    public string        StepCode       { get; }
    public int           SequenceNumber { get; }
    public StepAppliesTo AppliesTo      { get; }

    private readonly decimal  _rate;
    private readonly decimal? _wageBase;    // annual wage ceiling
    private readonly decimal? _periodCap;
    private readonly decimal? _annualCap;
    private readonly decimal? _wageThreshold;            // annual wage FLOOR — tax only above it (Phase 12.8)
    private readonly string?  _wageBaseAccumulatorCode;  // YTD taxable-wage accumulator the floor/ceiling reads
    private readonly bool     _useFicaTaxableWages;  // true for SOCIAL_INSURANCE steps

    public FlatRateStep(string stepCode, int sequenceNumber, StepAppliesTo appliesTo,
        decimal rate, decimal? wageBase, decimal? periodCap, decimal? annualCap,
        decimal? wageThreshold = null, string? wageBaseAccumulatorCode = null,
        bool useFicaTaxableWages = false)
    {
        StepCode                 = stepCode;
        SequenceNumber           = sequenceNumber;
        AppliesTo                = appliesTo;
        _rate                    = rate;
        _wageBase                = wageBase;
        _periodCap               = periodCap;
        _annualCap               = annualCap;
        _wageThreshold           = wageThreshold;
        _wageBaseAccumulatorCode = wageBaseAccumulatorCode;
        _useFicaTaxableWages     = useFicaTaxableWages;
    }

    public Task<CalculationContext> ExecuteAsync(CalculationContext ctx, CancellationToken ct = default)
    {
        if (ctx.ExemptFlag) return Task.FromResult(ctx);

        var periodWages = _useFicaTaxableWages ? ctx.FicaTaxableWages : ctx.IncomeTaxableWages;

        var base_ = _wageBase.HasValue
            ? Math.Min(periodWages, _wageBase.Value / ctx.PayPeriodsPerYear)
            : periodWages;

        // Annual wage FLOOR (Phase 12.8 — e.g. Additional Medicare $200K). Tax only the part
        // of THIS period's wages that pushes cumulative YTD past the threshold, read from the
        // linked YTD taxable-wage accumulator (prior approved periods). Exact, not prorated.
        if (_wageThreshold.HasValue)
        {
            var ytdWages = _wageBaseAccumulatorCode is not null
                        && ctx.YtdBalances.TryGetValue(_wageBaseAccumulatorCode, out var w) ? w : 0m;
            var overThreshold = Math.Max(0m, (ytdWages + periodWages) - _wageThreshold.Value)
                              - Math.Max(0m, ytdWages - _wageThreshold.Value);
            base_ = Math.Min(base_, overThreshold);
        }

        var raw = base_ * _rate;

        if (_periodCap.HasValue)
            raw = Math.Min(raw, _periodCap.Value);

        if (_annualCap.HasValue && ctx.YtdBalances.TryGetValue(StepCode, out var ytd))
        {
            var remaining = Math.Max(0, _annualCap.Value - ytd);
            raw = Math.Min(raw, remaining);
        }

        var amount = Math.Max(0, raw);
        var next = AppliesTo switch
        {
            StepAppliesTo.Employer => ctx.WithEmployerStepResult(StepCode, amount),
            StepAppliesTo.Both     => ctx.WithBothStepResult(StepCode, amount),
            _                      => ctx.WithStepResult(StepCode, amount)
        };

        return Task.FromResult(next);
    }
}
