namespace AllWorkHRIS.Module.Benefits.Domain.Codes;

/// <summary>
/// A resolved §402(g) combined elective-deferral limit group (ADR-021): ONE IRS limit
/// shared across a set of member accumulators (e.g. 401K-PRE + 401K-ROTH). The limit, plan
/// year, and member set live at the group grain — not duplicated per leg — and the benefit
/// pipeline enforces it by summing the members' YTD against this single <see cref="LimitAmount"/>.
/// <see cref="MemberAccumulatorCodes"/> is the active member set for the resolved pay date.
/// </summary>
public sealed record DeferralLimitGroup
{
    public string                GroupCode              { get; init; } = "";
    public string                GroupName              { get; init; } = "";
    public Guid?                 LegalEntityId          { get; init; }
    public decimal               LimitAmount            { get; init; }
    public decimal?              CatchUpAmount          { get; init; }   // §414(v), later slice — not yet applied
    public int                   PlanYearStartMonth     { get; init; } = 1;
    public int                   PlanYearStartDay       { get; init; } = 1;
    public DateOnly              EffectiveFrom          { get; init; }
    public DateOnly?             EffectiveTo            { get; init; }
    public IReadOnlyList<string> MemberAccumulatorCodes { get; init; } = [];
}
