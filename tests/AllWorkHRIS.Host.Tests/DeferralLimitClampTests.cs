using System.Collections.Immutable;
using AllWorkHRIS.Core.Pipeline;
using AllWorkHRIS.Module.Benefits.Steps;
using Xunit;

namespace AllWorkHRIS.Host.Tests;

/// <summary>
/// Unit tests for the §402(g) combined elective-deferral clamp (ADR-021 / Phase 12.8.4a). Pure,
/// no DB: drives PreTaxBenefitStep and PostTaxPctBenefitStep directly with a combined limit and
/// the shared member set. The limit ($23,500 for 2026) spans 401K-PRE + 401K-ROTH as ONE cap —
/// a per-leg clamp would wrongly permit $47,000. Pre-tax runs first and fills the combined room;
/// Roth, reading the just-posted pre-tax deferral from context, takes only the remainder.
/// </summary>
public sealed class DeferralLimitClampTests
{
    private const decimal Limit = 23_500m;
    private static readonly IReadOnlyList<string> Members = ["401K-PRE", "401K-ROTH"];

    private static CalculationContext Ctx(
        decimal incomeWages = 100_000m,
        decimal ytdPre = 0m,
        decimal ytdRoth = 0m) => new()
    {
        PayPeriodsPerYear  = 26,
        GrossPayPeriod     = incomeWages,
        IncomeTaxableWages = incomeWages,
        FicaTaxableWages   = incomeWages,
        NetPay             = incomeWages,
        YtdBalances        = ImmutableDictionary<string, decimal>.Empty
            .SetItem("401K-PRE",  ytdPre)
            .SetItem("401K-ROTH", ytdRoth),
    };

    // ---- Pre-tax leg, in isolation -------------------------------------------------

    [Fact]
    public async Task PreTax_BelowCombinedLimit_TakesFullElectedAmount()
    {
        // No prior YTD, elect $1,000 pre-tax → full $1,000 (well under $23,500).
        var step = new PreTaxBenefitStep("401K-PRE", 110, 1_000m, null,
            reducesIncomeTax: true, reducesFica: false,
            combinedDeferralLimit: Limit, deferralMemberCodes: Members);

        var result = await step.ExecuteAsync(Ctx());

        Assert.Equal(1_000m, result.StepResults["401K-PRE"]);
    }

    [Fact]
    public async Task PreTax_CrossingCombinedLimit_FillsExactlyToRemainingRoom()
    {
        // YTD combined = 23,000 (all pre-tax). Elect $1,000 → only $500 of room remains.
        var step = new PreTaxBenefitStep("401K-PRE", 110, 1_000m, null,
            reducesIncomeTax: true, reducesFica: false,
            combinedDeferralLimit: Limit, deferralMemberCodes: Members);

        var result = await step.ExecuteAsync(Ctx(ytdPre: 23_000m));

        Assert.Equal(500m, result.StepResults["401K-PRE"]);
    }

    [Fact]
    public async Task PreTax_RothYtdConsumesRoom_PreTaxClampedAgainstCombined()
    {
        // The combined limit is shared: $23,000 of Roth YTD leaves only $500 for a pre-tax deferral.
        var step = new PreTaxBenefitStep("401K-PRE", 110, 1_000m, null,
            reducesIncomeTax: true, reducesFica: false,
            combinedDeferralLimit: Limit, deferralMemberCodes: Members);

        var result = await step.ExecuteAsync(Ctx(ytdRoth: 23_000m));

        Assert.Equal(500m, result.StepResults["401K-PRE"]);
    }

    [Fact]
    public async Task PreTax_AtCombinedLimit_TakesNothing()
    {
        var step = new PreTaxBenefitStep("401K-PRE", 110, 1_000m, null,
            reducesIncomeTax: true, reducesFica: false,
            combinedDeferralLimit: Limit, deferralMemberCodes: Members);

        var result = await step.ExecuteAsync(Ctx(ytdPre: 23_500m));

        Assert.Equal(0m, result.StepResults["401K-PRE"]);
    }

    [Fact]
    public async Task NoGroup_PreTaxIsUnclamped_EvenAboveTheLimit()
    {
        // Without a deferral-limit group the step keeps legacy behaviour: full elected amount.
        var step = new PreTaxBenefitStep("401K-PRE", 110, 30_000m, null,
            reducesIncomeTax: true, reducesFica: false);

        var result = await step.ExecuteAsync(Ctx());

        Assert.Equal(30_000m, result.StepResults["401K-PRE"]);
    }

    // ---- The key case: pre-tax + Roth share ONE limit (not 2×) ---------------------

    [Fact]
    public async Task PreTaxThenRoth_CombinedToTheLimit_NotDoubled()
    {
        // Elect $20,000 pre-tax, then a Roth percentage that would otherwise add far more.
        // Pre-tax fills first ($20,000 < $23,500 → full). Roth headroom = 23,500 − 20,000 = 3,500.
        var ctx = Ctx();

        var preTax = new PreTaxBenefitStep("401K-PRE", 110, 20_000m, null,
            reducesIncomeTax: true, reducesFica: false,
            combinedDeferralLimit: Limit, deferralMemberCodes: Members);
        ctx = await preTax.ExecuteAsync(ctx);

        // Roth at 50% of post-pre-tax income wages → proposed well above the remaining room.
        var roth = new PostTaxPctBenefitStep("401K-ROTH", 810, 0.50m, 1.0m,
            combinedDeferralLimit: Limit, deferralMemberCodes: Members);
        ctx = await roth.ExecuteAsync(ctx);

        Assert.Equal(20_000m, ctx.StepResults["401K-PRE"]);
        Assert.Equal(3_500m,  ctx.StepResults["401K-ROTH"]);
        // The whole point: combined deferral lands on the single limit, not 2× it.
        Assert.Equal(Limit, ctx.StepResults["401K-PRE"] + ctx.StepResults["401K-ROTH"]);
    }

    [Fact]
    public async Task PreTaxUnderLimit_RothTakesRemainder_BothPostPerLeg()
    {
        // Pre-tax $10,000 (full), Roth proposed $5,000 (< remaining $13,500) → full $5,000.
        // Members still post per-leg (W-2 reporting); only the limit is combined.
        var ctx = Ctx(incomeWages: 50_000m);

        var preTax = new PreTaxBenefitStep("401K-PRE", 110, 10_000m, null,
            reducesIncomeTax: true, reducesFica: false,
            combinedDeferralLimit: Limit, deferralMemberCodes: Members);
        ctx = await preTax.ExecuteAsync(ctx);

        // IncomeTaxableWages now 40,000; 12.5% = 5,000.
        var roth = new PostTaxPctBenefitStep("401K-ROTH", 810, 0.125m, 1.0m,
            combinedDeferralLimit: Limit, deferralMemberCodes: Members);
        ctx = await roth.ExecuteAsync(ctx);

        Assert.Equal(10_000m, ctx.StepResults["401K-PRE"]);
        Assert.Equal(5_000m,  ctx.StepResults["401K-ROTH"]);
    }

    [Fact]
    public async Task PreTaxAlreadyAtLimit_RothTakesNothing()
    {
        // Prior YTD pre-tax already at the limit → a fresh Roth election this period gets $0.
        var ctx = Ctx(ytdPre: 23_500m);

        var roth = new PostTaxPctBenefitStep("401K-ROTH", 810, 0.50m, 1.0m,
            combinedDeferralLimit: Limit, deferralMemberCodes: Members);
        ctx = await roth.ExecuteAsync(ctx);

        Assert.Equal(0m, ctx.StepResults["401K-ROTH"]);
    }
}
