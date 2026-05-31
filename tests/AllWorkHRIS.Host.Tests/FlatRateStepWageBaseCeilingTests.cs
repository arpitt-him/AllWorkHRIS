using System.Collections.Immutable;
using AllWorkHRIS.Core.Pipeline;
using AllWorkHRIS.Module.Tax.Steps;
using Xunit;

namespace AllWorkHRIS.Host.Tests;

/// <summary>
/// Unit tests for the FlatRateStep wage CEILING (ADR-019 / Phase 12.8.2) — Social
/// Security's exact YTD fill-to-cap. Pure, no DB: drives the YTD-cumulative ceiling
/// directly. SS taxes 6.2% of wages until cumulative YTD SS wages reach the $176,100
/// base, then nothing — replacing the old per-period proration of the base.
/// </summary>
public sealed class FlatRateStepWageBaseCeilingTests
{
    private const decimal SsRate     = 0.062m;
    private const decimal SsWageBase = 176_100m;

    // Social Security with a linked YTD wage-base accumulator → exact fill-to-cap.
    private static FlatRateStep SocialSecurity() => new(
        "US_FED_SS", 510, StepAppliesTo.Employee,
        rate: SsRate, wageBase: SsWageBase, periodCap: null, annualCap: null,
        wageThreshold: null, wageBaseAccumulatorCode: "US_FED_SS_WAGES",
        useFicaTaxableWages: true);

    private static CalculationContext Ctx(decimal periodFicaWages, decimal ytdSsWages) => new()
    {
        PayPeriodsPerYear = 26,
        FicaTaxableWages  = periodFicaWages,
        YtdBalances       = ImmutableDictionary<string, decimal>.Empty
            .SetItem("US_FED_SS_WAGES", ytdSsWages),
        ExemptFlag        = false,
    };

    private static async Task<decimal> RunAsync(FlatRateStep step, CalculationContext ctx)
    {
        var result = await step.ExecuteAsync(ctx);
        return result.StepResults.TryGetValue("US_FED_SS", out var amt) ? amt : 0m;
    }

    [Fact]
    public async Task BelowCap_TaxesFullPeriodWages()
    {
        // YTD 100k + 10k = 110k < 176.1k → full 10k taxed: 10000 * 6.2% = 620.00.
        Assert.Equal(620.00m, await RunAsync(SocialSecurity(), Ctx(periodFicaWages: 10_000m, ytdSsWages: 100_000m)));
    }

    [Fact]
    public async Task CrossingCap_FillsExactlyToTheWageBase()
    {
        // YTD 170k + 10k would be 180k, but only 6,100 of room remains to 176,100:
        // 6100 * 6.2% = 378.20 (not the full 620) — exact, not prorated.
        Assert.Equal(378.20m, await RunAsync(SocialSecurity(), Ctx(periodFicaWages: 10_000m, ytdSsWages: 170_000m)));
    }

    [Fact]
    public async Task AtOrAboveCap_TaxesNothing()
    {
        // Already past the wage base → no room → zero SS this period.
        Assert.Equal(0m, await RunAsync(SocialSecurity(), Ctx(periodFicaWages: 10_000m, ytdSsWages: 180_000m)));
    }

    [Fact]
    public async Task NoLinkedAccumulator_FallsBackToPerPeriodProration()
    {
        // A wage-based step WITHOUT a linked YTD accumulator keeps the legacy proration:
        // base = min(periodWages, wageBase / periods). 120,000 / 12 = 10,000 →
        // min(15,000, 10,000) = 10,000 → 10000 * 6.2% = 620.00.
        var legacy = new FlatRateStep(
            "US_FED_SS", 510, StepAppliesTo.Employee,
            rate: SsRate, wageBase: 120_000m, periodCap: null, annualCap: null,
            useFicaTaxableWages: true);
        var ctx = new CalculationContext
        {
            PayPeriodsPerYear = 12,
            FicaTaxableWages  = 15_000m,
            YtdBalances       = ImmutableDictionary<string, decimal>.Empty,
            ExemptFlag        = false,
        };
        var result = await legacy.ExecuteAsync(ctx);
        Assert.Equal(620.00m, result.StepResults["US_FED_SS"]);
    }
}
