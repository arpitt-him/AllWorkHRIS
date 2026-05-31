using System.Collections.Immutable;
using AllWorkHRIS.Core.Pipeline;
using AllWorkHRIS.Module.Tax.Steps;
using Xunit;

namespace AllWorkHRIS.Host.Tests;

/// <summary>
/// Unit tests for the FlatRateStep wage FLOOR (ADR-019 / Phase 12.8.1) — Additional
/// Medicare's $200K threshold. Pure, no DB: drives the YTD-cumulative clamp directly.
/// </summary>
public sealed class FlatRateStepThresholdTests
{
    // Additional Medicare: 0.9% on Medicare wages above $200,000 cumulative YTD.
    private static FlatRateStep AdditionalMedicare() => new(
        "US_FED_MEDICARE_ADDL", 512, StepAppliesTo.Employee,
        rate: 0.009m, wageBase: null, periodCap: null, annualCap: null,
        wageThreshold: 200_000m, wageBaseAccumulatorCode: "US_FED_MEDICARE_WAGES",
        useFicaTaxableWages: true);

    private static CalculationContext Ctx(decimal periodFicaWages, decimal ytdMedicareWages) => new()
    {
        PayPeriodsPerYear = 26,
        FicaTaxableWages  = periodFicaWages,
        YtdBalances       = ImmutableDictionary<string, decimal>.Empty
            .SetItem("US_FED_MEDICARE_WAGES", ytdMedicareWages),
        ExemptFlag        = false,
    };

    private static async Task<decimal> RunAsync(FlatRateStep step, CalculationContext ctx)
    {
        var result = await step.ExecuteAsync(ctx);
        return result.StepResults.TryGetValue("US_FED_MEDICARE_ADDL", out var amt) ? amt : 0m;
    }

    [Fact]
    public async Task BelowThreshold_NoAdditionalMedicare()
    {
        // YTD 150k + 10k this period = 160k < 200k → nothing above the floor.
        Assert.Equal(0m, await RunAsync(AdditionalMedicare(), Ctx(periodFicaWages: 10_000m, ytdMedicareWages: 150_000m)));
    }

    [Fact]
    public async Task CrossingThreshold_TaxesOnlyThePortionAboveTheFloor()
    {
        // YTD 195k + 10k = 205k → only the 5k above 200k is taxed: 5000 * 0.9% = 45.00.
        Assert.Equal(45.00m, await RunAsync(AdditionalMedicare(), Ctx(periodFicaWages: 10_000m, ytdMedicareWages: 195_000m)));
    }

    [Fact]
    public async Task FullyAboveThreshold_TaxesTheWholePeriod()
    {
        // Already past 200k → the entire 10k period is above the floor: 10000 * 0.9% = 90.00.
        Assert.Equal(90.00m, await RunAsync(AdditionalMedicare(), Ctx(periodFicaWages: 10_000m, ytdMedicareWages: 210_000m)));
    }

    [Fact]
    public async Task NoThreshold_TaxesFullWages_FloorPathInert()
    {
        // A plain flat-rate step (no wage_threshold) is unchanged: taxes all wages.
        var regularMedicare = new FlatRateStep(
            "US_FED_MEDICARE", 511, StepAppliesTo.Employee,
            rate: 0.0145m, wageBase: null, periodCap: null, annualCap: null,
            useFicaTaxableWages: true);
        var result = await regularMedicare.ExecuteAsync(Ctx(periodFicaWages: 10_000m, ytdMedicareWages: 999_999m));
        Assert.Equal(145.00m, result.StepResults["US_FED_MEDICARE"]);
    }
}
