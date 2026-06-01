using AllWorkHRIS.Core.Pipeline;

namespace AllWorkHRIS.Module.Benefits.Steps;

/// <summary>
/// §402(g) combined elective-deferral clamp (ADR-021). The limit is shared across a set of
/// member accumulators (e.g. 401K-PRE + 401K-ROTH), so the headroom for any one member step is:
///
///   remaining = limit − Σ(member YTD from prior approved runs) − Σ(member deferrals taken THIS period)
///
/// The calling step's own contribution isn't posted to <see cref="CalculationContext.StepResults"/>
/// until after it runs, so it's never double-subtracted. Because pre-tax (low sequence) runs before
/// Roth (high sequence), pre-tax fills the combined headroom first and Roth — reading the just-posted
/// pre-tax result from the context — takes only the remainder. Allocation near the cap is therefore
/// the employee's election order, not a plan setting (ADR-021 §3).
/// </summary>
internal static class DeferralLimitClamp
{
    public static decimal Apply(
        decimal proposedAmount,
        decimal combinedLimit,
        IReadOnlyList<string> memberCodes,
        CalculationContext ctx)
    {
        decimal priorYtd = 0m, thisPeriod = 0m;
        foreach (var code in memberCodes)
        {
            if (ctx.YtdBalances.TryGetValue(code, out var y)) priorYtd   += y;
            if (ctx.StepResults.TryGetValue(code, out var s)) thisPeriod += s;
        }

        var remaining = Math.Max(0m, combinedLimit - priorYtd - thisPeriod);
        return Math.Min(proposedAmount, remaining);
    }
}
