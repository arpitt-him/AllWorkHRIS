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
    private readonly bool     _useFicaTaxableWages;  // true for SOCIAL_INSURANCE steps

    public FlatRateStep(string stepCode, int sequenceNumber, StepAppliesTo appliesTo,
        decimal rate, decimal? wageBase, decimal? periodCap, decimal? annualCap,
        bool useFicaTaxableWages = false)
    {
        StepCode             = stepCode;
        SequenceNumber       = sequenceNumber;
        AppliesTo            = appliesTo;
        _rate                = rate;
        _wageBase            = wageBase;
        _periodCap           = periodCap;
        _annualCap           = annualCap;
        _useFicaTaxableWages = useFicaTaxableWages;
    }

    public Task<CalculationContext> ExecuteAsync(CalculationContext ctx, CancellationToken ct = default)
    {
        if (ctx.ExemptFlag) return Task.FromResult(ctx);

        var periodWages = _useFicaTaxableWages ? ctx.FicaTaxableWages : ctx.IncomeTaxableWages;

        var base_ = _wageBase.HasValue
            ? Math.Min(periodWages, _wageBase.Value / ctx.PayPeriodsPerYear)
            : periodWages;

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
