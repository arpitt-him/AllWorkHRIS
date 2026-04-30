namespace AllWorkHRIS.Module.Payroll.Domain.Accumulators;

public sealed record AccumulatorContribution
{
    public Guid        ContributionId               { get; init; }
    public Guid        AccumulatorId                { get; init; }
    public Guid        AccumulatorImpactId          { get; init; }
    public Guid        AccumulatorDefinitionId      { get; init; }
    public Guid?       ParentContributionId         { get; init; }
    public Guid?       RootContributionId           { get; init; }
    public int?        ContributionLineageSequence  { get; init; }
    public Guid?       CorrectionReferenceId        { get; init; }
    public Guid        SourceRunId                  { get; init; }
    public Guid?       SourceResultSetId            { get; init; }
    public Guid?       SourceEmployeeResultId       { get; init; }
    public Guid        SourcePeriodId               { get; init; }
    public Guid        ExecutionPeriodId            { get; init; }
    public Guid?       EmploymentId                 { get; init; }
    public int         ScopeTypeId                  { get; init; }
    public Guid?       ScopeObjectId                { get; init; }
    public decimal     ContributionAmount           { get; init; }
    public int         ContributionTypeId           { get; init; }
    public decimal?    BeforeValue                  { get; init; }
    public decimal?    AfterValue                   { get; init; }
    public DateTimeOffset ContributionTimestamp     { get; init; }
    public DateTimeOffset CreatedTimestamp          { get; init; }
}
