namespace AllWorkHRIS.Module.Benefits.Domain.Codes;

public sealed record DeductionEmployerMatch
{
    public Guid           MatchId              { get; init; }
    public Guid           DeductionId          { get; init; }
    public Guid?          EmployeeGroupId      { get; init; }
    public decimal        MatchRate            { get; init; }
    public decimal?       MatchCapPctOfGross   { get; init; }
    public decimal?       MatchCapAnnualAmount { get; init; }
    public string         MatchType            { get; init; } = "PER_PERIOD";  // PER_PERIOD | ANNUAL_TRUE_UP
    public DateOnly       EffectiveFrom        { get; init; }
    public DateOnly?      EffectiveTo          { get; init; }
    public DateTimeOffset CreatedAt            { get; init; }
}
