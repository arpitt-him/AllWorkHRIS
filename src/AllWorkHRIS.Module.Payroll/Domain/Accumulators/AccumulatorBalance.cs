namespace AllWorkHRIS.Module.Payroll.Domain.Accumulators;

public sealed record AccumulatorBalance
{
    public Guid        AccumulatorId              { get; init; }
    public Guid        AccumulatorDefinitionId    { get; init; }
    public int         AccumulatorFamilyId        { get; init; }
    public int         ScopeTypeId                { get; init; }
    public Guid?       ParticipantId              { get; init; }
    public Guid?       EmployerId                 { get; init; }
    public Guid?       JurisdictionId             { get; init; }
    public Guid?       PlanId                     { get; init; }
    public int         PeriodContextId            { get; init; }
    public Guid        CalendarContextId          { get; init; }
    public decimal     CurrentValue               { get; init; }
    public int         BalanceStatusId            { get; init; }
    public Guid        LastUpdatedRunId           { get; init; }
    public Guid?       LastUpdatedResultSetId     { get; init; }
    public DateTimeOffset LastUpdateTimestamp     { get; init; }
}
