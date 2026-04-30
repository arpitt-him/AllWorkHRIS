namespace AllWorkHRIS.Module.Payroll.Domain.Accumulators;

public sealed record AccumulatorImpact
{
    public Guid        AccumulatorImpactId         { get; init; }
    public Guid        AccumulatorDefinitionId     { get; init; }
    public Guid?       PayrollRunResultSetId       { get; init; }
    public Guid?       EmployeePayrollResultId     { get; init; }
    public Guid        PayrollRunId                { get; init; }
    public Guid?       EmploymentId                { get; init; }
    public Guid?       PersonId                    { get; init; }
    public int         ImpactStatusId              { get; init; }
    public int         ImpactSourceTypeId          { get; init; }
    public Guid?       SourceObjectId              { get; init; }
    public decimal     PriorValue                  { get; init; }
    public decimal     DeltaValue                  { get; init; }
    public decimal     NewValue                    { get; init; }
    public int         PostingDirectionId          { get; init; }
    public int         ScopeTypeId                 { get; init; }
    public Guid?       ScopeObjectId               { get; init; }
    public Guid?       JurisdictionId              { get; init; }
    public Guid?       RulePackId                  { get; init; }
    public Guid?       RuleVersionId               { get; init; }
    public bool        RetroactiveFlag             { get; init; }
    public bool        ReversalFlag                { get; init; }
    public bool        CorrectionFlag              { get; init; }
    public Guid?       PriorAccumulatorImpactId    { get; init; }
    public string?     Notes                       { get; init; }
    public DateTimeOffset ImpactTimestamp          { get; init; }
    public DateTimeOffset CreatedTimestamp         { get; init; }
    public DateTimeOffset UpdatedTimestamp         { get; init; }
}
